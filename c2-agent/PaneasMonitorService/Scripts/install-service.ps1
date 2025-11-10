# install-service.ps1
# Production installer for PaneasMonitorService
# Installs to C:\ProgramData\C2Agent\bin\ and registers as Windows Service
#
# Usage: Run as Administrator
#   .\install-service.ps1

param(
    [string]$ServiceName = "PaneasMonitorService",
    [string]$DisplayName = "Paneas Monitor Service",
    [string]$Description = "Monitors and protects Paneas Monitor agent process",
    [switch]$SkipBuild
)

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

Write-Host "=== PaneasMonitorService Production Installer ===" -ForegroundColor Cyan
Write-Host ""

# Define paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$agentDir = Join-Path (Split-Path -Parent $projectRoot) "Agent"
$installDir = "C:\ProgramData\C2Agent\bin"

Write-Host "Installation target: $installDir" -ForegroundColor Gray
Write-Host "Service name: $ServiceName" -ForegroundColor Gray
Write-Host ""

if (-not $SkipBuild) {
    # Step 1: Build and publish Agent.exe
    Write-Host "[1/6] Building Agent.exe (Release, win-x64, self-contained)..." -ForegroundColor Cyan
    Push-Location $agentDir
    $agentBuildOutput = dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true --nologo 2>&1
    $agentBuildSuccess = $LASTEXITCODE -eq 0
    Pop-Location

    if (-not $agentBuildSuccess) {
        Write-Host "ERROR: Failed to build Agent.exe" -ForegroundColor Red
        Write-Host $agentBuildOutput -ForegroundColor Red
        exit 1
    }

    Write-Host "Agent.exe built successfully" -ForegroundColor Green
    Write-Host ""

    # Step 2: Build PaneasMonitorService
    Write-Host "[2/6] Building PaneasMonitorService (Release)..." -ForegroundColor Cyan
    Push-Location $projectRoot
    $serviceBuildOutput = dotnet build -c Release --nologo 2>&1
    $serviceBuildSuccess = $LASTEXITCODE -eq 0
    Pop-Location

    if (-not $serviceBuildSuccess) {
        Write-Host "ERROR: Failed to build PaneasMonitorService" -ForegroundColor Red
        Write-Host $serviceBuildOutput -ForegroundColor Red
        exit 1
    }

    Write-Host "PaneasMonitorService built successfully" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[1-2/6] Skipping build (using existing binaries)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 3: Create installation directory
Write-Host "[3/6] Creating installation directory..." -ForegroundColor Cyan
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Write-Host "Created: $installDir" -ForegroundColor Green
} else {
    Write-Host "Directory already exists: $installDir" -ForegroundColor Gray
}
Write-Host ""

# Step 4: Copy binaries
Write-Host "[4/6] Installing binaries to $installDir..." -ForegroundColor Cyan

# Copy Agent binaries (excluding test files)
$agentPublishDir = Join-Path $agentDir "bin\x64\Release\net8.0\win-x64\publish"
if (Test-Path $agentPublishDir) {
    Write-Host "  Copying Agent binaries (filtering unnecessary files)..." -ForegroundColor Gray

    # Copy files selectively, excluding test videos, disabled files, and RecorderHelper
    Get-ChildItem -Path $agentPublishDir -Recurse | Where-Object {
        $_.Extension -notin @('.mp4', '.avi', '.mkv', '.DISABLED') -and
        $_.Name -notmatch '^test' -and
        $_.Name -notmatch '^RecorderHelper'
    } | ForEach-Object {
        $targetPath = Join-Path $installDir $_.FullName.Substring($agentPublishDir.Length + 1)
        if ($_.PSIsContainer) {
            New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
        } else {
            $targetDir = Split-Path -Parent $targetPath
            if (-not (Test-Path $targetDir)) {
                New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
            }
            Copy-Item -Path $_.FullName -Destination $targetPath -Force
        }
    }

    Write-Host "  Agent installed (test files excluded)" -ForegroundColor Green
} else {
    Write-Host "ERROR: Agent publish directory not found: $agentPublishDir" -ForegroundColor Red
    exit 1
}

