using Agent.Database;
using Agent.Database.Models;
using Microsoft.Extensions.Logging;

namespace Agent.Workers;

/// <summary>
/// Worker responsável por processar fila de uploads
/// </summary>
public class UploadWorker
{
    private readonly ILogger<UploadWorker> _logger;
    private readonly DatabaseManager _database;
    private readonly CancellationTokenSource _cts;
    private Task? _workerTask;
    private bool _isRunning;
    private HttpUploadClient? _uploadClient;
    private TusUploadClient? _tusClient;

    // Configurações
    public int PollIntervalSeconds { get; set; } = 30; // Verificar fila a cada 30 segundos
    public int MaxConcurrentUploads { get; set; } = 2; // Máximo de uploads simultâneos
    public int MaxRetries { get; set; } = 3; // Máximo de tentativas por vídeo
    public string? UploadEndpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? TusServerUrl { get; set; }
    public int TusMaxRetries { get; set; } = 3;
    public int TusRetryDelayMs { get; set; } = 1000;

    public UploadWorker(DatabaseManager database, ILogger<UploadWorker> logger)
    {
        _logger = logger;
        _database = database;
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Inicia o worker
    /// </summary>
    public void Start()
    {
        if (_isRunning)
        {
            _logger.LogInformation("[UploadWorker] Já está rodando");
            return;
        }

        // Inicializar cliente de upload se endpoint estiver configurado
        if (!string.IsNullOrEmpty(UploadEndpoint))
        {
            _uploadClient = new HttpUploadClient(UploadEndpoint, ApiKey ?? "", _database);
            _logger.LogInformation($"[UploadWorker] Upload HTTP configurado: {UploadEndpoint}");
        }
        else
        {
            _logger.LogInformation("[UploadWorker] AVISO: Endpoint de upload não configurado, uploads serão simulados");
        }

        // Inicializar cliente TUS se configurado
        if (!string.IsNullOrWhiteSpace(TusServerUrl))
        {
            _tusClient = new TusUploadClient(TusServerUrl!, TusMaxRetries, TusRetryDelayMs, _database);
            _logger.LogInformation($"[UploadWorker] Upload TUS configurado: {TusServerUrl}");
        }

        _isRunning = true;
        _workerTask = Task.Run(() => WorkerLoop(_cts.Token));
        _logger.LogInformation("[UploadWorker] Worker iniciado");
    }

    /// <summary>
    /// Para o worker
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("[UploadWorker] Parando worker...");
        _cts.Cancel();

        if (_workerTask != null)
        {
            await _workerTask;
        }

        _uploadClient?.Dispose();
        _tusClient?.Dispose();
        _isRunning = false;
        _logger.LogInformation("[UploadWorker] Worker parado");
    }

    /// <summary>
    /// Update upload settings (hot-reload for instant changes)
    /// </summary>
    public void UpdateSettings(
        bool enabled,
        int pollIntervalSeconds,
        int maxConcurrentUploads,
        int maxRetries,
        string endpoint,
        string apiKey,
        string tusServerUrl,
        int tusMaxRetries,
        int tusRetryDelayMs)
    {
        _logger.LogInformation("[UploadWorker] Updating settings...");

        PollIntervalSeconds = pollIntervalSeconds;
        MaxConcurrentUploads = maxConcurrentUploads;
        MaxRetries = maxRetries;
        UploadEndpoint = endpoint;
        ApiKey = apiKey;
        TusServerUrl = tusServerUrl;
        TusMaxRetries = tusMaxRetries;
        TusRetryDelayMs = tusRetryDelayMs;

        // Reinitialize clients if endpoints changed
        if (!string.IsNullOrEmpty(endpoint) && _uploadClient == null)
        {
            _uploadClient?.Dispose();
            _uploadClient = new HttpUploadClient(endpoint, apiKey ?? "", _database);
            _logger.LogInformation("[UploadWorker] HTTP upload client reinitialized: {Endpoint}", endpoint);
        }

        if (!string.IsNullOrWhiteSpace(tusServerUrl) && _tusClient == null)
        {
            _tusClient?.Dispose();
            _tusClient = new TusUploadClient(tusServerUrl, tusMaxRetries, tusRetryDelayMs, _database);
            _logger.LogInformation("[UploadWorker] TUS upload client reinitialized: {TusServerUrl}", tusServerUrl);
        }

        _logger.LogInformation("[UploadWorker] Settings updated successfully");
    }

