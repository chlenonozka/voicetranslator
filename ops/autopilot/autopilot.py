#!/usr/bin/env python3
"""A small, restart-safe Jules to GitHub reconciliation worker."""

from __future__ import annotations

import argparse
import contextlib
import json
import os
import re
import stat
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterator, Mapping, Protocol
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlparse
from urllib.request import Request, urlopen

try:
    import fcntl
except ImportError:  # pragma: no cover - the service runs on Linux; tests also run on Windows.
    fcntl = None


JULES_BASE_URL = "https://jules.googleapis.com/v1alpha"
GITHUB_BASE_URL = "https://api.github.com"
ALLOWED_CI_CONCLUSIONS = {"success", "neutral", "skipped"}
WAITING_STATES = {"QUEUED", "PLANNING", "IN_PROGRESS"}
TERMINAL_FAILURE_STATES = {"FAILED", "CANCELLED", "EXPIRED"}
MAX_HISTORY = 100
MAX_REPAIRS = 3

BUILD_PROMPT = """You are continuing autonomous development of this project.

Read every existing repository document, especially AGENTS.md, NORTH_STAR.md,
AUTONOMY.md, RESEARCH.md, README.md, the entire current repository, and Git
history. Research current public primary sources before making significant
technical decisions.

Choose one high-value, safely scoped task that advances the product. Implement
it completely, update tests and documentation, validate the result, and create
a pull request. Do not claim stubbed behavior is implemented. Do not ask for
standard technical decisions; choose the safest reasonable option yourself.
"""

FEEDBACK_PROMPT = (
    "Proceed autonomously. Study the repository and choose the safest "
    "reasonable option that advances the product. Continue without waiting "
    "for human feedback."
)


class AutopilotError(RuntimeError):
    """An expected operational error that is safe to log without secrets."""


class JsonClient(Protocol):
    def request(self, method: str, path: str, payload: Mapping[str, Any] | None = None) -> Any:
        ...


@dataclass(frozen=True)
class Config:
    jules_api_key: str
    github_token: str
    owner: str
    repository: str
    branch: str
    state_dir: Path
    install_dir: Path = Path("/opt/voicetranslator-autopilot")

    @classmethod
    def from_environment(cls) -> "Config":
        required = (
            "JULES_API_KEY",
            "GITHUB_TOKEN",
            "PROJECT_GITHUB_OWNER",
            "PROJECT_GITHUB_REPOSITORY",
            "PROJECT_GITHUB_BRANCH",
            "PROJECT_AUTOPILOT_STATE_DIR",
        )
        missing = [name for name in required if not os.environ.get(name)]
        if missing:
            raise AutopilotError("missing required configuration: " + ", ".join(missing))
        return cls(
            jules_api_key=os.environ["JULES_API_KEY"],
            github_token=os.environ["GITHUB_TOKEN"],
            owner=os.environ["PROJECT_GITHUB_OWNER"],
            repository=os.environ["PROJECT_GITHUB_REPOSITORY"],
            branch=os.environ["PROJECT_GITHUB_BRANCH"],
            state_dir=Path(os.environ["PROJECT_AUTOPILOT_STATE_DIR"]),
        )


class HttpJsonClient:
    def __init__(self, base_url: str, headers: Mapping[str, str]) -> None:
        self.base_url = base_url.rstrip("/")
        self.headers = dict(headers)

    def request(self, method: str, path: str, payload: Mapping[str, Any] | None = None) -> Any:
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        headers = {"Accept": "application/json", **self.headers}
        if data is not None:
            headers["Content-Type"] = "application/json"
        request = Request(self.base_url + path, data=data, headers=headers, method=method)
        try:
            with urlopen(request, timeout=30) as response:
                body = response.read()
        except HTTPError as error:
            raise AutopilotError(f"HTTP {error.code} for {method} {path}") from error
        except URLError as error:
            raise AutopilotError(f"network error for {method} {path}: {error.reason}") from error
        if not body:
            return {}
        try:
            return json.loads(body.decode("utf-8"))
        except json.JSONDecodeError as error:
            raise AutopilotError(f"invalid JSON response for {method} {path}") from error


