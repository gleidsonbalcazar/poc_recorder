using Agent.Database;
using Agent.Database.Models;

namespace Agent.Workers;

/// <summary>
/// Worker responsável por processar fila de uploads
/// </summary>
public class UploadWorker
{
    private readonly DatabaseManager _database;
    private readonly CancellationTokenSource _cts;
    private Task? _workerTask;
    private bool _isRunning;

    // Configurações
    public int PollIntervalSeconds { get; set; } = 30; // Verificar fila a cada 30 segundos
    public int MaxConcurrentUploads { get; set; } = 2; // Máximo de uploads simultâneos
    public int MaxRetries { get; set; } = 3; // Máximo de tentativas por vídeo

    public UploadWorker(DatabaseManager database)
    {
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
            Console.WriteLine("[UploadWorker] Já está rodando");
            return;
        }

        _isRunning = true;
        _workerTask = Task.Run(() => WorkerLoop(_cts.Token));
        Console.WriteLine("[UploadWorker] Worker iniciado");
    }

    /// <summary>
    /// Para o worker
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        Console.WriteLine("[UploadWorker] Parando worker...");
        _cts.Cancel();

        if (_workerTask != null)
        {
            await _workerTask;
        }

        _isRunning = false;
        Console.WriteLine("[UploadWorker] Worker parado");
    }

    /// <summary>
    /// Loop principal do worker
    /// </summary>
    private async Task WorkerLoop(CancellationToken ct)
    {
        Console.WriteLine("[UploadWorker] Loop iniciado");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Obter vídeos pendentes
                var pendingVideos = _database.GetPendingVideos(MaxConcurrentUploads);

                if (pendingVideos.Count > 0)
                {
                    Console.WriteLine($"[UploadWorker] {pendingVideos.Count} vídeo(s) pendente(s) de upload");

                    // Processar uploads em paralelo (com limite)
                    var uploadTasks = pendingVideos.Select(video => ProcessUpload(video, ct));
                    await Task.WhenAll(uploadTasks);
                }

                // Aguardar intervalo antes de verificar novamente
                await Task.Delay(PollIntervalSeconds * 1000, ct);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[UploadWorker] Worker cancelado");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UploadWorker] Erro no loop: {ex.Message}");
                await Task.Delay(5000, ct);
            }
        }

        Console.WriteLine("[UploadWorker] Loop finalizado");
    }

    /// <summary>
    /// Processa upload de um vídeo
    /// </summary>
    private async Task ProcessUpload(VideoRecord video, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[UploadWorker] Processando: {Path.GetFileName(video.FilePath)}");

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

            // TODO: Implementar upload real aqui
            // Por enquanto, simular upload com delay
            bool uploadSuccess = await SimulateUpload(uploadTaskId, video, ct);

            if (uploadSuccess)
            {
                _database.UpdateVideoStatus(video.Id, "done");
                Console.WriteLine($"[UploadWorker] ✓ Upload concluído: {Path.GetFileName(video.FilePath)}");
            }
            else
            {
                // Incrementar retry count
                _database.IncrementRetryCount(video.Id);

                if (video.RetryCount + 1 >= MaxRetries)
                {
                    _database.UpdateVideoStatus(video.Id, "error", "Máximo de tentativas excedido");
                    Console.WriteLine($"[UploadWorker] ✗ Falha permanente: {Path.GetFileName(video.FilePath)}");
                }
                else
                {
                    _database.UpdateVideoStatus(video.Id, "pending");
                    Console.WriteLine($"[UploadWorker] ⚠ Tentativa {video.RetryCount + 1}/{MaxRetries} falhou, reprocessando...");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UploadWorker] Erro ao processar upload: {ex.Message}");
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
                    Console.WriteLine($"[UploadWorker] Nenhum arquivo .mp4 encontrado em: {video.FilePath}");
                    return false;
                }

                // Validar pelo menos o primeiro arquivo
                var firstFile = files[0];
                return File.Exists(firstFile) && new FileInfo(firstFile).Length > 0;
            }

            // Arquivo único
            if (!File.Exists(video.FilePath))
            {
                Console.WriteLine($"[UploadWorker] Arquivo não encontrado: {video.FilePath}");
                return false;
            }

            var fileInfo = new FileInfo(video.FilePath);
            if (fileInfo.Length == 0)
            {
                Console.WriteLine($"[UploadWorker] Arquivo vazio: {video.FilePath}");
                return false;
            }

            // TODO: Adicionar validação com ffprobe para verificar integridade
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UploadWorker] Erro ao validar arquivo: {ex.Message}");
            return false;
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

                Console.WriteLine($"[UploadWorker] Progresso: {progress}% ({Path.GetFileName(video.FilePath)})");

                // Simular tempo de upload (ajustar baseado no tamanho)
                await Task.Delay(500, ct);
            }

            return true; // Sucesso simulado
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UploadWorker] Erro no upload simulado: {ex.Message}");
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
        Console.WriteLine("[UploadWorker] Reprocessando vídeos com erro...");
    }
}
