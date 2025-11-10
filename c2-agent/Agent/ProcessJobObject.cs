using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Agent;

/// <summary>
/// Windows Job Object wrapper to ensure child processes are automatically killed when parent exits.
/// Uses the JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE flag to tell Windows to kill all processes
/// in the job when the job handle is closed (which happens when Agent.exe exits).
/// </summary>
public class ProcessJobObject : IDisposable
{
    private SafeJobHandle? _jobHandle;
    private bool _disposed;

    #region P/Invoke Declarations

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeJobHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle hJob,
        JobObjectInfoType infoType,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    [Flags]
    private enum JobObjectLimit : uint
    {
        KillOnJobClose = 0x00002000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public JobObjectLimit LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }

    #endregion

    /// <summary>
    /// Create a new Job Object with KillOnJobClose flag.
    /// When this job object is disposed, Windows will automatically kill all assigned processes.
    /// </summary>
    public ProcessJobObject()
    {
        // Create anonymous job object
        _jobHandle = CreateJobObject(IntPtr.Zero, null);

        if (_jobHandle.IsInvalid)
        {
            throw new InvalidOperationException(
                $"Failed to create Job Object. Error: {Marshal.GetLastWin32Error()}");
        }

        // Configure job to kill all processes when job handle is closed
        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimit.KillOnJobClose
            }
        };

        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);

        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

            bool success = SetInformationJobObject(
                _jobHandle,
                JobObjectInfoType.ExtendedLimitInformation,
                extendedInfoPtr,
                (uint)length);

            if (!success)
            {
                throw new InvalidOperationException(
                    $"Failed to configure Job Object. Error: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    /// <summary>
    /// Assign a process to this Job Object.
    /// When the job is closed (app exits), Windows will automatically kill the process.
    /// </summary>
    /// <param name="process">Process to assign</param>
    /// <returns>True if successful</returns>
    public bool AssignProcess(Process process)
    {
        if (_disposed || _jobHandle == null || _jobHandle.IsInvalid)
        {
            throw new ObjectDisposedException(nameof(ProcessJobObject));
        }

        bool success = AssignProcessToJobObject(_jobHandle, process.Handle);

        if (!success)
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"[ProcessJobObject] Failed to assign process (PID: {process.Id}) to job. Error: {error}");
            return false;
        }

        Console.WriteLine($"[ProcessJobObject] Process (PID: {process.Id}) assigned to job - will auto-kill on Agent exit");
        return true;
    }

    /// <summary>
    /// Create a Job Object and immediately assign a process to it.
    /// </summary>
    /// <param name="process">Process to assign</param>
    /// <returns>ProcessJobObject instance (must be kept alive) or null on failure</returns>
    public static ProcessJobObject? CreateAndAssign(Process process)
    {
        try
        {
            var jobObject = new ProcessJobObject();

            if (jobObject.AssignProcess(process))
            {
                return jobObject;
            }
            else
            {
                jobObject.Dispose();
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessJobObject] Error creating job object: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Closing the job handle will automatically kill all assigned processes
        // due to JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE flag
        _jobHandle?.Dispose();
        _jobHandle = null;
    }
}
