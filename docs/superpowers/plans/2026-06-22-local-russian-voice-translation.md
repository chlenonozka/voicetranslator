# Local Russian Voice Translation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows 11 application that locally translates completed
Russian speech phrases into 16 target languages and speaks them with ephemeral
current-speaker timbre preservation.

**Architecture:** A .NET 10 WPF host owns UI, WASAPI devices, state, buffering,
and process lifecycle. A Python 3.11 FastAPI worker owns faster-whisper, NLLB,
XTTS-v2, model downloads, and CUDA memory profiles. The host launches the worker
on localhost with a random per-launch token; no speech content is persisted.

**Tech Stack:** C# 14, .NET 10, WPF, NAudio, Python 3.11, uv, FastAPI, Uvicorn,
faster-whisper/CTranslate2, transformers, sentencepiece, coqui-tts, PyTorch CUDA,
huggingface_hub, xUnit, pytest.

---

## File map

### .NET host

- `src/VoiceTranslator.Domain/`: session, language, model and worker value types.
- `src/VoiceTranslator.Application/Ports/`: interfaces consumed by orchestration.
- `src/VoiceTranslator.Application/Orchestration/`: bounded phrase flow and failure handling.
- `src/VoiceTranslator.Infrastructure.Audio/`: WASAPI capture, limiter and output routes.
- `src/VoiceTranslator.Infrastructure.LocalWorker/`: child process and HTTP client.
- `src/VoiceTranslator.WorkerHost/`: locates Python/uv and launches the packaged worker.
- `src/VoiceTranslator.App/`: WPF screens and view models.

### Python worker

- `worker/voice_translator_worker/api.py`: authenticated FastAPI endpoints.
- `worker/voice_translator_worker/auth.py`: launch-token validation.
- `worker/voice_translator_worker/models/model_manager.py`: pinned downloads and hashes.
- `worker/voice_translator_worker/models/gpu_profiles.py`: VRAM profiles and cleanup.
- `worker/voice_translator_worker/pipeline/asr.py`: Russian faster-whisper adapter.
- `worker/voice_translator_worker/pipeline/translation.py`: NLLB adapter and language map.
- `worker/voice_translator_worker/pipeline/synthesis.py`: ephemeral XTTS speaker sessions.
- `worker/voice_translator_worker/pipeline/service.py`: phrase pipeline.
- `worker/voice_translator_worker/privacy/session_store.py`: memory-only session store.

### Contracts and models

- `specs/001-realtime-voice-translation/contracts/worker.openapi.yaml`
- `models/manifests/*.json`

---

### Task 1: Replace cloud scaffold with local-worker projects

**Files:**

- Modify: `VoiceTranslator.slnx`
- Modify: `Directory.Packages.props`
- Modify: `src/VoiceTranslator.App/VoiceTranslator.App.csproj`
- Delete: `src/VoiceTranslator.Gateway/`
- Delete: `src/VoiceTranslator.Infrastructure.Cloud/`
- Create: `src/VoiceTranslator.WorkerHost/VoiceTranslator.WorkerHost.csproj`
- Create: `src/VoiceTranslator.Infrastructure.LocalWorker/VoiceTranslator.Infrastructure.LocalWorker.csproj`
- Modify: `.gitignore`

- [ ] **Step 1: Verify the current scaffold builds before the rename**

Run:

```powershell
& .\.dotnet\dotnet.exe build VoiceTranslator.slnx -c Release
```

Expected: `0 Warning(s), 0 Error(s)`.

- [ ] **Step 2: Remove cloud packages and add local-worker dependencies**

Keep these central packages in `Directory.Packages.props`:

```xml
<PackageVersion Include="FluentAssertions" Version="8.10.0" />
<PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.9" />
<PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.6.0" />
<PackageVersion Include="NAudio" Version="2.3.0" />
<PackageVersion Include="xunit" Version="2.9.3" />
<PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
<PackageVersion Include="coverlet.collector" Version="10.0.1" />
```

Delete all `Azure.*`, `Microsoft.CognitiveServices.Speech`,
`Microsoft.Identity.Client`, and `Microsoft.AspNetCore.Authentication.JwtBearer`
entries.

- [ ] **Step 3: Create focused local-worker projects**

Run:

