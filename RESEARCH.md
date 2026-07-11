# Research Mandate

Research is required before choosing a dependency or making a decision that
could materially affect privacy, reliability, latency, memory use, licence
compliance, security, or maintainability within the fixed product boundary.

Research is not a reason to expand scope. Competitive features, new platforms,
new languages, cloud services, accounts, and unrelated integrations are out of
scope unless the user explicitly reopens the product boundary.

Prefer official documentation, release notes, model cards, and primary project
repositories. Use GitHub issues and discussions to identify active failure
modes and user pain points. Do not copy code with an incompatible licence.

Write durable findings to `docs/research/` and architecture choices to
`docs/adr/`. Each note should state the source, date checked, decision, and
trade-offs so later sessions do not repeat the same investigation. Record only
findings that help resolve a validated defect, acceptance gap, or measured
maintenance opportunity.
