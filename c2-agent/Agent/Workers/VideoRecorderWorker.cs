using Agent.Database;
using Agent.Database.Models;
using Agent.Recording;
using Agent.Utilities;
using Microsoft.Extensions.Logging;

namespace Agent.Workers;

/// <summary>
/// Worker respons�vel por grava��o aut�noma de v�deo
/// </summary>
public class VideoRecorderWorker
{
    private readonly ILogger<VideoRecorderWorker> _logger;
    private readonly FFmpegRecorder _recorder;
    private readonly DatabaseManager _database;
    private readonly CancellationTokenSource _cts;
    private Task? _workerTask;
    private bool _isRunning;
    private FileSystemWatcher? _segmentWatcher;
    private readonly HashSet<string> _registeredSegments = new();
    private string? _currentProcessSnapshot;

    // Configura��es
    public int RecordingIntervalMinutes { get; set; } = 60; // Gravar a cada 60 minutos
    public int RecordingDurationMinutes { get; set; } = 60; // Dura��o de cada grava��o
    public bool ContinuousMode { get; set; } = true; // Grava��o cont�nua

    public VideoRecorderWorker(FFmpegRecorder recorder, DatabaseManager database, ILogger<VideoRecorderWorker> logger)
    {
        _logger = logger;
        _recorder = recorder;
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
            Console.WriteLine("[VideoRecorderWorker] J� est� rodando");
            return;
        }