```powershell
$dotnet = ".\.dotnet\dotnet.exe"
& $dotnet new classlib -n VoiceTranslator.Infrastructure.LocalWorker -o src/VoiceTranslator.Infrastructure.LocalWorker -f net10.0
& $dotnet new console -n VoiceTranslator.WorkerHost -o src/VoiceTranslator.WorkerHost -f net10.0
& $dotnet sln VoiceTranslator.slnx remove src/VoiceTranslator.Gateway/VoiceTranslator.Gateway.csproj
& $dotnet sln VoiceTranslator.slnx remove src/VoiceTranslator.Infrastructure.Cloud/VoiceTranslator.Infrastructure.Cloud.csproj
& $dotnet sln VoiceTranslator.slnx add src/VoiceTranslator.Infrastructure.LocalWorker/VoiceTranslator.Infrastructure.LocalWorker.csproj
& $dotnet sln VoiceTranslator.slnx add src/VoiceTranslator.WorkerHost/VoiceTranslator.WorkerHost.csproj
```

- [ ] **Step 4: Add ignore rules**

Append to `.gitignore`:

```gitignore
.venv/
worker/.venv/
worker/.pytest_cache/
worker/**/__pycache__/
*.pyc
.python-version
models/cache/
models/downloads/
worker-data/
```

- [ ] **Step 5: Build the renamed scaffold**

Run:

```powershell
& .\.dotnet\dotnet.exe build VoiceTranslator.slnx -c Release
```

Expected: PASS with no Azure project references.

- [ ] **Step 6: Commit**

```powershell
git add VoiceTranslator.slnx Directory.Packages.props .gitignore src tests
git commit -m "refactor: replace cloud scaffold with local worker"
```

---

### Task 2: Scaffold the authenticated Python worker

**Files:**

- Create: `worker/pyproject.toml`
- Create: `worker/voice_translator_worker/__init__.py`
- Create: `worker/voice_translator_worker/auth.py`
- Create: `worker/voice_translator_worker/api.py`
- Create: `worker/tests/test_auth.py`

- [ ] **Step 1: Create the Python project**

Create `worker/pyproject.toml`:

```toml
[project]
name = "voice-translator-worker"
version = "0.1.0"
requires-python = ">=3.11,<3.12"
dependencies = [
  "fastapi==0.128.0",
  "uvicorn[standard]>=0.38,<0.39",
  "python-multipart>=0.0.20,<0.1",
  "pydantic>=2.12,<3",
  "pydantic-settings>=2.12,<3",
  "httpx>=0.28,<0.29",
]

[project.optional-dependencies]
test = ["pytest>=9,<10", "pytest-asyncio>=1.3,<2"]
ml = [
  "faster-whisper>=1.2,<2",
  "ctranslate2>=4.6,<5",
  "transformers>=4.57,<5",
  "sentencepiece>=0.2,<0.3",
  "huggingface-hub>=0.34,<1",
  "coqui-tts>=0.27,<0.28",
]

[tool.pytest.ini_options]
testpaths = ["tests"]
asyncio_mode = "auto"
```

- [ ] **Step 2: Write the failing token test**

Create `worker/tests/test_auth.py`:

```python
from fastapi.testclient import TestClient

from voice_translator_worker.api import create_app


def test_health_rejects_missing_worker_token() -> None:
    client = TestClient(create_app("expected-token"))

    response = client.get("/v1/health")

    assert response.status_code == 401


def test_health_accepts_launch_token() -> None:
    client = TestClient(create_app("expected-token"))

    response = client.get(
        "/v1/health",
        headers={"X-Worker-Token": "expected-token"},
    )

    assert response.status_code == 200
    assert response.json()["status"] == "ready"
```

- [ ] **Step 3: Run the tests and verify RED**

Run:

```powershell
uv python install 3.11
uv sync --project worker --extra test
uv run --project worker pytest worker/tests/test_auth.py -q
```

Expected: import failure because `voice_translator_worker.api` does not exist.

- [ ] **Step 4: Implement token authentication and health**

Create `worker/voice_translator_worker/auth.py`:

```python
import hmac

from fastapi import Header, HTTPException, status


def token_dependency(expected_token: str):
    async def require_token(
        x_worker_token: str | None = Header(default=None),
    ) -> None:
        if x_worker_token is None or not hmac.compare_digest(
            x_worker_token,
            expected_token,
        ):
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="invalid worker token",
            )

    return require_token
```

Create `worker/voice_translator_worker/api.py`:

```python
from fastapi import Depends, FastAPI

from .auth import token_dependency


def create_app(launch_token: str) -> FastAPI:
    require_token = token_dependency(launch_token)
    app = FastAPI(title="Local Voice Translation Worker", version="0.2.0")

    @app.get("/v1/health", dependencies=[Depends(require_token)])
    async def health() -> dict[str, str]:
        return {"status": "ready"}

    return app
```

- [ ] **Step 5: Verify GREEN**

Run:

```powershell
uv run --project worker pytest worker/tests/test_auth.py -q
```

Expected: `2 passed`.

- [ ] **Step 6: Commit**

```powershell
git add worker
git commit -m "feat: add authenticated local worker"
```

---

