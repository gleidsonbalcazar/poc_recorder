using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.TaskScheduler;

// Alias to resolve naming conflict between System.Threading.Tasks.Task and TaskScheduler.Task
using TaskSchedulerTask = Microsoft.Win32.TaskScheduler.Task;
using SystemTask = System.Threading.Tasks.Task;

namespace PaneasMonitorService;

public class MonitorService : BackgroundService
{
    private readonly ILogger<MonitorService> _logger;
    private readonly IConfiguration _configuration;
    private readonly int _monitorIntervalSeconds;
    private readonly string _agentExecutablePath;
    private readonly string _taskName;
    private readonly string _taskXmlPath;

    public MonitorService(ILogger<MonitorService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Load configuration
        _monitorIntervalSeconds = configuration.GetValue<int>("Service:MonitorIntervalSeconds", 10);
        _agentExecutablePath = configuration.GetValue<string>("Service:AgentExecutablePath")
            ?? "C:\\Program Files\\Paneas Monitor\\bin\\Agent.exe";
        _taskName = configuration.GetValue<string>("Service:TaskName") ?? "PaneasMonitorTask";
        _taskXmlPath = configuration.GetValue<string>("Service:TaskXmlPath")
            ?? "C:\\Program Files\\Paneas Monitor\\config\\task-definition.xml";
    }

    protected override async SystemTask ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaneasMonitorService iniciado");
        _logger.LogInformation("Intervalo de monitoramento: {Interval}s", _monitorIntervalSeconds);
        _logger.LogInformation("Agent path: {Path}", _agentExecutablePath);
        _logger.LogInformation("Task name: {TaskName}", _taskName);

        // Wait a bit before starting monitoring (let system boot complete)
        await SystemTask.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Ensure Task Scheduler task exists and is enabled
                EnsureTaskExistsAndEnabled();

                // 2. Check if user session is active
                if (IsUserSessionActive())
                {
                    // 3. Check if Agent.exe is running in active session
                    if (!IsAgentRunning())
                    {
                        _logger.LogWarning("Agent.exe não está rodando na sessão ativa. Forçando execução da task...");
                        ForceRunTask();
                    }
                    else
                    {
                        _logger.LogDebug("Agent.exe está rodando. Tudo OK.");
                    }
                }
                else
                {
                    _logger.LogDebug("Nenhuma sessão de usuário ativa. Aguardando login...");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no loop de monitoramento");
            }

