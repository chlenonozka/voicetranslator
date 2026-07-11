# Product North Star

VoiceTranslator lets a Windows user hear their Russian speech translated into a
chosen language with their session-scoped timbre, while keeping audio,
transcripts, translations, and speaker conditioning on the local machine.

The product succeeds when a user can select working input and output devices,
start a predictable local session, and receive intelligible translated audio
with no cloud speech pipeline or persistent speech data.

Priorities are, in order:

1. Consent, privacy, and the model licence restrictions described in README.md.
2. Reliable audio routing and clear recovery from worker, device, or GPU failure.
3. Short phrase-end-to-playback latency on the stated RTX 3070 target.
4. Clear setup and diagnostics for a Windows desktop user.