### Task 3: Implement the domain state and language catalog with TDD

**Files:**

- Create: `tests/VoiceTranslator.UnitTests/Sessions/TranslationSessionTests.cs`
- Create: `tests/VoiceTranslator.UnitTests/Languages/TargetLanguageTests.cs`
- Create: `src/VoiceTranslator.Domain/Sessions/TranslationSession.cs`
- Create: `src/VoiceTranslator.Domain/Sessions/SessionState.cs`
- Create: `src/VoiceTranslator.Domain/Languages/TargetLanguage.cs`

- [ ] **Step 1: Write failing session tests**

```csharp
using FluentAssertions;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Sessions;

namespace VoiceTranslator.UnitTests.Sessions;

public sealed class TranslationSessionTests
{
    [Fact]
    public void Start_requires_speaker_consent()
    {
        var session = TranslationSession.Create(TargetLanguage.English);

        var act = session.Invoking(x => x.Start());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*consent*");
    }

    [Fact]
    public void Stop_clears_active_state()
    {
        var session = TranslationSession.Create(TargetLanguage.English);
        session.GrantSpeakerConsent(DateTimeOffset.UtcNow);
        session.MarkReady();
        session.Start();

        session.Stop();

        session.State.Should().Be(SessionState.Stopped);
    }
}
```

- [ ] **Step 2: Write failing 16-language test**

```csharp
using FluentAssertions;
using VoiceTranslator.Domain.Languages;

namespace VoiceTranslator.UnitTests.Languages;

public sealed class TargetLanguageTests
{
    [Fact]
    public void Catalog_contains_exactly_the_approved_targets()
    {
        TargetLanguage.All.Should().HaveCount(16);
        TargetLanguage.All.Should().Contain(TargetLanguage.English);
        TargetLanguage.All.Should().Contain(TargetLanguage.Hindi);
        TargetLanguage.All.Should().NotContain(x => x.Code == "ru");
    }
}
```

- [ ] **Step 3: Run RED**

```powershell
& .\.dotnet\dotnet.exe test tests/VoiceTranslator.UnitTests -c Release
```

Expected: compile failure because domain types do not exist.

- [ ] **Step 4: Implement the minimal state machine**

Create `src/VoiceTranslator.Domain/Sessions/SessionState.cs`:

```csharp
namespace VoiceTranslator.Domain.Sessions;

public enum SessionState
{
    Draft,
    Ready,
    Listening,
    Faulted,
    Stopped,
}
```

Create `src/VoiceTranslator.Domain/Sessions/TranslationSession.cs`:

```csharp
using VoiceTranslator.Domain.Languages;

namespace VoiceTranslator.Domain.Sessions;

public sealed class TranslationSession
{
    private TranslationSession(TargetLanguage targetLanguage)
    {
        TargetLanguage = targetLanguage;
    }

    public TargetLanguage TargetLanguage { get; }
    public SessionState State { get; private set; } = SessionState.Draft;
    public DateTimeOffset? SpeakerConsentAt { get; private set; }

    public static TranslationSession Create(TargetLanguage targetLanguage) =>
        new(targetLanguage);

    public void GrantSpeakerConsent(DateTimeOffset acceptedAt) =>
        SpeakerConsentAt = acceptedAt;

    public void MarkReady() => State = SessionState.Ready;

    public void Start()
    {
        if (SpeakerConsentAt is null)
        {
            throw new InvalidOperationException("Speaker consent is required.");
        }

        if (State != SessionState.Ready)
        {
            throw new InvalidOperationException("Session is not ready.");
        }

        State = SessionState.Listening;
    }

    public void Stop() => State = SessionState.Stopped;
}
```

Create `src/VoiceTranslator.Domain/Languages/TargetLanguage.cs` with the exact
16 records and XTTS/NLLB codes.

- [ ] **Step 5: Run GREEN**

```powershell
& .\.dotnet\dotnet.exe test tests/VoiceTranslator.UnitTests -c Release
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/VoiceTranslator.Domain tests/VoiceTranslator.UnitTests
git commit -m "feat: add local session and language domain"
```

---

### Task 4: Add pinned model manifests and verified downloads

**Files:**

- Create: `models/manifests/whisper-medium.json`
- Create: `models/manifests/whisper-small.json`
- Create: `models/manifests/nllb-600m.json`
- Create: `models/manifests/xtts-v2.json`
- Create: `worker/tests/test_model_manager.py`
- Create: `worker/voice_translator_worker/models/model_manager.py`

- [ ] **Step 1: Write failing manifest tests**

