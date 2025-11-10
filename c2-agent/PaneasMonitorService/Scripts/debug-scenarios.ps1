# debug-scenarios.ps1
# Helper script to test different monitoring scenarios
#
# Usage: Run as Administrator
#   .\debug-scenarios.ps1

param(
    [string]$Action,
    [string]$TaskName = "PaneasMonitorTask"
)

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    exit 1
}

function Show-Menu {
    Write-Host ""
    Write-Host "=== Debug Scenarios - PaneasMonitorService ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Choose an action:" -ForegroundColor White
    Write-Host ""
    Write-Host "  1. Check Task Scheduler (see if task exists)" -ForegroundColor Yellow
    Write-Host "  2. Disable Task Scheduler (simulate attack)" -ForegroundColor Yellow
    Write-Host "  3. Enable Task Scheduler" -ForegroundColor Yellow
    Write-Host "  4. Delete Task Scheduler (simulate removal)" -ForegroundColor Yellow
    Write-Host "  5. Run Task Scheduler manually" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  6. Check Agent.exe process" -ForegroundColor Cyan
    Write-Host "  7. Kill Agent.exe process (simulate kill)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  8. Check active user session" -ForegroundColor Magenta
    Write-Host "  9. Check PaneasMonitorService service" -ForegroundColor Magenta
    Write-Host ""
    Write-Host "  0. Exit" -ForegroundColor Gray
    Write-Host ""
}

function Check-Task {
    Write-Host ""
    Write-Host "=== Checking Task Scheduler ===" -ForegroundColor Cyan
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    if ($task) {
        Write-Host "Task found" -ForegroundColor Green
        Write-Host ""
        Write-Host "Details:" -ForegroundColor White
        Write-Host "  Name: $($task.TaskName)" -ForegroundColor Gray
        Write-Host "  State: $($task.State)" -ForegroundColor Gray
        Write-Host "  Enabled: $($task.Settings.Enabled)" -ForegroundColor Gray
        Write-Host "  Last Result: $($task.LastTaskResult)" -ForegroundColor Gray
        Write-Host "  Last Run: $($task.LastRunTime)" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Action:" -ForegroundColor White
        $action = $task.Actions[0]
        Write-Host "  Executable: $($action.Execute)" -ForegroundColor Gray
        Write-Host "  Arguments: $($action.Arguments)" -ForegroundColor Gray
    } else {
        Write-Host "Task not found" -ForegroundColor Red
        Write-Host "MonitorService should create the task automatically" -ForegroundColor Yellow
    }
}

function Disable-Task {
    Write-Host ""
    Write-Host "=== Disabling Task Scheduler ===" -ForegroundColor Cyan
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    if ($task) {
        Disable-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue | Out-Null
        Write-Host "Task disabled" -ForegroundColor Green
        Write-Host "MonitorService should re-enable the task automatically" -ForegroundColor Yellow
    } else {
        Write-Host "Task not found" -ForegroundColor Red
    }
}

function Enable-Task {
    Write-Host ""
    Write-Host "=== Enabling Task Scheduler ===" -ForegroundColor Cyan
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    if ($task) {
        Enable-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue | Out-Null
        Write-Host "Task enabled" -ForegroundColor Green
    } else {
        Write-Host "Task not found" -ForegroundColor Red
    }
}

function Delete-Task {
    Write-Host ""
    Write-Host "=== Deleting Task Scheduler ===" -ForegroundColor Cyan
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    if ($task) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        Write-Host "Task deleted" -ForegroundColor Green
        Write-Host "MonitorService should recreate the task automatically" -ForegroundColor Yellow
    } else {
        Write-Host "Task not found (already deleted)" -ForegroundColor Red
    }
}

function Run-Task {
    Write-Host ""
    Write-Host "=== Running Task Manually ===" -ForegroundColor Cyan
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    if ($task) {
        Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        Write-Host "Task executed" -ForegroundColor Green
        Write-Host "Wait a few seconds and check if Agent.exe is running" -ForegroundColor Yellow
    } else {
        Write-Host "Task not found" -ForegroundColor Red
    }
}