            // Wait before next check
            await SystemTask.Delay(TimeSpan.FromSeconds(_monitorIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("PaneasMonitorService parando...");
    }

    #region Task Scheduler Management

    private void EnsureTaskExistsAndEnabled()
    {
        try
        {
            using var ts = new TaskService();

            // Try to get existing task
            var task = ts.GetTask(_taskName);

            if (task == null)
            {
                _logger.LogWarning("Task '{TaskName}' not found. Attempting to create...", _taskName);

                try
                {
                    CreateScheduledTask();
                    _logger.LogInformation("Task created successfully");
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogError(ex, "PERMISSION DENIED: Cannot create task. Service must run as Administrator or SYSTEM");
                    _logger.LogError("Please run create-task.ps1 manually as Administrator");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create task: {Message}", ex.Message);
                }
            }
            else
            {
                // Check if task is enabled
                if (!task.Enabled)
                {
                    _logger.LogWarning("Task '{TaskName}' is disabled. Enabling...", _taskName);
                    task.Enabled = true;
                }

                // Log task configuration details
                _logger.LogDebug("Task '{TaskName}' configuration:", _taskName);
                _logger.LogDebug("  Enabled: {Enabled}", task.Enabled);
                _logger.LogDebug("  State: {State}", task.State);
                _logger.LogDebug("  Last Run: {LastRun}", task.LastRunTime);
                _logger.LogDebug("  Last Result: 0x{Result:X}", task.LastTaskResult);

                if (task.Definition.Actions.Count > 0 && task.Definition.Actions[0] is ExecAction action)
                {
                    _logger.LogDebug("  Action Path: {Path}", action.Path);
                    _logger.LogDebug("  Working Dir: {Dir}", action.WorkingDirectory ?? "(not set)");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao verificar/criar task");
        }
    }

    private void CreateScheduledTask()
    {
        try
        {
            // Validate Agent.exe exists before creating task
            if (!File.Exists(_agentExecutablePath))
            {
                _logger.LogError("Cannot create task: Agent.exe not found at: {Path}", _agentExecutablePath);
                return;
            }
            _logger.LogInformation("Agent executable verified at: {Path}", _agentExecutablePath);

            using var ts = new TaskService();

            // Create task definition
            var td = ts.NewTask();
            td.RegistrationInfo.Description = "Paneas Monitor - Auto-start Agent";
            td.RegistrationInfo.Author = "Paneas Monitor Service";

            // Trigger: When any user logs on
            td.Triggers.Add(new LogonTrigger());

            // Action: Run Agent.exe
            td.Actions.Add(new ExecAction(_agentExecutablePath, null, Path.GetDirectoryName(_agentExecutablePath)));

            // Settings
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.StopIfGoingOnBatteries = false;
            td.Settings.AllowHardTerminate = false;
            td.Settings.StartWhenAvailable = true;
            td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
            td.Settings.ExecutionTimeLimit = TimeSpan.Zero; // No time limit
            td.Settings.Hidden = false;
            td.Settings.Enabled = true;

            // Principal: Run in user's session when they log on
            // Use INTERACTIVE group (SID: S-1-5-4) for universal compatibility across all Windows languages
            // INTERACTIVE = currently logged-in user with active desktop session (perfect for screen recording)
            td.Principal.GroupId = "INTERACTIVE";
            td.Principal.LogonType = TaskLogonType.Group;
            td.Principal.RunLevel = TaskRunLevel.Highest;

            // Register task
            ts.RootFolder.RegisterTaskDefinition(
                _taskName,
                td,
                TaskCreation.CreateOrUpdate,
                "INTERACTIVE", // Group name
                null, // No password
                TaskLogonType.Group);

            _logger.LogInformation("Task '{TaskName}' created successfully", _taskName);
            _logger.LogInformation("  Executable: {Exe}", _agentExecutablePath);
            _logger.LogInformation("  Working Dir: {Dir}", Path.GetDirectoryName(_agentExecutablePath));
            _logger.LogInformation("  Trigger: Logon (any user)");
            _logger.LogInformation("  Run Level: Highest");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar task");
        }
    }

    private void ForceRunTask()
    {
        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask(_taskName);

            if (task != null)
            {
                var runningTask = task.Run();
                _logger.LogInformation("Task '{TaskName}' execution requested", _taskName);

                // Wait and verify if Agent started
                SystemTask.Delay(2000).Wait();
                if (IsAgentRunning())
                {
                    _logger.LogInformation("Agent.exe started successfully after task execution");
                }
                else
                {
                    _logger.LogWarning("Agent.exe not detected after task execution - may have failed or crashed");
                    _logger.LogWarning("Check Task Scheduler history: taskschd.msc > Task Scheduler Library > {TaskName}", _taskName);
                }
            }
            else
            {
                _logger.LogError("Cannot execute task '{TaskName}' - task not found", _taskName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing task");
        }
    }

    #endregion

    #region Session and Process Detection

    private bool IsUserSessionActive()
    {
        try
        {
            uint sessionId = WTSGetActiveConsoleSessionId();

            // 0xFFFFFFFF means no active session
            if (sessionId == 0xFFFFFFFF)
            {
                return false;
            }

            // Session 0 is system session (not a user session)
            if (sessionId == 0)
            {
                return false;
            }

            _logger.LogDebug("Sessão ativa detectada: Session ID {SessionId}", sessionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao verificar sessão ativa");
            return false;
        }
    }

    private bool IsAgentRunning()
    {
        try
        {
            uint activeSessionId = WTSGetActiveConsoleSessionId();

            if (activeSessionId == 0xFFFFFFFF || activeSessionId == 0)
            {
                return false;
            }

            // Get all processes named "Agent"
            var agentProcesses = Process.GetProcessesByName("Agent");
            _logger.LogDebug("Found {Count} Agent processes", agentProcesses.Length);

            foreach (var process in agentProcesses)
            {
                try
                {
                    uint processSessionId = GetProcessSessionId(process.Id);

                    if (processSessionId == activeSessionId)
                    {
                        _logger.LogDebug("Agent.exe encontrado rodando (PID: {PID}, Session: {SessionId})",
                            process.Id, processSessionId);
                        return true;
                    }
                }
                catch
                {
                    // Process may have exited, continue checking others
                    continue;
                }
            }

            _logger.LogDebug("Agent.exe não encontrado na sessão ativa {SessionId}", activeSessionId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao verificar processo Agent.exe");
            return false;
        }
    }

    private uint GetProcessSessionId(int processId)
    {
        if (ProcessIdToSessionId((uint)processId, out uint sessionId))
        {
            return sessionId;
        }
        return 0xFFFFFFFF;
    }

    #endregion

    #region Windows API P/Invoke

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    #endregion
}
