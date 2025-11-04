using ScreenRecorderLib;
using System.Diagnostics;

namespace Agent;

/// <summary>
/// Gerencia gravação de vídeo da tela usando ScreenRecorderLib
/// </summary>
public class VideoRecorder : IDisposable
{
    private Recorder? _recorder;
    private bool _isRecording;
    private string? _currentOutputPath;
    private readonly string _storageBasePath;
    private CancellationTokenSource? _periodicRecordingCts;
    private Task? _periodicRecordingTask;

    // Configurações padrão
    public int FPS { get; set; } = 20;
    public int VideoBitrate { get; set; } = 2000; // kbps
    public int AudioBitrate { get; set; } = 128; // kbps
    public int VideoQuality { get; set; } = 28; // H.264 CRF (0-51, menor = melhor qualidade)
    public bool CaptureAudio { get; set; } = true;
    public double MicrophoneVolume { get; set; } = 0.5; // Volume do microfone (0.5 = 50% para evitar clipping)
    public double SystemAudioVolume { get; set; } = 0.5; // Volume do áudio do sistema (0.5 = 50% para evitar clipping)
    public int PeriodicIntervalMinutes { get; set; } = 5; // Intervalo padrão para gravação periódica
    public int PeriodicDurationMinutes { get; set; } = 2; // Duração padrão de cada clip periódico

    public bool IsRecording => _isRecording;
    public bool IsPeriodicRecordingActive => _periodicRecordingTask != null && !_periodicRecordingTask.IsCompleted;
    public string? CurrentRecordingPath => _isRecording ? _currentOutputPath : null;

    public VideoRecorder(string storageBasePath)
    {
        _storageBasePath = storageBasePath;

        // Criar diretório de armazenamento se não existir
        if (!Directory.Exists(_storageBasePath))
        {
            Directory.CreateDirectory(_storageBasePath);
        }
    }

    /// <summary>
    /// Inicia gravação de vídeo da tela
    /// </summary>
    /// <param name="durationSeconds">Duração da gravação em segundos (0 = manual stop)</param>
    /// <returns>Caminho do arquivo de saída</returns>
    public async Task<string> StartRecording(int durationSeconds = 0)
    {
        if (_isRecording)
        {
            throw new InvalidOperationException("Gravação já está em andamento");
        }

        // Gerar nome do arquivo com timestamp
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"screen_{timestamp}.mp4";
        string dateFolder = Path.Combine(_storageBasePath, "videos", DateTime.Now.ToString("yyyy-MM-dd"));

        // Criar pasta da data se não existir
        if (!Directory.Exists(dateFolder))
        {
            Directory.CreateDirectory(dateFolder);
        }

        _currentOutputPath = Path.Combine(dateFolder, fileName);

        try
        {
            // Configurar opções de gravação
            var options = new RecorderOptions
            {
                VideoEncoderOptions = new VideoEncoderOptions
                {
                    Bitrate = VideoBitrate * 1000, // Converter para bps
                    Framerate = FPS,
                    IsFixedFramerate = false,
                    Encoder = new H264VideoEncoder
                    {
                        BitrateMode = H264BitrateControlMode.UnconstrainedVBR
                    }
                },
                AudioOptions = new AudioOptions
                {
                    IsAudioEnabled = CaptureAudio,
                    IsOutputDeviceEnabled = CaptureAudio, // Áudio do sistema (som da tela/apps)
                    IsInputDeviceEnabled = CaptureAudio, // Áudio do microfone
                    OutputVolume = (float)SystemAudioVolume, // Volume do sistema (0.5 = 50%)
                    InputVolume = (float)MicrophoneVolume, // Volume do microfone (0.5 = 50%)
                    Bitrate = ScreenRecorderLib.AudioBitrate.bitrate_128kbps,
                    Channels = ScreenRecorderLib.AudioChannels.Stereo
                },
                OutputOptions = new OutputOptions
                {
                    RecorderMode = RecorderMode.Video
                },
                LogOptions = new LogOptions
                {
                    IsLogEnabled = false // Desabilitar logs para evitar overhead
                }
            };

            _recorder = Recorder.CreateRecorder(options);

            // Configurar callback de erro
            _recorder.OnRecordingFailed += (sender, args) =>
            {
                Console.WriteLine($"[VideoRecorder] Erro na gravação: {args.Error}");
                _isRecording = false;
            };

            // Configurar callback de conclusão
            _recorder.OnRecordingComplete += (sender, args) =>
            {
                Console.WriteLine($"[VideoRecorder] Gravação concluída: {args.FilePath}");
                _isRecording = false;
            };

            // Iniciar gravação
            _recorder.Record(_currentOutputPath);
            _isRecording = true;

            Console.WriteLine($"[VideoRecorder] Gravação iniciada: {_currentOutputPath}");

            // Se duração foi especificada, agendar parada automática
            if (durationSeconds > 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(durationSeconds * 1000);
                    if (_isRecording)
                    {
                        await StopRecording();
                    }
                });
            }

