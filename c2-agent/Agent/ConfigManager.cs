using System.Text.Json;

namespace Agent;

/// <summary>
/// Perfis de gravação pré-configurados
/// </summary>
public enum RecordingProfile
{
    Performance,  // Otimizado para máquinas fracas (FPS baixo, bitrate reduzido)
    Balanced,     // Equilíbrio entre qualidade e performance
    Quality       // Máxima qualidade (maior uso de CPU e espaço)
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
            if (!File.Exists(path))
            {
                Console.WriteLine($"[ConfigManager] Arquivo não encontrado: {path}, usando valores padrão");
                return new ConfigManager();
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ConfigManager>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return config ?? new ConfigManager();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigManager] Erro ao carregar configuração: {ex.Message}");
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
                Console.WriteLine("[ConfigManager] Perfil: Performance (FPS: 15, Bitrate: 1200k, Segment: 60s)");
                break;

            case RecordingProfile.Balanced:
                // Equilíbrio entre qualidade e performance (reduz CPU em ~33%)
                FPS = 20;
                VideoBitrate = 1500;
                SegmentSeconds = 60;
                CaptureAudio = true;
                Console.WriteLine("[ConfigManager] Perfil: Balanced (FPS: 20, Bitrate: 1500k, Segment: 60s)");
                break;

            case RecordingProfile.Quality:
                // Máxima qualidade (configuração original)
                FPS = 30;
                VideoBitrate = 2000;
                SegmentSeconds = 30;
                CaptureAudio = true;
                Console.WriteLine("[ConfigManager] Perfil: Quality (FPS: 30, Bitrate: 2000k, Segment: 30s)");
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
