using Agent;
using Agent.Models;
using Agent.Database;
using Agent.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Serilog.Extensions.Logging.File;

namespace AgentApp
{
    class Program
    {
        [STAThread]
        static async Task Main(string[] args)
        {
            // Load configuration from appsettings.json FIRST
            var appConfig = ConfigManager.LoadFromFile();

            // Apply recording profile (Performance, Balanced, Quality)
            appConfig.Recording.ApplyProfile();

            // Define storage path early (needed for logging)
            string storageBasePath = string.IsNullOrEmpty(appConfig.Storage.BasePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaneasMonitor")
                : appConfig.Storage.BasePath;

            // Setup logging
            var logDirectory = Path.Combine(storageBasePath, "logs");
            Directory.CreateDirectory(logDirectory);
            var logFilePath = Path.Combine(logDirectory, $"agent-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                // Read logging config from appsettings (use app directory, works for both regular and single-file apps)
                var appDirectory = AppContext.BaseDirectory;
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(appDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
                var configuration = configBuilder.Build();

                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddConsole();
                builder.AddFile(logFilePath, minimumLevel: LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("╔═══════════════════════════════════════════╗");
            logger.LogInformation("║  Paneas Monitor - C2 & Autonomous Agent  ║");
            logger.LogInformation("║  POC - Sistema de Monitoramento           ║");
            logger.LogInformation("╚═══════════════════════════════════════════╝");
            logger.LogInformation("");
            logger.LogInformation("Mode: {Mode}", appConfig.Mode);
            logger.LogInformation("");

            // Legacy C2 Configuration
            var config = new AgentConfig
            {
                ServerUrl = appConfig.C2.ServerUrl,
                AgentId = GenerateAgentId(),
                Hostname = Environment.MachineName,
                ReconnectDelayMs = appConfig.C2.ReconnectDelaySeconds * 1000,
                MaxReconnectAttempts = -1 // Infinite
            };

            logger.LogInformation("Agent ID: {AgentId}", config.AgentId);
            logger.LogInformation("Hostname: {Hostname}", config.Hostname);
            if (appConfig.C2.Enabled)
            {
                logger.LogInformation("Server URL: {ServerUrl}", config.ServerUrl);
            }
            logger.LogInformation("");

            logger.LogInformation("Storage Path: {StoragePath}", storageBasePath);
            logger.LogInformation("");

            // Initialize database
            string dbPath = Path.Combine(storageBasePath, appConfig.Database.Path);
            var database = new DatabaseManager(dbPath);
            logger.LogInformation("Database: {DbPath}", dbPath);
            logger.LogInformation("");

            // Show queue stats
            var stats = database.GetQueueStats();
            logger.LogInformation("Queue Stats:");
            foreach (var stat in stats)
            {
                logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
            }
            logger.LogInformation("");

            // Kill orphaned FFmpeg processes from previous runs
            ProcessCleanup.KillOrphanedFFmpegProcesses();
            logger.LogInformation("");

            // Create executor and SSE client
            var executor = new CommandExecutor(
                agentId: config.AgentId,
                storageBasePath: storageBasePath,
                loggerFactory: loggerFactory,
                commandTimeoutMs: 60000
            );

            // Apply recording configuration (from appsettings)
            executor.VideoRecorder.SegmentSeconds = appConfig.Recording.SegmentSeconds;
            executor.VideoRecorder.FPS = appConfig.Recording.FPS;
            executor.VideoRecorder.VideoBitrate = appConfig.Recording.VideoBitrate;
            executor.VideoRecorder.CaptureAudio = appConfig.Recording.CaptureAudio;

            SseClient? sseClient = null;
            if (appConfig.C2.Enabled)
            {
                sseClient = new SseClient(config, executor, loggerFactory.CreateLogger<SseClient>());
            }

            // Initialize Workers (Autonomous mode)
            VideoRecorderWorker? recorderWorker = null;
            UploadWorker? uploadWorker = null;

            if (appConfig.Mode == "autonomous" || appConfig.Mode == "hybrid")
            {
                logger.LogInformation("[Workers] Initializing autonomous workers...");

                // Recorder Worker
                recorderWorker = new VideoRecorderWorker(
                    executor.VideoRecorder,
                    database,
                    loggerFactory.CreateLogger<VideoRecorderWorker>())
                {
                    ContinuousMode = appConfig.Recording.Continuous,
                    RecordingIntervalMinutes = appConfig.Recording.IntervalMinutes,
                    RecordingDurationMinutes = appConfig.Recording.DurationMinutes
                };

                // Upload Worker
                if (appConfig.Upload.Enabled)
                {
                    uploadWorker = new UploadWorker(
                        database,
                        loggerFactory.CreateLogger<UploadWorker>())
                    {
                        PollIntervalSeconds = appConfig.Upload.PollIntervalSeconds,
                        MaxConcurrentUploads = appConfig.Upload.MaxConcurrentUploads,
                        MaxRetries = appConfig.Upload.MaxRetries,
                        UploadEndpoint = appConfig.Upload.Endpoint,
                        ApiKey = appConfig.Upload.ApiKey,
                        TusServerUrl = appConfig.Tus.TusServerUrl,
                        TusMaxRetries = appConfig.Tus.MaxRetries,
                        TusRetryDelayMs = appConfig.Tus.RetryDelayMs
                    };
                }

                logger.LogInformation("[Workers] Workers initialized");
                logger.LogInformation("");
            }

            // Create ConfigurationManager for hot-reload support
            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var configManager = new Agent.ConfigurationManager(
                configPath,
                loggerFactory.CreateLogger<Agent.ConfigurationManager>()
            );

            // Register workers with ConfigurationManager for hot-reload
            configManager.RegisterComponents(
                recorderWorker: recorderWorker,
                uploadWorker: uploadWorker,
                sseClient: sseClient
            );

            // Start HTTP server with Web UI support
            var httpServer = new MediaHttpServer(
                storageBasePath,
                port: 9000,
                videoRecorder: executor.VideoRecorder,
                logger: loggerFactory.CreateLogger<MediaHttpServer>(),
                configManager: configManager,
                recorderWorker: recorderWorker,
                uploadWorker: uploadWorker,
                database: database,
                webUIPassword: appConfig.WebUI.Password
            );
            try
            {
                httpServer.Start();
                logger.LogInformation("");
                logger.LogInformation("Web UI: http://localhost:9000/config");
                logger.LogInformation("  Username: (any)");
                logger.LogInformation("  Password: {Password}", appConfig.WebUI.Password);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Warning: Could not start HTTP server: {Message}", ex.Message);
                logger.LogWarning("Preview and Web UI will not be available.");
            }

            // SseClient now logs directly using ILogger

            // Handle Ctrl+C gracefully
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                logger.LogInformation("");
                logger.LogInformation("Encerrando agente...");
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

            logger.LogInformation("═══════════════════════════════════════════");
            logger.LogInformation("  Agent running. Press Ctrl+C to stop.");
            logger.LogInformation("═══════════════════════════════════════════");
            logger.LogInformation("");

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
                            logger.LogInformation("Tentativa de reconexão #{ReconnectAttempt}...", reconnectAttempt);
                            await Task.Delay(config.ReconnectDelayMs, cts.Token);
                        }

                        await sseClient.ConnectAsync(cts.Token);

                        // If we reach here, connection was closed gracefully
                        logger.LogInformation("Conexão encerrada pelo servidor");
                    }
                    catch (OperationCanceledException)
                    {
                        // User requested shutdown
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError("Erro de conexão: {Message}", ex.Message);

                        if (config.MaxReconnectAttempts > 0 && reconnectAttempt >= config.MaxReconnectAttempts)
                        {
                            logger.LogError("Máximo de tentativas ({MaxAttempts}) atingido. Encerrando...", config.MaxReconnectAttempts);
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
            logger.LogInformation("");
            logger.LogInformation("Stopping workers...");

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

            logger.LogInformation("");
            logger.LogInformation("Agente encerrado.");

            loggerFactory.Dispose();
        }

        /// <summary>
        /// Generate agent ID based on hostname and username
        /// </summary>
        static string GenerateAgentId()
        {
            var hostname = Environment.MachineName.ToLowerInvariant();
            var username = Environment.UserName.ToLowerInvariant();
            return $"{hostname}-{username}";
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