        _isRunning = true;
        _workerTask = Task.Run(() => WorkerLoop(_cts.Token));
        _logger.LogInformation("[VideoRecorderWorker] Worker iniciado");
    }

    /// <summary>
    /// Para o worker
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("[VideoRecorderWorker] Parando worker...");
        _cts.Cancel();

        if (_workerTask != null)
        {
            await _workerTask;
        }

        // Se estiver gravando, parar a grava��o
        if (_recorder.IsRecording)
        {
            await _recorder.StopRecording();
        }

        // Parar watcher de segmentos (se ativo)
        try { _segmentWatcher?.Dispose(); _segmentWatcher = null; } catch { }
        _currentProcessSnapshot = null;

        _isRunning = false;
        _logger.LogInformation("[VideoRecorderWorker] Worker parado");
    }

    /// <summary>
    /// Update recording intervals (hot-reload for instant changes)
    /// </summary>
    public void UpdateIntervals(int intervalMinutes, int durationMinutes)
    {
        _logger.LogInformation("[VideoRecorderWorker] Updating intervals: Interval={IntervalMinutes}min, Duration={DurationMinutes}min",
            intervalMinutes, durationMinutes);

        RecordingIntervalMinutes = intervalMinutes;
        RecordingDurationMinutes = durationMinutes;

        _logger.LogInformation("[VideoRecorderWorker] Intervals updated successfully");
    }

    /// <summary>
    /// Loop principal do worker
    /// </summary>
    private async Task WorkerLoop(CancellationToken ct)
    {
        _logger.LogInformation("[VideoRecorderWorker] Loop iniciado");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (ContinuousMode)
                {
                    await RecordContinuousSession(ct);
                }
                else
                {
                    await RecordScheduledSession(ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[VideoRecorderWorker] Worker cancelado");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("[VideoRecorderWorker] Erro no loop: {Message}", ex.Message);
                // Aguardar um pouco antes de tentar novamente
                await Task.Delay(5000, ct);
            }
        }

        _logger.LogInformation("[VideoRecorderWorker] Loop finalizado");
    }

    /// <summary>
    /// Grava sess�o cont�nua (sem parar)
    /// </summary>
    private async Task RecordContinuousSession(CancellationToken ct)
    {
        Console.WriteLine("[VideoRecorderWorker] Iniciando grava��o cont�nua...");

        // Capturar processos no in�cio da sess�o
        string processSnapshot = ProcessMonitor.CaptureAndSerialize();
        _currentProcessSnapshot = processSnapshot;

        // Iniciar grava��o (dura��o 0 = sem limite)
        string outputPath = await _recorder.StartRecording(0);

        // Extrair session key do path
        string? sessionKey = ExtractSessionKey(outputPath);

        // Inserir registro no banco (v�deos segmentados ser�o adicionados conforme s�o criados)
        var record = new VideoRecord
        {
            FilePath = outputPath,
            SessionKey = sessionKey,
            ProcessSnapshot = processSnapshot,
            Status = "recording",
            FileSizeBytes = 0
        };

        long recordId = _database.InsertVideoRecord(record);
        _logger.LogInformation("[VideoRecorderWorker] Sessão contínua iniciada (ID: {RecordId}, Session: {SessionKey})", recordId, sessionKey ?? "N/A");

        // Se segmenta??o estiver ativa, monitorar novos segmentos e enfileirar como 'pending'
        try
        {
            if (_recorder.SegmentSeconds > 0 && Directory.Exists(outputPath))
            {
                _logger.LogInformation("[VideoRecorderWorker] Segmentação ativa: {SegmentSeconds}s por arquivo", _recorder.SegmentSeconds);
                _segmentWatcher = new FileSystemWatcher(outputPath, "*.mp4")
                {
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
                };

                FileSystemEventHandler onCreatedOrChanged = async (s, e) =>
                {
                    var path = e.FullPath;
                    try
                    {
                        lock (_registeredSegments)
                        {
                            if (_registeredSegments.Contains(path)) return;
                        }

                        if (await WaitForFileStableAsync(path, ct))
                        {
                            lock (_registeredSegments)
                            {
                                if (_registeredSegments.Contains(path)) return;
                                _registeredSegments.Add(path);
                            }
                            if (!string.IsNullOrEmpty(sessionKey))
                            {
                                RegisterSegment(path, sessionKey);
                            }
                        }
                    }
                    catch { }
                };

                _segmentWatcher.Created += onCreatedOrChanged;
                _segmentWatcher.Changed += onCreatedOrChanged;

                // Processar quaisquer arquivos j? existentes
                try
                {
                    foreach (var file in Directory.GetFiles(outputPath, "*.mp4", SearchOption.TopDirectoryOnly))
                    {
                        if (await WaitForFileStableAsync(file, ct))
                        {
                            lock (_registeredSegments)
                            {
                                if (_registeredSegments.Contains(file)) continue;
                                _registeredSegments.Add(file);
                            }
                            if (!string.IsNullOrEmpty(sessionKey))
                            {
                                RegisterSegment(file, sessionKey);
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("[VideoRecorderWorker] Erro ao configurar FileSystemWatcher: {Message}", ex.Message);
        }

        // Aguardar cancelamento (grava��o cont�nua at� ser parada)
        await Task.Delay(Timeout.Infinite, ct);
    }

    /// <summary>
    /// Grava sess�o agendada (com dura��o e intervalo)
    /// </summary>
    private async Task RecordScheduledSession(CancellationToken ct)
    {
        _logger.LogInformation("[VideoRecorderWorker] Iniciando sessão agendada: {Duration}min de gravação a cada {Interval}min", RecordingDurationMinutes, RecordingIntervalMinutes);

        // Capturar processos no in�cio da sess�o
        string processSnapshot = ProcessMonitor.CaptureAndSerialize();
        _currentProcessSnapshot = processSnapshot;

        // Iniciar grava��o com dura��o limitada
        int durationSeconds = RecordingDurationMinutes * 60;
        string outputPath = await _recorder.StartRecording(durationSeconds);

        // Extrair session key
        string? sessionKey = ExtractSessionKey(outputPath);

        // Inserir registro no banco
        var record = new VideoRecord
        {
            FilePath = outputPath,
            SessionKey = sessionKey,
            ProcessSnapshot = processSnapshot,
            Status = "pending", // Pendente de upload
            FileSizeBytes = 0 // Ser� atualizado depois
        };

        long recordId = _database.InsertVideoRecord(record);

        // Aguardar t�rmino da grava��o
        await Task.Delay((durationSeconds + 5) * 1000, ct);

        // Atualizar tamanho do arquivo
        if (Directory.Exists(outputPath))
        {
            var files = Directory.GetFiles(outputPath, "*.mp4");
            long totalSize = files.Sum(f => new FileInfo(f).Length);

            var existingRecord = _database.GetVideoRecord(recordId);
            if (existingRecord != null)
            {
                existingRecord.FileSizeBytes = totalSize;
                _database.UpdateVideoStatus(recordId, "pending");
            }
        }

        _logger.LogInformation("[VideoRecorderWorker] Sessão agendada concluída (ID: {RecordId}, Duração: {Duration}min)", recordId, RecordingDurationMinutes);

        // Aguardar intervalo at� pr�xima grava��o
        int waitMinutes = RecordingIntervalMinutes - RecordingDurationMinutes;
        if (waitMinutes > 0)
        {
            _logger.LogInformation("[VideoRecorderWorker] Aguardando {WaitMinutes}min até próxima gravação...", waitMinutes);
            await Task.Delay(waitMinutes * 60 * 1000, ct);
        }
    }

    /// <summary>
    /// Extrai session key do path (ex: session_1430)
    /// </summary>
    private string? ExtractSessionKey(string path)
    {
        try
        {
            var dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            if (dirName?.StartsWith("session_") == true)
            {
                return dirName;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Registra segmento individual na fila
    /// </summary>
    public void RegisterSegment(string filePath, string sessionKey)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
                return;

            // Capturar snapshot de processos no momento do registro do segmento (per-segment)
            string processSnapshot = ProcessMonitor.CaptureAndSerialize();

            var record = new VideoRecord
            {
                FilePath = filePath,
                SessionKey = sessionKey,
                ProcessSnapshot = processSnapshot,
                Status = "pending",
                FileSizeBytes = fileInfo.Length
            };

            long id = _database.InsertVideoRecord(record);
            _logger.LogInformation("[VideoRecorderWorker] Segmento registrado (ID: {RecordId}, Arquivo: {FileName}, Tamanho: {SizeKB} KB)", id, Path.GetFileName(filePath), fileInfo.Length / 1024);
        }
        catch (Exception ex)
        {
            _logger.LogError("[VideoRecorderWorker] Erro ao registrar segmento: {Message}", ex.Message);
        }
    }

    private static async Task<bool> WaitForFileStableAsync(string path, CancellationToken ct, int checks = 3, int delayMs = 400)
    {
        try
        {
            long lastSize = -1;
            for (int i = 0; i < checks; i++)
            {
                ct.ThrowIfCancellationRequested();
                var fi = new FileInfo(path);
                if (!fi.Exists) return false;
                if (fi.Length > 0 && fi.Length == lastSize)
                {
                    return true;
                }
                lastSize = fi.Length;
                await Task.Delay(delayMs, ct);
            }
        }
        catch { }
        return false;
    }
}