```python
from pathlib import Path

import pytest

from voice_translator_worker.models.model_manager import (
    LicenseNotAccepted,
    ModelManager,
)


def test_noncommercial_model_requires_acknowledgement(tmp_path: Path) -> None:
    manager = ModelManager(tmp_path)

    with pytest.raises(LicenseNotAccepted):
        manager.ensure_license("nllb-600m", accepted=False)


def test_hash_mismatch_rejects_model(tmp_path: Path) -> None:
    manager = ModelManager(tmp_path)
    file_path = tmp_path / "model.bin"
    file_path.write_bytes(b"corrupt")

    assert manager.verify_sha256(file_path, "00" * 32) is False
```

- [ ] **Step 2: Run RED**

```powershell
uv run --project worker pytest worker/tests/test_model_manager.py -q
```

Expected: import failure.

- [ ] **Step 3: Implement license and hash primitives**

```python
import hashlib
from pathlib import Path


class LicenseNotAccepted(RuntimeError):
    pass


class ModelManager:
    def __init__(self, model_root: Path) -> None:
        self.model_root = model_root

    def ensure_license(self, model_id: str, accepted: bool) -> None:
        if model_id == "nllb-600m" and not accepted:
            raise LicenseNotAccepted(
                "NLLB is restricted to personal noncommercial use."
            )

    @staticmethod
    def verify_sha256(path: Path, expected: str) -> bool:
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        return digest == expected.lower()
```

- [ ] **Step 4: Add pinned manifests**

Each manifest must contain:

```json
{
  "id": "nllb-600m",
  "repo_id": "facebook/nllb-200-distilled-600M",
  "revision": "f8d333a098d19b4fd9a8b18f94170487ad3f821d",
  "license": "CC-BY-NC-4.0",
  "commercial_use_allowed": false,
  "files": []
}
```

Use these pinned revisions:

```text
facebook/nllb-200-distilled-600M
  f8d333a098d19b4fd9a8b18f94170487ad3f821d
Systran/faster-whisper-medium
  08e178d48790749d25932bbc082711ddcfdfbc4f
Systran/faster-whisper-small
  536b0662742c02347bc0e980a01041f333bce120
coqui/XTTS-v2
  6c2b0d75eae4b7047358e3b6bd9325f857d43f77
```

After `snapshot_download`, generate an install receipt listing every downloaded
relative path and SHA-256. Future launches verify the receipt before marking the
model installed; an empty `files` list is valid only in the source manifest,
never in an installed receipt.

- [ ] **Step 5: Verify GREEN**

```powershell
uv run --project worker pytest worker/tests/test_model_manager.py -q
```

- [ ] **Step 6: Commit**

```powershell
git add models worker
git commit -m "feat: add verified local model management"
```

---

### Task 5: Implement RTX 3070 performance profiles

**Files:**

- Create: `worker/tests/test_gpu_profiles.py`
- Create: `worker/voice_translator_worker/models/gpu_profiles.py`

- [ ] **Step 1: Write failing profile tests**

```python
from voice_translator_worker.models.gpu_profiles import choose_profile


def test_8gb_gpu_uses_balanced_profile_when_memory_is_available() -> None:
    profile = choose_profile(total_bytes=8 * 1024**3, free_bytes=7 * 1024**3)

    assert profile.name == "balanced"
    assert profile.whisper_model == "medium"
    assert profile.whisper_compute_type == "int8"


def test_low_free_memory_uses_small_whisper() -> None:
    profile = choose_profile(total_bytes=8 * 1024**3, free_bytes=3 * 1024**3)

    assert profile.name == "low-memory"
    assert profile.whisper_model == "small"
```

- [ ] **Step 2: Run RED**

```powershell
uv run --project worker pytest worker/tests/test_gpu_profiles.py -q
```

- [ ] **Step 3: Implement deterministic profile selection**

```python
from dataclasses import dataclass


@dataclass(frozen=True)
class GpuProfile:
    name: str
    whisper_model: str
    whisper_compute_type: str
    nllb_compute_type: str


def choose_profile(total_bytes: int, free_bytes: int) -> GpuProfile:
    gib = 1024**3
    if total_bytes >= 7 * gib and free_bytes >= 5 * gib:
        return GpuProfile("balanced", "medium", "int8", "int8_float16")
    return GpuProfile("low-memory", "small", "int8", "int8_float16")
```

- [ ] **Step 4: Add runtime cleanup**

Implement `release_torch_memory()`:

```python
import gc
import torch


def release_torch_memory() -> None:
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.synchronize()
        torch.cuda.empty_cache()
```

- [ ] **Step 5: Run GREEN**

```powershell
uv run --project worker pytest worker/tests/test_gpu_profiles.py -q
```

- [ ] **Step 6: Commit**

```powershell
git add worker
git commit -m "feat: add RTX 3070 memory profiles"
```

---

### Task 6: Implement Russian ASR with confidence rejection

