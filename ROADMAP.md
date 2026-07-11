# Roadmap

## Fixed Scope

- The approved specification is the feature set. It is not a seed for new
  product ideas.
- The Windows WPF host, authenticated localhost worker, local-only privacy
  boundary, and balanced/low-memory RTX 3070 profiles are the supported
  product, not starting points for additional platforms or services.

## Completion Gates

These are validation obligations for the existing product, not feature work:

1. Run and record the RTX 3070 physical-output acceptance test.
2. Run and record all 16 language and output-routing E2E checks.
3. Record capability, latency/VRAM, meaning, speaker-similarity, and quickstart
   validation evidence in the files named by `specs/001-realtime-voice-translation/tasks.md`.
4. Fix defects uncovered by those checks without expanding the product scope.

## Maintenance Mode

After the completion gates pass, select work in this order:

1. Privacy, security, and data-loss defects.
2. Reproducible crashes, failed startup, broken device routing, model loading,
   or recovery defects.
3. Regressions caused by runtime, dependency, driver, or operating-system
   updates.
4. Measured latency, VRAM, reliability, accessibility, test, CI, and
   documentation improvements that preserve the fixed behavior.

Do not add new user-facing capabilities merely because no bug is currently
open. A clean backlog means monitor compatibility and keep validation current.
