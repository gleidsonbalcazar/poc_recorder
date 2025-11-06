using Agent;
using Agent.Models;
using Agent.Database;
using Agent.Workers;

namespace AgentApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("╔═══════════════════════════════════════════╗");
            Console.WriteLine("║  Paneas Monitor - C2 & Autonomous Agent  ║");
            Console.WriteLine("║  POC - Sistema de Monitoramento           ║");
            Console.WriteLine("╚═══════════════════════════════════════════╝");
            Console.WriteLine();

            // Load configuration from appsettings.json
            var appConfig = ConfigManager.LoadFromFile();
            Console.WriteLine($"Mode: {appConfig.Mode}");
            Console.WriteLine();

            // Legacy C2 Configuration
            var config = new AgentConfig
            {
                ServerUrl = appConfig.C2.ServerUrl,
                AgentId = GenerateAgentId(),
                Hostname = Environment.MachineName,
                ReconnectDelayMs = appConfig.C2.ReconnectDelaySeconds * 1000,
                MaxReconnectAttempts = -1 // Infinite
            };

            Console.WriteLine($"Agent ID: {config.AgentId}");
            Console.WriteLine($"Hostname: {config.Hostname}");
            if (appConfig.C2.Enabled)
            {
                Console.WriteLine($"Server URL: {config.ServerUrl}");
            }
            Console.WriteLine();

            // Define storage path for media files
            string storageBasePath = string.IsNullOrEmpty(appConfig.Storage.BasePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "C2Agent")
                : appConfig.Storage.BasePath;

            Console.WriteLine($"Storage Path: {storageBasePath}");
            Console.WriteLine();

            // Initialize database
            string dbPath = Path.Combine(storageBasePath, appConfig.Database.Path);
            var database = new DatabaseManager(dbPath);
            Console.WriteLine($"Database: {dbPath}");
            Console.WriteLine();

            // Show queue stats
            var stats = database.GetQueueStats();
            Console.WriteLine("Queue Stats:");
            foreach (var stat in stats)
            {
                Console.WriteLine($"  {stat.Key}: {stat.Value}");
            }
            Console.WriteLine();

            // Create executor and SSE client
            var executor = new CommandExecutor(
                agentId: config.AgentId,
                storageBasePath: storageBasePath,
                commandTimeoutMs: 60000
            );

            // Apply recording configuration
            executor.VideoRecorder.SegmentSeconds = appConfig.Recording.SegmentSeconds;
            executor.VideoRecorder.FPS = appConfig.Recording.FPS;
            executor.VideoRecorder.VideoBitrate = appConfig.Recording.VideoBitrate;
            executor.VideoRecorder.CaptureAudio = appConfig.Recording.CaptureAudio;

            SseClient? sseClient = null;
            if (appConfig.C2.Enabled)
            {
                sseClient = new SseClient(config, executor);
            }

            // Initialize Workers (Autonomous mode)
            VideoRecorderWorker? recorderWorker = null;
            UploadWorker? uploadWorker = null;

            if (appConfig.Mode == "autonomous" || appConfig.Mode == "hybrid")
            {
                Console.WriteLine("[Workers] Initializing autonomous workers...");

                // Recorder Worker
                recorderWorker = new VideoRecorderWorker(executor.VideoRecorder, database)
                {
                    ContinuousMode = appConfig.Recording.Continuous,
                    RecordingIntervalMinutes = appConfig.Recording.IntervalMinutes,
                    RecordingDurationMinutes = appConfig.Recording.DurationMinutes
                };

                // Upload Worker
                if (appConfig.Upload.Enabled)
                {
                    uploadWorker = new UploadWorker(database)
                    {
                        PollIntervalSeconds = appConfig.Upload.PollIntervalSeconds,
                        MaxConcurrentUploads = appConfig.Upload.MaxConcurrentUploads,
                        MaxRetries = appConfig.Upload.MaxRetries
                    };
                }

                Console.WriteLine("[Workers] Workers initialized");
                Console.WriteLine();
            }

            // Start HTTP server for media preview (with recorder reference to prevent locked file access)
            var httpServer = new MediaHttpServer(
                storageBasePath,
                port: 9000,
                videoRecorder: executor.VideoRecorder
            );
            try
            {
                httpServer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not start HTTP server: {ex.Message}");
                Console.WriteLine("Preview functionality will not be available.");
            }

            // Subscribe to log events (C2 mode)
            if (sseClient != null)
            {
                sseClient.OnLog += (sender, message) =>
                {
                    Console.WriteLine(message);
                };
            }

            // Handle Ctrl+C gracefully
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine();
                Console.WriteLine("Encerrando agente...");
                cts.Cancel();
            };

            // Start Workers
            if (recorderWorker != null)
            {
                recorderWorker.Start();
            }

            if (uploadWorker != null)
            {
                uploadWorker.Start();
            }

            Console.WriteLine("═══════════════════════════════════════════");
            Console.WriteLine("  Agent running. Press Ctrl+C to stop.");
            Console.WriteLine("═══════════════════════════════════════════");
            Console.WriteLine();

            // Main loop with reconnect logic (C2 mode)
            if (appConfig.C2.Enabled && sseClient != null)
            {
                int reconnectAttempt = 0;

                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        reconnectAttempt++;

                        if (reconnectAttempt > 1)
                        {
                            Console.WriteLine($"Tentativa de reconexão #{reconnectAttempt}...");
                            await Task.Delay(config.ReconnectDelayMs, cts.Token);
                        }

                        await sseClient.ConnectAsync(cts.Token);

                        // If we reach here, connection was closed gracefully
                        Console.WriteLine("Conexão encerrada pelo servidor");
                    }
                    catch (OperationCanceledException)
                    {
                        // User requested shutdown
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro de conexão: {ex.Message}");

                        if (config.MaxReconnectAttempts > 0 && reconnectAttempt >= config.MaxReconnectAttempts)
                        {
                            Console.WriteLine($"Máximo de tentativas ({config.MaxReconnectAttempts}) atingido. Encerrando...");
                            break;
                        }
                    }
                }
            }
            else
            {
                // Autonomous mode - just wait for cancellation
                await Task.Delay(Timeout.Infinite, cts.Token);
            }

            // Cleanup resources
            Console.WriteLine();
            Console.WriteLine("Stopping workers...");

            if (recorderWorker != null)
            {
                await recorderWorker.StopAsync();
            }

            if (uploadWorker != null)
            {
                await uploadWorker.StopAsync();
            }

            httpServer.Dispose();
            executor.Dispose();
            database.Dispose();

            Console.WriteLine();
            Console.WriteLine("Agente encerrado.");
        }

        /// <summary>
        /// Generate agent ID based on hostname
        /// </summary>
        static string GenerateAgentId()
        {
            var hostname = Environment.MachineName.ToLowerInvariant();
            return hostname;
        }

        /// <summary>
        /// Get server URL from command line args or use default
        /// </summary>
        static string GetServerUrl(string[] args)
        {
            if (args.Length > 0)
            {
                return args[0];
            }

            // Try to read from environment variable
            var envUrl = Environment.GetEnvironmentVariable("C2_SERVER_URL");
            if (!string.IsNullOrEmpty(envUrl))
            {
                return envUrl;
            }

            return "http://localhost:8000";
        }
    }
}
