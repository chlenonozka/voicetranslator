from fastapi import Depends, FastAPI

from .auth import token_dependency


def create_app(launch_token: str) -> FastAPI:
    require_token = token_dependency(launch_token)
    app = FastAPI(
        title="Local Voice Translation Worker",
        version="0.2.0",
    )

    @app.get(
        "/v1/health",
        dependencies=[Depends(require_token)],
    )
    async def health() -> dict[str, str]:
        return {"status": "ready"}

    return app
