using System.Text.Json.Serialization;

namespace Agent.Models
{
    /// <summary>
    /// Tipos de comando suportados pelo agente
    /// </summary>
    public enum CommandType
    {
        Shell,              // Comando shell (cmd.exe)
        VideoStart,         // Iniciar gravação de vídeo
        VideoStop,          // Parar gravação de vídeo
        VideoConfig,        // Configurar gravação periódica de vídeo
        MediaList,          // Listar arquivos de mídia
        MediaClean,         // Limpar arquivos antigos
        MediaStats,         // Obter estatísticas de armazenamento
        MediaDelete,        // Deletar arquivo específico
        MediaListSessions,  // Listar sessões de gravação
        MediaSessionDetails,// Obter detalhes de uma sessão específica
        StatusQuery         // Obter status completo do agente
    }

    /// <summary>
    /// Command received from server via SSE
    /// </summary>
    public class Command
    {
        [JsonPropertyName("task_id")]
        public string TaskId { get; set; } = string.Empty;

        [JsonPropertyName("command")]
        public string CommandText { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("duration")]
        public int? Duration { get; set; }

        [JsonPropertyName("fps")]
        public int? Fps { get; set; }

        [JsonPropertyName("quality")]
        public int? Quality { get; set; }

        [JsonPropertyName("interval")]
        public int? Interval { get; set; }

        [JsonPropertyName("days")]
        public int? Days { get; set; }

        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("segment_seconds")]
        public int? SegmentSeconds { get; set; }

        [JsonPropertyName("session_key")]
        public string? SessionKey { get; set; }

        /// <summary>
        /// Parse command type from command text or type field
        /// </summary>
        public CommandType GetCommandType()
        {
            // Se Type foi especificado, usar ele
            if (!string.IsNullOrEmpty(Type))
            {
                return Type.ToLower() switch
                {
                    "video:start" => CommandType.VideoStart,
                    "video:stop" => CommandType.VideoStop,
                    "video:config" => CommandType.VideoConfig,
                    "media:list" => CommandType.MediaList,
                    "media:clean" => CommandType.MediaClean,
                    "media:stats" => CommandType.MediaStats,
                    "media:delete" => CommandType.MediaDelete,
                    "media:list-sessions" => CommandType.MediaListSessions,
                    "media:session-details" => CommandType.MediaSessionDetails,
                    "status:query" => CommandType.StatusQuery,
                    _ => CommandType.Shell
                };
            }

            // Caso contrário, tentar detectar pelo texto do comando
            string cmd = CommandText.ToLower().Trim();
            if (cmd.StartsWith("video:")) return ParseVideoCommand(cmd);
            if (cmd.StartsWith("media:")) return ParseMediaCommand(cmd);
            if (cmd.StartsWith("status:")) return ParseStatusCommand(cmd);

            return CommandType.Shell;
        }

        private CommandType ParseVideoCommand(string cmd)
        {
            if (cmd.Contains("start")) return CommandType.VideoStart;
            if (cmd.Contains("stop")) return CommandType.VideoStop;
            if (cmd.Contains("config")) return CommandType.VideoConfig;
            return CommandType.Shell;
        }

        private CommandType ParseMediaCommand(string cmd)
        {
            if (cmd.Contains("list-sessions")) return CommandType.MediaListSessions;
            if (cmd.Contains("session-details")) return CommandType.MediaSessionDetails;
            if (cmd.Contains("list")) return CommandType.MediaList;
            if (cmd.Contains("clean")) return CommandType.MediaClean;
            if (cmd.Contains("stats")) return CommandType.MediaStats;
            if (cmd.Contains("delete")) return CommandType.MediaDelete;
            return CommandType.Shell;
        }

