using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Agent.Database;
using Agent.Database.Models;

namespace Agent.Upload;

/// <summary>
/// Cliente HTTP para upload de vídeos com progress tracking
/// </summary>
public class HttpUploadClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly DatabaseManager _database;

    public HttpUploadClient(string endpoint, string apiKey, DatabaseManager database)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _database = database;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30) // 30 minutos timeout
        };

        // Configurar headers
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
        }
    }

    /// <summary>
    /// Upload de vídeo individual com progress tracking
    /// </summary>
    public async Task<bool> UploadVideoAsync(VideoRecord video, long uploadTaskId, CancellationToken ct = default)
    {
        try
        {
            // Verificar se é arquivo único ou diretório com segmentos
            if (Directory.Exists(video.FilePath))
            {
                return await UploadSegmentedVideoAsync(video, uploadTaskId, ct);
            }
            else if (File.Exists(video.FilePath))
            {
                return await UploadSingleVideoAsync(video, uploadTaskId, ct);
            }
            else
            {
                Console.WriteLine($"[HttpUploadClient] Arquivo não encontrado: {video.FilePath}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HttpUploadClient] Erro no upload: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Upload de arquivo único
    /// </summary>
    private async Task<bool> UploadSingleVideoAsync(VideoRecord video, long uploadTaskId, CancellationToken ct)
    {
        try
        {
            var fileInfo = new FileInfo(video.FilePath);
            if (!fileInfo.Exists)
            {
                Console.WriteLine($"[HttpUploadClient] Arquivo não existe: {video.FilePath}");
                return false;
            }

            Console.WriteLine($"[HttpUploadClient] Uploading: {fileInfo.Name} ({FormatBytes(fileInfo.Length)})");

            using var content = new MultipartFormDataContent();

            // Adicionar arquivo
            var fileStream = File.OpenRead(video.FilePath);
            var streamContent = new ProgressStreamContent(
                fileStream,
                (bytesUploaded) => UpdateProgress(uploadTaskId, bytesUploaded, fileInfo.Length)
            );
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            content.Add(streamContent, "file", fileInfo.Name);

            // Adicionar metadados
            content.Add(new StringContent(video.SessionKey ?? "unknown"), "session_key");
            content.Add(new StringContent(video.CreatedAt.ToString("o")), "created_at");

            if (!string.IsNullOrEmpty(video.ProcessSnapshot))
            {
                content.Add(new StringContent(video.ProcessSnapshot), "process_snapshot");
            }

            // Enviar request
            var response = await _httpClient.PostAsync(_endpoint, content, ct);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[HttpUploadClient] ✓ Upload concluído: {fileInfo.Name}");
                return true;
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[HttpUploadClient] ✗ Upload falhou: {response.StatusCode}");
                Console.WriteLine($"[HttpUploadClient] Response: {errorBody}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HttpUploadClient] Erro no upload: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Upload de vídeo segmentado (múltiplos arquivos)
    /// </summary>
    private async Task<bool> UploadSegmentedVideoAsync(VideoRecord video, long uploadTaskId, CancellationToken ct)
    {
        try
        {
            var directory = new DirectoryInfo(video.FilePath);
            var videoFiles = directory.GetFiles("*.mp4").OrderBy(f => f.Name).ToArray();

            if (videoFiles.Length == 0)
            {
                Console.WriteLine($"[HttpUploadClient] Nenhum arquivo .mp4 encontrado em: {video.FilePath}");
                return false;
            }

            long totalBytes = videoFiles.Sum(f => f.Length);
            long uploadedBytes = 0;

            Console.WriteLine($"[HttpUploadClient] Uploading segmented video: {videoFiles.Length} files ({FormatBytes(totalBytes)})");

            using var content = new MultipartFormDataContent();

            // Adicionar metadados da sessão
            content.Add(new StringContent(video.SessionKey ?? "unknown"), "session_key");
            content.Add(new StringContent(video.CreatedAt.ToString("o")), "created_at");
            content.Add(new StringContent(videoFiles.Length.ToString()), "segment_count");

            if (!string.IsNullOrEmpty(video.ProcessSnapshot))
            {
                content.Add(new StringContent(video.ProcessSnapshot), "process_snapshot");
            }

            // Adicionar todos os segmentos
            int segmentIndex = 0;
            foreach (var file in videoFiles)
            {
                var fileStream = File.OpenRead(file.FullName);
                var streamContent = new ProgressStreamContent(
                    fileStream,
                    (bytesRead) =>
                    {
                        long currentTotal = uploadedBytes + bytesRead;
                        UpdateProgress(uploadTaskId, currentTotal, totalBytes);
                    }
                );
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
                content.Add(streamContent, $"segment_{segmentIndex}", file.Name);

                uploadedBytes += file.Length;
                segmentIndex++;
            }

            // Enviar request
            var response = await _httpClient.PostAsync(_endpoint, content, ct);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[HttpUploadClient] ✓ Upload de sessão concluído: {videoFiles.Length} segmentos");
                return true;
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[HttpUploadClient] ✗ Upload de sessão falhou: {response.StatusCode}");
                Console.WriteLine($"[HttpUploadClient] Response: {errorBody}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HttpUploadClient] Erro no upload segmentado: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Atualiza progresso no banco de dados
    /// </summary>
    private void UpdateProgress(long uploadTaskId, long bytesUploaded, long totalBytes)
    {
        int progress = totalBytes > 0 ? (int)((bytesUploaded * 100) / totalBytes) : 0;
        _database.UpdateUploadProgress(uploadTaskId, bytesUploaded, progress);
    }

    /// <summary>
    /// Formata bytes em formato legível
    /// </summary>
    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// HttpContent wrapper para tracking de progresso
/// </summary>
internal class ProgressStreamContent : HttpContent
{
    private const int BufferSize = 8192;
    private readonly Stream _content;
    private readonly Action<long> _progressCallback;

    public ProgressStreamContent(Stream content, Action<long> progressCallback)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _progressCallback = progressCallback;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await _content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;
            _progressCallback?.Invoke(totalBytesRead);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _content.Length;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content?.Dispose();
        }
        base.Dispose(disposing);
    }
}
