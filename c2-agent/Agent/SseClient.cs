using System.Text;
using System.Text.Json;
using Agent.Models;
using Microsoft.Extensions.Logging;

namespace Agent
{
    /// <summary>
    /// SSE (Server-Sent Events) client for receiving commands from server
    /// </summary>
    public class SseClient
    {
        private readonly ILogger<SseClient> _logger;
        private readonly HttpClient _httpClient;
        private readonly AgentConfig _config;
        private readonly CommandExecutor _executor;
        private CancellationTokenSource? _cancellationTokenSource;

        public event EventHandler<string>? OnLog;

        public SseClient(AgentConfig config, CommandExecutor executor, ILogger<SseClient> logger)
        {
            _logger = logger;
            _config = config;
            _executor = executor;
            _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        /// <summary>
        /// Start listening to SSE stream
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var streamUrl = $"{_config.ServerUrl}/agent/stream/{_config.AgentId}?hostname={_config.Hostname}";
            Log($"Conectando ao servidor SSE: {streamUrl}");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                request.Headers.Add("Accept", "text/event-stream");
                request.Headers.Add("Cache-Control", "no-cache");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    _cancellationTokenSource.Token
                );

                response.EnsureSuccessStatusCode();

                Log($"Conectado! Status: {response.StatusCode}");

                await using var stream = await response.Content.ReadAsStreamAsync(_cancellationTokenSource.Token);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                Log("[SSE] Iniciando processamento do stream...");
                await ProcessStreamAsync(reader, _cancellationTokenSource.Token);
                Log("[SSE] ProcessStreamAsync terminou - conexão será encerrada");
            }
            catch (HttpRequestException ex)
            {
                Log($"Erro HTTP: {ex.Message}");
                throw;
            }
            catch (OperationCanceledException)
            {
                Log("Conexão cancelada");
                throw;
            }
            catch (Exception ex)
            {
                Log($"Erro inesperado: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Process SSE stream line by line
        /// </summary>
        private async Task ProcessStreamAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            string? eventType = null;
            var dataBuilder = new StringBuilder();

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();

                if (line == null) // End of stream
                {
                    Log("[DISCONNECT] Stream encerrado pelo servidor (ReadLineAsync retornou null)");
                    Log("[DISCONNECT] Isso geralmente indica que o servidor fechou a conexão ou timeout de rede");
                    break;
                }

                // Empty line marks end of event
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (dataBuilder.Length > 0 && eventType == "command")
                    {
                        var data = dataBuilder.ToString();
                        await ProcessCommandAsync(data);
                        dataBuilder.Clear();
                    }
                    eventType = null;
                    continue;
                }

                // Comment (heartbeat)
                if (line.StartsWith(":"))
                {
                    // Log heartbeat periodically for diagnostics (every 10th heartbeat)
                    // Uncomment below line if you want to see heartbeat activity:
                    // Log($"[HEARTBEAT] Recebido: {line}");
                    continue; // Ignore heartbeats
                }

                // Event type
                if (line.StartsWith("event:"))
                {
                    eventType = line.Substring(6).Trim();
                    continue;
                }

                // Data
                if (line.StartsWith("data:"))
                {
                    var data = line.Substring(5).Trim();
                    dataBuilder.AppendLine(data);
                }
            }
        }

        /// <summary>
        /// Process command event
        /// </summary>
        private async Task ProcessCommandAsync(string jsonData)
        {
            try
            {
                var command = JsonSerializer.Deserialize<Command>(jsonData);

                if (command == null || string.IsNullOrEmpty(command.TaskId))
                {
                    Log("Comando inválido recebido");
                    return;
                }

                // Log command received
                CommandType commandType = command.GetCommandType();
                if (commandType == CommandType.Shell)
                {
                    Log($"Comando recebido [Task {command.TaskId}]: {command.CommandText}");
                }
                else
                {
                    Log($"Comando de mídia recebido [Task {command.TaskId}]: {commandType}");
                }

                // Execute command using new ExecuteCommand method
                var result = await _executor.ExecuteCommand(command);

                Log($"Comando executado [Task {command.TaskId}] - Exit Code: {result.ExitCode}");

                // Send result back to server
                await SendResultAsync(result);
            }
            catch (JsonException ex)
            {
                Log($"Erro ao parsear comando JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"Erro ao processar comando: {ex.Message}");
            }
        }

        /// <summary>
        /// Send command result back to server
        /// </summary>
        private async Task SendResultAsync(Result result)
        {
            try
            {
                var resultUrl = $"{_config.ServerUrl}/result";
                var json = JsonSerializer.Serialize(result);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(resultUrl, content);
                response.EnsureSuccessStatusCode();

                Log($"Resultado enviado para servidor [Task {result.TaskId}]");
            }
            catch (Exception ex)
            {
                Log($"Erro ao enviar resultado: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnect from server
        /// </summary>
        public void Disconnect()
        {
            _cancellationTokenSource?.Cancel();
            Log("Desconectando...");
        }

        /// <summary>
        /// Log message
        /// </summary>
        private void Log(string message)
        {
            // Use ILogger for file/console output
            _logger.LogInformation("{Message}", message);
        }
    }
}
