import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace


MODULE_PATH = Path(__file__).parents[1] / "autopilot.py"
SPEC = importlib.util.spec_from_file_location("autopilot", MODULE_PATH)
autopilot = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = autopilot
SPEC.loader.exec_module(autopilot)


class FakeClient:
    def __init__(self, responses=None):
        self.responses = list(responses or [])
        self.calls = []

    def request(self, method, path, payload=None):
        self.calls.append((method, path, payload))
        if not self.responses:
            return {}
        response = self.responses.pop(0)
        if isinstance(response, Exception):
            raise response
        return response


class AutopilotTests(unittest.TestCase):
    def make_app(self, state_dir, jules=None, github=None):
        config = SimpleNamespace(
            owner="chlenonozka",
            repository="voicetranslator",
            branch="main",
            state_dir=Path(state_dir),
            install_dir=Path(state_dir) / "install",
        )
        return autopilot.Autopilot(
            config,
            autopilot.StateStore(Path(state_dir)),
            jules or FakeClient(),
            github or FakeClient(),
            checkout_sync=lambda _: None,
        )

    def test_no_workflow_runs_is_pending(self):
        self.assertEqual(("pending", []), autopilot.classify_workflow_runs([]))

    def test_incomplete_workflow_run_is_pending(self):
        self.assertEqual(
            ("pending", []),
            autopilot.classify_workflow_runs([{"name": "CI", "status": "in_progress", "conclusion": None}]),
        )

    def test_successful_workflow_runs_pass(self):
        self.assertEqual(
            ("passed", []),
            autopilot.classify_workflow_runs(
                [
                    {"name": "CI", "status": "completed", "conclusion": "success"},
                    {"name": "optional", "status": "completed", "conclusion": "skipped"},
                ]
            ),
        )

    def test_failed_workflow_run_fails(self):
        outcome, failures = autopilot.classify_workflow_runs(
            [{"name": "CI", "status": "completed", "conclusion": "failure"}]
        )
        self.assertEqual("failed", outcome)
        self.assertEqual(["CI (failure)"], failures)

    def test_feedback_message_is_not_repeated(self):
        with tempfile.TemporaryDirectory() as directory:
            jules = FakeClient()
            app = self.make_app(directory, jules=jules)
            state = app.store.empty_state()
            state.update(active_session_id="abc", session_type="build")
            session = {"state": "AWAITING_USER_FEEDBACK"}
            app.reconcile_session(state, session)
            app.reconcile_session(state, session)
            sends = [call for call in jules.calls if call[1].endswith(":sendMessage")]
            self.assertEqual(1, len(sends))

    def test_feedback_message_is_sent_for_a_new_feedback_episode(self):
        with tempfile.TemporaryDirectory() as directory:
            jules = FakeClient()
            app = self.make_app(directory, jules=jules)
            state = app.store.empty_state()
            state.update(active_session_id="abc", session_type="build")

            app.reconcile_session(state, {"state": "AWAITING_USER_FEEDBACK"})
            app.reconcile_session(state, {"state": "IN_PROGRESS"})
            app.reconcile_session(state, {"state": "AWAITING_USER_FEEDBACK"})

            sends = [call for call in jules.calls if call[1].endswith(":sendMessage")]
            self.assertEqual(2, len(sends))

    def test_three_failed_repairs_leave_pr_open(self):
        with tempfile.TemporaryDirectory() as directory:
            app = self.make_app(directory)
            state = app.store.empty_state()
            state.update(active_session_id="repair", session_type="repair", pr_number=8, repair_attempts=3)
            session = {"outputs": [{"pullRequest": {"url": "https://github.com/chlenonozka/voicetranslator/pull/8"}}]}
            github = FakeClient(
                [
                    {"number": 8, "state": "open", "head": {"sha": "deadbeef", "ref": "fix/ci"}},
                    {"workflow_runs": [{"name": "CI", "status": "completed", "conclusion": "failure"}]},
                ]
            )
            app.github = github
            app.reconcile_completed_session(state, session)
            self.assertIsNone(state["active_session_id"])
            self.assertFalse(any(call[0] == "POST" for call in app.jules.calls))

    def test_state_writes_are_atomic_json(self):
        with tempfile.TemporaryDirectory() as directory:
            store = autopilot.StateStore(Path(directory))
            state = store.empty_state()
            state["active_session_id"] = "session-1"
            store.save(state)
            self.assertEqual("session-1", json.loads(store.path.read_text(encoding="utf-8"))["active_session_id"])
            self.assertFalse(list(Path(directory).glob("state.*.tmp")))


if __name__ == "__main__":
    unittest.main()