# Copy PaneasMonitorService binaries (excluding unnecessary files)
$servicePublishDir = Join-Path $projectRoot "bin\x64\Release\net8.0"
if (Test-Path $servicePublishDir) {
    Write-Host "  Copying PaneasMonitorService binaries (filtering unnecessary files)..." -ForegroundColor Gray

    # Exclude: debug symbols (.pdb), dev configs, language packs, browser runtimes
    $excludePatterns = @('\\de\\', '\\es\\', '\\fr\\', '\\it\\', '\\ja\\', '\\pl\\', '\\ru\\', '\\sv\\', '\\tr\\', '\\zh-CN\\', '\\zh-Hant\\', '\\browser\\')

    Get-ChildItem -Path $servicePublishDir -Recurse | Where-Object {
        $excluded = $false
        foreach ($pattern in $excludePatterns) {
            if ($_.FullName -match $pattern) {
                $excluded = $true
                break
            }
        }
        -not $excluded -and
        $_.Extension -ne '.pdb' -and
        $_.Name -ne 'appsettings.Development.json'
    } | ForEach-Object {
        $targetPath = Join-Path $installDir $_.FullName.Substring($servicePublishDir.Length + 1)
        if ($_.PSIsContainer) {
            New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
        } else {
            $targetDir = Split-Path -Parent $targetPath
            if (-not (Test-Path $targetDir)) {
                New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
            }
            Copy-Item -Path $_.FullName -Destination $targetPath -Force
        }
    }

    Write-Host "  PaneasMonitorService installed (localization packs excluded)" -ForegroundColor Green
} else {
    Write-Host "ERROR: PaneasMonitorService build directory not found: $servicePublishDir" -ForegroundColor Red
    exit 1
}

# Create PaneasMonitorService appsettings.json
Write-Host "  Creating appsettings.Service.json..." -ForegroundColor Gray
$serviceSettingsContent = @"
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Service": {
    "MonitorIntervalSeconds": 10,
    "AgentExecutablePath": "C:\\ProgramData\\C2Agent\\bin\\Agent.exe",
    "TaskName": "PaneasMonitorTask"
  }
}
"@

$serviceSettingsPath = Join-Path $installDir "appsettings.Service.json"
$serviceSettingsContent | Out-File -FilePath $serviceSettingsPath -Encoding UTF8 -Force
Write-Host "  appsettings.Service.json created" -ForegroundColor Green

# Copy Agent appsettings.json from source
Write-Host "  Copying Agent appsettings.json..." -ForegroundColor Gray
$agentSettingsSource = Join-Path $agentDir "appsettings.json"
$agentSettingsTarget = Join-Path $installDir "appsettings.json"

