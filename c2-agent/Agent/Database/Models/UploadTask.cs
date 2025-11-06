namespace Agent.Database.Models;

/// <summary>
/// Representa uma tarefa de upload na fila
/// </summary>
public class UploadTask
{
    public long Id { get; set; }

    /// <summary>
    /// ID do VideoRecord associado
    /// </summary>
    public long VideoRecordId { get; set; }

    /// <summary>
    /// URL de destino do upload
    /// </summary>
    public string? UploadUrl { get; set; }

    /// <summary>
    /// Status: pending, in_progress, completed, failed
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Progresso do upload (0-100)
    /// </summary>
    public int Progress { get; set; } = 0;

    /// <summary>
    /// Bytes enviados
    /// </summary>
    public long BytesUploaded { get; set; } = 0;

    /// <summary>
    /// Total de bytes a enviar
    /// </summary>
    public long TotalBytes { get; set; } = 0;

    /// <summary>
    /// Número de tentativas realizadas
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Máximo de tentativas permitidas
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Última mensagem de erro
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Data/hora da última tentativa
    /// </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// Data/hora da próxima tentativa (para retry com backoff)
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Data/hora de criação
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Data/hora de conclusão
    /// </summary>
    public DateTime? CompletedAt { get; set; }
}
