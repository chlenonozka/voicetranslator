import argparse
import os
from collections.abc import Callable, Sequence

import uvicorn
from fastapi import FastAPI

from .api import create_app


ServerRunner = Callable[..., None]


def run(
    arguments: Sequence[str] | None = None,
    *,
    runner: ServerRunner = uvicorn.run,
) -> None:
    parser = argparse.ArgumentParser(
        description="Local voice translation worker",
    )
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    options = parser.parse_args(arguments)

    launch_token = os.environ.get("VOICE_TRANSLATOR_WORKER_TOKEN")
    if not launch_token:
        raise RuntimeError("Worker launch token is required.")

    app: FastAPI = create_app(launch_token)
    runner(app, host=options.host, port=options.port)


if __name__ == "__main__":
    run()
