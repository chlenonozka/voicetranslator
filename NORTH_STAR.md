# Product North Star

VoiceTranslator lets a Windows user hear their Russian speech translated into a
chosen language with a reusable local voice profile, while keeping audio,
transcripts, translations, and speaker conditioning on the local machine.

The product succeeds when a user can select working input and output devices,
start a predictable local session, and receive intelligible translated audio
with no cloud speech pipeline. Named voice references are stored only when the
user creates a profile and are encrypted for the current Windows account.

## Fixed Product Boundary

The first release is a Windows 11 desktop application for one local
speaker. Its complete functional boundary is:

- Russian phrase input translated locally into the 16 languages defined in the
  approved specification.
- Local ASR, translation, and speaker-conditioned synthesis after explicit
  model and licence acknowledgement.
- Physical output, virtual cable, or both; a visible and keyboard-accessible
  session flow; and verified model/device capability checks.
- User-managed, encrypted local voice-reference profiles usable across all
  target languages. Transcripts, translations, ordinary session audio,
  embeddings, and launch secrets are not persisted.
- Balanced and low-memory operation on an RTX 3070 8 GB, including safe
  recovery from worker, device, and CUDA memory failures.

This boundary is closed. Do not add source languages, target languages, cloud
services, accounts, synchronization, collaboration, history, persistent
recording history, web or mobile clients, new product modes, or speculative UI
features unless the user explicitly changes this document.

## Completion Standard

Before proposing any new capability, prove the existing boundary with the
remaining acceptance evidence in ROADMAP.md. The target is not a larger
product; it is a dependable implementation of the specification:

- p90 phrase-end-to-playback at or below five seconds on the target hardware;
- all advertised languages and routing modes pass local preflight and E2E
  checks;
- failures enter a safe state within two seconds; and
- privacy, licence acknowledgement, and bounded encrypted-profile guarantees
  remain true.

Once those gates are met, the project enters maintenance mode. Prefer bugs,
regressions, performance, reliability, security, dependency compatibility,
tests, and documentation over new capabilities.

Priorities are, in order:

1. Privacy and the model licence restrictions described in README.md.
2. Reliable audio routing and clear recovery from worker, device, or GPU failure.
3. Short phrase-end-to-playback latency on the stated RTX 3070 target.
4. Clear setup and diagnostics for a Windows desktop user.
