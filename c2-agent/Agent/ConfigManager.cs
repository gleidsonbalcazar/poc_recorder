using System.Text.Json;

namespace Agent;

/// <summary>
/// Perfis de gravação pré-configurados
/// </summary>
public enum RecordingProfile
{
    Performance,  // Otimizado para máquinas fracas (FPS baixo, bitrate reduzido)
    Balanced,     // Equilíbrio entre qualidade e performance
    Quality,      // Máxima qualidade (maior uso de CPU e espaço)
    LowEnd        // Máquinas extremamente fracas (MJPEG, CPU mínimo)
}

/// <summary>
/// Gerencia configuração da aplicação
/// </summary>
public class ConfigManager
{
    public string Mode { get; set; } = "hybrid"; // c2, autonomous, hybrid
    public RecordingConfig Recording { get; set; } = new();
    public UploadConfig Upload { get; set; } = new();
    public C2Config C2 { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public TusConfig Tus { get; set; } = new();
    public WebUIConfig WebUI { get; set; } = new();

    public static ConfigManager LoadFromFile(string path = "appsettings.json")
    {
        try
        {
            // If path is relative, try to find it in AppContext.BaseDirectory first
            string resolvedPath = path;

            if (!Path.IsPathRooted(path))
            {
                // Try AppContext.BaseDirectory first (where the .exe is)
                var baseDir = AppContext.BaseDirectory;
                var baseDirPath = Path.Combine(baseDir, path);

                if (File.Exists(baseDirPath))
                {
                    resolvedPath = baseDirPath;
                }
                // Otherwise fall back to CurrentDirectory
                else if (!File.Exists(path))
                {
                    Console.WriteLine($"[ConfigManager] ❌ Arquivo não encontrado em:");
                    Console.WriteLine($"[ConfigManager]   - BaseDirectory: {baseDirPath}");
                    Console.WriteLine($"[ConfigManager]   - CurrentDirectory: {Path.GetFullPath(path)}");
                    Console.WriteLine($"[ConfigManager] ⚠️ Usando valores padrão (ServerUrl = http://localhost:8000)");
                    return new ConfigManager();
                }
            }
            else if (!File.Exists(path))
            {
                Console.WriteLine($"[ConfigManager] ❌ Arquivo não encontrado: {path}");
                Console.WriteLine($"[ConfigManager] ⚠️ Usando valores padrão (ServerUrl = http://localhost:8000)");
                return new ConfigManager();
            }

            var json = File.ReadAllText(resolvedPath);
            Console.WriteLine($"[ConfigManager] ✓ Arquivo lido com sucesso de: {resolvedPath}");

            var config = JsonSerializer.Deserialize<ConfigManager>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config != null)
            {
                Console.WriteLine($"[ConfigManager] ✓ Configuração deserializada:");
                Console.WriteLine($"[ConfigManager]   - Mode: {config.Mode}");
                Console.WriteLine($"[ConfigManager]   - C2.ServerUrl: {config.C2.ServerUrl}");
                Console.WriteLine($"[ConfigManager]   - Upload.Endpoint: {config.Upload.Endpoint}");
                Console.WriteLine($"[ConfigManager]   - Tus.TusServerUrl: {config.Tus.TusServerUrl}");
            }
            else
            {
                Console.WriteLine($"[ConfigManager] ⚠️ Deserialização retornou null, usando valores padrão");
            }

            Console.WriteLine($"[ConfigManager] ========================================");
            return config ?? new ConfigManager();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigManager] ❌ Erro ao carregar configuração: {ex.Message}");
            Console.WriteLine($"[ConfigManager] Stack trace: {ex.StackTrace}");
            Console.WriteLine($"[ConfigManager] ⚠️ Usando valores padrão (ServerUrl = http://localhost:8000)");
            Console.WriteLine($"[ConfigManager] ========================================");
            return new ConfigManager();
        }
    }

    public void SaveToFile(string path = "appsettings.json")
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(path, json);
            Console.WriteLine($"[ConfigManager] Configuração salva em: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigManager] Erro ao salvar configuração: {ex.Message}");
        }
    }
}

public class RecordingConfig
{
    public bool Continuous { get; set; } = false;
    public int IntervalMinutes { get; set; } = 60;
    public int DurationMinutes { get; set; } = 60;
    public int SegmentSeconds { get; set; } = 30;
    public int FPS { get; set; } = 30;
    public int VideoBitrate { get; set; } = 2000;
    public bool CaptureAudio { get; set; } = true;
    public string Profile { get; set; } = "Performance"; // Perfil padrão: Performance
    public string Codec { get; set; } = "libx264"; // Codec de vídeo: libx264 (H.264) ou mjpeg
    public int VideoQuality { get; set; } = 23; // Qualidade MJPEG (1-31, menor=melhor) ou CRF para H.264

