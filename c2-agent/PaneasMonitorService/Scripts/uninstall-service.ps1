# uninstall-service.ps1
# Uninstalls PaneasMonitorService Windows Service
#
# Usage: Run as Administrator
#   .\uninstall-service.ps1

param(
    [string]$ServiceName = "PaneasMonitorService"
)

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

Write-Host "=== Uninstalling PaneasMonitorService ===" -ForegroundColor Cyan
Write-Host ""

# Check if service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "WARNING: Service '$ServiceName' is not installed" -ForegroundColor Yellow
    exit 0
}

Write-Host "Service found:" -ForegroundColor Gray
Write-Host "  Name: $($service.Name)" -ForegroundColor White
Write-Host "  Status: $($service.Status)" -ForegroundColor White
Write-Host ""

# Confirm uninstall
$response = Read-Host "Do you want to uninstall the service? (Y/N)"
if ($response -ne 'Y' -and $response -ne 'y') {
    Write-Host "Uninstallation cancelled by user" -ForegroundColor Yellow
    exit 0
}

# Stop service if running
if ($service.Status -eq 'Running') {
    Write-Host ""
    Write-Host "Stopping service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    $service = Get-Service -Name $ServiceName
    if ($service.Status -eq 'Stopped') {
        Write-Host "Service stopped" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Service may not have stopped completely (Status: $($service.Status))" -ForegroundColor Yellow
        Write-Host "Trying to uninstall anyway..." -ForegroundColor Gray
    }
}

# Remove service
Write-Host ""
Write-Host "Removing Windows Service..." -ForegroundColor Cyan
$result = sc.exe delete $ServiceName

if ($LASTEXITCODE -eq 0) {
    Write-Host "Service removed" -ForegroundColor Green
} else {
    Write-Host "ERROR: Failed to remove service (error code: $LASTEXITCODE)" -ForegroundColor Red
    Write-Host "Continuing with cleanup anyway..." -ForegroundColor Yellow
}

# Remove scheduled task
Write-Host ""
Write-Host "Removing scheduled task..." -ForegroundColor Cyan
$task = Get-ScheduledTask -TaskName "PaneasMonitorTask" -ErrorAction SilentlyContinue
if ($task) {
    try {
        Unregister-ScheduledTask -TaskName "PaneasMonitorTask" -Confirm:$false
        Write-Host "Scheduled task removed" -ForegroundColor Green
    } catch {
        Write-Host "Warning: Failed to remove scheduled task: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "Scheduled task not found (already removed or never created)" -ForegroundColor Gray
}

# Kill running processes
Write-Host ""
Write-Host "Stopping running processes..." -ForegroundColor Cyan

# Kill Agent.exe
$agentProcesses = Get-Process -Name "Agent" -ErrorAction SilentlyContinue
if ($agentProcesses) {
    Write-Host "Found $($agentProcesses.Count) Agent process(es) - stopping..." -ForegroundColor Gray
    Stop-Process -Name "Agent" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Host "Agent processes stopped" -ForegroundColor Green
} else {
    Write-Host "No Agent processes running" -ForegroundColor Gray
}

# Kill FFmpeg (in case orphaned)
$ffmpegProcesses = Get-Process -Name "ffmpeg" -ErrorAction SilentlyContinue
if ($ffmpegProcesses) {
    Write-Host "Found $($ffmpegProcesses.Count) FFmpeg process(es) - stopping..." -ForegroundColor Gray
    Stop-Process -Name "ffmpeg" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Host "FFmpeg processes stopped" -ForegroundColor Green
} else {
    Write-Host "No FFmpeg processes running" -ForegroundColor Gray
}

# Remove binaries and data
Write-Host ""
Write-Host "Removing installation files..." -ForegroundColor Cyan

$installDir = "C:\ProgramData\C2Agent"
$removeAll = $null

if (Test-Path $installDir) {
    Write-Host ""
    Write-Host "The installation directory contains:" -ForegroundColor White
    Write-Host "  - Binaries (Agent.exe, PaneasMonitorService.exe, FFmpeg)" -ForegroundColor Gray
    Write-Host "  - Configuration files (appsettings.json)" -ForegroundColor Gray
    Write-Host "  - Recorded videos (if any)" -ForegroundColor Gray
    Write-Host "  - Logs" -ForegroundColor Gray
    Write-Host ""

    $removeAll = Read-Host "Do you want to remove ALL files including videos and logs? (Y/N)"

    if ($removeAll -eq 'Y' -or $removeAll -eq 'y') {
        try {
            Remove-Item -Path $installDir -Recurse -Force -ErrorAction Stop
            Write-Host "Installation directory removed: $installDir" -ForegroundColor Green
        } catch {
            Write-Host "Warning: Failed to remove some files: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "You may need to manually delete: $installDir" -ForegroundColor Yellow
        }
    } else {
        Write-Host "Installation directory preserved: $installDir" -ForegroundColor Yellow
        Write-Host "You can manually delete it later if needed" -ForegroundColor Gray
    }
} else {
    Write-Host "Installation directory not found (already cleaned)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Uninstall Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Removed:" -ForegroundColor White
Write-Host "  [OK] Windows Service: $ServiceName" -ForegroundColor Gray
Write-Host "  [OK] Scheduled Task: PaneasMonitorTask" -ForegroundColor Gray
Write-Host "  [OK] Running Processes: Agent.exe, FFmpeg" -ForegroundColor Gray
if (Test-Path $installDir) {
    if ($removeAll -eq 'Y' -or $removeAll -eq 'y') {
        Write-Host "  [OK] Installation Files: $installDir" -ForegroundColor Gray
    } else {
        Write-Host "  [!] Installation Files: Preserved at $installDir" -ForegroundColor Yellow
    }
} else {
    Write-Host "  [OK] Installation Files: Not found (already cleaned)" -ForegroundColor Gray
}
Write-Host ""
Write-Host "PaneasMonitorService has been completely removed" -ForegroundColor Cyan
