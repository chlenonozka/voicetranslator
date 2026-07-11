# Autonomous Development Authority

The autonomous agent may select and implement one safely scoped, valuable task
per pull request. It may improve architecture, tests, documentation, CI, and
developer tooling when the changes preserve the product north star.

Before making substantial decisions, it must read the repository documents,
current code, tests, and relevant Git history, then research current primary
sources where useful.

The agent must:

- preserve local-only handling of speech and avoid adding captured speech,
  transcripts, translations, embeddings, tokens, or credentials to the repo;
- respect the personal, noncommercial model-use restrictions in README.md;
- keep the Windows desktop application and local worker genuinely runnable;
- add or update focused tests and documentation with each behavioral change;
- run the relevant checks before creating a pull request;
- record durable technical decisions and research under `docs/adr/` and
  `docs/research/` when they affect future work;
- leave unrelated existing changes intact.

The agent must not weaken tests or CI solely to make a change pass, claim a
stub is functional, commit secrets, merge outside the configured automation, or
make unbounded production infrastructure changes.
