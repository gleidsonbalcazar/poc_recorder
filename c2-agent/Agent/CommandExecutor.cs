using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Agent.Models;

namespace Agent
{
    /// <summary>
    /// Executes commands on Windows using cmd.exe and handles media commands
    /// </summary>
    public class CommandExecutor : IDisposable
    {
        private readonly int _commandTimeoutMs;
        private readonly FFmpegRecorder _videoRecorder;
        private readonly MediaStorage _mediaStorage;
        private readonly string _agentId;

        // Public properties to access recorders (for MediaHttpServer)
        public FFmpegRecorder VideoRecorder => _videoRecorder;

        public CommandExecutor(string agentId, string storageBasePath, int commandTimeoutMs = 60000)
        {
            _commandTimeoutMs = commandTimeoutMs;
            _agentId = agentId;

            // Inicializar componentes de mídia
            _videoRecorder = new FFmpegRecorder(storageBasePath);
            _mediaStorage = new MediaStorage(storageBasePath);

            Console.WriteLine($"[CommandExecutor] Inicializado com storage: {storageBasePath}");
        }

        /// <summary>
        /// Execute a command based on its type and return a Result
        /// </summary>
        public async Task<Result> ExecuteCommand(Command command)
        {
            var result = new Result
            {
                TaskId = command.TaskId,
                AgentId = _agentId,
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            try
            {
                CommandType commandType = command.GetCommandType();

                switch (commandType)
                {
                    case CommandType.VideoStart:
                        await HandleVideoStart(command, result);
                        break;

                    case CommandType.VideoStop:
                        await HandleVideoStop(command, result);
                        break;

                    case CommandType.VideoConfig:
                        HandleVideoConfig(command, result);
                        break;

                    case CommandType.MediaList:
                        HandleMediaList(command, result);
                        break;

                    case CommandType.MediaClean:
                        HandleMediaClean(command, result);
                        break;

                    case CommandType.MediaStats:
                        HandleMediaStats(command, result);
                        break;

                    case CommandType.MediaDelete:
                        HandleMediaDelete(command, result);
                        break;

                    case CommandType.Shell:
                    default:
                        HandleShellCommand(command, result);
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Error = $"Exception: {ex.Message}";
                result.ExitCode = -1;
                result.Output = "";
            }

            return result;
        }

        /// <summary>
        /// Execute a shell command (backward compatibility)
        /// </summary>
        public (string output, string error, int exitCode) Execute(string command)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = processStartInfo };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited = process.WaitForExit(_commandTimeoutMs);

                if (!exited)
                {
                    process.Kill(true);
                    return (
                        output: "",
                        error: $"Command timed out after {_commandTimeoutMs}ms",
                        exitCode: -1
                    );
                }

                var output = outputBuilder.ToString().Trim();
                var error = errorBuilder.ToString().Trim();
                var exitCode = process.ExitCode;

                return (output, error, exitCode);
            }
            catch (Exception ex)
            {
                return (
                    output: "",
                    error: $"Exception executing command: {ex.Message}",
                    exitCode: -1
                );
            }
        }

        // ===== MEDIA COMMAND HANDLERS =====

        private async Task HandleVideoStart(Command command, Result result)
        {
            int duration = command.Duration ?? 0;

            if (command.Fps.HasValue)
                _videoRecorder.FPS = command.Fps.Value;

            if (command.Quality.HasValue)
                _videoRecorder.VideoQuality = command.Quality.Value;

            string outputPath = await _videoRecorder.StartRecording(duration);

            result.Output = $"Gravação de vídeo iniciada{(duration > 0 ? $" ({duration}s)" : " (manual stop)")}";
            result.ExitCode = 0;
            result.MediaFile = new MediaFileResult
            {
                FilePath = outputPath,
                FileName = Path.GetFileName(outputPath),
                Type = "video"
            };
        }

