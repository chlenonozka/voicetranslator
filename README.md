# Voice Translator

Windows 11 desktop application for real-time cloud voice translation with
session-scoped speaker timbre preservation.

The implementation follows the active Spec Kit plan at
`specs/001-realtime-voice-translation/plan.md`.

## Prerequisites

- Windows 11 x64
- .NET 10 SDK
- Azure tenant access required by the provider feasibility gate
- Signed virtual-audio-cable for virtual microphone scenarios

This repository includes a workspace-local SDK under `.dotnet/`. In PowerShell:

```powershell
$dotnet = "$PWD\.dotnet\dotnet.exe"
```

## Build and test

```powershell
& $dotnet restore
& $dotnet build --configuration Release --no-restore
& $dotnet test --configuration Release --no-build
& $dotnet format --verify-no-changes
```

Run Windows-only acceptance suites explicitly:

```powershell
& $dotnet test tests/VoiceTranslator.PerformanceTests --configuration Release
& $dotnet test tests/VoiceTranslator.WindowsE2ETests --configuration Release
```

## Run

```powershell
& $dotnet run --project src/VoiceTranslator.App
& $dotnet run --project src/VoiceTranslator.Gateway
```

Do not add Azure keys, captured audio, transcripts, translations, consent
recordings, or reusable voice features to repository files or logs.
