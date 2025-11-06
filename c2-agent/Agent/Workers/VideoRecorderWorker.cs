using Agent.Database;
using Agent.Database.Models;

namespace Agent.Workers;

/// <summary>
/// Worker responsável por gravação autônoma de vídeo
/// </summary>
public class VideoRecorderWorker
{
    private readonly FFmpegRecorder _recorder;
    private readonly DatabaseManager _database;
    private readonly CancellationTokenSource _cts;
    private Task? _workerTask;
    private bool _isRunning;

    // Configurações
    public int RecordingIntervalMinutes { get; set; } = 60; // Gravar a cada 60 minutos
    public int RecordingDurationMinutes { get; set; } = 60; // Duração de cada gravação
    public bool ContinuousMode { get; set; } = true; // Gravação contínua

    public VideoRecorderWorker(FFmpegRecorder recorder, DatabaseManager database)
    {
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
            Console.WriteLine("[VideoRecorderWorker] Já está rodando");
            return;
        }

        _isRunning = true;
        _workerTask = Task.Run(() => WorkerLoop(_cts.Token));
        Console.WriteLine("[VideoRecorderWorker] Worker iniciado");
    }

    /// <summary>
    /// Para o worker
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        Console.WriteLine("[VideoRecorderWorker] Parando worker...");
        _cts.Cancel();

        if (_workerTask != null)
        {
            await _workerTask;
        }

        // Se estiver gravando, parar a gravação
        if (_recorder.IsRecording)
        {
            await _recorder.StopRecording();
        }

        _isRunning = false;
        Console.WriteLine("[VideoRecorderWorker] Worker parado");
    }

    /// <summary>
    /// Loop principal do worker
    /// </summary>
    private async Task WorkerLoop(CancellationToken ct)
    {
        Console.WriteLine("[VideoRecorderWorker] Loop iniciado");

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
                Console.WriteLine("[VideoRecorderWorker] Worker cancelado");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VideoRecorderWorker] Erro no loop: {ex.Message}");
                // Aguardar um pouco antes de tentar novamente
                await Task.Delay(5000, ct);
            }
        }

        Console.WriteLine("[VideoRecorderWorker] Loop finalizado");
    }

    /// <summary>
    /// Grava sessão contínua (sem parar)
    /// </summary>
    private async Task RecordContinuousSession(CancellationToken ct)
    {
        Console.WriteLine("[VideoRecorderWorker] Iniciando gravação contínua...");

        // Capturar processos no início da sessão
        string processSnapshot = ProcessMonitor.CaptureAndSerialize();

        // Iniciar gravação (duração 0 = sem limite)
        string outputPath = await _recorder.StartRecording(0);

        // Extrair session key do path
        string? sessionKey = ExtractSessionKey(outputPath);

        // Inserir registro no banco (vídeos segmentados serão adicionados conforme são criados)
        var record = new VideoRecord
        {
            FilePath = outputPath,
            SessionKey = sessionKey,
            ProcessSnapshot = processSnapshot,
            Status = "recording",
            FileSizeBytes = 0
        };

        long recordId = _database.InsertVideoRecord(record);
        Console.WriteLine($"[VideoRecorderWorker] Registro criado: ID={recordId}");

        // Aguardar cancelamento (gravação contínua até ser parada)
        await Task.Delay(Timeout.Infinite, ct);
    }

    /// <summary>
    /// Grava sessão agendada (com duração e intervalo)
    /// </summary>
    private async Task RecordScheduledSession(CancellationToken ct)
    {
        Console.WriteLine($"[VideoRecorderWorker] Iniciando gravação agendada: {RecordingDurationMinutes}min");

        // Capturar processos no início da sessão
        string processSnapshot = ProcessMonitor.CaptureAndSerialize();

        // Iniciar gravação com duração limitada
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
            FileSizeBytes = 0 // Será atualizado depois
        };

        long recordId = _database.InsertVideoRecord(record);

        // Aguardar término da gravação
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

        Console.WriteLine($"[VideoRecorderWorker] Gravação concluída: ID={recordId}");

        // Aguardar intervalo até próxima gravação
        int waitMinutes = RecordingIntervalMinutes - RecordingDurationMinutes;
        if (waitMinutes > 0)
        {
            Console.WriteLine($"[VideoRecorderWorker] Aguardando {waitMinutes}min até próxima gravação");
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

            var record = new VideoRecord
            {
                FilePath = filePath,
                SessionKey = sessionKey,
                Status = "pending",
                FileSizeBytes = fileInfo.Length
            };

            long id = _database.InsertVideoRecord(record);
            Console.WriteLine($"[VideoRecorderWorker] Segmento registrado: {Path.GetFileName(filePath)} (ID={id})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VideoRecorderWorker] Erro ao registrar segmento: {ex.Message}");
        }
    }
}
