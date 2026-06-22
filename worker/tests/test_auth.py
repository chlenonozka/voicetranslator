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