        private CommandType ParseStatusCommand(string cmd)
        {
            if (cmd.Contains("query")) return CommandType.StatusQuery;
            return CommandType.Shell;
        }
    }

    /// <summary>
    /// Result to send back to server
    /// </summary>
    public class Result
    {
        [JsonPropertyName("task_id")]
        public string TaskId { get; set; } = string.Empty;

        [JsonPropertyName("agent_id")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("output")]
        public string Output { get; set; } = string.Empty;

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("exit_code")]
        public int ExitCode { get; set; }

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        [JsonPropertyName("media_file")]
        public MediaFileResult? MediaFile { get; set; }

        [JsonPropertyName("media_files")]
        public List<MediaFileResult>? MediaFiles { get; set; }

        [JsonPropertyName("storage_stats")]
        public StorageStatsResult? StorageStats { get; set; }

        [JsonPropertyName("sessions")]
        public List<SessionInfo>? Sessions { get; set; }

        [JsonPropertyName("agent_status")]
        public AgentStatusResult? AgentStatus { get; set; }
    }

    /// <summary>
    /// Media file information for results
    /// </summary>
    public class MediaFileResult
    {
        [JsonPropertyName("file_path")]
        public string FilePath { get; set; } = "";

        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("size_mb")]
        public double SizeMB { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = "";

        [JsonPropertyName("duration_minutes")]
        public double? DurationMinutes { get; set; }
    }

    /// <summary>
    /// Storage statistics for results
    /// </summary>
    public class StorageStatsResult
    {
        [JsonPropertyName("total_files")]
        public int TotalFiles { get; set; }

        [JsonPropertyName("video_files")]
        public int VideoFiles { get; set; }

        [JsonPropertyName("total_size_mb")]
        public double TotalSizeMB { get; set; }

        [JsonPropertyName("video_size_mb")]
        public double VideoSizeMB { get; set; }

        [JsonPropertyName("base_path")]
        public string BasePath { get; set; } = "";
    }

    /// <summary>
    /// Recording session information (multiple segments grouped together)
    /// </summary>
    public class SessionInfo
    {
        [JsonPropertyName("session_key")]
        public string SessionKey { get; set; } = "";

        [JsonPropertyName("segment_count")]
        public int SegmentCount { get; set; }

        [JsonPropertyName("total_size_bytes")]
        public long TotalSizeBytes { get; set; }

        [JsonPropertyName("total_size_mb")]
        public double TotalSizeMB { get; set; }

        [JsonPropertyName("start_time")]
        public string StartTime { get; set; } = "";

        [JsonPropertyName("end_time")]
        public string EndTime { get; set; } = "";

        [JsonPropertyName("duration_minutes")]
        public double DurationMinutes { get; set; }

        [JsonPropertyName("date_folder")]
        public string DateFolder { get; set; } = "";

        [JsonPropertyName("segments")]
        public List<MediaFileResult>? Segments { get; set; }
    }

    /// <summary>
    /// Agent configuration
    /// </summary>
    public class AgentConfig
    {
        public string ServerUrl { get; set; } = "http://localhost:8000";
        public string AgentId { get; set; } = string.Empty;
        public string Hostname { get; set; } = Environment.MachineName;
        public int ReconnectDelayMs { get; set; } = 5000;
        public int MaxReconnectAttempts { get; set; } = -1; // -1 = infinite
    }

    /// <summary>
    /// Complete agent status (for status:query command)
    /// </summary>
    public class AgentStatusResult
    {
        [JsonPropertyName("recording")]
        public RecordingStatusResult Recording { get; set; } = new();

        [JsonPropertyName("database")]
        public DatabaseStatsResult Database { get; set; } = new();

        [JsonPropertyName("upload")]
        public UploadStatusResult Upload { get; set; } = new();

        [JsonPropertyName("system")]
        public SystemInfoResult System { get; set; } = new();
    }

    /// <summary>
    /// Current recording status
    /// </summary>
    public class RecordingStatusResult
    {
        [JsonPropertyName("is_recording")]
        public bool IsRecording { get; set; }

        [JsonPropertyName("session_key")]
        public string? SessionKey { get; set; }

        [JsonPropertyName("started_at")]
        public string? StartedAt { get; set; }

        [JsonPropertyName("duration_seconds")]
        public long DurationSeconds { get; set; }

        [JsonPropertyName("segment_count")]
        public int SegmentCount { get; set; }

        [JsonPropertyName("current_file")]
        public string? CurrentFile { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = ""; // continuous, scheduled, manual
    }

    /// <summary>
    /// Database statistics
    /// </summary>
    public class DatabaseStatsResult
    {
        [JsonPropertyName("pending")]
        public int Pending { get; set; }

        [JsonPropertyName("uploading")]
        public int Uploading { get; set; }

        [JsonPropertyName("done")]
        public int Done { get; set; }

        [JsonPropertyName("error")]
        public int Error { get; set; }

        [JsonPropertyName("total_size_mb")]
        public double TotalSizeMB { get; set; }
    }

    /// <summary>
    /// Upload worker status
    /// </summary>
    public class UploadStatusResult
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("active_uploads")]
        public int ActiveUploads { get; set; }

        [JsonPropertyName("endpoint")]
        public string? Endpoint { get; set; }
    }

    /// <summary>
    /// System information
    /// </summary>
    public class SystemInfoResult
    {
        [JsonPropertyName("os_version")]
        public string OsVersion { get; set; } = "";

        [JsonPropertyName("storage_path")]
        public string StoragePath { get; set; } = "";

        [JsonPropertyName("disk_space_gb")]
        public double DiskSpaceGB { get; set; }
    }
}