function Check-Agent {
    Write-Host ""
    Write-Host "=== Checking Agent.exe Process ===" -ForegroundColor Cyan
    $processes = Get-Process -Name "Agent" -ErrorAction SilentlyContinue

    if ($processes) {
        Write-Host "Agent.exe found ($($processes.Count) instance(s))" -ForegroundColor Green
        Write-Host ""
        foreach ($proc in $processes) {
            Write-Host "  PID: $($proc.Id)" -ForegroundColor Gray
            Write-Host "  Session ID: $($proc.SessionId)" -ForegroundColor Gray
            Write-Host "  CPU Time: $($proc.CPU)" -ForegroundColor Gray
            Write-Host "  Memory: $([math]::Round($proc.WorkingSet64 / 1MB, 2)) MB" -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        Write-Host "Agent.exe is not running" -ForegroundColor Red
        Write-Host "MonitorService should start Agent.exe automatically" -ForegroundColor Yellow
    }
}

function Kill-Agent {
    Write-Host ""
    Write-Host "=== Killing Agent.exe Process ===" -ForegroundColor Cyan
    $processes = Get-Process -Name "Agent" -ErrorAction SilentlyContinue

    if ($processes) {
        foreach ($proc in $processes) {
            Write-Host "Killing PID $($proc.Id)..." -ForegroundColor Yellow
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
        Write-Host "Agent.exe terminated" -ForegroundColor Green
        Write-Host "MonitorService should restart Agent.exe automatically" -ForegroundColor Yellow
    } else {
        Write-Host "Agent.exe is not running" -ForegroundColor Red
    }
}

function Check-Session {
    Write-Host ""
    Write-Host "=== Checking Active Sessions ===" -ForegroundColor Cyan

    # Get all sessions
    $sessions = query user 2>$null

    if ($sessions) {
        Write-Host "Sessions found:" -ForegroundColor Green
        Write-Host ""
        Write-Host $sessions -ForegroundColor Gray
    } else {
        Write-Host "No active user session" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "Session ID of current process: $PID -> $(Get-Process -Id $PID | Select-Object -ExpandProperty SessionId)" -ForegroundColor Gray
}

function Check-Service {
    Write-Host ""
    Write-Host "=== Checking PaneasMonitorService ===" -ForegroundColor Cyan
    $service = Get-Service -Name "PaneasMonitorService" -ErrorAction SilentlyContinue

    if ($service) {
        Write-Host "Service found" -ForegroundColor Green
        Write-Host ""
        Write-Host "Details:" -ForegroundColor White
        Write-Host "  Name: $($service.Name)" -ForegroundColor Gray
        Write-Host "  Display Name: $($service.DisplayName)" -ForegroundColor Gray
        Write-Host "  Status: $($service.Status)" -ForegroundColor Gray
        Write-Host "  Start Type: $($service.StartType)" -ForegroundColor Gray
        Write-Host ""

        if ($service.Status -eq 'Running') {
            Write-Host "Service is running" -ForegroundColor Green

            # Try to find process
            $scOutput = sc.exe queryex PaneasMonitorService
            Write-Host ""
            Write-Host "Detailed information:" -ForegroundColor White
            Write-Host $scOutput -ForegroundColor Gray
        } else {
            Write-Host "Service is NOT running" -ForegroundColor Red
            Write-Host "Start the service with: Start-Service -Name PaneasMonitorService" -ForegroundColor Yellow
        }
    } else {
        Write-Host "Service is not installed" -ForegroundColor Red
        Write-Host "Install the service with: .\install-service.ps1" -ForegroundColor Yellow
    }
}

# Main menu loop
while ($true) {
    Show-Menu
    $choice = Read-Host "Choose an option"

    switch ($choice) {
        "1" { Check-Task }
        "2" { Disable-Task }
        "3" { Enable-Task }
        "4" { Delete-Task }
        "5" { Run-Task }
        "6" { Check-Agent }
        "7" { Kill-Agent }
        "8" { Check-Session }
        "9" { Check-Service }
        "0" {
            Write-Host ""
            Write-Host "Exiting..." -ForegroundColor Gray
            exit 0
        }
        default {
            Write-Host ""
            Write-Host "Invalid option!" -ForegroundColor Red
        }
    }

    Write-Host ""
    Write-Host "Press Enter to continue..." -ForegroundColor Gray
    Read-Host
}