    /// <summary>
    /// Aplica configurações baseadas no perfil selecionado
    /// Se valores customizados existirem no appsettings.json, eles têm prioridade
    /// </summary>
    public void ApplyProfile()
    {
        // Parse profile string to enum (case-insensitive)
        if (!Enum.TryParse<RecordingProfile>(Profile, true, out var profileEnum))
        {
            Console.WriteLine($"[ConfigManager] Perfil inválido '{Profile}', usando Performance como padrão");
            profileEnum = RecordingProfile.Performance;
        }

        // Aplicar configurações do perfil
        switch (profileEnum)
        {
            case RecordingProfile.Performance:
                // Otimizado para máquinas fracas (reduz CPU em ~50%)
                FPS = 15;
                VideoBitrate = 1200;
                SegmentSeconds = 60;
                CaptureAudio = true;
                Codec = "libx264";
                Console.WriteLine("[ConfigManager] Perfil: Performance (FPS: 15, Bitrate: 1200k, Segment: 60s)");
                break;

            case RecordingProfile.Balanced:
                // Equilíbrio entre qualidade e performance (reduz CPU em ~33%)
                FPS = 20;
                VideoBitrate = 1500;
                SegmentSeconds = 60;
                CaptureAudio = true;
                Codec = "libx264";
                Console.WriteLine("[ConfigManager] Perfil: Balanced (FPS: 20, Bitrate: 1500k, Segment: 60s)");
                break;

            case RecordingProfile.Quality:
                // Máxima qualidade (configuração original)
                FPS = 30;
                VideoBitrate = 2000;
                SegmentSeconds = 30;
                CaptureAudio = true;
                Codec = "libx264";
                Console.WriteLine("[ConfigManager] Perfil: Quality (FPS: 30, Bitrate: 2000k, Segment: 30s)");
                break;

            case RecordingProfile.LowEnd:
                // Máquinas extremamente fracas (Pentium/Celeron)
                // MJPEG usa menos CPU/RAM mas gera arquivos 2-3x maiores
                FPS = 10;
                SegmentSeconds = 120;
                CaptureAudio = true;
                Codec = "mjpeg";
                VideoQuality = 5; // Quality 5 (1-31, menor=melhor)
                VideoBitrate = 0; // Não usado com MJPEG (usa quality)
                Console.WriteLine("[ConfigManager] Perfil: LowEnd (MJPEG, FPS: 10, Quality: 5, Segment: 120s)");
                Console.WriteLine("[ConfigManager] AVISO: MJPEG gera arquivos 2-3x maiores que H.264!");
                break;
        }
    }

    /// <summary>
    /// Retorna descrição do perfil atual
    /// </summary>
    public string GetProfileDescription()
    {
        if (!Enum.TryParse<RecordingProfile>(Profile, true, out var profileEnum))
        {
            return "Performance (padrão)";
        }

        return profileEnum switch
        {
            RecordingProfile.Performance => "Performance - Otimizado para máquinas fracas (~9 MB/min, CPU reduzido em 50%)",
            RecordingProfile.Balanced => "Balanced - Equilíbrio entre qualidade e performance (~12 MB/min, CPU reduzido em 33%)",
            RecordingProfile.Quality => "Quality - Máxima qualidade (~16 MB/min, uso normal de CPU)",
            RecordingProfile.LowEnd => "LowEnd - Máquinas extremamente fracas, MJPEG (~18 MB/min, CPU mínimo, arquivos 2-3x maiores)",
            _ => "Performance (padrão)"
        };
    }
}

public class UploadConfig
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 30;
    public int MaxConcurrentUploads { get; set; } = 2;
    public int MaxRetries { get; set; } = 3;
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public class C2Config
{
    public bool Enabled { get; set; } = true;
    public string ServerUrl { get; set; } = "http://localhost:8000";
    public int ReconnectDelaySeconds { get; set; } = 5;
}

public class DatabaseConfig
{
    public string Path { get; set; } = "paneas_monitor.db";
}

public class StorageConfig
{
    public string BasePath { get; set; } = "";
}

public class TusConfig
{
    public string TusServerUrl { get; set; } = "";
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
}

public class WebUIConfig
{
    public bool Enabled { get; set; } = true;
    public string Password { get; set; } = "admin";
}
