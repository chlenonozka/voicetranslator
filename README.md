# Voice Translator

Windows 11 desktop application for local phrase-by-phrase translation of
Russian speech into 16 target languages with session-scoped speaker timbre.

Audio, transcripts, translations, and speaker conditioning are kept in memory
and are not intentionally persisted.

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

The base test environment does not install multi-gigabyte ML dependencies:

```powershell
& $uv python install 3.11
& $uv sync --project worker --extra test
```

Install the CUDA/ML environment when models will be used:

```powershell
& $uv sync --project worker --extra test --extra ml
```

## Download verified models

Models are pinned to exact Hugging Face commit revisions. The downloader
creates SHA-256 install receipts and converts NLLB to CTranslate2 format.

```powershell
& $uv run --project worker --extra ml voice-translator-models `
  --accept-noncommercial
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

Start the managed local worker host:

```powershell
& $dotnet run --project src/VoiceTranslator.WorkerHost
```

Start the WPF interface in another terminal:

```powershell
& $dotnet run --project src/VoiceTranslator.App
```

The worker binds to `127.0.0.1`, receives a new 256-bit token for every launch,
and rejects unauthenticated requests.

Do not add downloaded models, captured audio, transcripts, translations,
speaker references, embeddings, or launch tokens to repository files or logs.
