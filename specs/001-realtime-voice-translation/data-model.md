# Data Model: Локальный перевод русской речи

## TranslationSession

| Field | Type | Rules |
|---|---|---|
| `SessionId` | UUID | Local correlation only |
| `TargetLanguage` | enum | One of 16 preflight-passing targets |
| `InputDeviceId` | string | Active WASAPI capture endpoint |
| `OutputMode` | enum | Physical, VirtualCable, Both |
| `PerformanceProfile` | enum | Economical, Balanced, or Performance |
| `VoiceProfileId` | UUID | Existing profile created by the explicit recording flow |
| `WorkerProcessId` | integer | Must match healthy launched worker |
| `State` | SessionState | Controlled by state machine |

## WorkerProcess

| Field | Type | Rules |
|---|---|---|
| `ProcessId` | integer | Child process owned by host |
| `Endpoint` | loopback URI | 127.0.0.1 only |
| `LaunchToken` | secret string | Memory only, new per launch |
| `Profile` | enum | Economical, Balanced, or Performance |
| `Health` | enum | Starting, Ready, Busy, Faulted, Stopped |
| `LastHeartbeatAt` | timestamp | Failure after configured timeout |

## ModelManifest

| Field | Type | Rules |
|---|---|---|
| `ModelId` | string | whisper-medium/small, nllb, xtts |
| `RepositoryId` | string | Hugging Face repo |
| `Revision` | commit hash | Pinned |
| `License` | string | Displayed before download |
| `CommercialUseAllowed` | bool | False when NLLB is installed |
| `ExpectedFiles` | list | Manifest-controlled |
| `Sha256` | map | Verified before use |
| `InstalledAt` | timestamp? | Metadata only |

## CapabilityCatalog

| Field | Type | Rules |
|---|---|---|
| `TargetLanguage` | enum | One of 16 candidates |
| `TranslationReady` | bool | NLLB probe |
| `SynthesisReady` | bool | XTTS probe |
| `P90Latency` | duration? | Hardware preflight result |
| `Available` | derived bool | All probes pass |

## SpeakerSession

Ephemeral worker-memory object.

| Field | Type | Rules |
|---|---|---|
| `SessionId` | UUID | Matches host session |
| `ReferencePcm` | byte buffer | Memory only |
| `Conditioning` | tensor/object | Memory only |
| `CreatedAt` | timestamp | Cleared on stop/disconnect |

## VoiceProfile

User-managed host-side object shared by all target languages.

| Field | Type | Rules |
|---|---|---|
| `ProfileId` | UUID | Local identifier |
| `Name` | string | Unique, user-editable, at most 60 characters |
| `CreatedAt` | timestamp | Metadata only |
| `EncryptedReferenceWav` | byte buffer | DPAPI CurrentUser, never plaintext on disk |

## SpeechSegment

Ephemeral worker-memory object containing Russian PCM, recognition confidence,
temporary text, target language and monotonic stage timestamps.

## TranslatedUtterance

Ephemeral synthesized PCM returned to the host and disposed after selected
output sinks finish or the request is canceled.

## Invariants

- No `SpeakerSession`, `SpeechSegment`, or `TranslatedUtterance` is serialized.
- Only `VoiceProfile.EncryptedReferenceWav` may persist speech data.
- Worker disconnect clears every active speaker session.
- Model downloads cannot occur during an active translation session.
- A target language is selectable only when its capability row is available.
- Low-memory retry occurs at most once per phrase.
