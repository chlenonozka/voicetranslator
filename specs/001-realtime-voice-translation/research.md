# Research: Локальный перевод русской речи

## Russian ASR

**Decision**: `Systran/faster-whisper-medium`, fixed `language="ru"`, VAD,
CUDA int8; low-memory fallback `small`.

**Rationale**: MIT model, Russian support, CTranslate2 quantization. Published
large-model benchmarks show int8 materially reduces VRAM.

## Translation

**Decision**: `facebook/nllb-200-distilled-600M`, converted to CTranslate2,
source `rus_Cyrl`, target selected from explicit language mapping.

**Rationale**: Supports Russian and all selected target languages. License is
CC-BY-NC-4.0, compatible only with the approved personal noncommercial scope.

## Voice synthesis

**Decision**: maintained `coqui-tts` package with
`tts_models/multilingual/multi-dataset/xtts_v2`.

**Rationale**: Maintained fork publishes Windows wheels, supports Python
3.10–3.14 and XTTS-v2's 17 languages, including Russian plus the 16 targets.

## IPC

**Decision**: FastAPI bound to `127.0.0.1` on an OS-assigned port, protected by
an `X-Worker-Token` value generated for each launch.

**Rationale**: OpenAPI generation, simple .NET client generation/validation,
lifespan model cleanup and restart isolation.

## Model acquisition

**Decision**: Hugging Face `snapshot_download` using pinned revisions and local
directories; app-managed manifest and license acknowledgement; offline mode
during inference.

## RTX 3070 memory

**Decision**: staged model residency, NLLB CTranslate2 unload/load, PyTorch
inference mode and cache cleanup. Balanced/low-memory startup benchmark decides
Whisper medium vs small.

## References

- [faster-whisper](https://github.com/SYSTRAN/faster-whisper)
- [NLLB-200 distilled 600M](https://huggingface.co/facebook/nllb-200-distilled-600M)
- [XTTS-v2](https://huggingface.co/coqui/XTTS-v2)
- [maintained coqui-tts](https://pypi.org/project/coqui-tts/)
- [CTranslate2 memory management](https://github.com/OpenNMT/CTranslate2/blob/master/docs/memory.md)