**Files:**

- Create: `worker/tests/test_asr.py`
- Create: `worker/voice_translator_worker/pipeline/asr.py`

- [ ] **Step 1: Write tests against an injected model**

```python
from voice_translator_worker.pipeline.asr import RussianAsr


class FakeSegment:
    text = " Привет "
    avg_logprob = -0.2
    no_speech_prob = 0.01


class FakeModel:
    def transcribe(self, audio, **kwargs):
        return iter([FakeSegment()]), object()


def test_asr_forces_russian_and_trims_text() -> None:
    asr = RussianAsr(FakeModel())

    result = asr.transcribe([0.0, 0.1])

    assert result.text == "Привет"
    assert result.accepted is True
```

- [ ] **Step 2: Run RED**

```powershell
uv run --project worker pytest worker/tests/test_asr.py -q
```

- [ ] **Step 3: Implement the adapter**

```python
from dataclasses import dataclass
from typing import Protocol


class WhisperLike(Protocol):
    def transcribe(self, audio, **kwargs): ...


@dataclass(frozen=True)
class Recognition:
    text: str
    accepted: bool


class RussianAsr:
    def __init__(self, model: WhisperLike) -> None:
        self.model = model

    def transcribe(self, audio) -> Recognition:
        segments, _ = self.model.transcribe(
            audio,
            language="ru",
            task="transcribe",
            vad_filter=True,
            beam_size=5,
            condition_on_previous_text=False,
        )
        values = list(segments)
        text = " ".join(segment.text.strip() for segment in values).strip()
        accepted = bool(text) and all(
            segment.no_speech_prob < 0.6 and segment.avg_logprob > -1.0
            for segment in values
        )
        return Recognition(text=text, accepted=accepted)
```

- [ ] **Step 4: Verify GREEN**

```powershell
uv run --project worker pytest worker/tests/test_asr.py -q
```

- [ ] **Step 5: Commit**

```powershell
git add worker
git commit -m "feat: add Russian speech recognition"
```

---

### Task 7: Implement NLLB translation for the 16 targets

**Files:**

- Create: `worker/tests/test_translation.py`
- Create: `worker/voice_translator_worker/pipeline/languages.py`
- Create: `worker/voice_translator_worker/pipeline/translation.py`

- [ ] **Step 1: Write the exact mapping test**

```python
from voice_translator_worker.pipeline.languages import TARGET_LANGUAGES


def test_target_catalog_has_16_languages() -> None:
    assert len(TARGET_LANGUAGES) == 16
    assert TARGET_LANGUAGES["en"].nllb == "eng_Latn"
    assert TARGET_LANGUAGES["ar"].nllb == "arb_Arab"
    assert TARGET_LANGUAGES["zh"].xtts == "zh-cn"
```

- [ ] **Step 2: Run RED**

```powershell
uv run --project worker pytest worker/tests/test_translation.py -q
```

- [ ] **Step 3: Implement immutable language metadata**

```python
from dataclasses import dataclass


@dataclass(frozen=True)
class Target:
    nllb: str
    xtts: str


TARGET_LANGUAGES = {
    "ar": Target("arb_Arab", "ar"),
    "zh": Target("zho_Hans", "zh-cn"),
    "cs": Target("ces_Latn", "cs"),
    "nl": Target("nld_Latn", "nl"),
    "en": Target("eng_Latn", "en"),
    "fr": Target("fra_Latn", "fr"),
    "de": Target("deu_Latn", "de"),
    "hi": Target("hin_Deva", "hi"),
    "hu": Target("hun_Latn", "hu"),
    "it": Target("ita_Latn", "it"),
    "ja": Target("jpn_Jpan", "ja"),
    "ko": Target("kor_Hang", "ko"),
    "pl": Target("pol_Latn", "pl"),
    "pt": Target("por_Latn", "pt"),
    "es": Target("spa_Latn", "es"),
    "tr": Target("tur_Latn", "tr"),
}
```

- [ ] **Step 4: Implement CTranslate2 NLLB adapter**

Use source code `rus_Cyrl`, target prefix from the mapping, and call
`translator.unload_model()` after translation when the GPU manager requests
XTTS residency.

- [ ] **Step 5: Verify GREEN**

```powershell
uv run --project worker pytest worker/tests/test_translation.py -q
```

- [ ] **Step 6: Commit**

```powershell
git add worker
git commit -m "feat: add Russian NLLB translation"
```

---

### Task 8: Implement ephemeral XTTS speaker sessions

**Files:**

- Create: `worker/tests/test_synthesis.py`
- Create: `worker/voice_translator_worker/privacy/session_store.py`
- Create: `worker/voice_translator_worker/pipeline/synthesis.py`

- [ ] **Step 1: Write cleanup-first tests**

