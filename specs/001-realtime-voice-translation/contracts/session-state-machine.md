# Contract: Desktop Session State Machine

## Commands

| Command | Allowed states | Success state |
|---|---|---|
| `RefreshCapabilities` | Draft, Ready, Faulted | Draft or Ready |
| `RunPreflight` | Draft, Faulted | Ready |
| `StartSession` | Ready | Listening |
| `PauseSession` | Listening, Processing, Playing | Paused |
| `ResumeSession` | Paused | Listening |
| `StopSession` | Ready, Listening, Processing, Playing, Paused, Faulted | Stopping |
| `RetryRecoverableFailure` | Paused, Faulted | Preflighting |

## Start preconditions

- A saved voice profile is selected, or a non-empty new profile name is set.
- Local worker authentication is valid.
- Capability catalog is unexpired.
- Selected language pair passes provider preflight.
- Input and every selected output endpoint are active.
- Virtual output test has passed when selected.

## Observable state

Every state change emits:

- localized status label;
- keyboard-focus-safe UI update;
- privacy indicator (`microphoneActive`, `cloudTransferActive`);
- recoverable action when one exists;
- correlation ID and privacy-safe timing event.

## Failure rules

- Device loss stops only the affected stream, then transitions to `Faulted`.
- Cloud timeout pauses capture after the bounded queue is full.
- Provider throttling never triggers unbounded retries.
- Deleting a profile removes its metadata and encrypted reference before it can
  be selected for another session.
- Output buffers are discarded on stop; stale audio is never played later.
