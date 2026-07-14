from dataclasses import dataclass
from time import perf_counter
from typing import Protocol
from uuid import UUID, uuid4

from voice_translator_worker.privacy.session_store import SpeakerSessionStore

from .asr import Recognition
from .languages import TARGET_LANGUAGES


class SpeakerSessionNotFound(LookupError):
    pass


class LowConfidenceRecognition(ValueError):
    pass


class InvalidTargetLanguage(ValueError):
    pass


class SpeakerConditioner(Protocol):
    def create(self, reference_wav: bytes) -> object: ...


class PhraseRecognizer(Protocol):
    def transcribe(self, audio: bytes) -> Recognition: ...


class PhraseTranslator(Protocol):
    def translate(
        self,
        text: str,
        target_code: str,
        *,
        unload_after: bool = False,
    ) -> str: ...


class PhraseSynthesizer(Protocol):
    def synthesize(
        self,
        session_id: UUID,
        text: str,
        target_code: str,
    ) -> bytes: ...


class ModelResidency(Protocol):
    def activate_profile(self, profile: str) -> None: ...

    def unload_all(self) -> None: ...


class StaticModelResidency:
    def activate_profile(self, profile: str) -> None:
        pass

    def unload_all(self) -> None:
        pass


@dataclass(frozen=True)
class PhraseResult:
    request_id: UUID
    audio_wav: bytes
    asr_ms: float
    translate_ms: float
    synthesize_ms: float
    performance_profile: str


class PhrasePipeline:
    def __init__(
        self,
        *,
        conditioner: SpeakerConditioner,
        asr: PhraseRecognizer,
        translator: PhraseTranslator,
        synthesizer: PhraseSynthesizer,
        sessions: SpeakerSessionStore,
        performance_profile: str,
        profile_controller: ModelResidency | None = None,
    ) -> None:
        self.conditioner = conditioner
        self.asr = asr
        self.translator = translator
        self.synthesizer = synthesizer
        self.sessions = sessions
        self.performance_profile = performance_profile
        self.profile_controller = profile_controller or StaticModelResidency()

    def create_speaker_session(
        self,
        reference_wav: bytes,
        performance_profile: str | None = None,
    ) -> UUID:
        active_profile = performance_profile or self.performance_profile
        self.profile_controller.activate_profile(active_profile)
        session_id = uuid4()
        conditioning = self.conditioner.create(reference_wav)
        self.sessions.put(session_id, bytearray(reference_wav), conditioning)
        return session_id

    def delete_speaker_session(self, session_id: UUID) -> None:
        self.sessions.delete(session_id)

    def translate_phrase(
        self,
        session_id: UUID,
        target_code: str,
        audio_wav: bytes,
        *,
        performance_profile: str | None = None,
    ) -> PhraseResult:
        if not self.sessions.contains(session_id):
            raise SpeakerSessionNotFound(str(session_id))
        if target_code not in TARGET_LANGUAGES:
            raise InvalidTargetLanguage(target_code)

        active_profile = performance_profile or self.performance_profile
        self.profile_controller.activate_profile(active_profile)

        started = perf_counter()
        recognition = self.asr.transcribe(audio_wav)
        asr_ms = _elapsed_ms(started)
        if not recognition.accepted:
            raise LowConfidenceRecognition(recognition.text)

        started = perf_counter()
        translated = self.translator.translate(
            recognition.text,
            target_code,
            unload_after=active_profile != "performance",
        )
        translate_ms = _elapsed_ms(started)

        started = perf_counter()
        synthesized = self.synthesizer.synthesize(
            session_id,
            translated,
            target_code,
        )
        synthesize_ms = _elapsed_ms(started)

        return PhraseResult(
            request_id=uuid4(),
            audio_wav=synthesized,
            asr_ms=asr_ms,
            translate_ms=translate_ms,
            synthesize_ms=synthesize_ms,
            performance_profile=active_profile,
        )

    def clear(self) -> None:
        self.sessions.clear()

    def unload_all(self) -> None:
        self.profile_controller.unload_all()


def _elapsed_ms(started: float) -> float:
    return (perf_counter() - started) * 1000
