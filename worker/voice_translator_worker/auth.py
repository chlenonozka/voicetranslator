import hmac
from collections.abc import Callable, Coroutine
from typing import Any

from fastapi import Header, HTTPException, status


TokenDependency = Callable[
    [str | None],
    Coroutine[Any, Any, None],
]


def token_dependency(expected_token: str) -> TokenDependency:
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
