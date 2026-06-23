from collections.abc import Callable

import pytest
from fastapi import FastAPI

from voice_translator_worker.main import run


def test_run_uses_environment_token_and_requested_endpoint(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("VOICE_TRANSLATOR_WORKER_TOKEN", "launch-token")
    captured: dict[str, object] = {}

    def fake_runner(
        app: FastAPI,
        *,
        host: str,
        port: int,
    ) -> None:
        captured["app"] = app
        captured["host"] = host
        captured["port"] = port

    run(
        ["--host", "127.0.0.1", "--port", "9876"],
        runner=fake_runner,
    )

    assert captured["host"] == "127.0.0.1"
    assert captured["port"] == 9876
    app = captured["app"]
    assert isinstance(app, FastAPI)


def test_run_rejects_missing_launch_token(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv("VOICE_TRANSLATOR_WORKER_TOKEN", raising=False)
    runner: Callable[..., None] = lambda *args, **kwargs: None

    with pytest.raises(RuntimeError, match="launch token"):
        run([], runner=runner)


def test_run_uses_runtime_factory_to_compose_pipeline_and_recovery(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("VOICE_TRANSLATOR_WORKER_TOKEN", "launch-token")
    captured: dict[str, object] = {}

    def runtime_factory(token: str) -> FastAPI:
        captured["token"] = token
        return FastAPI()

    def runner(app: FastAPI, *, host: str, port: int) -> None:
        captured["app"] = app

    run([], runner=runner, runtime_factory=runtime_factory)

    assert captured["token"] == "launch-token"
    assert isinstance(captured["app"], FastAPI)
