# Architecture Decision Record: 001 - Local Python Worker for ML Pipelines

## Status
Accepted

## Context
VoiceTranslator requires running machine learning models for local phrase-by-phrase translation of Russian speech into 16 target languages with session-scoped speaker timbre. Specifically, the system needs ASR (faster-whisper), text translation (NLLB), and voice synthesis (coqui-tts).

The primary host application is a .NET 10 WPF application, which handles UI, virtual/physical audio routing, WASAPI audio capture and playback. However, Python remains the predominant ecosystem for ML models. Attempting to run all ML inference directly within the .NET process (e.g. using ONNX Runtime) poses challenges regarding maintainability, model compatibility, and available libraries (especially for XTTS).

## Decision
We decided to replace the Azure/cloud gateway with a local Python 3.11 worker to handle all ML pipelines (ASR, translation, and synthesis).

The local worker will:
* Be written in Python 3.11 and manage its dependencies via `uv`.
* Expose its capabilities over localhost using FastAPI on an OS-assigned port.
* Use a single-launch token (`X-Worker-Token`) generated per launch by the .NET host to authenticate requests and reject unauthenticated calls.

### RTX 3070 Memory Management
Given the memory constraints of an 8GB RTX 3070 and the simultaneous running of multiple heavy models:
* We implement staged model residency: NLLB CTranslate2 is unloaded and loaded dynamically around translations.
* PyTorch inference mode is utilized heavily, accompanied by explicit CUDA cache cleanups.
* A preflight startup benchmark dynamically decides whether to use the full `Whisper medium` model or downgrade to `Whisper small` based on available VRAM and performance targets.

## Consequences

### Positive
* **Ecosystem Compatibility:** Seamlessly integrates with `faster-whisper`, `CTranslate2`, `transformers`, and `coqui-tts`, avoiding complex cross-compilation or bridging to .NET.
* **Isolation:** Worker crashes, CUDA out-of-memory errors, and Python library failures are isolated from the WPF UI process, allowing the .NET host to gracefully handle errors, tear down the worker, and restart it when necessary.
* **Privacy:** All interactions happen over loopback (`127.0.0.1`), preserving the local-only privacy boundary required by the `NORTH_STAR.md`.
* **Reliability:** FastAPI facilitates OpenAPI generation and straightforward client generation for robust validation and automated lifecycle management.

### Negative
* **Packaging Complexity:** We must package and bootstrap a local Python 3.11 environment alongside the .NET WPF application on Windows.
* **IPC Overhead:** Transferring raw audio back and forth via HTTP requests has minor overhead compared to in-process memory sharing. However, phrase-by-phrase processing ensures this overhead stays well within the 5-second p90 latency budget.