```python
from uuid import uuid4

from voice_translator_worker.privacy.session_store import SpeakerSessionStore


def test_delete_removes_reference_and_conditioning() -> None:
    store = SpeakerSessionStore()
    session_id = uuid4()
    store.put(session_id, bytearray(b"pcm"), object())

    store.delete(session_id)

    assert store.contains(session_id) is False
```

- [ ] **Step 2: Run RED**

```powershell
uv run --project worker pytest worker/tests/test_synthesis.py -q
```

- [ ] **Step 3: Implement a memory-only store**

```python
from dataclasses import dataclass
from uuid import UUID


@dataclass
class SpeakerSession:
    reference_pcm: bytearray
    conditioning: object


class SpeakerSessionStore:
    def __init__(self) -> None:
        self._sessions: dict[UUID, SpeakerSession] = {}

    def put(self, session_id: UUID, pcm: bytearray, conditioning: object) -> None:
        self._sessions[session_id] = SpeakerSession(pcm, conditioning)

    def get(self, session_id: UUID) -> SpeakerSession:
        return self._sessions[session_id]

    def contains(self, session_id: UUID) -> bool:
        return session_id in self._sessions

    def delete(self, session_id: UUID) -> None:
        session = self._sessions.pop(session_id, None)
        if session is not None:
            session.reference_pcm[:] = b""

    def clear(self) -> None:
        for session_id in list(self._sessions):
            self.delete(session_id)
```

- [ ] **Step 4: Implement XTTS behind an injected engine**

The adapter receives `speaker_wav` from memory, maps the target language through
`TARGET_LANGUAGES`, returns WAV bytes, and never accepts a filesystem path from
the API.

- [ ] **Step 5: Run GREEN**

```powershell
uv run --project worker pytest worker/tests/test_synthesis.py -q
```

- [ ] **Step 6: Commit**

```powershell
git add worker
git commit -m "feat: add ephemeral XTTS speaker sessions"
```

---

### Task 9: Implement the authenticated phrase API

**Files:**

- Modify: `worker/voice_translator_worker/api.py`
- Create: `worker/voice_translator_worker/pipeline/service.py`
- Create: `worker/tests/test_api.py`

- [ ] **Step 1: Write an API test using fake pipeline components**

The test must:

1. create a speaker session with WAV bytes;
2. post Russian phrase WAV and target `en`;
3. assert `audio/wav`;
4. delete the session;
5. assert a second translation returns 404.

- [ ] **Step 2: Run RED**

```powershell
uv run --project worker pytest worker/tests/test_api.py -q
```

- [ ] **Step 3: Implement endpoints from `worker.openapi.yaml`**

Use `UploadFile`, cap reference/phrase uploads at 30 seconds of mono PCM,
validate target code before inference, and return these headers:

```text
X-Request-Id
X-Asr-Ms
X-Translate-Ms
X-Synthesize-Ms
X-Performance-Profile
```

- [ ] **Step 4: Add lifespan cleanup**

On shutdown:

```python
@asynccontextmanager
async def lifespan(app: FastAPI):
    yield
    app.state.speaker_sessions.clear()
    app.state.model_registry.unload_all()
    release_torch_memory()
```

- [ ] **Step 5: Run GREEN**

```powershell
uv run --project worker pytest worker/tests -q
```

- [ ] **Step 6: Commit**

```powershell
git add worker specs/001-realtime-voice-translation/contracts/worker.openapi.yaml
git commit -m "feat: expose local phrase translation API"
```

---

### Task 10: Launch and authenticate the worker from .NET

**Files:**

- Create: `tests/VoiceTranslator.IntegrationTests/Worker/WorkerProcessManagerTests.cs`
- Create: `src/VoiceTranslator.Application/Ports/ILocalWorker.cs`
- Create: `src/VoiceTranslator.Infrastructure.LocalWorker/WorkerProcessManager.cs`
- Create: `src/VoiceTranslator.Infrastructure.LocalWorker/LocalWorkerClient.cs`
- Create: `src/VoiceTranslator.WorkerHost/Program.cs`

- [ ] **Step 1: Write a failing launch-token test**

```csharp
[Fact]
public async Task StartAsync_generates_a_new_token_for_each_process()
{
    var launcher = new FakeWorkerLauncher();
    var manager = new WorkerProcessManager(launcher);

    WorkerHandle first = await manager.StartAsync(CancellationToken.None);
    await manager.StopAsync(CancellationToken.None);
    WorkerHandle second = await manager.StartAsync(CancellationToken.None);

    first.Token.Should().NotBe(second.Token);
}
```

- [ ] **Step 2: Run RED**

```powershell
& .\.dotnet\dotnet.exe test tests/VoiceTranslator.IntegrationTests -c Release
```