if (Test-Path $agentSettingsSource) {
    Copy-Item -Path $agentSettingsSource -Destination $agentSettingsTarget -Force

    # Modify Storage.BasePath to use ProgramData instead of LocalApplicationData
    try {
        $agentConfig = Get-Content $agentSettingsTarget -Raw | ConvertFrom-Json
        $agentConfig.Storage.BasePath = "C:\ProgramData\C2Agent"
        $agentConfig | ConvertTo-Json -Depth 10 | Out-File -FilePath $agentSettingsTarget -Encoding UTF8 -Force
        Write-Host "  Agent appsettings.json configured" -ForegroundColor Green
    } catch {
        Write-Host "  WARNING: Could not modify Storage.BasePath: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ERROR: Agent appsettings.json not found at: $agentSettingsSource" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 4.5: Download and install FFmpeg
Write-Host "  Downloading FFmpeg (if needed)..." -ForegroundColor Gray

$ffmpegDir = Join-Path $installDir "ffmpeg"
$ffmpegExePath = Join-Path $ffmpegDir "ffmpeg.exe"

if (-not (Test-Path $ffmpegExePath)) {
    try {
        New-Item -ItemType Directory -Path $ffmpegDir -Force | Out-Null

        $ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
        $tempZip = Join-Path $env:TEMP "ffmpeg-temp.zip"

        Write-Host "  Downloading from: $ffmpegUrl" -ForegroundColor Gray
        Invoke-WebRequest -Uri $ffmpegUrl -OutFile $tempZip -UseBasicParsing

        Write-Host "  Extracting ffmpeg.exe..." -ForegroundColor Gray
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($tempZip)
        $ffmpegEntry = $zip.Entries | Where-Object { $_.Name -eq "ffmpeg.exe" } | Select-Object -First 1

        if ($ffmpegEntry) {
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($ffmpegEntry, $ffmpegExePath, $true)
            Write-Host "  FFmpeg installed successfully" -ForegroundColor Green
        } else {
            Write-Host "  WARNING: ffmpeg.exe not found in archive" -ForegroundColor Yellow
        }

        $zip.Dispose()
        Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Host "  WARNING: Failed to download FFmpeg: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "  Agent will download it automatically on first use" -ForegroundColor Gray
    }
} else {
    Write-Host "  FFmpeg already installed" -ForegroundColor Gray
}
Write-Host ""

# Step 4.6: Create scheduled task
Write-Host "  Creating scheduled task..." -ForegroundColor Gray
$createTaskScript = Join-Path $scriptDir "create-task.ps1"
$agentExePath = Join-Path $installDir "Agent.exe"

if (Test-Path $createTaskScript) {
    try {
        & $createTaskScript -AgentPath $agentExePath -TaskName "PaneasMonitorTask" | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Scheduled task created successfully" -ForegroundColor Green
        } else {
            Write-Host "  WARNING: Failed to create scheduled task (exit code: $LASTEXITCODE)" -ForegroundColor Yellow
            Write-Host "  PaneasMonitorService will attempt to create it automatically" -ForegroundColor Gray
        }
    } catch {
        Write-Host "  WARNING: Error creating task: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  WARNING: create-task.ps1 not found at: $createTaskScript" -ForegroundColor Yellow
}
Write-Host ""

# Verify installation
$serviceExePath = Join-Path $installDir "PaneasMonitorService.exe"

if ((Test-Path $agentExePath) -and (Test-Path $serviceExePath)) {
    Write-Host "Binaries installed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Installed files:" -ForegroundColor White
    Write-Host "  Agent.exe:                  $agentExePath" -ForegroundColor Gray
    Write-Host "  PaneasMonitorService.exe:   $serviceExePath" -ForegroundColor Gray
    Write-Host "  appsettings.json (Agent):   $agentSettingsTarget" -ForegroundColor Gray
    Write-Host "  appsettings.Service.json:   $serviceSettingsPath" -ForegroundColor Gray
    Write-Host ""
} else {
    Write-Host "ERROR: Installation verification failed" -ForegroundColor Red
    Write-Host "Missing files in: $installDir" -ForegroundColor Yellow
    exit 1
}

# Step 5: Check if service already exists
Write-Host "[5/6] Checking for existing service..." -ForegroundColor Cyan
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host ""
    Write-Host "WARNING: Service '$ServiceName' already exists!" -ForegroundColor Yellow
    Write-Host "Current status: $($existingService.Status)" -ForegroundColor Gray
    Write-Host ""

    $response = Read-Host "Do you want to uninstall and reinstall? (Y/N)"
    if ($response -eq 'Y' -or $response -eq 'y') {
        Write-Host "Stopping service..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2

        Write-Host "Removing service..." -ForegroundColor Yellow
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
        Write-Host "Service removed" -ForegroundColor Green
    } else {
        Write-Host "Installation cancelled by user" -ForegroundColor Yellow
        exit 0
    }
}
Write-Host ""

# Step 6: Install service
Write-Host "[6/6] Installing Windows Service..." -ForegroundColor Cyan
$result = sc.exe create $ServiceName binPath= $serviceExePath start= auto DisplayName= $DisplayName

if ($LASTEXITCODE -eq 0) {
    Write-Host "Service registered successfully" -ForegroundColor Green

    # Set description
    sc.exe description $ServiceName $Description | Out-Null

    # Configure recovery options (restart on failure)
    Write-Host "Configuring recovery options..." -ForegroundColor Gray
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    Write-Host "Recovery options configured" -ForegroundColor Green

    # Start service
    Write-Host ""
    Write-Host "Starting service..." -ForegroundColor Cyan
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2

    $service = Get-Service -Name $ServiceName
    if ($service.Status -eq 'Running') {
        Write-Host "Service started successfully!" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Service installed but did not start. Status: $($service.Status)" -ForegroundColor Yellow
        Write-Host "Check Event Viewer logs (Application)" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "=== Installation Complete ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Service Details:" -ForegroundColor White
    Write-Host "  Name: $ServiceName" -ForegroundColor Gray
    Write-Host "  Path: $serviceExePath" -ForegroundColor Gray
    Write-Host "  Agent: $agentExePath" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Management Commands:" -ForegroundColor White
    Write-Host "  Check logs:   Get-EventLog -LogName Application -Source $ServiceName -Newest 20" -ForegroundColor Gray
    Write-Host "  Stop service: Stop-Service -Name $ServiceName" -ForegroundColor Gray
    Write-Host "  Uninstall:    .\uninstall-service.ps1" -ForegroundColor Gray

} else {
    Write-Host "ERROR: Failed to install service" -ForegroundColor Red
    Write-Host "Error code: $LASTEXITCODE" -ForegroundColor Red
    exit 1
}
