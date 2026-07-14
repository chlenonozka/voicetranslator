# Quickstart Validation Guide

## Prerequisites

- Windows 11 x64 with RTX 3070 8 ГБ and current NVIDIA driver.
- Workspace-local .NET 10 SDK.
- Python 3.11 managed by uv.
- At least 20 ГБ free disk space.
- Headphones and optionally a signed virtual-audio-cable.

## Setup

```powershell
& .\.dotnet\dotnet.exe restore
uv python install 3.11
.\worker\bootstrap.ps1
.\worker\bootstrap.ps1 -DownloadModels -AcceptNoncommercial
```

Model download must display and require acknowledgement of NLLB's
CC-BY-NC-4.0 noncommercial restriction.

## Verification

```powershell
& .\.dotnet\dotnet.exe build VoiceTranslator.slnx -c Release
& .\.dotnet\dotnet.exe test VoiceTranslator.slnx -c Release
uv run --project worker pytest worker/tests -q
```

## Hardware acceptance

1. Run preflight and confirm RTX 3070, selected profile, hashes and 16 language
   probes.
2. Create and name a voice profile from a short current-speaker phrase, or
   select an existing encrypted local profile.
3. Translate ten approved Russian phrases to English.
4. Repeat a validation phrase for every other target language.
5. Test physical, virtual and dual outputs.
6. Force worker termination and CUDA OOM.
7. Verify safe state within two seconds, no ordinary session artifacts on disk,
   and only DPAPI-encrypted data in the voice-profile directory.

Expected: p90 output begins within five seconds; only preflight-passing
languages are selectable; no speech content persists outside explicitly
created encrypted voice profiles.
