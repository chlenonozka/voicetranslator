# VoiceTranslator Jules Autopilot

This directory contains a small standard-library Python worker that reconciles
one Jules session with GitHub every two minutes. It has no listener and opens
no ports. It only makes outbound HTTPS requests to Jules and GitHub.

## Server installation

Copy this directory to the server, then run:

```bash
install -d -m 0755 /opt/voicetranslator-autopilot/source
cp -a /path/to/ops/autopilot/. /opt/voicetranslator-autopilot/source/
/opt/voicetranslator-autopilot/source/voicetranslator-autopilot-update
```

Installation creates an inactive systemd timer and does not alter Nginx,
3x-ui, Docker, firewall rules, or other services.

## Configure secrets and start

Run this through an interactive SSH terminal so neither secret appears in shell
history or process arguments:

```bash
/usr/local/sbin/voicetranslator-autopilot-configure
```

The helper stores the two secrets in `/etc/voicetranslator-autopilot.env` as
`root:root` mode `0600`, enables the timer, and starts a first cycle. Do not
print or copy that file.

The GitHub token must be fine-grained, scoped to `chlenonozka/voicetranslator`,
with repository permissions for Actions read, Contents write, and Pull requests
read/write. The Jules API key must have the repository connected as a Jules
source.

## Operations

```bash
python3 /opt/voicetranslator-autopilot/autopilot.py status
python3 /opt/voicetranslator-autopilot/autopilot.py pause-now
python3 /opt/voicetranslator-autopilot/autopilot.py pause-after-current
python3 /opt/voicetranslator-autopilot/autopilot.py resume
systemctl start voicetranslator-autopilot-stop-all.service
systemctl status voicetranslator-autopilot.timer --no-pager
journalctl -u voicetranslator-autopilot.service -n 100 --no-pager
```

`pause-now` prevents further reconciliation cycles but does not attempt to
cancel a cloud session. `pause-after-current` permits the active PR to finish
and merge only if its GitHub Actions runs pass.

The autopilot also pauses itself after a Jules failure or pause, a completed
session without a pull request, an externally closed pull request, or failed
CI. It does not start a repair or a new session automatically in those cases.
Resume it explicitly only after reviewing the stopped session.

`voicetranslator-autopilot-stop-all.service` deletes all incomplete Jules
sessions and pauses the autopilot. It preserves completed and failed sessions
as history.

## Local tests

```bash
python -m unittest discover -s ops/autopilot/tests -v
```