            return _currentOutputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VideoRecorder] Erro ao iniciar gravação: {ex.Message}");
            _isRecording = false;
            throw;
        }
    }

    /// <summary>
    /// Para a gravação atual
    /// </summary>
    /// <returns>Caminho do arquivo gravado</returns>
    public async Task<string> StopRecording()
    {
        if (!_isRecording || _recorder == null)
        {
            throw new InvalidOperationException("Nenhuma gravação em andamento");
        }

        try
        {
            _recorder.Stop();
            _isRecording = false;

            // Aguardar um pouco para garantir que o arquivo foi finalizado
            await Task.Delay(500);

            string outputPath = _currentOutputPath ?? "unknown";
            Console.WriteLine($"[VideoRecorder] Gravação parada: {outputPath}");

            // Limpar recorder
            _recorder.Dispose();
            _recorder = null;

            return outputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VideoRecorder] Erro ao parar gravação: {ex.Message}");
            _isRecording = false;
            throw;
        }
    }

    /// <summary>
    /// Inicia gravação periódica automática
    /// </summary>
    public void StartPeriodicRecording()
    {
        if (IsPeriodicRecordingActive)
        {
            throw new InvalidOperationException("Gravação periódica já está ativa");
        }

        _periodicRecordingCts = new CancellationTokenSource();
        _periodicRecordingTask = Task.Run(async () => await PeriodicRecordingLoop(_periodicRecordingCts.Token));

        Console.WriteLine($"[VideoRecorder] Gravação periódica iniciada: {PeriodicDurationMinutes}min a cada {PeriodicIntervalMinutes}min");
    }

    /// <summary>
    /// Para a gravação periódica automática
    /// </summary>
    public async Task StopPeriodicRecording()
    {
        if (!IsPeriodicRecordingActive)
        {
            throw new InvalidOperationException("Gravação periódica não está ativa");
        }

        _periodicRecordingCts?.Cancel();

        // Se estiver gravando, parar a gravação atual
        if (_isRecording)
        {
            await StopRecording();
        }

        // Aguardar task finalizar
        if (_periodicRecordingTask != null)
        {
            await _periodicRecordingTask;
        }

        _periodicRecordingCts?.Dispose();
        _periodicRecordingCts = null;
        _periodicRecordingTask = null;

        Console.WriteLine("[VideoRecorder] Gravação periódica parada");
    }

    /// <summary>
    /// Loop de gravação periódica
    /// </summary>
    private async Task PeriodicRecordingLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Iniciar gravação
                await StartRecording(PeriodicDurationMinutes * 60);

                // Aguardar duração da gravação
                await Task.Delay(PeriodicDurationMinutes * 60 * 1000, ct);

                // Aguardar intervalo antes da próxima gravação
                int remainingInterval = PeriodicIntervalMinutes - PeriodicDurationMinutes;
                if (remainingInterval > 0)
                {
                    await Task.Delay(remainingInterval * 60 * 1000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelamento normal
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VideoRecorder] Erro na gravação periódica: {ex.Message}");
                // Aguardar um pouco antes de tentar novamente
                await Task.Delay(5000, ct);
            }
        }
    }

    /// <summary>
    /// Obtém informações sobre o arquivo de vídeo
    /// </summary>
    public VideoInfo? GetVideoInfo(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var fileInfo = new FileInfo(filePath);

            return new VideoInfo
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                SizeBytes = fileInfo.Length,
                SizeMB = Math.Round(fileInfo.Length / (1024.0 * 1024.0), 2),
                CreatedAt = fileInfo.CreationTime,
                DurationEstimateMinutes = EstimateVideoDuration(fileInfo.Length)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VideoRecorder] Erro ao obter info do vídeo: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Estima duração do vídeo baseado no tamanho do arquivo
    /// </summary>
    private double EstimateVideoDuration(long fileSizeBytes)
    {
        // Estimativa aproximada: bitrate total em MB/min
        double totalBitrateMbps = (VideoBitrate + AudioBitrate) / 1000.0;
        double totalBitrateMBPerMin = (totalBitrateMbps * 60) / 8;
        double fileSizeMB = fileSizeBytes / (1024.0 * 1024.0);

        return Math.Round(fileSizeMB / totalBitrateMBPerMin, 2);
    }

    public void Dispose()
    {
        if (_isRecording && _recorder != null)
        {
            try
            {
                _recorder.Stop();
            }
            catch { }
        }

        _recorder?.Dispose();
        _periodicRecordingCts?.Cancel();
        _periodicRecordingCts?.Dispose();
    }
}

/// <summary>
/// Informações sobre um arquivo de vídeo
/// </summary>
public class VideoInfo
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public double SizeMB { get; set; }
    public DateTime CreatedAt { get; set; }
    public double DurationEstimateMinutes { get; set; }
}
