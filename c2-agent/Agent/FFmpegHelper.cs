using System.Diagnostics;
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
    /// Limpa cache de dispositivos (forçar nova detecção)
    /// </summary>
    public static void ClearDeviceCache()
    {
        _cachedAudioDevices = null;
    }
}
