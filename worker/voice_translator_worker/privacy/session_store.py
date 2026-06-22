from dataclasses import dataclass
from uuid import UUID


@dataclass
class SpeakerSession:
    reference_pcm: bytearray
    conditioning: object


class SpeakerSessionStore:
    def __init__(self) -> None:
        self._sessions: dict[UUID, SpeakerSession] = {}

    def put(
        self,
        session_id: UUID,
        pcm: bytearray,
        conditioning: object,
    ) -> None:
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
