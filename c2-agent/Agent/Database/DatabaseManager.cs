using Microsoft.Data.Sqlite;
using System.Text.Json;
using Agent.Database.Models;

namespace Agent.Database;

/// <summary>
/// Gerencia o banco de dados SQLite e operações de persistência
/// </summary>
public class DatabaseManager : IDisposable
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public DatabaseManager(string dbPath)
    {
        _dbPath = dbPath;
        _connectionString = $"Data Source={dbPath}";

        // Garantir que o diretório existe
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        InitializeDatabase();
    }

    /// <summary>
    /// Cria as tabelas se não existirem
    /// </summary>
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var createTablesCommand = connection.CreateCommand();
        createTablesCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS video_queue (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL,
                session_key TEXT,
                process_snapshot TEXT,
                status TEXT NOT NULL DEFAULT 'pending',
                created_at TEXT NOT NULL,
                uploaded_at TEXT,
                retry_count INTEGER DEFAULT 0,
                error_message TEXT,
                file_size_bytes INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS upload_tasks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                video_record_id INTEGER NOT NULL,
                upload_url TEXT,
                status TEXT NOT NULL DEFAULT 'pending',
                progress INTEGER DEFAULT 0,
                bytes_uploaded INTEGER DEFAULT 0,
                total_bytes INTEGER DEFAULT 0,
                retry_count INTEGER DEFAULT 0,
                max_retries INTEGER DEFAULT 3,
                error_message TEXT,
                last_attempt_at TEXT,
                next_retry_at TEXT,
                created_at TEXT NOT NULL,
                completed_at TEXT,
                FOREIGN KEY (video_record_id) REFERENCES video_queue(id)
            );

            CREATE INDEX IF NOT EXISTS idx_video_status ON video_queue(status);
            CREATE INDEX IF NOT EXISTS idx_video_created ON video_queue(created_at);
            CREATE INDEX IF NOT EXISTS idx_upload_status ON upload_tasks(status);
            CREATE INDEX IF NOT EXISTS idx_upload_next_retry ON upload_tasks(next_retry_at);
        ";

        createTablesCommand.ExecuteNonQuery();
        Console.WriteLine($"[DatabaseManager] Database initialized at: {_dbPath}");
    }

    // ===== VIDEO RECORDS =====

    /// <summary>
    /// Insere um novo registro de vídeo
    /// </summary>
    public long InsertVideoRecord(VideoRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO video_queue
            (file_path, session_key, process_snapshot, status, created_at, file_size_bytes)
            VALUES
            (@filePath, @sessionKey, @processSnapshot, @status, @createdAt, @fileSize);
            SELECT last_insert_rowid();
        ";

        command.Parameters.AddWithValue("@filePath", record.FilePath);
        command.Parameters.AddWithValue("@sessionKey", record.SessionKey ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@processSnapshot", record.ProcessSnapshot ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@status", record.Status);
        command.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("o"));
        command.Parameters.AddWithValue("@fileSize", record.FileSizeBytes);

        var id = (long?)command.ExecuteScalar();
        return id ?? 0;
    }

    /// <summary>
    /// Atualiza o status de um registro de vídeo
    /// </summary>
    public void UpdateVideoStatus(long id, string status, string? errorMessage = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE video_queue
            SET status = @status,
                error_message = @errorMessage,
                uploaded_at = @uploadedAt
            WHERE id = @id
        ";

        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@errorMessage", errorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@uploadedAt",
            status == "done" ? DateTime.UtcNow.ToString("o") : (object)DBNull.Value);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Incrementa contador de retry
    /// </summary>
    public void IncrementRetryCount(long id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE video_queue SET retry_count = retry_count + 1 WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Obtém registros pendentes de upload
    /// </summary>
    public List<VideoRecord> GetPendingVideos(int limit = 10)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT * FROM video_queue
            WHERE status = 'pending'
            ORDER BY created_at ASC
            LIMIT @limit
        ";
        command.Parameters.AddWithValue("@limit", limit);

        var records = new List<VideoRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(MapVideoRecord(reader));
        }

        return records;
    }

    /// <summary>
    /// Obtém um registro por ID
    /// </summary>
    public VideoRecord? GetVideoRecord(long id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM video_queue WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return MapVideoRecord(reader);
        }

        return null;
    }

    /// <summary>
    /// Obtém estatísticas da fila
    /// </summary>
    public Dictionary<string, int> GetQueueStats()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT status, COUNT(*) as count
            FROM video_queue
            GROUP BY status
        ";

        var stats = new Dictionary<string, int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            stats[reader.GetString(0)] = reader.GetInt32(1);
        }

        return stats;
    }

    // ===== UPLOAD TASKS =====

    /// <summary>
    /// Cria uma task de upload para um vídeo
    /// </summary>
    public long CreateUploadTask(long videoRecordId, long totalBytes)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO upload_tasks
            (video_record_id, total_bytes, created_at)
            VALUES
            (@videoRecordId, @totalBytes, @createdAt);
            SELECT last_insert_rowid();
        ";

        command.Parameters.AddWithValue("@videoRecordId", videoRecordId);
        command.Parameters.AddWithValue("@totalBytes", totalBytes);
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));

        var id = (long?)command.ExecuteScalar();
        return id ?? 0;
    }

    /// <summary>
    /// Atualiza progresso do upload
    /// </summary>
    public void UpdateUploadProgress(long taskId, long bytesUploaded, int progress)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE upload_tasks
            SET bytes_uploaded = @bytesUploaded,
                progress = @progress,
                last_attempt_at = @lastAttempt
            WHERE id = @id
        ";

        command.Parameters.AddWithValue("@id", taskId);
        command.Parameters.AddWithValue("@bytesUploaded", bytesUploaded);
        command.Parameters.AddWithValue("@progress", progress);
        command.Parameters.AddWithValue("@lastAttempt", DateTime.UtcNow.ToString("o"));

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Mapeia SqliteDataReader para VideoRecord
    /// </summary>
    private VideoRecord MapVideoRecord(SqliteDataReader reader)
    {
        return new VideoRecord
        {
            Id = reader.GetInt64(0),
            FilePath = reader.GetString(1),
            SessionKey = reader.IsDBNull(2) ? null : reader.GetString(2),
            ProcessSnapshot = reader.IsDBNull(3) ? null : reader.GetString(3),
            Status = reader.GetString(4),
            CreatedAt = DateTime.Parse(reader.GetString(5)),
            UploadedAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
            RetryCount = reader.GetInt32(7),
            ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
            FileSizeBytes = reader.GetInt64(9)
        };
    }

    public void Dispose()
    {
        // SQLite connections são gerenciadas automaticamente pelo using
        GC.SuppressFinalize(this);
    }
}
