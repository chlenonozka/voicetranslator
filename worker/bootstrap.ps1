param(
    [switch]$DownloadModels,
    [switch]$AcceptNoncommercial
)

$ErrorActionPreference = "Stop"
$workerRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$uvCommand = Get-Command uv -ErrorAction SilentlyContinue
if ($null -eq $uvCommand) {
    $fallback = Join-Path $env:USERPROFILE ".local\bin\uv.exe"
    if (-not (Test-Path -LiteralPath $fallback)) {
        throw "uv was not found. Install it from https://docs.astral.sh/uv/."
    }
    $uv = $fallback
}
else {
    $uv = $uvCommand.Source
}

& $uv python install 3.11
if ($LASTEXITCODE -ne 0) {
    throw "uv could not install Python 3.11."
}

& $uv sync --project $workerRoot --extra ml --locked
if ($LASTEXITCODE -ne 0) {
    throw "uv could not create the local ML worker environment."
}

if ($DownloadModels) {
    if (-not $AcceptNoncommercial) {
        throw "Model download requires -AcceptNoncommercial."
    }

    & $uv run `
        --project $workerRoot `
        --extra ml `
        voice-translator-models `
        --accept-noncommercial
    if ($LASTEXITCODE -ne 0) {
        throw "Verified model download failed."
    }
}

Write-Output "Worker environment is ready at $workerRoot\.venv"
