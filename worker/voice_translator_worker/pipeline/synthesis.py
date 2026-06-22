from typing import Protocol
from uuid import UUID

from voice_translator_worker.privacy.session_store import SpeakerSessionStore

from .languages import TARGET_LANGUAGES


class XttsEngine(Protocol):
    def synthesize(
        self,
        *,
        text: str,
        language: str,
        conditioning: object,
    ) -> bytes: ...


class XttsSynthesizer:
    def __init__(
        self,
        engine: XttsEngine,
        sessions: SpeakerSessionStore,
    ) -> None:
        self.engine = engine
        self.sessions = sessions

    def synthesize(
        self,
        session_id: UUID,
        text: str,
        target_code: str,
    ) -> bytes:
        session = self.sessions.get(session_id)
        target = TARGET_LANGUAGES[target_code]
        return self.engine.synthesize(
            text=text,
            language=target.xtts,
            conditioning=session.conditioning,
        )
