# Tasks: Локальный перевод русской речи

**Input**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`,
`quickstart.md`, and the detailed Superpowers implementation plan.

## Phase 1: Existing .NET setup

- [x] T001 Create `VoiceTranslator.slnx` and source/test projects
- [x] T002 Add .NET 10 analyzer and deterministic-build settings in `Directory.Build.props`
- [x] T003 Add central package management in `Directory.Packages.props`
- [x] T004 Add WPF host bootstrap in `src/VoiceTranslator.App/App.xaml.cs`
- [x] T005 Add initial process/service bootstrap in `src/VoiceTranslator.Gateway/Program.cs`
- [x] T006 Add privacy-safe test fixture project in `tests/VoiceTranslator.Testing/`
- [x] T007 Document build and test commands in `README.md`

## Phase 2: Rebase scaffold for local worker

- [x] T008 Remove Azure/MSAL/storage package references from `Directory.Packages.props` and source project files
- [x] T009 Rename `src/VoiceTranslator.Infrastructure.Cloud/` to `src/VoiceTranslator.Infrastructure.LocalWorker/`
- [x] T010 Replace `src/VoiceTranslator.Gateway/` with `src/VoiceTranslator.WorkerHost/`
- [x] T011 Add Python, uv, model cache, and local worker patterns to `.gitignore`
- [x] T012 Create Python 3.11 project and locked dependency groups in `worker/pyproject.toml`
- [ ] T013 Add worker and model manifest directories to `VoiceTranslator.slnx`
- [ ] T014 Update local setup, licensing, model download, and run commands in `README.md`

## Phase 3: Contracts and shared domain

- [ ] T015 [P] Write failing worker authentication and OpenAPI tests in `worker/tests/test_auth.py` and `tests/VoiceTranslator.ContractTests/Worker/WorkerOpenApiTests.cs`
- [x] T016 [P] Write failing session-state tests in `tests/VoiceTranslator.UnitTests/Sessions/TranslationSessionTests.cs`
- [ ] T017 [P] Write failing model manifest/license tests in `tests/VoiceTranslator.UnitTests/Models/ModelManifestTests.cs`
- [ ] T018 [P] Write failing 16-language mapping tests in `worker/tests/test_languages.py`
- [ ] T019 Implement local session, worker, model, capability, and audio entities in `src/VoiceTranslator.Domain/`
- [ ] T020 Implement application ports for worker lifecycle, model management, phrase translation, and output in `src/VoiceTranslator.Application/Ports/`
- [ ] T021 Implement FastAPI token dependency, lifespan cleanup, health and cancellation endpoints in `worker/voice_translator_worker/`
- [ ] T022 Implement typed .NET worker client models and token header handler in `src/VoiceTranslator.Infrastructure.LocalWorker/`

## Phase 4: Model management and hardware preflight

- [ ] T023 [P] Write failing manifest hash, pinned revision, license acknowledgement, and offline tests in `worker/tests/test_model_manager.py`
- [ ] T024 [P] Write failing CUDA profile and OOM downgrade tests in `worker/tests/test_gpu_profiles.py`
- [x] T025 Create pinned manifests for Whisper medium/small, NLLB and XTTS in `models/manifests/`
- [ ] T026 Implement Hugging Face `snapshot_download` and hash verification in `worker/voice_translator_worker/models/model_manager.py`
- [ ] T027 Implement CUDA/VRAM inspection, balanced profile, low-memory profile and cleanup in `worker/voice_translator_worker/models/gpu_profiles.py`
- [ ] T028 Implement per-language translation/synthesis preflight in `worker/voice_translator_worker/pipeline/preflight.py`
- [ ] T029 Implement .NET model inventory/download progress client in `src/VoiceTranslator.Infrastructure.LocalWorker/Models/`
- [ ] T030 Add WPF model/license/preflight screen in `src/VoiceTranslator.App/Views/ModelSetupView.xaml`

## Phase 5: User Story 1 — Russian phrase translation

- [ ] T031 [P] [US1] Write failing Russian ASR confidence and VAD tests in `worker/tests/test_asr.py`
- [ ] T032 [P] [US1] Write failing NLLB Russian-to-target tests in `worker/tests/test_translation.py`
- [ ] T033 [P] [US1] Write failing XTTS speaker-session cleanup tests in `worker/tests/test_synthesis.py`
- [ ] T034 [P] [US1] Write failing WASAPI capture and limiter tests in `tests/VoiceTranslator.IntegrationTests/Audio/`
- [ ] T035 [US1] Write failing end-to-end fake-worker pipeline test in `tests/VoiceTranslator.IntegrationTests/TranslationPipelineTests.cs`
- [ ] T036 [US1] Implement faster-whisper Russian ASR adapter in `worker/voice_translator_worker/pipeline/asr.py`
- [ ] T037 [US1] Implement CTranslate2 NLLB translator and language map in `worker/voice_translator_worker/pipeline/translation.py`
- [ ] T038 [US1] Implement ephemeral XTTS speaker session and synthesis in `worker/voice_translator_worker/pipeline/synthesis.py`
- [ ] T039 [US1] Implement `/v1/speaker-sessions` and `/v1/translate-phrase` in `worker/voice_translator_worker/api.py`
- [ ] T040 [US1] Implement worker process launch, health, restart and token creation in `src/VoiceTranslator.Infrastructure.LocalWorker/WorkerProcessManager.cs`
- [ ] T041 [US1] Implement WASAPI capture/playback and limiter in `src/VoiceTranslator.Infrastructure.Audio/`
- [ ] T042 [US1] Implement bounded phrase orchestration in `src/VoiceTranslator.Application/Orchestration/TranslationPipeline.cs`
- [ ] T043 [US1] Implement WPF consent, target language, status and start/stop flow in `src/VoiceTranslator.App/`
- [ ] T044 [US1] Add RTX 3070 physical-output acceptance test in `tests/VoiceTranslator.WindowsE2ETests/PhysicalOutputTests.cs`

## Phase 6: User Story 2 — Languages, models and routes

- [ ] T045 [P] [US2] Write failing model download UI and catalog tests in `tests/VoiceTranslator.UnitTests/Models/`
- [ ] T046 [P] [US2] Write failing WASAPI device-change and virtual-cable tests in `tests/VoiceTranslator.IntegrationTests/Audio/`
- [ ] T047 [US2] Implement capability catalog persistence without speech content in `src/VoiceTranslator.Infrastructure.LocalWorker/Models/CapabilityCatalogStore.cs`
- [ ] T048 [US2] Implement device enumeration and virtual/dual output router in `src/VoiceTranslator.Infrastructure.Audio/Routing/`
- [ ] T049 [US2] Implement channel test and feedback warning in `src/VoiceTranslator.Application/Orchestration/OutputChannelTestService.cs`
- [ ] T050 [US2] Complete model/device/language/output controls in `src/VoiceTranslator.App/Views/MainWindow.xaml`
- [ ] T051 [US2] Add all-16-language and output routing E2E tests in `tests/VoiceTranslator.WindowsE2ETests/`

## Phase 7: User Story 3 — Recovery and privacy

- [ ] T052 [P] [US3] Write worker crash, cancellation and heartbeat tests in `tests/VoiceTranslator.IntegrationTests/Recovery/WorkerRecoveryTests.cs`
- [ ] T053 [P] [US3] Write CUDA OOM retry and cleanup tests in `worker/tests/test_recovery.py`
- [ ] T054 [P] [US3] Write no-speech-persistence tests in `worker/tests/test_privacy.py` and `tests/VoiceTranslator.IntegrationTests/Privacy/`
- [ ] T055 [US3] Implement worker failure coordinator and two-second shutdown in `src/VoiceTranslator.Application/Orchestration/SessionFailureCoordinator.cs`
- [ ] T056 [US3] Implement OOM cleanup and one low-memory retry in `worker/voice_translator_worker/pipeline/recovery.py`
- [ ] T057 [US3] Implement device-loss handling and stale-buffer disposal in `src/VoiceTranslator.Infrastructure.Audio/`
- [ ] T058 [US3] Add accessible error and recovery UI in `src/VoiceTranslator.App/`

## Phase 8: Validation and packaging

- [ ] T059 [P] Add .NET and Python formatting, tests, license checks, and secret scanning in `.github/workflows/ci.yml`
- [ ] T060 Run all 16 language probes and record results in `specs/001-realtime-voice-translation/capability-results.md`
- [ ] T061 Run RTX 3070 latency/VRAM tests and record p50/p90 in `specs/001-realtime-voice-translation/performance-results.md`
- [ ] T062 Run meaning and speaker-similarity evaluation and record results in `specs/001-realtime-voice-translation/evaluation-results.md`
- [ ] T063 Package .NET host plus managed Python worker bootstrap without model files in `src/VoiceTranslator.App/Package/`
- [ ] T064 Execute `quickstart.md` and record final evidence in `specs/001-realtime-voice-translation/validation-results.md`

## Dependencies

`Phase 2 → Phase 3 → Phase 4 → US1 → US2 → US3 → Validation`

US1 is the MVP. No model inference implementation begins until manifest,
licensing and RTX 3070 preflight tests are green.
