using System.Diagnostics;

namespace Agent;

/// <summary>
/// Utilities for cleaning up orphaned processes from previous Agent runs
/// </summary>
public static class ProcessCleanup
{
    /// <summary>
    /// Kill all FFmpeg processes that may be orphaned from previous Agent runs.
    /// This ensures a clean slate when Agent starts, preventing accumulation of orphaned FFmpeg processes.
    /// </summary>
    /// <returns>Number of processes killed</returns>
    public static int KillOrphanedFFmpegProcesses()
    {
        int killedCount = 0;

        try
        {
            Console.WriteLine("[ProcessCleanup] Checking for orphaned FFmpeg processes...");

            // Get all ffmpeg.exe processes
            var ffmpegProcesses = Process.GetProcessesByName("ffmpeg");

            if (ffmpegProcesses.Length == 0)
            {
                Console.WriteLine("[ProcessCleanup] No orphaned FFmpeg processes found");
                return 0;
            }

            Console.WriteLine($"[ProcessCleanup] Found {ffmpegProcesses.Length} FFmpeg process(es) - cleaning up...");

            foreach (var process in ffmpegProcesses)
            {
                try
                {
                    Console.WriteLine($"[ProcessCleanup] Killing FFmpeg process (PID: {process.Id})");

                    // Kill process tree (in case ffmpeg spawned child processes)
                    process.Kill(entireProcessTree: true);

                    // Wait for process to exit (max 2 seconds)
                    bool exited = process.WaitForExit(2000);

                    if (exited)
                    {
                        killedCount++;
                        Console.WriteLine($"[ProcessCleanup] ✓ Killed FFmpeg process (PID: {process.Id})");
                    }
                    else
                    {
                        Console.WriteLine($"[ProcessCleanup] ⚠ FFmpeg process (PID: {process.Id}) did not exit in time");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ProcessCleanup] Error killing FFmpeg process (PID: {process.Id}): {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            Console.WriteLine($"[ProcessCleanup] Cleanup complete: {killedCount} FFmpeg process(es) killed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessCleanup] Error during FFmpeg cleanup: {ex.Message}");
        }

        return killedCount;
    }
}
