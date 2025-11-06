namespace Agent.Database.Models;

/// <summary>
/// Representa um snapshot de processo capturado
/// </summary>
public class ProcessSnapshot
{
    /// <summary>
    /// Nome do processo
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Título da janela principal (se disponível)
    /// </summary>
    public string? WindowTitle { get; set; }

    /// <summary>
    /// Process ID
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Uso de memória em MB
    /// </summary>
    public double MemoryMB { get; set; }

    /// <summary>
    /// Caminho do executável
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// Data/hora de início do processo
    /// </summary>
    public DateTime? StartTime { get; set; }
}

/// <summary>
/// Container para lista de snapshots de processos
/// </summary>
public class ProcessSnapshotList
{
    /// <summary>
    /// Timestamp da captura
    /// </summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Lista de processos ativos
    /// </summary>
    public List<ProcessSnapshot> Processes { get; set; } = new();

    /// <summary>
    /// Informações do sistema no momento da captura
    /// </summary>
    public SystemInfo? System { get; set; }
}

/// <summary>
/// Informações do sistema
/// </summary>
public class SystemInfo
{
    /// <summary>
    /// Nome da máquina
    /// </summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// Nome do usuário
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Uso de CPU (%)
    /// </summary>
    public double CpuUsage { get; set; }

    /// <summary>
    /// Memória total em GB
    /// </summary>
    public double TotalMemoryGB { get; set; }

    /// <summary>
    /// Memória disponível em GB
    /// </summary>
    public double AvailableMemoryGB { get; set; }
}
