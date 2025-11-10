using System.Diagnostics;

namespace Agent;

/// <summary>
/// Gerencia gravação de vídeo da tela usando FFmpeg
/// </summary>
public class FFmpegRecorder : IDisposable
{
    private Process? _ffmpegProcess;
    private AudioManager? _audioManager;
    private bool _isRecording;
    private string? _currentOutputPath;
    private string? _currentOutputPattern; // Pattern para segmentação (ex: screen_%Y%m%d_%H%M%S.mp4)
    private readonly string _storageBasePath;
    private CancellationTokenSource? _periodicRecordingCts;
    private Task? _periodicRecordingTask;
    private Task? _autoStopTask;
    private ProcessJobObject? _ffmpegJobObject;

    // Configurações padrão
    public int FPS { get; set; } = 30; // Aumentado de 20 para 30 (melhor qualidade)
    public int VideoBitrate { get; set; } = 2000; // kbps
    public int AudioBitrate { get; set; } = 128; // kbps (não usado diretamente)
    public int VideoQuality { get; set; } = 28; // H.264 CRF (não usado no FFmpeg com preset)
    public bool CaptureAudio { get; set; } = true;
    public double MicrophoneVolume { get; set; } = 0.5; // Não usado (FFmpeg não tem controle direto)
    public double SystemAudioVolume { get; set; } = 0.5; // Não usado (FFmpeg não tem controle direto)
    public int SegmentSeconds { get; set; } = 30; // Duração de cada segmento (0 = sem segmentação)
    public string? PreferredMicName { get; set; } = null; // Nome preferido do microfone (matching parcial)
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

