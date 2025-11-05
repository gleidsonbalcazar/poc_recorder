using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent;

/// <summary>
/// Gerencia armazenamento local de arquivos de mídia
/// </summary>
public class MediaStorage
{
    private readonly string _basePath;
    private readonly string _metadataPath;

    public string VideoPath => Path.Combine(_basePath, "videos");

    public MediaStorage(string basePath)
    {
        _basePath = basePath;
        _metadataPath = Path.Combine(_basePath, "metadata.json");

        // Criar estrutura de diretórios
        InitializeDirectories();
    }

    /// <summary>
    /// Inicializa estrutura de diretórios
    /// </summary>
    private void InitializeDirectories()
    {
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(VideoPath);
    }

    /// <summary>
    /// Lista todos os arquivos de vídeo
    /// </summary>
    public List<MediaFileInfo> ListVideoFiles(int maxFiles = 100)
    {
        return ListFiles(VideoPath, "*.mp4", maxFiles);
    }

    /// <summary>
    /// Lista todos os arquivos de mídia (apenas vídeo)
    /// </summary>
    public List<MediaFileInfo> ListAllMediaFiles(int maxFiles = 100)
    {
        return ListVideoFiles(maxFiles);
    }

    /// <summary>
    /// Lista arquivos de um diretório
    /// </summary>
    private List<MediaFileInfo> ListFiles(string directory, string pattern, int maxFiles)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return new List<MediaFileInfo>();
            }

            var files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories)
                .Select(filePath => CreateMediaFileInfo(filePath))
                .Where(info => info != null)
                .OrderByDescending(f => f!.CreatedAt)
                .Take(maxFiles)
                .Cast<MediaFileInfo>()
                .ToList();

            return files;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao listar arquivos: {ex.Message}");
            return new List<MediaFileInfo>();
        }
    }

    /// <summary>
    /// Cria informações sobre um arquivo de mídia
    /// </summary>
    private MediaFileInfo? CreateMediaFileInfo(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);

            return new MediaFileInfo
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                Type = GetMediaType(filePath),
                SizeBytes = fileInfo.Length,
                SizeMB = Math.Round(fileInfo.Length / (1024.0 * 1024.0), 2),
                CreatedAt = fileInfo.CreationTime,
                LastModified = fileInfo.LastWriteTime
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao criar info do arquivo {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Determina o tipo de mídia baseado na extensão
    /// </summary>
    private MediaType GetMediaType(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".mp4" => MediaType.Video,
            _ => MediaType.Unknown
        };
    }

    /// <summary>
    /// Limpa arquivos mais antigos que X dias
    /// </summary>
    public int CleanOldFiles(int daysOld)
    {
        int deletedCount = 0;
        DateTime cutoffDate = DateTime.Now.AddDays(-daysOld);

        try
        {
            // Limpar vídeos antigos
            deletedCount += CleanDirectory(VideoPath, cutoffDate);

            Console.WriteLine($"[MediaStorage] {deletedCount} arquivos removidos (mais antigos que {daysOld} dias)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao limpar arquivos: {ex.Message}");
        }

        return deletedCount;
    }

    /// <summary>
    /// Deleta um arquivo específico por nome
    /// </summary>
    public bool DeleteFile(string filename)
    {
        try
        {
            // Procurar o arquivo em todos os diretórios
            string? filePath = FindFile(filename);

            if (filePath == null || !File.Exists(filePath))
            {
                Console.WriteLine($"[MediaStorage] Arquivo não encontrado: {filename}");
                return false;
            }

            File.Delete(filePath);
            Console.WriteLine($"[MediaStorage] Arquivo deletado: {filename}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao deletar arquivo {filename}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Procura um arquivo por nome em todos os diretórios de mídia
    /// </summary>
    private string? FindFile(string filename)
    {
        try
        {
            // Procurar em vídeos
            string videoPath = Path.Combine(VideoPath, filename);
            if (File.Exists(videoPath))
                return videoPath;

            // Procurar recursivamente
            var allFiles = Directory.GetFiles(_basePath, filename, SearchOption.AllDirectories);
            return allFiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao procurar arquivo: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Limpa arquivos antigos de um diretório
    /// </summary>
    private int CleanDirectory(string directory, DateTime cutoffDate)
    {
        int deletedCount = 0;

        try
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            var oldFiles = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(f => File.GetCreationTime(f) < cutoffDate)
                .ToList();

            foreach (var file in oldFiles)
            {
                try
                {
                    File.Delete(file);
                    deletedCount++;
                    Console.WriteLine($"[MediaStorage] Removido: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MediaStorage] Erro ao remover {file}: {ex.Message}");
                }
            }

            // Remover pastas vazias
            CleanEmptyDirectories(directory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao limpar diretório {directory}: {ex.Message}");
        }

        return deletedCount;
    }

    /// <summary>
    /// Remove diretórios vazios recursivamente
    /// </summary>
    private void CleanEmptyDirectories(string directory)
    {
        try
        {
            foreach (var subDir in Directory.GetDirectories(directory))
            {
                CleanEmptyDirectories(subDir);

                if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                {
                    Directory.Delete(subDir);
                    Console.WriteLine($"[MediaStorage] Pasta vazia removida: {Path.GetFileName(subDir)}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao limpar pastas vazias: {ex.Message}");
        }
    }

    /// <summary>
    /// Obtém estatísticas de armazenamento
    /// </summary>
    public StorageStats GetStorageStats()
    {
        try
        {
            var videoFiles = ListVideoFiles(int.MaxValue);

            long videoSize = videoFiles.Sum(f => f.SizeBytes);

            return new StorageStats
            {
                TotalFiles = videoFiles.Count,
                VideoFiles = videoFiles.Count,
                TotalSizeBytes = videoSize,
                TotalSizeMB = Math.Round(videoSize / (1024.0 * 1024.0), 2),
                VideoSizeMB = Math.Round(videoSize / (1024.0 * 1024.0), 2),
                BasePath = _basePath
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao obter estatísticas: {ex.Message}");
            return new StorageStats { BasePath = _basePath };
        }
    }

    /// <summary>
    /// Salva metadados dos arquivos em JSON
    /// </summary>
    public void SaveMetadata()
    {
        try
        {
            var metadata = new MediaMetadata
            {
                LastUpdated = DateTime.Now,
                StorageStats = GetStorageStats(),
                Videos = ListVideoFiles(int.MaxValue)
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string json = JsonSerializer.Serialize(metadata, options);
            File.WriteAllText(_metadataPath, json);

            Console.WriteLine($"[MediaStorage] Metadados salvos: {_metadataPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao salvar metadados: {ex.Message}");
        }
    }

    /// <summary>
    /// Carrega metadados do arquivo JSON
    /// </summary>
    public MediaMetadata? LoadMetadata()
    {
        try
        {
            if (!File.Exists(_metadataPath))
            {
                return null;
            }

            string json = File.ReadAllText(_metadataPath);
            return JsonSerializer.Deserialize<MediaMetadata>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao carregar metadados: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Prepara arquivo para upload via tUS (futuro)
    /// </summary>
    public UploadPrepareInfo PrepareForUpload(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Arquivo não encontrado", filePath);
            }

            var fileInfo = new FileInfo(filePath);

            return new UploadPrepareInfo
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                SizeBytes = fileInfo.Length,
                ContentType = GetContentType(filePath),
                Checksum = CalculateChecksum(filePath),
                ReadyForUpload = true
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao preparar arquivo para upload: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Obtém o content-type baseado na extensão
    /// </summary>
    private string GetContentType(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".mp4" => "video/mp4",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Calcula checksum SHA256 do arquivo
    /// </summary>
    private string CalculateChecksum(string filePath)
    {
        try
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return Convert.ToBase64String(hash);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao calcular checksum: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Lista segmentos de vídeo agrupados por sessão de gravação (baseado em padrão de timestamp)
    /// Útil para segmentação automática onde múltiplos arquivos pertencem à mesma gravação
    /// </summary>
    /// <param name="dateFolder">Pasta específica de data (ex: "2025-11-05"), ou null para todas</param>
    /// <returns>Dictionary com chave sendo prefixo da sessão e valor lista de segmentos</returns>
    public Dictionary<string, List<MediaFileInfo>> ListVideoSegmentsBySession(string? dateFolder = null)
    {
        try
        {
            var searchPath = dateFolder != null
                ? Path.Combine(VideoPath, dateFolder)
                : VideoPath;

            if (!Directory.Exists(searchPath))
            {
                return new Dictionary<string, List<MediaFileInfo>>();
            }

            // Listar todos os arquivos
            var allFiles = Directory.GetFiles(searchPath, "*.mp4", SearchOption.AllDirectories)
                .Select(filePath => CreateMediaFileInfo(filePath))
                .Where(info => info != null)
                .Cast<MediaFileInfo>()
                .ToList();

            // Agrupar por prefixo (ex: "screen_20251105_1430" para segmentos "_143000", "_143030", etc)
            var sessions = new Dictionary<string, List<MediaFileInfo>>();

            foreach (var file in allFiles)
            {
                // Extrair prefixo base do nome do arquivo
                // Ex: "screen_20251105_143022.mp4" → "screen_20251105_1430"
                var fileName = Path.GetFileNameWithoutExtension(file.FileName);

                string sessionKey;
                if (fileName.StartsWith("screen_") && fileName.Length >= 20)
                {
                    // Usar primeiros 20 caracteres como chave da sessão (até os minutos)
                    sessionKey = fileName.Substring(0, 20); // "screen_20251105_1430"
                }
                else
                {
                    sessionKey = fileName; // Fallback: usar nome completo
                }

                if (!sessions.ContainsKey(sessionKey))
                {
                    sessions[sessionKey] = new List<MediaFileInfo>();
                }

                sessions[sessionKey].Add(file);
            }

            // Ordenar segmentos dentro de cada sessão
            foreach (var session in sessions.Values)
            {
                session.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
            }

            return sessions;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaStorage] Erro ao listar segmentos por sessão: {ex.Message}");
            return new Dictionary<string, List<MediaFileInfo>>();
        }
    }
}

/// <summary>
/// Tipo de mídia
/// </summary>
public enum MediaType
{
    Unknown,
    Video
}

/// <summary>
/// Informações sobre um arquivo de mídia
/// </summary>
public class MediaFileInfo
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public MediaType Type { get; set; }
    public long SizeBytes { get; set; }
    public double SizeMB { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Estatísticas de armazenamento
/// </summary>
public class StorageStats
{
    public int TotalFiles { get; set; }
    public int VideoFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public double TotalSizeMB { get; set; }
    public double VideoSizeMB { get; set; }
    public string BasePath { get; set; } = "";
}

/// <summary>
/// Metadados completos da mídia
/// </summary>
public class MediaMetadata
{
    public DateTime LastUpdated { get; set; }
    public StorageStats? StorageStats { get; set; }
    public List<MediaFileInfo> Videos { get; set; } = new();
}

/// <summary>
/// Informações para preparar upload via tUS
/// </summary>
public class UploadPrepareInfo
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "";
    public string Checksum { get; set; } = "";
    public bool ReadyForUpload { get; set; }
}
