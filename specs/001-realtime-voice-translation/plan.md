# Implementation Plan: Локальный перевод русской речи

**Branch**: `codex/realtime-voice-translation` | **Date**: 2026-06-22 |
**Spec**: [spec.md](./spec.md)

## Summary

Сохранить .NET 10 WPF host для UI и WASAPI, заменить Azure/gateway на локальный
Python 3.11 worker. Worker использует faster-whisper medium для русского ASR,
NLLB-200 distilled 600M для текста и maintained `coqui-tts` XTTS-v2 для
синтеза с тембром. IPC — localhost FastAPI с одноразовым launch token.

## Technical Context

**Languages**: C# 14/.NET 10; Python 3.11

**Primary Dependencies**: WPF, NAudio, Microsoft.Extensions.Hosting,
FastAPI, Uvicorn, faster-whisper/CTranslate2, transformers, sentencepiece,
coqui-tts, PyTorch/torchaudio CUDA, huggingface_hub

**Storage**: Только immutable model files, manifests, настройки устройств и
результаты capability preflight. Аудио, текст и speaker conditioning не
персистятся.

**Testing**: xUnit, pytest, OpenAPI contract tests, fake worker integration,
Windows E2E, RTX 3070 latency/VRAM benchmarks

**Target Platform**: Windows 11 x64, RTX 3070 8 ГБ, локальный Python worker

**Performance Goals**: p90 phrase-end-to-playback ≤5 секунд; worker/device
failure → safe state ≤2 секунд

**Constraints**: русский вход; 16 целевых языков; личное некоммерческое
использование; offline inference; balanced и low-memory profiles; no generic
voice fallback

## Constitution Check

- **Consent/privacy — PASS**: вся речь локальна, timbre opt-in per session,
  ephemeral buffers очищаются на stop/crash.
- **Responsiveness — PASS**: staged model residency, bounded queue и OOM retry.
- **Predictable audio — PASS**: limiter, sink isolation и stale-buffer disposal.
- **Verification — PASS**: unit/contract/integration/hardware acceptance.
- **Simplicity — PASS**: отдельный worker изолирует CUDA/Python без собственного
  audio driver или облачного gateway.

## Architecture

```text
WPF/NAudio host
  ├── session state + devices + output routing
  ├── model manager UI
  └── authenticated localhost IPC
          ↓
Python FastAPI worker
  ├── faster-whisper (Russian ASR)
  ├── CTranslate2 NLLB (text translation)
  ├── coqui-tts XTTS-v2 (speaker-preserving TTS)
  └── GPU profile manager + ephemeral session memory
```

## Project Structure

```text
src/
├── VoiceTranslator.App/
├── VoiceTranslator.Domain/
├── VoiceTranslator.Application/
├── VoiceTranslator.Infrastructure.Audio/
├── VoiceTranslator.Infrastructure.LocalWorker/
└── VoiceTranslator.WorkerHost/

worker/
├── pyproject.toml
├── uv.lock
├── voice_translator_worker/
│   ├── api.py
│   ├── auth.py
│   ├── settings.py
│   ├── models/
│   ├── pipeline/
│   └── privacy/
└── tests/

models/
└── manifests/

tests/
├── VoiceTranslator.UnitTests/
├── VoiceTranslator.ContractTests/
├── VoiceTranslator.IntegrationTests/
├── VoiceTranslator.WindowsE2ETests/
└── VoiceTranslator.PerformanceTests/
```

## Model and memory decisions

- faster-whisper starts with `medium`, CUDA `int8`; low-memory falls back to
  `small`, CUDA `int8`.
- NLLB is converted to CTranslate2 and uses CPU `int8` by default to avoid
  requiring CUDA 12 `cublas64_12.dll` alongside the PyTorch CUDA 13 runtime; it
  is unloaded outside translation.
- XTTS-v2 is loaded for speaker conditioning/synthesis and released when the
  session ends.
- GPU manager uses `torch.cuda.mem_get_info`, `memory_reserved`,
  `empty_cache`, CTranslate2 `unload_model`, and one low-memory retry.

## Removed architecture

The Azure Speech SDK, Translator REST API, Personal Voice REST API, Entra/MSAL,
ASP.NET gateway, Azure Table Storage, and Azure Queue Storage are removed.

## Detailed execution plan

The bite-sized TDD execution plan is:

[docs/superpowers/plans/2026-06-22-local-russian-voice-translation.md](../../docs/superpowers/plans/2026-06-22-local-russian-voice-translation.md)
