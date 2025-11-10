# build-all-languages.ps1
# Builds Paneas C2 Agent MSI installers for all supported languages

param(
    [switch]$Clean,
    [switch]$SkipBuild,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Script directory
$scriptDir = $PSScriptRoot
$installerDir = Split-Path $scriptDir -Parent
$rootDir = Split-Path $installerDir -Parent
$outputDir = Join-Path $installerDir "bin\$Configuration"

Write-Host "=======================================" -ForegroundColor Cyan
Write-Host " Paneas C2 Agent - MSI Build Script" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "WARNING: Not running as Administrator. Some operations may fail." -ForegroundColor Yellow
    Write-Host ""
}

# Clean output directory
if ($Clean) {
    Write-Host "[1/5] Cleaning output directory..." -ForegroundColor Yellow
    if (Test-Path $outputDir) {
        Remove-Item -Path $outputDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    Write-Host "      Output directory cleaned" -ForegroundColor Green
} else {
    Write-Host "[1/5] Using existing output directory" -ForegroundColor Gray
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
Write-Host ""

# Build Agent and Service binaries
if (-not $SkipBuild) {
    Write-Host "[2/5] Building Agent and Service binaries..." -ForegroundColor Yellow

    # Build Agent.exe
    Write-Host "      Building Agent.exe ($Configuration)..." -ForegroundColor Gray
    $agentDir = Join-Path $rootDir "Agent"
    Push-Location $agentDir
    dotnet publish -c $Configuration -r win-x64 --self-contained -p:PublishSingleFile=true -v quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "      ERROR: Failed to build Agent.exe" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    Pop-Location
    Write-Host "      Agent.exe built successfully" -ForegroundColor Green

    # Build PaneasMonitorService.exe
    Write-Host "      Building PaneasMonitorService.exe ($Configuration)..." -ForegroundColor Gray
    $serviceDir = Join-Path $rootDir "PaneasMonitorService"
    Push-Location $serviceDir
    dotnet publish -c $Configuration -r win-x64 --self-contained -p:PublishSingleFile=true -v quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "      ERROR: Failed to build PaneasMonitorService.exe" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    Pop-Location
    Write-Host "      PaneasMonitorService.exe built successfully" -ForegroundColor Green
} else {
    Write-Host "[2/5] Skipping binary build (using existing binaries)" -ForegroundColor Gray
}
Write-Host ""

# Check if FFmpeg exists
Write-Host "[3/5] Checking FFmpeg..." -ForegroundColor Yellow
$ffmpegPath = Join-Path $rootDir "Agent\ffmpeg\ffmpeg.exe"
if (Test-Path $ffmpegPath) {
    Write-Host "      FFmpeg found: $ffmpegPath" -ForegroundColor Green
} else {
    Write-Host "      WARNING: FFmpeg not found at $ffmpegPath" -ForegroundColor Yellow
    Write-Host "      MSI build may fail. Please download FFmpeg first." -ForegroundColor Yellow
}
Write-Host ""

# Build MSI for each language
Write-Host "[4/5] Building MSI installers..." -ForegroundColor Yellow
Write-Host ""

$languages = @("pt-BR")  # TODO: Add es-MX and en-US when ready for multi-language release
$builtMSIs = @()

foreach ($lang in $languages) {
    Write-Host "  Building $lang installer..." -ForegroundColor Cyan

    # Output MSI name
    $msiName = "PaneasC2Agent-1.0.0-$lang.msi"
    $msiPath = Join-Path $outputDir $msiName

    # WiX build command
    $wxsFiles = @(
        "Product.wxs",
        "Files.wxs",
        "ServiceInstall.wxs",
        "TaskScheduler.wxs",
        "Dialogs\ConfigurationDialog.wxs"
    )

    $wxsArgs = @()
    foreach ($wxs in $wxsFiles) {
        $wxsArgs += Join-Path $installerDir $wxs
    }

    # Localization file
    $locFile = Join-Path $installerDir "Localization\Product_$lang.wxl"

    try {
        # Build MSI
        & wix build $wxsArgs `
            -ext WixToolset.UI.wixext `
            -ext WixToolset.Util.wixext `
            -culture $lang `
            -loc $locFile `
            -d "ProjectDir=$installerDir" `
            -out $msiPath `
            2>&1 | Out-Null

        if ($LASTEXITCODE -eq 0) {
            Write-Host "  [OK] Built: $msiName" -ForegroundColor Green
            $builtMSIs += $msiPath
        } else {
            Write-Host "  [!] Failed to build $lang MSI (exit code: $LASTEXITCODE)" -ForegroundColor Red
        }
    } catch {
        Write-Host "  [!] Error building $lang MSI: $($_.Exception.Message)" -ForegroundColor Red
    }
    Write-Host ""
}

# Summary
Write-Host "[5/5] Build Summary" -ForegroundColor Yellow
Write-Host ""
if ($builtMSIs.Count -gt 0) {
    Write-Host "Successfully built $($builtMSIs.Count) MSI installer(s):" -ForegroundColor Green
    foreach ($msi in $builtMSIs) {
        $msiInfo = Get-Item $msi
        $sizeMB = [math]::Round($msiInfo.Length / 1MB, 2)
        Write-Host "  - $($msiInfo.Name) (${sizeMB} MB)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "Output directory: $outputDir" -ForegroundColor White
} else {
    Write-Host "No MSI installers were built successfully." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host " Build Complete!" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "  1. Test installation: msiexec /i `"$($builtMSIs[0])`"" -ForegroundColor Gray
Write-Host "  2. Sign installers: .\sign-installers.ps1" -ForegroundColor Gray
Write-Host "  3. Distribute to clients" -ForegroundColor Gray
Write-Host ""
