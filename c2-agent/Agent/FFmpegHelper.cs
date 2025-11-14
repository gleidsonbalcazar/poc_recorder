using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Agent;

/// <summary>
/// Classe auxiliar para operações com FFmpeg
/// </summary>
public static class FFmpegHelper
{
    private static string? _ffmpegPath;
    private static List<string>? _cachedAudioDevices;

    /// <summary>
    /// Obtém o caminho para o executável ffmpeg.exe
    /// </summary>
    public static string GetFFmpegPath()
    {
        if (_ffmpegPath != null)
        {
            return _ffmpegPath;
        }

        // Tentar localizar ffmpeg.exe
        var paths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(Environment.CurrentDirectory, "ffmpeg", "ffmpeg.exe"),
            "ffmpeg.exe" // No PATH do sistema
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                _ffmpegPath = Path.GetFullPath(path);
                Console.WriteLine($"[FFmpegHelper] FFmpeg encontrado: {_ffmpegPath}");
                return _ffmpegPath;
            }
        }

        throw new FileNotFoundException(
            "ffmpeg.exe não encontrado. Coloque o arquivo em: " +
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe")
        );
    }

    /// <summary>
    /// Verifica se FFmpeg está disponível
    /// </summary>
    public static bool IsFFmpegAvailable()
    {
        try
        {
            GetFFmpegPath();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Garante que FFmpeg está disponível, baixando automaticamente se necessário
    /// </summary>
    public static async Task EnsureFFmpegAvailable()
    {
        // Se já está disponível, não fazer nada
        if (IsFFmpegAvailable())
        {
            return;
        }

        Console.WriteLine("[FFmpegHelper] FFmpeg não encontrado, iniciando download automático...");

        string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
        string targetPath = Path.Combine(targetDir, "ffmpeg.exe");

        // Criar diretório se não existir
        Directory.CreateDirectory(targetDir);

        // URL do FFmpeg essentials build
        string downloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

        Console.WriteLine($"[FFmpegHelper] Baixando de: {downloadUrl}");

        int retries = 3;
        for (int attempt = 1; attempt <= retries; attempt++)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);

                // Baixar arquivo
                var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                string tempZipPath = Path.Combine(Path.GetTempPath(), "ffmpeg-temp.zip");

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;
                    int lastPercent = 0;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (totalBytes.HasValue)
                        {
                            int percent = (int)((totalRead * 100) / totalBytes.Value);
                            if (percent >= lastPercent + 10)
                            {
                                Console.WriteLine($"[FFmpegHelper] Progresso: {percent}% ({totalRead / (1024 * 1024)}MB / {totalBytes.Value / (1024 * 1024)}MB)");
                                lastPercent = percent;
                            }
                        }
                    }
                }

                Console.WriteLine("[FFmpegHelper] Download concluído. Extraindo ffmpeg.exe...");

                // Extrair apenas ffmpeg.exe do ZIP
                using (var archive = System.IO.Compression.ZipFile.OpenRead(tempZipPath))
                {
                    var ffmpegEntry = archive.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));

                    if (ffmpegEntry == null)
                    {
                        throw new Exception("ffmpeg.exe não encontrado no arquivo ZIP");
                    }

                    ffmpegEntry.ExtractToFile(targetPath, overwrite: true);
                }

                // Limpar arquivo temporário
                try
                {
                    File.Delete(tempZipPath);
                }
                catch { }

                Console.WriteLine($"[FFmpegHelper] ✓ FFmpeg instalado em: {targetPath}");
                Console.WriteLine("[FFmpegHelper] ✓ Pronto para uso!");

                // Limpar cache para forçar nova detecção
                _ffmpegPath = null;

                // Verificar se agora está disponível
                if (IsFFmpegAvailable())
                {
                    return;
                }
                else
                {
                    throw new Exception("FFmpeg foi baixado mas não pode ser localizado");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FFmpegHelper] Tentativa {attempt}/{retries} falhou: {ex.Message}");

                if (attempt == retries)
                {
                    throw new Exception(
                        $"Falha ao baixar FFmpeg após {retries} tentativas. " +
                        "Verifique sua conexão com a internet ou baixe manualmente de: " +
                        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip " +
                        $"e coloque ffmpeg.exe em: {targetDir}",
                        ex
                    );
                }

                // Aguardar antes de tentar novamente
                await Task.Delay(2000);
            }
        }
    }

    /// <summary>
    /// Lista todos os dispositivos de áudio disponíveis no Windows (DirectShow)
    /// </summary>
    public static List<string> ListAudioDevices()
    {
        if (_cachedAudioDevices != null)
        {
            return _cachedAudioDevices;
        }

        var devices = new List<string>();

        try
        {
            var ffmpegPath = GetFFmpegPath();

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-list_devices true -f dshow -i dummy",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return devices;
            }

            // FFmpeg lista dispositivos no stderr
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // Parsear saída para extrair nomes de dispositivos de áudio
            // Formato: [dshow @ ...] "Device Name" (audio)
            var regex = new Regex(@"\""([^\""]+)\""\s+\(audio\)");
            var matches = regex.Matches(stderr);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    devices.Add(match.Groups[1].Value);
                }
            }

            _cachedAudioDevices = devices;
            Console.WriteLine($"[FFmpegHelper] {devices.Count} dispositivos de áudio encontrados");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FFmpegHelper] Erro ao listar dispositivos: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Detecta automaticamente o dispositivo de microfone
    /// </summary>
    public static string? DetectMicrophone()
    {
        var devices = ListAudioDevices();

        // Procurar por palavras-chave comuns de microfone
        var keywords = new[] { "microphone", "microfone", "mic", "input" };

        foreach (var keyword in keywords)
        {
            var device = devices.FirstOrDefault(d =>
                d.ToLower().Contains(keyword.ToLower()));

            if (device != null)
            {
                Console.WriteLine($"[FFmpegHelper] Microfone detectado: {device}");
                return device;
            }
        }

        // Se não encontrou, retornar o primeiro dispositivo disponível
        if (devices.Count > 0)
        {
            Console.WriteLine($"[FFmpegHelper] Usando primeiro dispositivo como microfone: {devices[0]}");
            return devices[0];
        }

        Console.WriteLine("[FFmpegHelper] Nenhum microfone detectado");
        return null;
    }

    /// <summary>
    /// Detecta automaticamente o dispositivo Stereo Mix (áudio do sistema)
    /// </summary>
    public static string? DetectStereoMix()
    {
        var devices = ListAudioDevices();

        // Procurar por Stereo Mix ou equivalentes
        var keywords = new[]
        {
            "stereo mix",
            "wave out mix",
            "what u hear",
            "what you hear",
            "loopback",
            "mixagem estéreo",
            "mistura"
        };

        foreach (var keyword in keywords)
        {
            var device = devices.FirstOrDefault(d =>
                d.ToLower().Contains(keyword.ToLower()));

            if (device != null)
            {
                Console.WriteLine($"[FFmpegHelper] Stereo Mix detectado: {device}");
                return device;
            }
        }

        Console.WriteLine("[FFmpegHelper] Stereo Mix não detectado (pode não estar habilitado)");
        return null;
    }

    /// <summary>
    /// Constrói os argumentos de linha de comando para gravação com FFmpeg
    /// </summary>
    public static string BuildRecordingArguments(
        string outputPath,
        int framerate = 20,
        string? micDevice = null,
        string? stereoMixDevice = null,
        string preset = "ultrafast",
        int videoBitrate = 2000)
    {
        var args = new List<string>();

        // Captura de tela (gdigrab)
        args.Add("-f gdigrab");
        args.Add($"-framerate {framerate}");
        args.Add("-i desktop");

        // Captura de áudio
        bool hasMic = !string.IsNullOrEmpty(micDevice);
        bool hasStereoMix = !string.IsNullOrEmpty(stereoMixDevice);

        if (hasMic)
        {
            args.Add("-f dshow");
            args.Add($"-i audio=\"{micDevice}\"");
        }

        if (hasStereoMix)
        {
            args.Add("-f dshow");
            args.Add($"-i audio=\"{stereoMixDevice}\"");
        }

        // Mixar áudio se temos múltiplas fontes
        if (hasMic && hasStereoMix)
        {
            args.Add("-filter_complex \"[1:a][2:a]amerge=inputs=2[a]\"");
            args.Add("-map 0:v");
            args.Add("-map \"[a]\"");
        }
        else if (hasMic || hasStereoMix)
        {
            args.Add("-map 0:v");
            args.Add("-map 1:a");
        }

        // Codec e qualidade
        args.Add("-c:v libx264");
        args.Add($"-preset {preset}");
        args.Add($"-b:v {videoBitrate}k");
        args.Add("-pix_fmt yuv420p");

        if (hasMic || hasStereoMix)
        {
            args.Add("-ac 2"); // Stereo
            args.Add("-ar 44100"); // Sample rate
        }

        // Sobrescrever arquivo se existir
        args.Add("-y");

        // Arquivo de saída
        args.Add($"\"{outputPath}\"");

        return string.Join(" ", args);
    }

    /// <summary>
    /// Constrói argumentos FFmpeg usando Named Pipe para áudio com segmentação automática
    /// </summary>
    /// <param name="outputPattern">Pattern para arquivos de saída com strftime (ex: video_%Y%m%d_%H%M%S.mp4)</param>
    /// <param name="pipePath">Caminho do Named Pipe (ex: \\.\pipe\C2Agent_Audio)</param>
    /// <param name="framerate">Taxa de quadros</param>
    /// <param name="segmentSeconds">Duração de cada segmento em segundos (0 = sem segmentação)</param>
    /// <param name="preset">Preset H.264</param>
    /// <param name="videoBitrate">Bitrate do vídeo em kbps</param>
    /// <returns>String com argumentos do FFmpeg</returns>
    public static string BuildRecordingArgumentsWithPipe(
        string outputPattern,
        string pipePath,
        int framerate = 30,
        int segmentSeconds = 30,
        string preset = "ultrafast",
        int videoBitrate = 2000)
    {
        Console.WriteLine("[SYNC] ===== CONFIGURAÇÃO FFmpeg =====");
        Console.WriteLine("[SYNC] INPUT 0: VIDEO (gdigrab desktop) ← MASTER CLOCK");
        Console.WriteLine("[SYNC] INPUT 1: AUDIO (Named Pipe) ← SINCRONIZADO COM VÍDEO");
        Console.WriteLine("[SYNC] thread_queue_size: 4096 (evita audio drops)");
        Console.WriteLine("[SYNC] =====================================");

        var args = new List<string>();

        // Captura de tela (gdigrab) - INPUT 0 (master clock)
        args.Add("-f gdigrab");
        args.Add($"-framerate {framerate}");
        args.Add("-i desktop");

        // Input de áudio via Named Pipe (PCM s16le 48kHz stereo) - INPUT 1
        args.Add("-f s16le");
        args.Add("-ar 48000");
        args.Add("-ac 2");
        args.Add("-thread_queue_size 4096");
        args.Add($"-i \"{pipePath}\"");

        // GOP optimization (force keyframe every segment for clean cuts)
        int gopSize = Math.Max(framerate * 2, 30);
        args.Add($"-g {gopSize}");

        if (segmentSeconds > 0)
        {
            // Segmentação automática
            args.Add("-f segment");
            args.Add($"-segment_time {segmentSeconds}");
            args.Add("-reset_timestamps 1");
            args.Add("-strftime 1");

            // Force keyframes at segment boundaries
            args.Add($"-force_key_frames \"expr:gte(t,n_forced*{segmentSeconds})\"");
        }

        // Map streams
        args.Add("-map 0:v"); // Video from gdigrab (input 0)
        args.Add("-map 1:a"); // Audio from pipe (input 1)

        // Video codec e qualidade
        args.Add("-c:v libx264");
        args.Add($"-preset {preset}");
        args.Add($"-b:v {videoBitrate}k");
        args.Add("-pix_fmt yuv420p");

        // Audio codec (comprime PCM para AAC)
        args.Add("-c:a aac");
        args.Add("-b:a 128k");

        // Sobrescrever arquivo se existir
        args.Add("-y");

        // Arquivo de saída (ou pattern com strftime)
        args.Add($"\"{outputPattern}\"");

        return string.Join(" ", args);
    }

    /// <summary>
    /// Limpa cache de dispositivos (forçar nova detecção)
    /// </summary>
    public static void ClearDeviceCache()
    {
        _cachedAudioDevices = null;
    }
}
