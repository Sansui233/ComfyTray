[CmdletBinding()]
param(
    [string]$Project = "ComfyTray.csproj",
    [string]$Configuration = "Release",
    [string]$Output = "artifacts/release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot $Project
$outputPath = Join-Path $repoRoot $Output

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $outputPath | Out-Null

$buildArgs = @(
    "build",
    $projectPath,
    "-c", $Configuration
)

Write-Host "Building $Project"
Write-Host "Target framework: net48"
Write-Host "Configuration: $Configuration"
Write-Host "Output: $outputPath"

dotnet @buildArgs

$buildOutputPath = Join-Path $repoRoot "bin\$Configuration\net48"
$exePath = Join-Path $buildOutputPath "ComfyTray.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Build completed, but ComfyTray.exe was not found at: $exePath"
}

Copy-Item -LiteralPath $exePath -Destination $outputPath

$configPath = Join-Path $buildOutputPath "ComfyTray.exe.config"
if (Test-Path -LiteralPath $configPath) {
    Copy-Item -LiteralPath $configPath -Destination $outputPath
}

$releaseExePath = Join-Path $outputPath "ComfyTray.exe"
Write-Host "Release executable: $releaseExePath"