    /// <summary>
    /// Loop principal do worker
    /// </summary>
    private async Task WorkerLoop(CancellationToken ct)
    {
        _logger.LogInformation("[UploadWorker] Loop iniciado");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Obter vídeos pendentes
                var pendingVideos = _database.GetPendingVideos(MaxConcurrentUploads);

                if (pendingVideos.Count > 0)
                {
                    _logger.LogInformation($"[UploadWorker] {pendingVideos.Count} vídeo(s) pendente(s) de upload");

                    // Processar uploads em paralelo (com limite)
                    var uploadTasks = pendingVideos.Select(video => ProcessUpload(video, ct));
                    await Task.WhenAll(uploadTasks);
                }

                // Aguardar intervalo antes de verificar novamente
                await Task.Delay(PollIntervalSeconds * 1000, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[UploadWorker] Worker cancelado");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[UploadWorker] Erro no loop: {ex.Message}");
                await Task.Delay(5000, ct);
            }
        }

        _logger.LogInformation("[UploadWorker] Loop finalizado");
    }

    /// <summary>
    /// Processa upload de um vídeo
    /// </summary>
    private async Task ProcessUpload(VideoRecord video, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation($"[UploadWorker] Processando: {Path.GetFileName(video.FilePath)}");

            // Atualizar status para "uploading"
            _database.UpdateVideoStatus(video.Id, "uploading");

            // Validar arquivo antes de upload
            if (!ValidateVideoFile(video))
            {
                _database.UpdateVideoStatus(video.Id, "error", "Arquivo inválido ou corrompido");
                return;
            }

            // Criar task de upload
            long uploadTaskId = _database.CreateUploadTask(video.Id, video.FileSizeBytes);

            // Upload real se cliente estiver configurado, senão simular
            bool uploadSuccess;
                        if (_tusClient != null)
            {
                uploadSuccess = await _tusClient.UploadAsync(video, uploadTaskId, ct);
            }
            else if (_uploadClient != null)
            {
                uploadSuccess = await _uploadClient.UploadVideoAsync(video, uploadTaskId, ct);
            }
            else
            {
                _logger.LogInformation("[UploadWorker] AVISO: Simulando upload (endpoint não configurado)");
                uploadSuccess = await SimulateUpload(uploadTaskId, video, ct);
            }

            if (uploadSuccess)
            {
                _database.UpdateVideoStatus(video.Id, "done");
                _logger.LogInformation($"[UploadWorker] ✓ Upload concluído: {Path.GetFileName(video.FilePath)}");
            }
            else
            {
                // Incrementar retry count
                _database.IncrementRetryCount(video.Id);

                if (video.RetryCount + 1 >= MaxRetries)
                {
                    _database.UpdateVideoStatus(video.Id, "error", "Máximo de tentativas excedido");
                    _logger.LogInformation($"[UploadWorker] ✗ Falha permanente: {Path.GetFileName(video.FilePath)}");
                }
                else
                {
                    _database.UpdateVideoStatus(video.Id, "pending");
                    _logger.LogInformation($"[UploadWorker] ⚠ Tentativa {video.RetryCount + 1}/{MaxRetries} falhou, reprocessando...");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[UploadWorker] Erro ao processar upload: {ex.Message}");
            _database.UpdateVideoStatus(video.Id, "error", ex.Message);
        }
    }

    /// <summary>
    /// Valida arquivo de vídeo usando FFmpeg
    /// </summary>
    private bool ValidateVideoFile(VideoRecord video)
    {
        try
        {
            // Se for um diretório (segmentação), verificar arquivos dentro
            if (Directory.Exists(video.FilePath))
            {
                var files = Directory.GetFiles(video.FilePath, "*.mp4");
                if (files.Length == 0)
                {
                    _logger.LogInformation($"[UploadWorker] Nenhum arquivo .mp4 encontrado em: {video.FilePath}");
                    return false;
                }

                // Validar pelo menos o primeiro arquivo
                var firstFile = files[0];
                if (!File.Exists(firstFile))
                {
                    var msg1 = $"Arquivo n�o encontrado: {firstFile}";
                    _logger.LogInformation($"[UploadWorker] {msg1}");
                    throw new FileNotFoundException(msg1, firstFile);
                }
                var fi1 = new FileInfo(firstFile);
                if (fi1.Length <= 0)
                {
                    var msg2 = $"Arquivo vazio: {firstFile}";
                    _logger.LogInformation($"[UploadWorker] {msg2}");
                    throw new Exception(msg2);
                }
                return true;
            }

            // Arquivo único
            if (!File.Exists(video.FilePath))
            {
                _logger.LogInformation($"[UploadWorker] Arquivo não encontrado: {video.FilePath}");
                return false;
            }

            var fileInfo = new FileInfo(video.FilePath);
            if (fileInfo.Length == 0)
            {
                var msg = $"Arquivo vazio: {video.FilePath}";
                _logger.LogInformation($"[UploadWorker] {msg}");
                throw new Exception(msg);
            }

            // TODO: Adicionar validação com ffprobe para verificar integridade
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[UploadWorker] Erro ao validar arquivo: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Simula upload (substituir por implementação real)
    /// </summary>
    private async Task<bool> SimulateUpload(long uploadTaskId, VideoRecord video, CancellationToken ct)
    {
        try
        {
            // Simular progresso do upload
            for (int progress = 0; progress <= 100; progress += 20)
            {
                if (ct.IsCancellationRequested)
                    return false;

                long bytesUploaded = (video.FileSizeBytes * progress) / 100;
                _database.UpdateUploadProgress(uploadTaskId, bytesUploaded, progress);

                _logger.LogInformation($"[UploadWorker] Progresso: {progress}% ({Path.GetFileName(video.FilePath)})");

                // Simular tempo de upload (ajustar baseado no tamanho)
                await Task.Delay(500, ct);
            }

            return true; // Sucesso simulado
        }
        catch (Exception ex)
        {
            _logger.LogError($"[UploadWorker] Erro no upload simulado: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Obtém estatísticas da fila
    /// </summary>
    public Dictionary<string, int> GetQueueStats()
    {
        return _database.GetQueueStats();
    }

    /// <summary>
    /// Força reprocessamento de vídeos com erro
    /// </summary>
    public void RetryFailedVideos()
    {
        // TODO: Implementar query para resetar vídeos com status='error' para 'pending'
        _logger.LogInformation("[UploadWorker] Reprocessando vídeos com erro...");
    }
}






