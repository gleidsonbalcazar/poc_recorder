# test-console.ps1
# Development installer and runner for PaneasMonitorService
# Installs to C:\ProgramData\C2Agent\bin\ and runs in console mode
#
# Usage: Run as Administrator
#   .\test-console.ps1

param(
    [switch]$SkipBuild
)

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "WARNING: This script should be run as Administrator!" -ForegroundColor Yellow
    Write-Host "Some features (Task Scheduler, Session detection) may not work" -ForegroundColor Gray
    Write-Host ""
    $response = Read-Host "Do you want to continue anyway? (Y/N)"
    if ($response -ne 'Y' -and $response -ne 'y') {
        Write-Host "Execution cancelled" -ForegroundColor Yellow
        exit 0
    }
}

Write-Host "=== PaneasMonitorService Development Installer ===" -ForegroundColor Cyan
Write-Host ""

# Define paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$agentDir = Join-Path (Split-Path -Parent $projectRoot) "Agent"
$installDir = "C:\ProgramData\C2Agent\bin"

Write-Host "Installation target: $installDir" -ForegroundColor Gray
Write-Host ""

if (-not $SkipBuild) {
    # Step 1: Build and publish Agent.exe
    Write-Host "[1/5] Building Agent.exe (Release, win-x64, self-contained)..." -ForegroundColor Cyan
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
    Write-Host "[2/5] Building PaneasMonitorService (Release)..." -ForegroundColor Cyan
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
    Write-Host "[1-2/5] Skipping build (using existing binaries)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 3: Create installation directory
Write-Host "[3/5] Creating installation directory..." -ForegroundColor Cyan
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Write-Host "Created: $installDir" -ForegroundColor Green
} else {
    Write-Host "Directory already exists: $installDir" -ForegroundColor Gray
}
Write-Host ""

# Step 4: Copy binaries
Write-Host "[4/5] Installing binaries to $installDir..." -ForegroundColor Cyan

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
    Write-Host "Installation complete!" -ForegroundColor Green
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

# Step 4.7: Test Agent.exe configuration
Write-Host "[4.7/5] Testing Agent.exe configuration..." -ForegroundColor Cyan
Push-Location $installDir

try {
    # Start Agent in background briefly to check if it loads config
    Write-Host "  Starting Agent.exe (test)..." -ForegroundColor Gray
    $agentProc = Start-Process -FilePath ".\Agent.exe" -NoNewWindow -PassThru
    Start-Sleep -Seconds 3

    if ($agentProc.HasExited) {
        Write-Host "  WARNING: Agent.exe exited immediately (exit code: $($agentProc.ExitCode))" -ForegroundColor Yellow
        Write-Host "  This may indicate a configuration error" -ForegroundColor Gray
        Write-Host "  Check: $agentSettingsTarget" -ForegroundColor Gray
    } else {
        Write-Host "  Agent.exe started successfully (test passed)" -ForegroundColor Green
        Write-Host "  Stopping test Agent..." -ForegroundColor Gray
        Stop-Process -Id $agentProc.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
} catch {
    Write-Host "  WARNING: Could not test Agent.exe: $($_.Exception.Message)" -ForegroundColor Yellow
}

Pop-Location
Write-Host ""

# Step 5: Run service
Write-Host "[5/5] Running PaneasMonitorService in console mode..." -ForegroundColor Cyan
Write-Host "Press Ctrl+C to stop" -ForegroundColor Yellow
Write-Host ""
Write-Host "Logs:" -ForegroundColor White
Write-Host "----------------------------------------" -ForegroundColor Gray

try {
    Push-Location $installDir
    & ".\PaneasMonitorService.exe"
    Pop-Location
} catch {
    Pop-Location
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "----------------------------------------" -ForegroundColor Gray
Write-Host "Execution finished" -ForegroundColor Cyan
