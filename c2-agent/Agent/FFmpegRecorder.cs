using System.Diagnostics;

namespace Agent;

/// <summary>
/// Gerencia gravação de vídeo da tela usando FFmpeg
/// </summary>
public class FFmpegRecorder : IDisposable
{
    private Process? _ffmpegProcess;
    private bool _isRecording;
    private string? _currentOutputPath;
    private readonly string _storageBasePath;
    private CancellationTokenSource? _periodicRecordingCts;
    private Task? _periodicRecordingTask;
    private Task? _autoStopTask;

    // Configurações padrão
    public int FPS { get; set; } = 20;
    public int VideoBitrate { get; set; } = 2000; // kbps
    public int AudioBitrate { get; set; } = 128; // kbps (não usado diretamente)
    public int VideoQuality { get; set; } = 28; // H.264 CRF (não usado no FFmpeg com preset)
    public bool CaptureAudio { get; set; } = true;
    public double MicrophoneVolume { get; set; } = 0.5; // Não usado (FFmpeg não tem controle direto)
    public double SystemAudioVolume { get; set; } = 0.5; // Não usado (FFmpeg não tem controle direto)
    public int PeriodicIntervalMinutes { get; set; } = 5;
    public int PeriodicDurationMinutes { get; set; } = 2;

    public bool IsRecording => _isRecording;
    public bool IsPeriodicRecordingActive => _periodicRecordingTask != null && !_periodicRecordingTask.IsCompleted;
    public string? CurrentRecordingPath => _isRecording ? _currentOutputPath : null;

    public FFmpegRecorder(string storageBasePath)
    {
        _storageBasePath = storageBasePath;

        // Criar diretório de armazenamento se não existir
        if (!Directory.Exists(_storageBasePath))
        {
            Directory.CreateDirectory(_storageBasePath);
        }

        // Verificar se FFmpeg está disponível
        if (!FFmpegHelper.IsFFmpegAvailable())
        {
            throw new FileNotFoundException(
                "FFmpeg não encontrado. Coloque ffmpeg.exe na pasta: " +
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg")
            );
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
            // Detectar dispositivos de áudio
            string? micDevice = null;
            string? stereoMixDevice = null;

            if (CaptureAudio)
            {
                micDevice = FFmpegHelper.DetectMicrophone();
                stereoMixDevice = FFmpegHelper.DetectStereoMix();

                if (micDevice == null && stereoMixDevice == null)
                {
                    Console.WriteLine("[FFmpegRecorder] ⚠️  Nenhum dispositivo de áudio detectado, gravando só vídeo");
                }
                else if (stereoMixDevice == null)
                {
                    Console.WriteLine("[FFmpegRecorder] ⚠️  Stereo Mix não detectado, gravando só microfone");
                }
            }

            // Construir argumentos de comando
            string arguments = FFmpegHelper.BuildRecordingArguments(
                _currentOutputPath,
                FPS,
                micDevice,
                stereoMixDevice,
                "ultrafast",
                VideoBitrate
            );

            // Se tem duração limitada, adicionar parâmetro -t
            if (durationSeconds > 0)
            {
                arguments = $"-t {durationSeconds} " + arguments;
            }

            Console.WriteLine($"[FFmpegRecorder] Iniciando gravação: {_currentOutputPath}");
            Console.WriteLine($"[FFmpegRecorder] Comando: ffmpeg {arguments}");

            // Iniciar processo FFmpeg
            var startInfo = new ProcessStartInfo
            {
                FileName = FFmpegHelper.GetFFmpegPath(),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            _ffmpegProcess = Process.Start(startInfo);

            if (_ffmpegProcess == null)
            {
                throw new Exception("Falha ao iniciar processo FFmpeg");
            }

            _isRecording = true;

            // Monitorar stderr do FFmpeg em background (para ver erros)
            _ = Task.Run(() => MonitorFFmpegOutput(_ffmpegProcess));

            // Se duração foi especificada, agendar parada automática
            if (durationSeconds > 0)
            {
                _autoStopTask = Task.Run(async () =>
                {
                    await Task.Delay((durationSeconds + 2) * 1000); // +2s de margem
                    if (_isRecording && _ffmpegProcess != null && !_ffmpegProcess.HasExited)
                    {
                        await StopRecording();
                    }
                });
            }

            return _currentOutputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FFmpegRecorder] Erro ao iniciar gravação: {ex.Message}");
            _isRecording = false;
            throw;
        }
    }