        private async Task HandleVideoStop(Command command, Result result)
        {
            if (!_videoRecorder.IsRecording)
            {
                result.Error = "Nenhuma gravação de vídeo em andamento";
                result.ExitCode = 1;
                return;
            }

            string outputPath = await _videoRecorder.StopRecording();
            var videoInfo = _videoRecorder.GetVideoInfo(outputPath);

            result.Output = $"Gravação de vídeo parada: {Path.GetFileName(outputPath)}";
            result.ExitCode = 0;

            if (videoInfo != null)
            {
                result.MediaFile = new MediaFileResult
                {
                    FilePath = videoInfo.FilePath,
                    FileName = videoInfo.FileName,
                    Type = "video",
                    SizeBytes = videoInfo.SizeBytes,
                    SizeMB = videoInfo.SizeMB,
                    CreatedAt = videoInfo.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    DurationMinutes = videoInfo.DurationEstimateMinutes
                };
            }
        }

        private void HandleVideoConfig(Command command, Result result)
        {
            if (command.Interval.HasValue)
                _videoRecorder.PeriodicIntervalMinutes = command.Interval.Value;

            if (command.Duration.HasValue)
                _videoRecorder.PeriodicDurationMinutes = command.Duration.Value;

            _videoRecorder.StartPeriodicRecording();

            result.Output = $"Gravação periódica configurada: {_videoRecorder.PeriodicDurationMinutes}min a cada {_videoRecorder.PeriodicIntervalMinutes}min";
            result.ExitCode = 0;
        }

        private void HandleMediaList(Command command, Result result)
        {
            var mediaFiles = _mediaStorage.ListAllMediaFiles(100);

            result.Output = $"Total de arquivos: {mediaFiles.Count}";
            result.ExitCode = 0;
            result.MediaFiles = mediaFiles.Select(f => new MediaFileResult
            {
                FilePath = f.FilePath,
                FileName = f.FileName,
                Type = f.Type.ToString().ToLower(),
                SizeBytes = f.SizeBytes,
                SizeMB = f.SizeMB,
                CreatedAt = f.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }).ToList();
        }

        private void HandleMediaClean(Command command, Result result)
        {
            int daysOld = command.Days ?? 7; // Padrão: 7 dias
            int deletedCount = _mediaStorage.CleanOldFiles(daysOld);

            result.Output = $"{deletedCount} arquivos removidos (mais antigos que {daysOld} dias)";
            result.ExitCode = 0;
        }

        private void HandleMediaStats(Command command, Result result)
        {
            var stats = _mediaStorage.GetStorageStats();

            result.Output = $"Total: {stats.TotalFiles} arquivos ({stats.TotalSizeMB:F2} MB)";
            result.ExitCode = 0;
            result.StorageStats = new StorageStatsResult
            {
                TotalFiles = stats.TotalFiles,
                VideoFiles = stats.VideoFiles,
                TotalSizeMB = stats.TotalSizeMB,
                VideoSizeMB = stats.VideoSizeMB,
                BasePath = stats.BasePath
            };
        }

        private void HandleMediaDelete(Command command, Result result)
        {
            // Parse filename from command text or filename property
            string? filename = command.Filename;

            if (string.IsNullOrEmpty(filename))
            {
                // Try to extract from command text: "media:delete filename.mp4"
                var parts = command.CommandText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    filename = parts[1];
                }
            }

            if (string.IsNullOrEmpty(filename))
            {
                result.Error = "Nome do arquivo não especificado";
                result.ExitCode = 1;
                return;
            }

            bool deleted = _mediaStorage.DeleteFile(filename);

            if (deleted)
            {
                result.Output = $"Arquivo deletado: {filename}";
                result.ExitCode = 0;
            }
            else
            {
                result.Error = $"Falha ao deletar arquivo: {filename}";
                result.ExitCode = 1;
            }
        }

        private void HandleShellCommand(Command command, Result result)
        {
            var (output, error, exitCode) = Execute(command.CommandText);

            result.Output = output;
            result.Error = string.IsNullOrEmpty(error) ? null : error;
            result.ExitCode = exitCode;
        }

        public void Dispose()
        {
            _videoRecorder?.Dispose();
        }
    }
}
