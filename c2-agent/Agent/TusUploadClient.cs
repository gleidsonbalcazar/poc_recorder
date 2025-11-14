using System.Text;
using Agent.Database;
using Agent.Database.Models;
using TusDotNetClient;

namespace Agent;

/// <summary>
/// Cliente para upload via protocolo TUS (tusd). Implementa criação e PATCH com metadados base64.
/// Não bloqueante: projetado para ser chamado em Tasks pelo UploadWorker.
/// </summary>
    public class TusUploadClient : IDisposable
    {
    private readonly string _serverUrl; // ex: http://localhost:1080/files/
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;
    private readonly DatabaseManager _database;
    private readonly TusClient _client;

    public TusUploadClient(string serverUrl, int maxRetries, int retryDelayMs, DatabaseManager database)
    {
        _serverUrl = serverUrl.TrimEnd('/') + "/";
        _maxRetries = Math.Max(0, maxRetries);
        _retryDelayMs = Math.Max(0, retryDelayMs);
        _database = database;
        _client = new TusClient();
    }

    /// <summary>
    /// Faz upload via TUS de um registro (arquivo único ou diretório segmentado). Retorna sucesso/falha.
    /// </summary>
    public async Task<bool> UploadAsync(VideoRecord video, long uploadTaskId, CancellationToken ct = default)
    {
        try
        {
            if (Directory.Exists(video.FilePath))
            {
                var files = Directory.GetFiles(video.FilePath, "*.mp4", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f)
                    .ToArray();
                if (files.Length == 0)
                {
                    Console.WriteLine($"[TusUploadClient] Nenhum .mp4 em diretório: {video.FilePath}");
                    return false;
                }

                long total = files.Sum(f => new FileInfo(f).Length);
                long uploadedSoFar = 0;

                foreach (var file in files)
                {
                    bool ok = await UploadSingleFileAsync(
                        file,
                        video.ProcessSnapshot,
                        uploadTaskId,
                        baseOffset: uploadedSoFar,
                        totalBytes: total,
                        stationId: video.SessionKey ?? "unknown",
                        ct: ct);
                    if (!ok)
                        return false;
                    uploadedSoFar += new FileInfo(file).Length;
                }

                return true;
            }
            else if (File.Exists(video.FilePath))
            {
                return await UploadSingleFileAsync(
                    video.FilePath,
                    video.ProcessSnapshot,
                    uploadTaskId,
                    baseOffset: 0,
                    totalBytes: new FileInfo(video.FilePath).Length,
                    stationId: video.SessionKey ?? "unknown",
                    ct: ct);
            }
            else
            {
                Console.WriteLine($"[TusUploadClient] Caminho não encontrado: {video.FilePath}");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[TusUploadClient] Upload cancelado");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TusUploadClient] Erro: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> UploadSingleFileAsync(string filePath, string? processSnapshot, long uploadTaskId, long baseOffset, long totalBytes, string stationId, CancellationToken ct)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists || fi.Length == 0)
        {
            Console.WriteLine($"[TusUploadClient] Arquivo inválido: {filePath}");
            return false;
        }

        // Metadados no padrão tusd (o cliente aplica base64 internamente)
        // Usaremos chaves: filename, filetype, fileextension, stationId (processes removido por ser muito grande)
        string? fileUrl = null;
        Console.WriteLine($"[TusUploadClient] Criando upload no servidor TUS para: {fi.Name} ({fi.Length} bytes)");
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                fileUrl = await _client.CreateAsync(
                    _serverUrl,
                    fi.Length,
                    ("filename", fi.Name),
                    ("filetype", "video/mp4"),
                    ("fileextension", fi.Extension.TrimStart('.')),
                    ("stationId", stationId)
                );
                Console.WriteLine($"[TusUploadClient] Upload criado: {fileUrl}");
                break;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                Console.WriteLine($"[TusUploadClient] Erro na criação (tentativa {attempt + 1}/{_maxRetries}): {ex.Message}");
            }

            await Task.Delay(_retryDelayMs, ct);
        }

        if (string.IsNullOrEmpty(fileUrl))
            return false;

        // Upload com callback de progresso do cliente TUS (TusOperation)
        Console.WriteLine($"[TusUploadClient] Iniciando upload: {fi.Name}");
        var logLock = new object();
        int lastPercent = -1;
        var lastLog = DateTime.UtcNow;
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var op = _client.UploadAsync(fileUrl!, fi, cancellationToken: ct);
                op.Progressed += (transferred, total) =>
                {
                    try
                    {
                        long absolute = baseOffset + transferred;
                        int percent = totalBytes > 0 ? (int)((absolute * 100) / totalBytes) : 0;
                        _database.UpdateUploadProgress(uploadTaskId, absolute, percent);

                        // Logar progresso no console (a cada mudança de 10% ou 2s)
                        var now = DateTime.UtcNow;
                        bool shouldLog;
                        lock (logLock)
                        {
                            shouldLog = percent / 10 > lastPercent / 10 || (now - lastLog).TotalSeconds >= 2;
                            if (shouldLog)
                            {
                                lastPercent = percent;
                                lastLog = now;
                            }
                        }
                        if (shouldLog)
                        {
                            Console.WriteLine($"[TusUploadClient] {fi.Name}: {percent}% ({absolute}/{totalBytes} bytes)");
                        }
                    }
                    catch { }
                };
                await op;

                // Finaliza progresso do arquivo
                long totalNow = baseOffset + fi.Length;
                int finalPercent = totalBytes > 0 ? (int)((totalNow * 100) / totalBytes) : 100;
                _database.UpdateUploadProgress(uploadTaskId, totalNow, finalPercent);
                Console.WriteLine($"[TusUploadClient] Upload concluído: {fi.Name}");
                return true;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                Console.WriteLine($"[TusUploadClient] Erro no upload (tentativa {attempt + 1}/{_maxRetries}): {ex.Message}");
                await Task.Delay(_retryDelayMs, ct);
            }
        }

        return false;
    }

    private static string B64(string input)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
    }

    public void Dispose() { }
}