    /// <summary>
    /// Para a gravação atual
    /// </summary>
    public async Task<string> StopRecording()
    {
        if (!_isRecording || _ffmpegProcess == null)
        {
            throw new InvalidOperationException("Nenhuma gravação em andamento");
        }

        try
        {
            Console.WriteLine("[FFmpegRecorder] Parando gravação...");

            // Enviar 'q' para FFmpeg terminar gracefully
            try
            {
                if (!_ffmpegProcess.HasExited)
                {
                    _ffmpegProcess.StandardInput.WriteLine("q");
                    _ffmpegProcess.StandardInput.Flush();

                    // Aguardar até 3 segundos para processo terminar
                    bool exited = _ffmpegProcess.WaitForExit(3000);

                    if (!exited)
                    {
                        Console.WriteLine("[FFmpegRecorder] FFmpeg não terminou gracefully, forçando kill");
                        _ffmpegProcess.Kill();
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Processo já terminou
            }

            _isRecording = false;

            // Aguardar um pouco para garantir que o arquivo foi finalizado
            await Task.Delay(1000);

            string outputPath = _currentOutputPath ?? "unknown";
            Console.WriteLine($"[FFmpegRecorder] Gravação parada: {outputPath}");

            // Limpar processo
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;

            return outputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FFmpegRecorder] Erro ao parar gravação: {ex.Message}");
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

        Console.WriteLine($"[FFmpegRecorder] Gravação periódica iniciada: {PeriodicDurationMinutes}min a cada {PeriodicIntervalMinutes}min");
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

        Console.WriteLine("[FFmpegRecorder] Gravação periódica parada");
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
                // Iniciar gravação com duração limitada
                await StartRecording(PeriodicDurationMinutes * 60);

                // Aguardar duração da gravação + margem
                await Task.Delay((PeriodicDurationMinutes * 60 + 5) * 1000, ct);

                // Garantir que gravação foi parada
                if (_isRecording)
                {
                    await StopRecording();
                }

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
                Console.WriteLine($"[FFmpegRecorder] Erro na gravação periódica: {ex.Message}");
                // Aguardar um pouco antes de tentar novamente
                await Task.Delay(5000, ct);
            }
        }
    }

    /// <summary>
    /// Monitora saída do FFmpeg para detectar erros
    /// </summary>
    private async Task MonitorFFmpegOutput(Process process)
    {
        try
        {
            while (!process.HasExited)
            {
                string? line = await process.StandardError.ReadLineAsync();
                if (line != null && (line.Contains("error") || line.Contains("Error") || line.Contains("ERROR")))
                {
                    Console.WriteLine($"[FFmpegRecorder] FFmpeg error: {line}");
                }
            }

            int exitCode = process.ExitCode;
            if (exitCode != 0)
            {
                Console.WriteLine($"[FFmpegRecorder] FFmpeg terminou com código: {exitCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FFmpegRecorder] Erro ao monitorar FFmpeg: {ex.Message}");
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
            Console.WriteLine($"[FFmpegRecorder] Erro ao obter info do vídeo: {ex.Message}");
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
        if (_isRecording && _ffmpegProcess != null)
        {
            try
            {
                if (!_ffmpegProcess.HasExited)
                {
                    _ffmpegProcess.Kill();
                }
            }
            catch { }
        }

        _ffmpegProcess?.Dispose();
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
