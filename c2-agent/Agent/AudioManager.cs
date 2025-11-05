using System.Diagnostics;
using System.IO.Pipes;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Agent;

/// <summary>
/// Gerencia captura de áudio via NAudio WASAPI e envia PCM para FFmpeg via Named Pipe
/// </summary>
public sealed class AudioManager : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly string? _preferredMicName;
    private CancellationTokenSource? _cts;

    private NamedPipeServerStream? _pipe;
    private WasapiLoopbackCapture? _loopback;
    private WasapiCapture? _mic;
    private BufferedWaveProvider? _loopBuf;
    private BufferedWaveProvider? _micBuf;
    private Task? _writerTask;
    private Task? _connectionTask;

    public AudioManager(string? preferredMicName = null, string pipeName = "C2Agent_Audio")
    {
        _pipeName = pipeName;
        _preferredMicName = preferredMicName;
    }

    public string FullPipePath => $"\\\\.\\pipe\\{_pipeName}";
    public bool IsRunning { get; private set; }
    public bool IsConnected => _pipe?.IsConnected ?? false;

    /// <summary>
    /// Inicia captura de áudio e cria Named Pipe server
    /// </summary>
    public async Task StartAsync()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("AudioManager já está rodando");
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            Console.WriteLine("[AudioManager] Inicializando...");

            // Criar Named Pipe server
            _pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.Out,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0
            );

            Console.WriteLine($"[AudioManager] Named Pipe criado: {FullPipePath}");

            // Configurar dispositivos de áudio
            SetupAudioDevices();

            // Iniciar captura
            Console.WriteLine("[AudioManager] Iniciando captura de áudio...");
            _loopback?.StartRecording();
            _mic?.StartRecording();

            IsRunning = true;
            Console.WriteLine("[AudioManager] ✓ Captura de áudio iniciada");

            // Aguardar conexão do FFmpeg em background
            _connectionTask = Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine("[AudioManager] Aguardando conexão do FFmpeg...");
                    await _pipe!.WaitForConnectionAsync(token).ConfigureAwait(false);
                    Console.WriteLine("[AudioManager] ✓ FFmpeg conectado ao pipe");

                    // Iniciar loop de escrita
                    _writerTask = Task.Run(() => WriterLoop(token), token);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[AudioManager] Aguardo de conexão cancelado");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AudioManager] Erro ao aguardar conexão: {ex.Message}");
                }
            }, token);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioManager] Erro ao iniciar: {ex.Message}");
            IsRunning = false;
            throw;
        }
    }

    /// <summary>
    /// Configura dispositivos de áudio WASAPI
    /// </summary>
    private void SetupAudioDevices()
    {
        var mm = new MMDeviceEnumerator();

        // System loopback (áudio do sistema)
        try
        {
            var render = mm.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            Console.WriteLine($"[AudioManager] System Audio: {render.FriendlyName}");

            _loopback = new WasapiLoopbackCapture(render);
            _loopback.ShareMode = AudioClientShareMode.Shared;
            _loopback.DataAvailable += (s, e) =>
            {
                _loopBuf?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };

            _loopBuf = new BufferedWaveProvider(_loopback.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioManager] ⚠️  Erro ao configurar System Audio: {ex.Message}");
            throw;
        }

        // Microfone
        try
        {
            MMDevice capture = mm.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

            // Se nome preferido especificado, tentar encontrar
            if (!string.IsNullOrWhiteSpace(_preferredMicName))
            {
                var cand = mm.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                    .FirstOrDefault(d => d.FriendlyName.Contains(_preferredMicName, StringComparison.OrdinalIgnoreCase));

                if (cand != null)
                {
                    capture = cand;
                    Console.WriteLine($"[AudioManager] Microfone preferido encontrado: {capture.FriendlyName}");
                }
            }
            else
            {
                Console.WriteLine($"[AudioManager] Microfone: {capture.FriendlyName}");
            }

            _mic = new WasapiCapture(capture);
            _mic.ShareMode = AudioClientShareMode.Shared;
            _mic.DataAvailable += (s, e) =>
            {
                _micBuf?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };

            _micBuf = new BufferedWaveProvider(_mic.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioManager] ⚠️  Erro ao configurar Microfone: {ex.Message}");
            // Microfone é opcional, continua sem ele
        }
    }

    /// <summary>
    /// Loop que mixa áudio e escreve no Named Pipe
    /// </summary>
    private void WriterLoop(CancellationToken token)
    {
        try
        {
            Console.WriteLine("[AudioManager] Writer loop iniciado");

            // Build providers chain
            var loopProv = new WaveToSampleProvider(_loopBuf!);
            var micProv = new WaveToSampleProvider(_micBuf!);

            // Ensure both are 48kHz and stereo for mixing
            var loop48 = new WdlResamplingSampleProvider(loopProv, 48000);
            var mic48 = new WdlResamplingSampleProvider(micProv, 48000);

            ISampleProvider loopStereo = loop48.WaveFormat.Channels == 2
                ? loop48
                : new MonoToStereoSampleProvider(loop48);

            ISampleProvider micStereo = mic48.WaveFormat.Channels == 2
                ? mic48
                : new MonoToStereoSampleProvider(mic48);

            var mixer = new MixingSampleProvider(new[] { loopStereo, micStereo })
            {
                ReadFully = true
            };

            var mixed16 = new SampleToWaveProvider16(mixer);

            // 10ms chunks: 48000 * 0.01 * 2ch * 2bytes = 1920 bytes
            var chunk = new byte[480 * 2 * 2];

            var sw = Stopwatch.StartNew();
            long totalBytes = 0;
            long chunksWritten = 0;

            Console.WriteLine("[AudioManager] Escrevendo PCM no pipe (48kHz, stereo, 16-bit)...");

            while (!token.IsCancellationRequested)
            {
                int read = mixed16.Read(chunk, 0, chunk.Length);
                if (read <= 0)
                {
                    // No data yet; sleep briefly
                    Thread.Sleep(5);
                    continue;
                }

                _pipe!.Write(chunk, 0, read);
                _pipe.Flush();
                totalBytes += read;
                chunksWritten++;

                // Log a cada 5 segundos
                if (chunksWritten % 500 == 0)
                {
                    double secondsEmitted = totalBytes / 192000.0;
                    Console.WriteLine($"[AudioManager] {secondsEmitted:F1}s de áudio enviado ({totalBytes / 1024}KB)");
                }

                // Pace approximately real-time based on bytes written
                // bytes per second = 48000 * 2ch * 2bytes = 192000
                double secondsEmittedNow = totalBytes / 192000.0;
                double shouldMs = secondsEmittedNow * 1000.0;
                double behind = shouldMs - sw.Elapsed.TotalMilliseconds;

                if (behind > 5)
                {
                    // we're ahead of wallclock, sleep a bit
                    Thread.Sleep((int)Math.Min(behind, 10));
                }
            }

            Console.WriteLine($"[AudioManager] Writer loop finalizado. Total: {totalBytes / 1024}KB enviados");
        }
        catch (IOException ex)
        {
            // Pipe foi fechado, normal durante shutdown
            Console.WriteLine($"[AudioManager] Pipe fechado: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioManager] Erro no writer loop: {ex.Message}");
        }
    }

    /// <summary>
    /// Para captura de áudio e fecha Named Pipe
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        Console.WriteLine("[AudioManager] Parando...");

        try
        {
            // Cancelar operations
            _cts?.Cancel();

            // Parar capturas
            try { _loopback?.StopRecording(); } catch { }
            try { _mic?.StopRecording(); } catch { }

            // Aguardar tasks finalizarem
            if (_writerTask != null)
            {
                try { await _writerTask.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { Console.WriteLine("[AudioManager] Writer task timeout"); }
                catch { }
            }

            if (_connectionTask != null)
            {
                try { await _connectionTask.WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (TimeoutException) { Console.WriteLine("[AudioManager] Connection task timeout"); }
                catch { }
            }

            IsRunning = false;
            Console.WriteLine("[AudioManager] ✓ Parado");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioManager] Erro ao parar: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleanup resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        try { _loopback?.Dispose(); } catch { }
        try { _mic?.Dispose(); } catch { }

        if (_pipe != null)
        {
            try { _pipe.Flush(); } catch { }
            try { _pipe.Dispose(); } catch { }
        }

        _cts?.Dispose();
    }
}
