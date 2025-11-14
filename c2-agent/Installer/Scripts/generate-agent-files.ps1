# generate-agent-files.ps1
# Generates AgentFiles.wxs with all files from Agent publish directory

param(
    [string]$PublishDir = "..\..\Agent\bin\x64\Release\net8.0\win-x64\publish",
    [string]$OutputFile = "..\AgentFiles.wxs"
)

$ErrorActionPreference = "Stop"

# Resolve paths
$scriptDir = $PSScriptRoot
$publishPath = Join-Path $scriptDir $PublishDir | Resolve-Path
$outputPath = Join-Path $scriptDir $OutputFile

Write-Host "Generating AgentFiles.wxs from: $publishPath"
Write-Host "Output: $outputPath"
Write-Host ""

# Get all files (excluding subdirectories like ffmpeg)
$files = Get-ChildItem -Path $publishPath -File | Where-Object {
    $_.Name -notlike "*.pdb" -and  # Exclude debug symbols
    $_.Name -ne "appsettings.json" # Already defined separately
}

Write-Host "Found $($files.Count) files to include"

# Generate XML
$xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">

  <Fragment>
    <!-- Auto-generated Agent Files (DO NOT EDIT MANUALLY) -->
    <!-- Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss") -->
    <!-- Files from: $($publishPath.Path) -->

    <ComponentGroup Id="AgentBinaries" Directory="BinFolder">

"@

# Generate components for each file
$componentIndex = 1
foreach ($file in $files) {
    $fileName = $file.Name
    $fileId = "AgentFile$componentIndex"
    $componentId = "AgentComp$componentIndex"
    $guid = [guid]::NewGuid().ToString().ToUpper()

    # Use relative path from Installer directory
    $relativePath = "`$(var.ProjectDir)\..\Agent\bin\x64\Release\net8.0\win-x64\publish\$fileName"

    $xml += @"

      <!-- $fileName -->
      <Component Id="$componentId" Guid="$guid">
        <File Id="$fileId"
              Name="$fileName"
              Source="$relativePath"
              KeyPath="yes" />
      </Component>
"@

    $componentIndex++
}

$xml += @"

    </ComponentGroup>

  </Fragment>

</Wix>
"@

# Write to file
$xml | Out-File -FilePath $outputPath -Encoding UTF8

Write-Host ""
Write-Host "Successfully generated AgentFiles.wxs with $($files.Count) files!" -ForegroundColor Green
Write-Host "ComponentGroup ID: AgentBinaries" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Add <ComponentGroupRef Id='AgentBinaries' /> to Product.wxs Feature"
Write-Host "  2. Build MSI with: build-all-languages.ps1 -SkipBuild"