        // Garantir que FFmpeg está disponível (baixar automaticamente se necessário)
        Console.WriteLine("[FFmpegRecorder] Verificando FFmpeg...");
        FFmpegHelper.EnsureFFmpegAvailable().Wait();
        Console.WriteLine("[FFmpegRecorder] FFmpeg OK, pronto para gravar");
    }

    /// <summary>
    /// Inicia gravação de vídeo da tela com áudio via NAudio + Named Pipe
    /// </summary>
    /// <param name="durationSeconds">Duração da gravação em segundos (0 = manual stop)</param>
    /// <returns>Caminho do arquivo de saída (ou pattern se segmentação ativa)</returns>
    public async Task<string> StartRecording(int durationSeconds = 0)
    {
        if (_isRecording)
        {
            throw new InvalidOperationException("Gravação já está em andamento");
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[SYNC] ===== INICIANDO GRAVAÇÃO =====");
            Console.WriteLine($"[SYNC] T+{sw.ElapsedMilliseconds}ms: Início do StartRecording()");

            // Criar pasta da data
            string dateFolder = Path.Combine(_storageBasePath, "videos", DateTime.Now.ToString("yyyy-MM-dd"));
            if (!Directory.Exists(dateFolder))
            {
                Directory.CreateDirectory(dateFolder);
            }

            // Criar pasta da sessão dentro da pasta da data
            string sessionFolder = Path.Combine(dateFolder, $"session_{DateTime.Now:HHmm}");
            if (!Directory.Exists(sessionFolder))
            {
                Directory.CreateDirectory(sessionFolder);
            }

            // Determinar output (arquivo único ou pattern para segmentação)
            if (SegmentSeconds > 0)
            {
                // Segmentação: usar pattern com strftime
                _currentOutputPattern = Path.Combine(sessionFolder, "screen_%Y%m%d_%H%M%S.mp4");
                _currentOutputPath = sessionFolder; // Diretório onde segmentos serão salvos
                Console.WriteLine($"[FFmpegRecorder] Modo segmentação: {SegmentSeconds}s por arquivo");
                Console.WriteLine($"[FFmpegRecorder] Pattern: {_currentOutputPattern}");
            }
            else
            {
                // Arquivo único
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"screen_{timestamp}.mp4";
                _currentOutputPath = Path.Combine(sessionFolder, fileName);
                _currentOutputPattern = null;
                Console.WriteLine($"[FFmpegRecorder] Modo arquivo único: {_currentOutputPath}");
            }

            // [FASE 1] Iniciar AudioManager (captura áudio via NAudio)
            if (CaptureAudio)
            {
                Console.WriteLine($"[SYNC] T+{sw.ElapsedMilliseconds}ms: Iniciando AudioManager...");
                _audioManager = new AudioManager(PreferredMicName);
                await _audioManager.StartAsync();
                Console.WriteLine($"[SYNC] T+{sw.ElapsedMilliseconds}ms: ✓ AudioManager iniciado (pipe: {_audioManager.FullPipePath})");
                Console.WriteLine($"[SYNC] ⚠️  ÁUDIO COMEÇOU A CAPTURAR (aguardando FFmpeg conectar...)");
            }

            // [FASE 2] Construir argumentos FFmpeg com Named Pipe
            string outputForFFmpeg = SegmentSeconds > 0 ? _currentOutputPattern! : _currentOutputPath!;

            string arguments = FFmpegHelper.BuildRecordingArgumentsWithPipe(
                outputForFFmpeg,
                _audioManager?.FullPipePath ?? "\\\\.\\pipe\\C2Agent_Audio",
                FPS,
                SegmentSeconds,
                "ultrafast",
                VideoBitrate
            );

            // Se duração limitada (e SEM segmentação), adicionar -t
            if (durationSeconds > 0 && SegmentSeconds == 0)
            {
                arguments = $"-t {durationSeconds} " + arguments;
            }

            Console.WriteLine($"[SYNC] T+{sw.ElapsedMilliseconds}ms: Iniciando processo FFmpeg...");
            Console.WriteLine($"[FFmpegRecorder] Comando: ffmpeg {arguments}");

            // [FASE 3] Iniciar processo FFmpeg
            var startInfo = new ProcessStartInfo
            {
                FileName = FFmpegHelper.GetFFmpegPath(),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            _ffmpegProcess = Process.Start(startInfo);

            if (_ffmpegProcess == null)
            {
                throw new Exception("Falha ao iniciar processo FFmpeg");
            }

            // Assign FFmpeg to Job Object for automatic cleanup on Agent exit
            _ffmpegJobObject = ProcessJobObject.CreateAndAssign(_ffmpegProcess);

            if (_ffmpegJobObject == null)
            {
                Console.WriteLine("[FFmpegRecorder] ⚠ Warning: Failed to assign FFmpeg to Job Object - process may become orphaned if Agent crashes");
            }

            _isRecording = true;
            Console.WriteLine($"[SYNC] T+{sw.ElapsedMilliseconds}ms: ✓ Processo FFmpeg iniciado (PID: {_ffmpegProcess.Id})");
            Console.WriteLine($"[SYNC] FFmpeg agora vai conectar ao pipe de áudio e iniciar captura de vídeo...");
            Console.WriteLine($"[SYNC] ===== GRAVAÇÃO EM ANDAMENTO =====");

            // Monitorar stderr do FFmpeg em background
            _ = Task.Run(() => MonitorFFmpegOutput(_ffmpegProcess));

            // Auto-stop se duração especificada
            if (durationSeconds > 0)
            {
                _autoStopTask = Task.Run(async () =>
                {
                    await Task.Delay((durationSeconds + 2) * 1000);
                    if (_isRecording && _ffmpegProcess != null && !_ffmpegProcess.HasExited)
                    {
                        await StopRecording();
                    }
                });
            }

            return _currentOutputPath!;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FFmpegRecorder] Erro ao iniciar gravação: {ex.Message}");
            _isRecording = false;

            // Cleanup em caso de erro
            if (_audioManager != null)
            {
                try { await _audioManager.DisposeAsync(); } catch { }
                _audioManager = null;
            }

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
                        _ffmpegProcess.WaitForExit(); // Aguardar kill completar
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                // Pode ocorrer se processo já terminou ou StandardInput não disponível
                Console.WriteLine($"[FFmpegRecorder] Aviso ao enviar 'q': {ex.Message}");

                // Tentar kill como fallback
                try
                {
                    if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                    {
                        Console.WriteLine("[FFmpegRecorder] Forçando kill como fallback...");
                        _ffmpegProcess.Kill();
                        _ffmpegProcess.WaitForExit();
                    }
                }
                catch (Exception killEx)
                {
                    Console.WriteLine($"[FFmpegRecorder] Erro ao tentar kill: {killEx.Message}");
                }
            }

            _isRecording = false;

            // Parar AudioManager
            if (_audioManager != null)
            {
                Console.WriteLine("[FFmpegRecorder] Parando AudioManager...");
                try
                {
                    await _audioManager.StopAsync();
                    await _audioManager.DisposeAsync();
                    _audioManager = null;
                    Console.WriteLine("[FFmpegRecorder] ✓ AudioManager parado");
                }
                catch (Exception audioEx)
                {
                    Console.WriteLine($"[FFmpegRecorder] Erro ao parar AudioManager: {audioEx.Message}");
                }
            }

            // Aguardar um pouco para garantir que o arquivo foi finalizado
            Console.WriteLine("[FFmpegRecorder] Aguardando finalização do arquivo...");
            await Task.Delay(2000);

            string outputPath = _currentOutputPath ?? "unknown";

            // Limpar processo
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;

            // Cleanup Job Object
            _ffmpegJobObject?.Dispose();
            _ffmpegJobObject = null;

            // Com segmentação, não precisa verificar acesso (segmentos já foram finalizados)
            if (SegmentSeconds > 0)
            {
                Console.WriteLine($"[FFmpegRecorder] ✓ Gravação parada (segmentos em: {outputPath})");
            }
            else
            {
                // Verificar se arquivo está acessível
                Console.WriteLine("[FFmpegRecorder] Verificando acesso ao arquivo...");
                bool fileAccessible = await WaitForFileAccess(outputPath, maxRetries: 10, delayMs: 500);

                if (fileAccessible)
                {
                    Console.WriteLine($"[FFmpegRecorder] ✓ Gravação parada: {outputPath}");
                }
                else
                {
                    Console.WriteLine($"[FFmpegRecorder] ⚠️  Gravação parada mas arquivo pode estar travado: {outputPath}");
                }
            }

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

    /// <summary>
    /// Aguarda até que o arquivo esteja acessível para leitura
    /// </summary>
    /// <param name="filePath">Caminho do arquivo</param>
    /// <param name="maxRetries">Número máximo de tentativas</param>
    /// <param name="delayMs">Delay entre tentativas em milissegundos</param>
    /// <returns>True se arquivo ficou acessível, False caso contrário</returns>
    private async Task<bool> WaitForFileAccess(string filePath, int maxRetries = 10, int delayMs = 500)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // Tentar abrir o arquivo em modo read
                using (var fs = File.OpenRead(filePath))
                {
                    // Se chegou aqui, arquivo está acessível
                    Console.WriteLine($"[FFmpegRecorder] Arquivo acessível após {i + 1} tentativa(s)");
                    return true;
                }
            }
            catch (IOException)
            {
                // Arquivo ainda está travado
                if (i < maxRetries - 1)
                {
                    Console.WriteLine($"[FFmpegRecorder] Arquivo ainda travado, tentativa {i + 1}/{maxRetries}...");
                    await Task.Delay(delayMs);
                }
            }
            catch (Exception ex)
            {
                // Outro tipo de erro (arquivo não existe, permissões, etc)
                Console.WriteLine($"[FFmpegRecorder] Erro ao verificar acesso ao arquivo: {ex.Message}");
                return false;
            }
        }

        Console.WriteLine($"[FFmpegRecorder] ⚠️  Arquivo ainda pode estar travado após {maxRetries} tentativas");
        return false;
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

        // Cleanup Job Object (will kill FFmpeg if still running)
        _ffmpegJobObject?.Dispose();
        _ffmpegJobObject = null;

        // Cleanup AudioManager
        if (_audioManager != null)
        {
            try
            {
                _audioManager.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

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
