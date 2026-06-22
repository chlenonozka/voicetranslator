# Local Russian Voice Translation Design

**Date**: 2026-06-22  
**Status**: Approved design, pending written-spec review  
**Use**: Personal, noncommercial only

## Goal

Build a Windows 11 desktop application that listens to one Russian-speaking
user, translates complete phrases locally, and outputs translated speech while
preserving recognizable characteristics of that user's voice.

After model download, speech content must remain on the computer. The
application must support headphones or speakers, a separately installed
virtual-audio-cable, or both.

## Supported languages

Russian is the only source language in the first release.

The selectable target languages are the intersection supported by the selected
translation and voice-synthesis models:

- Arabic
- Chinese
- Czech
- Dutch
- English
- French
- German
- Hindi
- Hungarian
- Italian
- Japanese
- Korean
- Polish
- Portuguese
- Spanish
- Turkish

The application must not advertise a language until an installed-model
preflight has successfully translated and synthesized its validation phrase.

## Constraints

- Target hardware: Windows 11 x64, NVIDIA RTX 3070 with 8 GB VRAM.
- Target phrase-end-to-playback delay: 3–5 seconds under the validated hardware
  profile.
- Processing is phrase-based rather than simultaneous phoneme-level dubbing.
- Models are downloaded once and then used offline.
- NLLB model licensing restricts the application to personal,
  noncommercial use.
- The user may use only their own voice and must explicitly enable timbre
  preservation for each session.
- Reference audio and derived speaker conditioning must be held only in memory
  and cleared when the session stops.
- No raw audio, transcripts, translations, or voice embeddings may be written
  to logs or persistent application storage.

## Selected approach

Use a split-process architecture:

- The existing .NET 10 WPF application owns the user interface, session state,
  audio devices, safety limits, and virtual-cable routing.
- A local Python ML worker owns model loading and inference.
- The processes communicate only over a loopback IPC endpoint with an
  authenticated per-launch token.

This is preferred over embedding Python in .NET because native ML packages have
conflicting CUDA and runtime dependencies. A separate worker can be restarted
after a model failure without terminating the audio UI and can be tested through
a stable contract.

## Model pipeline

### Speech recognition

Use `Systran/faster-whisper-medium` through CTranslate2:

- fixed Russian input language;
- CUDA execution with FP16 or INT8/FP16 selected by startup benchmark;
- voice activity detection and phrase segmentation;
- low-confidence output is rejected rather than translated.

### Translation

Use `facebook/nllb-200-distilled-600M`:

- source token is fixed to Russian;
- target token is selected from the approved 16-language catalog;
- inference uses quantization appropriate for the 8 GB VRAM budget;
- translated text is an ephemeral in-memory value.

### Voice synthesis

Use `coqui/XTTS-v2`:

- obtain a short reference sample from the current speaker after explicit
  session consent;
- compute speaker conditioning once per session;
- synthesize translated text in the selected target language;
- never expose an import workflow for another person's voice sample;
- erase reference buffers and conditioning when the session ends.

## GPU memory strategy

The RTX 3070 cannot safely keep all large models resident without measurement.
The worker therefore uses staged residency:

1. Keep faster-whisper loaded while listening.
2. At phrase completion, release inactive recognition buffers.
3. Load or activate quantized NLLB for translation.
4. Load or activate XTTS for synthesis using cached session conditioning.
5. Apply an LRU/offload policy based on measured free VRAM.

Startup calibration chooses between:

- **Balanced**: Whisper medium plus quantized NLLB and XTTS with selective CPU
  offload.
- **Low-memory**: Whisper small plus more aggressive model offload when the
  balanced profile fails its VRAM preflight.

The UI displays the active performance profile. The application must stop with
an actionable error if neither profile can run safely.

## Component boundaries

### WPF desktop host

- Enumerates WASAPI input and render endpoints.
- Captures and normalizes microphone PCM.
- Displays language, device, model, privacy, and session status.
- Sends phrase audio to the local worker.
- Routes synthesized PCM to physical and/or virtual outputs.
- Applies output limiter and bounded buffering.
- Clears all session buffers during stop or failure.

### Local ML worker

- Validates CUDA, model files, licenses, and target-language support.
- Loads models according to the selected memory profile.
- Accepts Russian PCM phrase requests.
- Returns recognition confidence, translated text status, synthesized PCM, and
  privacy-safe stage timings.
- Holds speaker reference and conditioning in process memory only.
- Shuts down and clears state when the desktop host disconnects.

### Model manager

- Downloads pinned model revisions after user confirmation.
- Verifies file hashes and expected licenses.
- Stores only immutable model files and metadata.
- Supports deleting downloaded models.
- Never downloads or updates models during an active translation session.

## IPC contract

Use a local loopback HTTP or named-pipe protocol generated from an OpenAPI
contract. The worker binds only to localhost or the current-user pipe namespace.

Required operations:

- health and hardware preflight;
- model inventory and download progress;
- language capability catalog;
- create/close ephemeral speaker session;
- recognize, translate, and synthesize one phrase;
- cancel current phrase;
- retrieve privacy-safe timings.

Every worker launch receives a random token over inherited process state. Calls
without that token are rejected. The protocol never provides endpoints for
uploading arbitrary persistent voice profiles.

## Data flow

1. The user selects microphone, output, and one of the 16 target languages.
2. Preflight verifies the GPU profile, installed models, selected devices, and
   target-language synthesis.
3. The user explicitly enables timbre preservation and records a short
   reference sample.
4. The WPF host captures Russian speech and detects phrase boundaries.
5. Phrase PCM is sent to the local worker.
6. The worker performs Russian ASR, text translation, and XTTS synthesis.
7. Synthesized PCM returns to the host and is routed through the limiter to the
   selected output sinks.
8. On stop, both processes clear phrase text, PCM, and speaker conditioning.

## Degradation and errors

- If the phrase queue is full, discard stale unsynthesized work and keep the
  newest complete phrase.
- If CUDA runs out of memory, cancel the phrase, clear GPU caches, and retry once
  using the low-memory profile.
- If the retry fails, stop the session and report which model exceeded the
  hardware budget.
- If a target language fails preflight, hide it from the selectable catalog.
- If the worker exits, stop microphone transfer and outputs within two seconds,
  then offer a worker restart.
- Device loss stops only the affected stream and never replays stale audio.
- The application must not silently replace timbre-preserving synthesis with a
  generic voice.

## Testing

### Automated

- .NET unit tests for session state, queueing, privacy, and output routing.
- Python unit tests for request validation, language mapping, and cleanup.
- IPC contract tests shared between .NET and Python.
- Worker integration tests using short synthetic/non-personal fixtures.
- Tests proving logs and settings exclude audio, text, and speaker embeddings.
- Failure tests for worker crash, cancellation, CUDA OOM, and missing models.

### Hardware acceptance

On the RTX 3070 machine:

- benchmark all 16 target languages;
- record median and p90 stage latency;
- verify the 3–5 second target on approved test phrases;
- verify speaker-similarity quality with the current user's consent;
- verify physical, virtual, and dual output;
- verify no session artifacts remain after normal stop, crash, or forced worker
  termination.

## Out of scope

- Commercial use.
- Cloud inference.
- Input languages other than Russian.
- Importing or maintaining profiles for another person's voice.
- Multiple simultaneous speakers.
- Conversation history or transcript export.
- Custom virtual audio driver development.
