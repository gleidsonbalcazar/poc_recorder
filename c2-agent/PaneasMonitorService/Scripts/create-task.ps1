# create-task.ps1
# Manually creates the PaneasMonitorTask scheduled task
# Run as Administrator

param(
    [string]$TaskName = "PaneasMonitorTask",
    [string]$AgentPath = "C:\ProgramData\C2Agent\bin\Agent.exe"
)

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

Write-Host "=== Creating Scheduled Task: $TaskName ===" -ForegroundColor Cyan
Write-Host ""

# Check if Agent.exe exists
if (-not (Test-Path $AgentPath)) {
    Write-Host "ERROR: Agent.exe not found at: $AgentPath" -ForegroundColor Red
    exit 1
}

Write-Host "Agent executable verified: $AgentPath" -ForegroundColor Gray
Write-Host ""

# Remove existing task if present
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removing existing task..." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Existing task removed" -ForegroundColor Green
    Write-Host ""
}

# Create task components
Write-Host "Creating task components..." -ForegroundColor Gray

$action = New-ScheduledTaskAction `
    -Execute $AgentPath `
    -WorkingDirectory (Split-Path $AgentPath)

$trigger = New-ScheduledTaskTrigger -AtLogOn

$principal = New-ScheduledTaskPrincipal `
    -GroupId "INTERACTIVE" `
    -RunLevel Highest

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew `
    -StartWhenAvailable

Write-Host "Registering task..." -ForegroundColor Gray

# Register task
try {
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description "Paneas Monitor - Auto-start Agent" | Out-Null

    Write-Host "Task registered successfully" -ForegroundColor Green
} catch {
    Write-Host "ERROR: Failed to register task: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Verify task creation
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($task) {
    Write-Host "=== Task Created Successfully ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Task Details:" -ForegroundColor White
    Write-Host "  Name:        $TaskName" -ForegroundColor Gray
    Write-Host "  State:       $($task.State)" -ForegroundColor Gray
    Write-Host "  Enabled:     $($task.Settings.Enabled)" -ForegroundColor Gray
    Write-Host "  Action:      $AgentPath" -ForegroundColor Gray
    Write-Host "  Working Dir: $(Split-Path $AgentPath)" -ForegroundColor Gray
    Write-Host "  Trigger:     At Logon (any user)" -ForegroundColor Gray
    Write-Host "  Run Level:   Highest" -ForegroundColor Gray
    Write-Host ""
    Write-Host "The task will automatically start Agent.exe when a user logs in." -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "ERROR: Task verification failed - task not found after registration" -ForegroundColor Red
    exit 1
}
