---
name: build-test
description: Runs the fast build/test gate — auto-format, lint, unit tests, and build (excludes integration tests). Dave's fast done-done gate.
---

Framework-owned **recipe** — no placeholders. It runs the commands named in the **Project profile →
Commands** table (`.github/copilot-instructions.md`); that table is the single source of truth for the
actual shell commands, so this file never hardcodes them.

This recipe is **authoritative for gate membership and order**; the Commands table's `Gate` column is a
hint. Reorder for your stack if needed (e.g. type-aware linters that require compiled output should run
after `build`).

## The fast gate (Dave)

**Session bootstrap boundary.** JARVIS/the assistant owns Rust-nightly install/update at session
startup. Dave never invokes `Initialize-RustNightly.ps1` or rustup install/update. If any
current-session nightly check fails, stop and hand back to JARVIS; do not download, update, repair,
or fall back to stale state. A caller-supplied role/switch is not authorization: consumers require
the assistant startup's matching owner/ready-phase/token-hash provenance.

Run these commands in order, resolving each name from the Commands table:

1. `format:fix` — auto-format; this variant **writes** changes
2. `build` — Release restore/rebuild, which prepares assets for the no-restore analyzer pass
3. `lint` — a second Release rebuild with analyzers enabled, no restore, and warnings promoted to errors
4. `test:quick` — unit tests only

…then any additional commands the consumer added to the Commands table.

**Run rules.** Every core row above is **required** — a required command whose Value is `none` or empty
is a **misconfiguration**: **stop and report it** (never silently skip). The skip-`none` rule applies
only to the **optional** rows (`dry-check`, `mutation-test`, `crap-check`), which run in the full gate —
the fast gate has none.

**Unit-only, trait-driven.** Test categories follow
[`docs/meta-design.md#writing-tests`](../../docs/meta-design.md#writing-tests). Before execution,
`test:quick` discovers all 204 assembly cases and fails unless `type=UnitTests` selects 96,
`type=IntegrationTests` selects 108, and the single `scope=External` case is an integration test.
Missing, dual-classified, added, or otherwise drifting cases fail actionably. Quick then runs exactly
the 96 `type=UnitTests` cases; Cargo/process integration, the external freshness overlay, the
standalone acceptance harness, and optional gates (DRY, mutation, CRAP) belong to Bhaskar's full gate
(`.github/skills/build-test-full.md`).

DRY, mutation, and CRAP are disabled in feature 001 (`none`) and deferred to feature 002 P0.

**Zero tolerance, with one temporary exception.** Any warning or error fails the gate except
`MSB3277`, which feature 001 alone grandfathers in the lint command. Feature 002 must resolve the
assembly conflicts and remove that exception; no other warning code may be added to it. Dave is not
done-done until the gate passes under that exact policy.