- [ ] **Step 3: Implement the worker handle**

```csharp
namespace VoiceTranslator.Application.Ports;

public sealed record WorkerHandle(
    int ProcessId,
    Uri Endpoint,
    string Token,
    string PerformanceProfile);
```

Generate a 32-byte token with `RandomNumberGenerator.GetBytes(32)`, encode it as
hex, pass it only through the child process environment, and set it on the
client's `X-Worker-Token` header.

- [ ] **Step 4: Implement readiness and shutdown**

The manager must wait for authenticated `/v1/health`, kill the child tree after
a two-second graceful timeout, and dispose its `HttpClient`.

- [ ] **Step 5: Verify GREEN**

```powershell
& .\.dotnet\dotnet.exe test tests/VoiceTranslator.IntegrationTests -c Release
```

- [ ] **Step 6: Commit**

```powershell
git add src tests
git commit -m "feat: manage authenticated local worker process"
```

---

### Task 11: Implement audio capture, queueing, and physical output MVP

**Files:**

- Create: `tests/VoiceTranslator.IntegrationTests/Audio/AudioPipelineTests.cs`
- Create: `src/VoiceTranslator.Infrastructure.Audio/Capture/WasapiMicrophoneCapture.cs`
- Create: `src/VoiceTranslator.Infrastructure.Audio/Playback/WasapiPlaybackSink.cs`
- Create: `src/VoiceTranslator.Infrastructure.Audio/SignalSafety/SoftLimiter.cs`
- Create: `src/VoiceTranslator.Application/Orchestration/BoundedPhraseQueue.cs`
- Create: `src/VoiceTranslator.Application/Orchestration/TranslationPipeline.cs`

- [ ] **Step 1: Write failing limiter and queue tests**

```csharp
[Fact]
public void Limiter_clamps_samples_to_safe_range()
{
    float[] samples = [2.0f, -2.0f, 0.25f];

    SoftLimiter.Process(samples);

    samples.Should().OnlyContain(x => x is >= -1.0f and <= 1.0f);
}

[Fact]
public void Queue_keeps_only_two_newest_complete_phrases()
{
    var queue = new BoundedPhraseQueue(capacity: 2);
    queue.Enqueue(new Phrase("one"));
    queue.Enqueue(new Phrase("two"));
    queue.Enqueue(new Phrase("three"));

    queue.Select(x => x.Id).Should().Equal("two", "three");
}
```

- [ ] **Step 2: Run RED**

```powershell
& .\.dotnet\dotnet.exe test tests/VoiceTranslator.IntegrationTests -c Release
```

- [ ] **Step 3: Implement minimal audio primitives**

Use `WasapiCapture` for the selected capture endpoint, normalize to mono 16 kHz
PCM for the worker, and use `WasapiOut` plus `BufferedWaveProvider` for output.
Set `DiscardOnBufferOverflow = true`; clear buffers on stop.

- [ ] **Step 4: Implement sequential phrase orchestration**

The orchestrator:

1. receives a completed PCM phrase;
2. places it in capacity-two queue;
3. calls `ILocalWorker.TranslatePhraseAsync`;
4. drops the result when its request was canceled or superseded;
5. limits and plays returned PCM;
6. disposes all buffers.

- [ ] **Step 5: Run GREEN**

```powershell
& .\.dotnet\dotnet.exe test tests/VoiceTranslator.IntegrationTests -c Release
```

- [ ] **Step 6: Commit**

```powershell
git add src tests
git commit -m "feat: add bounded local audio pipeline"
```

---

### Task 12: Build the WPF MVP flow

**Files:**

- Create: `src/VoiceTranslator.App/ViewModels/MainViewModel.cs`
- Create: `src/VoiceTranslator.App/Views/ModelSetupView.xaml`
- Move/Modify: `src/VoiceTranslator.App/MainWindow.xaml` to `src/VoiceTranslator.App/Views/MainWindow.xaml`
- Modify: `src/VoiceTranslator.App/App.xaml.cs`
- Create: `tests/VoiceTranslator.UnitTests/ViewModels/MainViewModelTests.cs`

- [ ] **Step 1: Write failing command-state tests**

Test that Start is disabled until:

- model preflight passed;
- microphone and physical output selected;
- target language selected;
- per-session speaker consent accepted;
- worker is ready.

- [ ] **Step 2: Run RED**

```powershell
& .\.dotnet\dotnet.exe test tests/VoiceTranslator.UnitTests -c Release
```

- [ ] **Step 3: Implement the minimal view model**

Expose:

