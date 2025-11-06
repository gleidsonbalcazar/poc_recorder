using System.Diagnostics;
using System.Text.Json;
using Agent.Database.Models;

namespace Agent.Workers;

/// <summary>
/// Captura snapshots de processos ativos no sistema
/// </summary>
public class ProcessMonitor
{
    /// <summary>
    /// Captura snapshot dos processos atualmente rodando
    /// </summary>
    public static ProcessSnapshotList CaptureSnapshot()
    {
        var snapshot = new ProcessSnapshotList
        {
            CapturedAt = DateTime.UtcNow,
            System = CaptureSystemInfo()
        };

        try
        {
            var processes = Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    // Filtrar processos do sistema que não têm informação útil
                    if (string.IsNullOrEmpty(process.ProcessName))
                        continue;

                    var processSnapshot = new ProcessSnapshot
                    {
                        Name = process.ProcessName,
                        ProcessId = process.Id
                    };

                    // Tentar obter título da janela
                    try
                    {
                        if (!string.IsNullOrEmpty(process.MainWindowTitle))
                        {
                            processSnapshot.WindowTitle = process.MainWindowTitle;
                        }
                    }
                    catch { /* Ignora se não conseguir acessar */ }

                    // Tentar obter uso de memória
                    try
                    {
                        processSnapshot.MemoryMB = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0), 2);
                    }
                    catch { /* Ignora se não conseguir acessar */ }

                    // Tentar obter caminho do executável
                    try
                    {
                        processSnapshot.ExecutablePath = process.MainModule?.FileName;
                    }
                    catch { /* Ignora se não conseguir acessar (permissões) */ }

                    // Tentar obter data de início
                    try
                    {
                        processSnapshot.StartTime = process.StartTime;
                    }
                    catch { /* Ignora se não conseguir acessar */ }

                    snapshot.Processes.Add(processSnapshot);
                }
                catch (Exception ex)
                {
                    // Alguns processos podem lançar exceções de acesso
                    Console.WriteLine($"[ProcessMonitor] Erro ao processar {process.ProcessName}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            // Ordenar por uso de memória (maior primeiro)
            snapshot.Processes = snapshot.Processes
                .OrderByDescending(p => p.MemoryMB)
                .ToList();

            Console.WriteLine($"[ProcessMonitor] Capturado snapshot com {snapshot.Processes.Count} processos");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessMonitor] Erro ao capturar processos: {ex.Message}");
        }

        return snapshot;
    }

    /// <summary>
    /// Captura informações do sistema
    /// </summary>
    private static SystemInfo CaptureSystemInfo()
    {
        var info = new SystemInfo
        {
            MachineName = Environment.MachineName,
            UserName = Environment.UserName
        };

        try
        {
            // Obter informações de memória
            var gcInfo = GC.GetGCMemoryInfo();
            info.TotalMemoryGB = Math.Round(gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0 * 1024.0), 2);

            // Memória disponível aproximada
            var process = Process.GetCurrentProcess();
            var totalMemory = gcInfo.TotalAvailableMemoryBytes;
            var usedMemory = process.WorkingSet64;
            info.AvailableMemoryGB = Math.Round((totalMemory - usedMemory) / (1024.0 * 1024.0 * 1024.0), 2);

            process.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessMonitor] Erro ao obter info do sistema: {ex.Message}");
        }

        return info;
    }

    /// <summary>
    /// Serializa snapshot para JSON
    /// </summary>
    public static string SerializeSnapshot(ProcessSnapshotList snapshot)
    {
        try
        {
            return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessMonitor] Erro ao serializar snapshot: {ex.Message}");
            return "{}";
        }
    }

    /// <summary>
    /// Captura e serializa snapshot em uma única operação
    /// </summary>
    public static string CaptureAndSerialize()
    {
        var snapshot = CaptureSnapshot();
        return SerializeSnapshot(snapshot);
    }

    /// <summary>
    /// Filtra processos relevantes (com janela ou alto uso de memória)
    /// </summary>
    public static ProcessSnapshotList CaptureRelevantProcesses()
    {
        var fullSnapshot = CaptureSnapshot();

        // Filtrar apenas processos com janela ou alto uso de memória (> 100MB)
        var relevantProcesses = fullSnapshot.Processes
            .Where(p => !string.IsNullOrEmpty(p.WindowTitle) || p.MemoryMB > 100)
            .ToList();

        return new ProcessSnapshotList
        {
            CapturedAt = fullSnapshot.CapturedAt,
            System = fullSnapshot.System,
            Processes = relevantProcesses
        };
    }
}
