# Voice Translator

Windows 11 desktop application for local phrase-by-phrase translation of
Russian speech into 16 target languages with reusable local voice profiles.

Transcripts, translations, and ordinary session audio are kept in memory and
are not intentionally persisted. Named voice-reference profiles are the only
exception: they are encrypted for the current Windows user with DPAPI and
stored under `%LOCALAPPDATA%\VoiceTranslator\VoiceProfiles`.

## License restriction

This configuration is intended only for personal, noncommercial use. NLLB and
XTTS have model-specific license restrictions. Review their licenses before
downloading them and pass `--accept-noncommercial` only if you accept those
terms.

## Prerequisites

- Windows 11 x64
- NVIDIA GPU with CUDA support; the tested target is an RTX 3070 8 GB
- .NET 10 SDK, or the workspace-local SDK in `.dotnet`
- Python 3.11 and [uv](https://docs.astral.sh/uv/)
- Optional signed virtual audio cable for virtual-microphone routing

In PowerShell:

```powershell
$dotnet = "$PWD\.dotnet\dotnet.exe"
$uv = "$env:USERPROFILE\.local\bin\uv.exe"
```

## Install the worker

Create the local Python 3.11 environment and install CUDA/ML dependencies:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\worker\bootstrap.ps1
```

For development tests, also install the test group:

```powershell
& $uv sync --project worker --extra test --extra ml
```

## Download verified models

Models are pinned to exact Hugging Face commit revisions. The downloader
creates SHA-256 install receipts and converts NLLB to CTranslate2 format.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\worker\bootstrap.ps1 -DownloadModels -AcceptNoncommercial
```

By default, verified artifacts are installed under:

```text
%USERPROFILE%\.voice-translator\models
```

Future launches verify the install receipts before loading any model.

## Build and test

```powershell
& $dotnet restore VoiceTranslator.slnx
& $dotnet format VoiceTranslator.slnx --verify-no-changes --no-restore
& $dotnet build VoiceTranslator.slnx --configuration Release --no-restore
& $dotnet test VoiceTranslator.slnx --configuration Release --no-build --no-restore
& worker\.venv\Scripts\python.exe -m pytest worker\tests -q
```

## Run

For normal use, double-click:

```text
START Voice Translator.cmd
```

The launcher uses the workspace-local .NET SDK, verifies that the worker
environment and model inventory exist, then starts the WPF application. It
writes startup output to `artifacts\logs\voice-translator-launch.log`.

The WPF application starts and stops its own worker. Select an existing voice
profile to translate the first completed phrase immediately. To create a new
profile, click **Новый**, enter a name, and start a session; the first completed
phrase becomes its encrypted reference and subsequent phrases are translated.
The same profile can be selected for any target language, renamed, or deleted
in the application. The worker binds to `127.0.0.1`,
receives a new 256-bit token for every launch, and rejects unauthenticated
requests.

`VoiceTranslator.WorkerHost` remains available as a command-line diagnostic
host, but it must not be started at the same time as the WPF application.

Do not add downloaded models, captured audio, transcripts, translations,
decrypted speaker references, embeddings, or launch tokens to repository files
or logs.