```csharp
public IReadOnlyList<TargetLanguage> TargetLanguages { get; }
public TargetLanguage? SelectedTargetLanguage { get; set; }
public bool SpeakerConsentAccepted { get; set; }
public SessionState State { get; }
public string PerformanceProfile { get; }
public string StatusMessage { get; }
public ICommand StartCommand { get; }
public ICommand StopCommand { get; }
```

- [ ] **Step 4: Implement accessible UI**

The window must include keyboard-operable target/device selectors, explicit
noncommercial license notice, model setup state, timbre consent, status live
region, Start and Stop.

- [ ] **Step 5: Run GREEN and launch smoke test**

```powershell
& .\.dotnet\dotnet.exe test tests/VoiceTranslator.UnitTests -c Release
& .\.dotnet\dotnet.exe run --project src/VoiceTranslator.App
```

Expected: the window opens and remains responsive with worker absent; Start is
disabled and status explains the missing worker/models.

- [ ] **Step 6: Commit**

```powershell
git add src tests
git commit -m "feat: add local translation MVP interface"
```

---

### Task 13: Add virtual/dual output and recovery

**Files:**

- Create: `tests/VoiceTranslator.IntegrationTests/Recovery/WorkerRecoveryTests.cs`
- Create: `tests/VoiceTranslator.WindowsE2ETests/OutputRoutingTests.cs`
- Create: `worker/tests/test_recovery.py`
- Create: `worker/voice_translator_worker/pipeline/recovery.py`
- Create: `src/VoiceTranslator.Infrastructure.Audio/Routing/AudioOutputRouter.cs`
- Create: `src/VoiceTranslator.Application/Orchestration/SessionFailureCoordinator.cs`

- [ ] **Step 1: Write failure tests**

Cover:

- worker process exits;
- health heartbeat exceeds two seconds;
- balanced profile raises `torch.cuda.OutOfMemoryError`;
- low-memory retry also fails;
- one output sink fails while another remains active;
- stop clears queued and playing audio.

- [ ] **Step 2: Run RED**

```powershell
uv run --project worker pytest worker/tests/test_recovery.py -q
& .\.dotnet\dotnet.exe test tests/VoiceTranslator.IntegrationTests -c Release
```

- [ ] **Step 3: Implement one OOM retry**

Python flow:

```python
try:
    return pipeline.run(request, profile="balanced")
except torch.cuda.OutOfMemoryError:
    registry.unload_all()
    release_torch_memory()
    return pipeline.run(request, profile="low-memory")
```

Do not catch the second OOM; convert it to HTTP 507 and stop the session.

- [ ] **Step 4: Implement independent output sinks**

Physical and virtual sinks receive the same immutable PCM payload but maintain
separate buffers and cancellation. Never mix capture PCM into the virtual sink.

- [ ] **Step 5: Verify GREEN**

Run both test commands from Step 2. Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add worker src tests
git commit -m "feat: add output routing and local recovery"
```

---

### Task 14: Validate all languages, privacy, latency, and packaging

**Files:**

- Create: `worker/tests/test_privacy.py`
- Create: `tests/VoiceTranslator.IntegrationTests/Privacy/NoSpeechPersistenceTests.cs`
- Create: `tests/VoiceTranslator.PerformanceTests/Rtx3070LatencyTests.cs`
- Create: `specs/001-realtime-voice-translation/capability-results.md`
- Create: `specs/001-realtime-voice-translation/performance-results.md`
- Create: `specs/001-realtime-voice-translation/validation-results.md`
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Add persistence-denial tests**

After normal stop, worker crash, and OOM, recursively inspect writable app/worker
directories and assert no `.wav`, `.pcm`, transcript, translation, or embedding
artifact exists.

- [ ] **Step 2: Add 16-language hardware probe**

For every target:

1. translate one fixed Russian phrase;
2. synthesize with the session speaker;
3. record pass/fail, stage timings and peak VRAM;
4. expose only passing targets to the app.

- [ ] **Step 3: Run complete validation**

```powershell
& .\.dotnet\dotnet.exe format VoiceTranslator.slnx --verify-no-changes
& .\.dotnet\dotnet.exe build VoiceTranslator.slnx -c Release
& .\.dotnet\dotnet.exe test VoiceTranslator.slnx -c Release
uv run --project worker pytest worker/tests -q
```

Expected: all tests pass with no warnings.

- [ ] **Step 4: Run RTX 3070 acceptance**

Execute `specs/001-realtime-voice-translation/quickstart.md`. Record p50/p90,
active profile, per-language capability and cleanup evidence.

- [ ] **Step 5: Package without models**

Publish the WPF host and WorkerHost. Package Python bootstrap/lock files but not
downloaded model files or user speech.

- [ ] **Step 6: Commit**

```powershell
git add .github specs src worker tests models README.md
git commit -m "test: validate local Russian voice translation"
```
