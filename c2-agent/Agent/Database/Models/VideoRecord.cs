namespace Agent.Database.Models;

/// <summary>
/// Representa um registro de vídeo na fila de processamento
/// </summary>
public class VideoRecord
{
    public long Id { get; set; }

    /// <summary>
    /// Caminho completo do arquivo de vídeo
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Chave da sessão (ex: session_1430)
    /// </summary>
    public string? SessionKey { get; set; }

    /// <summary>
    /// Snapshot de processos ativos no momento da gravação (JSON)
    /// </summary>
    public string? ProcessSnapshot { get; set; }

    /// <summary>
    /// Status: pending, uploading, done, error
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Data/hora de criação do registro
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Data/hora do upload (null se ainda não enviado)
    /// </summary>
    public DateTime? UploadedAt { get; set; }

    /// <summary>
    /// Número de tentativas de upload
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Última mensagem de erro (se houver)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Tamanho do arquivo em bytes
    /// </summary>
    public long FileSizeBytes { get; set; }
}
