# Research: 001 - Local Translation Models

**Date Checked:** 2026-06-22
**Source:** `specs/001-realtime-voice-translation/research.md` and initial evaluation.

## Russian ASR

*   **Decision**: `Systran/faster-whisper-medium`, fixed `language="ru"`, VAD, CUDA int8; low-memory fallback `small`.
*   **Rationale**: MIT model, Russian support, CTranslate2 quantization. Published large-model benchmarks show int8 materially reduces VRAM.

## Translation

*   **Decision**: `facebook/nllb-200-distilled-600M`, converted to CTranslate2, source `rus_Cyrl`, target selected from explicit language mapping.
*   **Rationale**: Supports Russian and all 16 selected target languages. License is CC-BY-NC-4.0, which is compatible only with the approved personal noncommercial scope of this project.

## Voice Synthesis

*   **Decision**: Maintained `coqui-tts` package with `tts_models/multilingual/multi-dataset/xtts_v2`.
*   **Rationale**: Maintained fork publishes Windows wheels, supports Python 3.10–3.14 and XTTS-v2's 17 languages, including Russian plus the 16 targets. This allows for session-scoped speaker conditioning in the target language.

## Model Acquisition

*   **Decision**: Hugging Face `snapshot_download` using pinned revisions and local directories; app-managed manifest and license acknowledgement; offline mode during inference.
*   **Rationale**: This ensures deterministic and verified dependencies, maintaining local-only control without arbitrary remote updates, and explicitly handling user license consent.

## References

- [faster-whisper](https://github.com/SYSTRAN/faster-whisper)
- [NLLB-200 distilled 600M](https://huggingface.co/facebook/nllb-200-distilled-600M)
- [XTTS-v2](https://huggingface.co/coqui/XTTS-v2)
- [maintained coqui-tts](https://pypi.org/project/coqui-tts/)
- [CTranslate2 memory management](https://github.com/OpenNMT/CTranslate2/blob/master/docs/memory.md)
