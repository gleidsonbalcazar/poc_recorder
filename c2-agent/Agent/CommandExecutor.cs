using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Agent.Models;
using Microsoft.Extensions.Logging;

namespace Agent
{
    /// <summary>
    /// Executes commands on Windows using cmd.exe and handles media commands
    /// </summary>
    public class CommandExecutor : IDisposable
    {
        private readonly ILogger<CommandExecutor> _logger;
        private readonly int _commandTimeoutMs;
        private readonly FFmpegRecorder _videoRecorder;
        private readonly MediaStorage _mediaStorage;
        private readonly string _agentId;

        // Public properties to access recorders (for MediaHttpServer)
        public FFmpegRecorder VideoRecorder => _videoRecorder;

        public CommandExecutor(string agentId, string storageBasePath, ILoggerFactory loggerFactory, int commandTimeoutMs = 60000)
        {
            _logger = loggerFactory.CreateLogger<CommandExecutor>();
            _commandTimeoutMs = commandTimeoutMs;
            _agentId = agentId;

            // Inicializar componentes de mídia
            var ffmpegLogger = loggerFactory.CreateLogger<FFmpegRecorder>();
            _videoRecorder = new FFmpegRecorder(storageBasePath, ffmpegLogger);
            _mediaStorage = new MediaStorage(storageBasePath);

            _logger.LogInformation("[CommandExecutor] Inicializado com storage: {StoragePath}", storageBasePath);
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

                    case CommandType.MediaListSessions:
                        HandleMediaListSessions(command, result);
                        break;

                    case CommandType.MediaSessionDetails:
                        HandleMediaSessionDetails(command, result);
                        break;

                    case CommandType.StatusQuery:
                        HandleStatusQuery(command, result);
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
            result.ExitCode = 0;

            // Com segmentação, outputPath é um diretório; sem segmentação, é um arquivo
            if (_videoRecorder.SegmentSeconds > 0 && Directory.Exists(outputPath))
            {
                // Modo segmentação: listar segmentos criados
                var segments = Directory.GetFiles(outputPath, "*.mp4", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => File.GetCreationTime(f))
                    .ToList();

                result.Output = $"Gravação segmentada parada: {segments.Count} segmento(s) em {outputPath}";

                // Retornar informações do último segmento
                if (segments.Any())
                {
                    var lastSegment = segments.Last();
                    var videoInfo = _videoRecorder.GetVideoInfo(lastSegment);

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
            }
            else
            {
                // Modo arquivo único
                var videoInfo = _videoRecorder.GetVideoInfo(outputPath);

                result.Output = $"Gravação de vídeo parada: {Path.GetFileName(outputPath)}";

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

        private void HandleMediaListSessions(Command command, Result result)
        {
            var sessions = _mediaStorage.ListVideoSegmentsBySession();

            result.Output = $"Total de sessões: {sessions.Count}";
            result.ExitCode = 0;
            result.Sessions = sessions.Select(kvp => {
                var segments = kvp.Value;
                var startTime = segments.Min(s => s.CreatedAt);
                var endTime = segments.Max(s => s.CreatedAt);
                var duration = (endTime - startTime).TotalMinutes;

                // Extrair pasta de data do path do primeiro segmento
                var dateFolder = "";
                if (segments.Count > 0)
                {
                    var firstPath = segments[0].FilePath;
                    var parentDir = Path.GetDirectoryName(firstPath);
                    if (parentDir != null)
                    {
                        dateFolder = Path.GetFileName(parentDir);
                    }
                }

                return new SessionInfo
                {
                    SessionKey = kvp.Key,
                    SegmentCount = segments.Count,
                    TotalSizeBytes = segments.Sum(s => s.SizeBytes),
                    TotalSizeMB = segments.Sum(s => s.SizeMB),
                    StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    DurationMinutes = duration,
                    DateFolder = dateFolder
                };
            }).OrderByDescending(s => s.StartTime).ToList();
        }

        private void HandleMediaSessionDetails(Command command, Result result)
        {
            string? sessionKey = command.SessionKey;

            if (string.IsNullOrEmpty(sessionKey))
            {
                result.Error = "Session key não especificada";
                result.ExitCode = 1;
                return;
            }

            var allSessions = _mediaStorage.ListVideoSegmentsBySession();

            if (!allSessions.ContainsKey(sessionKey))
            {
                result.Error = $"Sessão não encontrada: {sessionKey}";
                result.ExitCode = 1;
                return;
            }

            var segments = allSessions[sessionKey];
            var startTime = segments.Min(s => s.CreatedAt);
            var endTime = segments.Max(s => s.CreatedAt);
            var duration = (endTime - startTime).TotalMinutes;

            // Extrair pasta de data
            var dateFolder = "";
            if (segments.Count > 0)
            {
                var firstPath = segments[0].FilePath;
                var parentDir = Path.GetDirectoryName(firstPath);
                if (parentDir != null)
                {
                    dateFolder = Path.GetFileName(parentDir);
                }
            }

            result.Output = $"Sessão {sessionKey}: {segments.Count} segmento(s)";
            result.ExitCode = 0;
            result.Sessions = new List<SessionInfo>
            {
                new SessionInfo
                {
                    SessionKey = sessionKey,
                    SegmentCount = segments.Count,
                    TotalSizeBytes = segments.Sum(s => s.SizeBytes),
                    TotalSizeMB = segments.Sum(s => s.SizeMB),
                    StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    DurationMinutes = duration,
                    DateFolder = dateFolder,
                    Segments = segments.Select(s => new MediaFileResult
                    {
                        FilePath = s.FilePath,
                        FileName = s.FileName,
                        Type = s.Type.ToString().ToLower(),
                        SizeBytes = s.SizeBytes,
                        SizeMB = s.SizeMB,
                        CreatedAt = s.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    }).ToList()
                }
            };
        }

        /// <summary>
        /// Handle status:query command - return complete agent status
        /// </summary>
        private void HandleStatusQuery(Command command, Result result)
        {
            try
            {
                var status = new AgentStatusResult
                {
                    Recording = GetRecordingStatus(),
                    Database = GetDatabaseStats(),
                    Upload = GetUploadStatus(),
                    System = GetSystemInfo()
                };

                result.AgentStatus = status;
                result.Output = "Status query executed successfully";
                result.ExitCode = 0;
            }
            catch (Exception ex)
            {
                result.Error = $"Error getting status: {ex.Message}";
                result.ExitCode = 1;
            }
        }

        private RecordingStatusResult GetRecordingStatus()
        {
            bool isRecording = _videoRecorder.IsRecording;
            string? sessionKey = null;
            string? startedAt = null;
            long durationSeconds = 0;
            int segmentCount = 0;
            string mode = "manual";

            if (isRecording && _videoRecorder.CurrentRecordingPath != null)
            {
                // Extract session key from path (e.g., "session_1331")
                var dirName = Path.GetFileName(_videoRecorder.CurrentRecordingPath.TrimEnd(Path.DirectorySeparatorChar));
                if (dirName?.StartsWith("session_") == true)
                {
                    sessionKey = dirName;

                    // Get start time and segment count
                    try
                    {
                        var sessionDir = new DirectoryInfo(_videoRecorder.CurrentRecordingPath);
                        if (sessionDir.Exists)
                        {
                            var segments = sessionDir.GetFiles("*.mp4");
                            segmentCount = segments.Length;

                            if (segments.Length > 0)
                            {
                                startedAt = segments.OrderBy(f => f.CreationTime).First().CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                                durationSeconds = (long)(DateTime.UtcNow - segments.OrderBy(f => f.CreationTime).First().CreationTime.ToUniversalTime()).TotalSeconds;
                            }
                        }
                    }
                    catch { }
                }
            }

            // Determine mode (this is a simplified version - would need config access for full accuracy)
            if (isRecording)
            {
                mode = "continuous"; // Assume continuous if recording without specific config
            }

            return new RecordingStatusResult
            {
                IsRecording = isRecording,
                SessionKey = sessionKey,
                StartedAt = startedAt,
                DurationSeconds = durationSeconds,
                SegmentCount = segmentCount,
                CurrentFile = sessionKey != null ? $"{sessionKey}/screen_*.mp4" : null,
                Mode = mode
            };
        }

        private DatabaseStatsResult GetDatabaseStats()
        {
            // Note: CommandExecutor doesn't have access to DatabaseManager
            // This would need to be passed in or accessed differently
            // For now, return empty stats
            return new DatabaseStatsResult
            {
                Pending = 0,
                Uploading = 0,
                Done = 0,
                Error = 0,
                TotalSizeMB = 0
            };
        }

        private UploadStatusResult GetUploadStatus()
        {
            // Note: CommandExecutor doesn't have access to UploadWorker
            // Return basic info
            return new UploadStatusResult
            {
                Enabled = false,
                ActiveUploads = 0,
                Endpoint = null
            };
        }

        private SystemInfoResult GetSystemInfo()
        {
            string storagePath = _mediaStorage.BasePath;
            double diskSpaceGB = 0;

            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(storagePath) ?? "C:\\");
                diskSpaceGB = Math.Round(driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 2);
            }
            catch { }

            return new SystemInfoResult
            {
                OsVersion = Environment.OSVersion.ToString(),
                StoragePath = storagePath,
                DiskSpaceGB = diskSpaceGB
            };
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
