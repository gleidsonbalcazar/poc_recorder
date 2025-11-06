using System.Text.Json;

namespace Agent;

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