class StateStore:
    def __init__(self, state_dir: Path) -> None:
        self.state_dir = state_dir
        self.path = state_dir / "state.json"
        self.lock_path = state_dir / "autopilot.lock"

    @staticmethod
    def empty_state() -> dict[str, Any]:
        return {
            "active_session_id": None,
            "session_type": None,
            "pr_number": None,
            "repair_attempts": 0,
            "feedback_sent": False,
            "last_feedback_activity_id": None,
            "paused": False,
            "pause_after_current": False,
            "history": [],
        }

    @contextlib.contextmanager
    def locked(self) -> Iterator[None]:
        self.state_dir.mkdir(mode=0o700, parents=True, exist_ok=True)
        with self.lock_path.open("a+", encoding="utf-8") as lock_file:
            if fcntl is None:
                import msvcrt

                lock_file.seek(0)
                lock_file.write("0")
                lock_file.flush()
                lock_file.seek(0)
                msvcrt.locking(lock_file.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                fcntl.flock(lock_file.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            try:
                yield
            finally:
                if fcntl is None:
                    import msvcrt

                    lock_file.seek(0)
                    msvcrt.locking(lock_file.fileno(), msvcrt.LK_UNLCK, 1)
                else:
                    fcntl.flock(lock_file.fileno(), fcntl.LOCK_UN)

    def load(self) -> dict[str, Any]:
        if not self.path.exists():
            return self.empty_state()
        try:
            stored = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise AutopilotError(f"cannot read state file: {error}") from error
        state = self.empty_state()
        state.update(stored)
        return state

    def save(self, state: Mapping[str, Any]) -> None:
        self.state_dir.mkdir(mode=0o700, parents=True, exist_ok=True)
        descriptor, temporary_name = tempfile.mkstemp(prefix="state.", suffix=".tmp", dir=self.state_dir)
        temporary_path = Path(temporary_name)
        try:
            with os.fdopen(descriptor, "w", encoding="utf-8") as temporary_file:
                json.dump(state, temporary_file, ensure_ascii=True, sort_keys=True, indent=2)
                temporary_file.write("\n")
                temporary_file.flush()
                os.fsync(temporary_file.fileno())
            os.chmod(temporary_path, 0o600)
            os.replace(temporary_path, self.path)
        finally:
            temporary_path.unlink(missing_ok=True)


def record_event(state: dict[str, Any], message: str) -> None:
    history = state.setdefault("history", [])
    history.append({"time": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "message": message})
    del history[:-MAX_HISTORY]


def clear_active_state(state: dict[str, Any]) -> None:
    state.update(
        active_session_id=None,
        session_type=None,
        pr_number=None,
        repair_attempts=0,
        feedback_sent=False,
        last_feedback_activity_id=None,
    )


def session_id(session: Mapping[str, Any]) -> str:
    value = str(session.get("id") or session.get("name", "").removeprefix("sessions/"))
    if not value:
        raise AutopilotError("Jules response did not include a session id")
    return value


def extract_pull_request_url(session: Mapping[str, Any]) -> str:
    for output in session.get("outputs", []):
        pull_request = output.get("pullRequest", {}) if isinstance(output, Mapping) else {}
        url = pull_request.get("url") if isinstance(pull_request, Mapping) else None
        if isinstance(url, str):
            return url
    raise AutopilotError("completed Jules session did not include a pull request URL")


def pull_request_number(url: str, owner: str, repository: str) -> int:
    parsed = urlparse(url)
    expected_path = f"/{owner}/{repository}/pull/"
    if parsed.scheme != "https" or parsed.netloc != "github.com" or not parsed.path.startswith(expected_path):
        raise AutopilotError("Jules returned a pull request outside the configured repository")
    value = parsed.path.removeprefix(expected_path).strip("/")
    if not re.fullmatch(r"[1-9][0-9]*", value):
        raise AutopilotError("Jules returned an invalid pull request URL")
    return int(value)


def classify_workflow_runs(runs: list[Mapping[str, Any]]) -> tuple[str, list[str]]:
    if not runs:
        return "pending", []
    failures: list[str] = []
    for run in runs:
        if run.get("status") != "completed":
            return "pending", []
        conclusion = run.get("conclusion")
        if conclusion not in ALLOWED_CI_CONCLUSIONS:
            failures.append(f"{run.get('name', 'workflow')} ({conclusion or 'no conclusion'})")
    return ("failed", failures) if failures else ("passed", [])


@contextlib.contextmanager
def git_askpass(token: str, state_dir: Path) -> Iterator[dict[str, str]]:
    state_dir.mkdir(mode=0o700, parents=True, exist_ok=True)
    path = state_dir / f"git-askpass-{os.getpid()}"
    path.write_text("#!/bin/sh\nprintf '%s\\n' \"$GITHUB_TOKEN\"\n", encoding="utf-8")
    path.chmod(stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR)
    environment = os.environ.copy()
    environment.update(GIT_ASKPASS=str(path), GIT_TERMINAL_PROMPT="0", GITHUB_TOKEN=token)
    try:
        yield environment
    finally:
        path.unlink(missing_ok=True)


def sync_checkout(config: Config) -> None:
    checkout = config.install_dir / "repo"
    remote = f"https://github.com/{config.owner}/{config.repository}.git"
    with git_askpass(config.github_token, config.state_dir) as environment:
        if not (checkout / ".git").exists():
            checkout.parent.mkdir(mode=0o755, parents=True, exist_ok=True)
            subprocess.run(
                ["git", "clone", "--origin", "origin", "--branch", config.branch, remote, str(checkout)],
                check=True,
                env=environment,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.PIPE,
                text=True,
            )
        subprocess.run(["git", "fetch", "origin", config.branch], check=True, cwd=checkout, env=environment)
        subprocess.run(["git", "reset", "--hard", f"origin/{config.branch}"], check=True, cwd=checkout, env=environment)


class Autopilot:
    def __init__(
        self,
        config: Config,
        store: StateStore,
        jules: JsonClient,
        github: JsonClient,
        checkout_sync=sync_checkout,
    ) -> None:
        self.config = config
        self.store = store
        self.jules = jules
        self.github = github
        self.checkout_sync = checkout_sync

    def run(self) -> None:
        with self.store.locked():
            state = self.store.load()
            if state["paused"]:
                return
            self.checkout_sync(self.config)
            if not state["active_session_id"]:
                if state["pause_after_current"]:
                    state["paused"] = True
                    state["pause_after_current"] = False
                    record_event(state, "paused before starting a new session")
                else:
                    self.start_build(state)
                self.store.save(state)
                return

            session = self.jules.request("GET", f"/sessions/{state['active_session_id']}")
            self.reconcile_session(state, session)
            self.store.save(state)

    def start_build(self, state: dict[str, Any]) -> None:
        payload = {
            "title": "Autonomous repository improvement",
            "prompt": BUILD_PROMPT,
            "sourceContext": {
                "source": f"sources/github/{self.config.owner}/{self.config.repository}",
                "githubRepoContext": {"startingBranch": self.config.branch},
            },
            "automationMode": "AUTO_CREATE_PR",
            "requirePlanApproval": False,
        }
        created = self.jules.request("POST", "/sessions", payload)
        state.update(
            active_session_id=session_id(created),
            session_type="build",
            feedback_sent=False,
            last_feedback_activity_id=None,
        )
        record_event(state, f"started build session {state['active_session_id']}")

    def start_repair(self, state: dict[str, Any], pr: Mapping[str, Any], failures: list[str]) -> None:
        number = int(pr["number"])
        branch = str(pr["head"]["ref"])
        attempts = int(state["repair_attempts"]) + 1
        prompt = (
            f"Repair pull request #{number} on its existing branch `{branch}`. Do not create a competing "
            "implementation or a new branch. Read the failing GitHub Actions results below, fix the root cause, "
            "update tests as appropriate, and push the repair to the existing pull request branch.\n\n"
            "Failed workflows:\n- " + "\n- ".join(failures)
        )
        payload = {
            "title": f"Repair CI for PR #{number}",
            "prompt": prompt,
            "sourceContext": {
                "source": f"sources/github/{self.config.owner}/{self.config.repository}",
                "githubRepoContext": {"startingBranch": branch},
            },
            "automationMode": "AUTO_CREATE_PR",
            "requirePlanApproval": False,
        }
        created = self.jules.request("POST", "/sessions", payload)
        state.update(
            active_session_id=session_id(created),
            session_type="repair",
            pr_number=number,
            repair_attempts=attempts,
            feedback_sent=False,
            last_feedback_activity_id=None,
        )
        record_event(state, f"started repair {attempts}/{MAX_REPAIRS} for PR #{number}")

    def latest_agent_message_activity_id(self, session_id_value: str) -> str | None:
        page_token: str | None = None
        seen_page_tokens: set[str] = set()
        latest: tuple[str, str] | None = None

        while True:
            query = {"pageSize": 100}
            if page_token:
                query["pageToken"] = page_token
            response = self.jules.request(
                "GET",
                f"/sessions/{session_id_value}/activities?{urlencode(query)}",
            )
            for activity in response.get("activities", []):
                if not isinstance(activity, Mapping) or not isinstance(activity.get("agentMessaged"), Mapping):
                    continue
                identity = str(activity.get("name") or activity.get("id") or "")
                if not identity:
                    continue
                candidate = (str(activity.get("createTime") or ""), identity)
                if latest is None or candidate > latest:
                    latest = candidate

            next_page_token = str(response.get("nextPageToken") or "")
            if not next_page_token:
                break
            if next_page_token in seen_page_tokens:
                raise AutopilotError("Jules activities pagination repeated a page token")
            seen_page_tokens.add(next_page_token)
            page_token = next_page_token

        return latest[1] if latest else None

    def reconcile_session(self, state: dict[str, Any], session: Mapping[str, Any]) -> None:
        status = str(session.get("state", ""))
        # A later feedback request is a new episode once Jules has resumed work.
        if status != "AWAITING_USER_FEEDBACK":
            state["feedback_sent"] = False
        if status in WAITING_STATES:
            return
        if status == "AWAITING_PLAN_APPROVAL":
            self.jules.request("POST", f"/sessions/{state['active_session_id']}:approvePlan", {})
            record_event(state, f"approved plan for session {state['active_session_id']}")
            return
        if status == "AWAITING_USER_FEEDBACK":
            activity_id = self.latest_agent_message_activity_id(str(state["active_session_id"]))
            already_answered = (
                state["last_feedback_activity_id"] == activity_id if activity_id else state["feedback_sent"]
            )
            if not already_answered:
                self.jules.request(
                    "POST", f"/sessions/{state['active_session_id']}:sendMessage", {"prompt": FEEDBACK_PROMPT}
                )
                state["feedback_sent"] = True
                state["last_feedback_activity_id"] = activity_id
                record_event(state, f"sent autonomous feedback to session {state['active_session_id']}")
            return
        if status == "COMPLETED":
            self.reconcile_completed_session(state, session)
            return
        if status == "PAUSED":
            record_event(state, f"session {state['active_session_id']} paused; starting a new session next cycle")
            clear_active_state(state)
            return
        if status in TERMINAL_FAILURE_STATES:
            record_event(state, f"session {state['active_session_id']} ended with {status}")
            clear_active_state(state)
            return
        raise AutopilotError(f"unknown Jules session state: {status or 'missing'}")

    def reconcile_completed_session(self, state: dict[str, Any], session: Mapping[str, Any]) -> None:
        try:
            url = extract_pull_request_url(session)
        except AutopilotError:
            record_event(state, f"session {state['active_session_id']} completed without a pull request; starting a new session next cycle")
            clear_active_state(state)
            return
        number = pull_request_number(url, self.config.owner, self.config.repository)
        pr = self.github.request("GET", f"/repos/{self.config.owner}/{self.config.repository}/pulls/{number}")
        if pr.get("state") != "open":
            record_event(state, f"PR #{number} is not open; clearing active state")
            clear_active_state(state)
            return
        sha = str(pr.get("head", {}).get("sha", ""))
        if not sha:
            raise AutopilotError(f"PR #{number} has no head SHA")
        query = urlencode({"head_sha": sha, "event": "pull_request", "per_page": 100})
        response = self.github.request(
            "GET", f"/repos/{self.config.owner}/{self.config.repository}/actions/runs?{query}"
        )
        outcome, failures = classify_workflow_runs(list(response.get("workflow_runs", [])))
        if outcome == "pending":
            record_event(state, f"CI is pending for PR #{number}")
            return
        if outcome == "passed":
            self.github.request(
                "PUT",
                f"/repos/{self.config.owner}/{self.config.repository}/pulls/{number}/merge",
                {"sha": sha, "merge_method": "squash"},
            )
            record_event(state, f"squash-merged PR #{number}")
            clear_active_state(state)
            if state["pause_after_current"]:
                state["paused"] = True
                state["pause_after_current"] = False
                record_event(state, "paused after current pull request")
            return
        if state["session_type"] == "repair" and int(state["repair_attempts"]) >= MAX_REPAIRS:
            record_event(state, f"PR #{number} failed after {MAX_REPAIRS} repair attempts; left open")
            clear_active_state(state)
            return
        self.start_repair(state, pr, failures)

    def command(self, name: str) -> int:
        with self.store.locked():
            state = self.store.load()
            if name == "status":
                print(json.dumps(state, indent=2, sort_keys=True))
                return 0
            if name == "pause-now":
                state.update(paused=True, pause_after_current=False)
                record_event(state, "paused immediately")
            elif name == "pause-after-current":
                if state["active_session_id"]:
                    state["pause_after_current"] = True
                    record_event(state, "will pause after the current pull request")
                else:
                    state["paused"] = True
                    record_event(state, "paused because no session is active")
            elif name == "resume":
                state.update(paused=False, pause_after_current=False)
                record_event(state, "resumed")
            else:
                raise AutopilotError(f"unknown command: {name}")
            self.store.save(state)
        return 0


def build_autopilot() -> Autopilot:
    config = Config.from_environment()
    return Autopilot(
        config=config,
        store=StateStore(config.state_dir),
        jules=HttpJsonClient(JULES_BASE_URL, {"x-goog-api-key": config.jules_api_key}),
        github=HttpJsonClient(
            GITHUB_BASE_URL,
            {
                "Authorization": f"Bearer {config.github_token}",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "voicetranslator-autopilot",
            },
        ),
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("run", "status", "pause-now", "pause-after-current", "resume"), nargs="?", default="run")
    arguments = parser.parse_args(argv)
    try:
        autopilot = build_autopilot()
        if arguments.command == "run":
            autopilot.run()
            return 0
        return autopilot.command(arguments.command)
    except BlockingIOError:
        return 0
    except (AutopilotError, subprocess.CalledProcessError) as error:
        print(f"autopilot: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
