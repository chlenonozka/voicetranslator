param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "$PSScriptRoot\..\..\..\artifacts\package"
)

$ErrorActionPreference = "Stop"
$workspace = Resolve-Path "$PSScriptRoot\..\..\.."
$dotnet = Join-Path $workspace ".dotnet\dotnet.exe"
$resolvedOutput = [System.IO.Path]::GetFullPath($Output)
$artifactsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $workspace "artifacts")
)

if (-not $resolvedOutput.StartsWith(
    $artifactsRoot,
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    throw "Package output must remain under $artifactsRoot."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

function Invoke-DotNet {
    param([string[]]$Arguments)

    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

Invoke-DotNet @(
    "restore",
    (Join-Path $workspace "src\VoiceTranslator.App"),
    "-r",
    $Runtime,
    "-p:NuGetAudit=false"
)
Invoke-DotNet @(
    "restore",
    (Join-Path $workspace "src\VoiceTranslator.WorkerHost"),
    "-r",
    $Runtime,
    "-p:NuGetAudit=false"
)

Invoke-DotNet @(
    "publish",
    (Join-Path $workspace "src\VoiceTranslator.App"),
    "-c",
    $Configuration,
    "-r",
    $Runtime,
    "--self-contained",
    "false",
    "--no-restore",
    "-o",
    (Join-Path $resolvedOutput "app")
)
Invoke-DotNet @(
    "publish",
    (Join-Path $workspace "src\VoiceTranslator.WorkerHost"),
    "-c",
    $Configuration,
    "-r",
    $Runtime,
    "--self-contained",
    "false",
    "--no-restore",
    "-o",
    (Join-Path $resolvedOutput "worker-host")
)

$workerOutput = Join-Path $resolvedOutput "worker"
New-Item -ItemType Directory -Force -Path $workerOutput | Out-Null
Copy-Item -Path @(
    (Join-Path $workspace "worker\bootstrap.ps1"),
    (Join-Path $workspace "worker\pyproject.toml"),
    (Join-Path $workspace "worker\uv.lock")
) -Destination $workerOutput
Copy-Item `
    (Join-Path $workspace "worker\voice_translator_worker") `
    -Destination $workerOutput `
    -Recurse

$manifestOutput = Join-Path $resolvedOutput "models\manifests"
New-Item -ItemType Directory -Force -Path $manifestOutput | Out-Null
Copy-Item `
    (Join-Path $workspace "models\manifests\*.json") `
    -Destination $manifestOutput

$forbidden = Get-ChildItem $resolvedOutput -Recurse -File | Where-Object {
    $_.Extension -in ".wav", ".pcm" -or
    $_.FullName -match "\\models\\(cache|downloads)\\"
}
if ($forbidden) {
    throw "Package contains forbidden speech or downloaded model artifacts."
}

Write-Output $resolvedOutput
