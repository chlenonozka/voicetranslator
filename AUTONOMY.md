# Autonomous Development Authority

The autonomous agent may select one safely scoped task per pull request only
when it advances the fixed product boundary in NORTH_STAR.md. The agent is a
maintenance and validation contributor, not a product manager.

Before making substantial decisions, it must read the repository documents,
current code, tests, and relevant Git history, then research current primary
sources where useful.

The agent must:

- preserve local-only handling of speech; named voice-reference profiles may
  persist only in the current user's DPAPI-encrypted local store, while
  transcripts, translations, ordinary captured phrases, decrypted references,
  embeddings, tokens, and credentials must never be added to the repo;
- respect the personal, noncommercial model-use restrictions in README.md;
- keep the Windows desktop application and local worker genuinely runnable;
- add or update focused tests and documentation with each behavioral change;
- run the relevant checks before creating a pull request;
- record durable technical decisions and research under `docs/adr/` and
  `docs/research/` when they affect future work;
- leave unrelated existing changes intact.

For each candidate task, first classify it as one of the following:

1. An unfulfilled acceptance gate or an explicit task in the approved spec.
2. A reproducible defect, regression, security issue, privacy issue, or
   dependency compatibility break.
3. A measured reliability, latency, memory, startup, or usability improvement
   within the fixed boundary.
4. Test, CI, packaging, or documentation work needed to verify one of the
   above.

If it cannot be classified this way, do not implement it. Record why it is out
of scope rather than reframing it as a product improvement. Do not create a
feature backlog from ideas found during research, and do not treat ROADMAP.md
as permission to expand the product.

The agent must not weaken tests or CI solely to make a change pass, claim a
stub is functional, commit secrets, merge outside the configured automation, or
make unbounded production infrastructure changes. It must not add a new user
feature, platform, language, service, data store, account system, or cloud
dependency without an explicit user instruction that changes the fixed scope.
