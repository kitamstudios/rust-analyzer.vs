---
name: build-test-full
description: Runs the full build/test gate — format check, lint, build, the complete test suite (unit + integration + acceptance), plus every optional quality gate that is set. Bhaskar's full done-done gate.
---

Framework-owned **recipe** — no placeholders. It runs the commands named in the **Project profile →
Commands** table (`.github/copilot-instructions.md`); that table is the single source of truth for the
actual shell commands, so this file never hardcodes them.

This recipe is **authoritative for gate membership and order**; the Commands table's `Gate` column is a
hint. Reorder for your stack if needed (e.g. type-aware linters that require compiled output should run
after `build`).

## The full gate (Bhaskar)

**Validate before running.** JARVIS/the assistant owns the one-time nightly install/update. Bhaskar
first runs the local validation-only command below; it performs no download, install, or update:

    pwsh -NoLogo -NoProfile -NonInteractive -File .\.github\scripts\Test-SessionBootstrap.ps1

If validation reports absent, stale, wrong-session, modified, or otherwise invalid nightly
state, stop immediately and hand back to JARVIS. Never invoke `Initialize-RustNightly.ps1`, run
rustup install/update, self-heal, or use stale fallback. Validation requires matching assistant
owner, `ready` phase, and token-hash provenance; a role string or direct initializer call cannot
substitute for JARVIS's in-memory startup handshake.

Run these commands in order, resolving each name from the Commands table:

1. `format:check` — check formatting only; this variant does **not** write
2. `build` — Release restore/rebuild, which prepares assets for the no-restore analyzer pass
3. `lint` — a second Release rebuild with analyzers enabled, no restore, and warnings promoted to errors
4. `test:full` — unit + integration + acceptance
5. `dry-check` — duplication/DRY checker _(if set)_
6. `mutation-test` — mutation tester _(if set)_
7. `crap-check` — CRAP metric, complexity × coverage _(if set)_

…plus any additional commands the consumer added to the Commands table.

**Run rules.** The skip-`none` rule applies **only to the optional rows** (`dry-check`, `mutation-test`,
`crap-check`): when their Value is `none` they don't run. A **required** command (`format:check`,
`lint`, `build`, `test:full`) whose Value is `none` or empty is a **misconfiguration**: **stop and
report it** — never silently skip.

**Every optional gate that is set runs on EVERY change.** The stronger and more varied these
constraints, the more tightly the agents' work can be supervised — so a consumer who fills in more
optional rows gets a stricter gate.

**Deferred quality tools.** `dry-check`, `mutation-test`, and `crap-check` are all `none` in feature
001, so Bhaskar skips them under the optional-command rule. Feature 002 P0 owns redesign and
re-enablement.

**Trait-driven test classification.** Test categories follow
[`docs/meta-design.md#writing-tests`](../../docs/meta-design.md#writing-tests). `test:full` discovers
all 204 assembly cases and fails unless `type=UnitTests` selects 96, `type=IntegrationTests` selects
108, and the single `scope=External` case is a subset of integration. Missing, dual-classified,
added, or otherwise drifting cases fail actionably. By default, full uses `scope!=External` to run
203 assembly cases (96 unit + 107 integration), then the standalone
`src/TestProjects/run-integrationtests.ps1` acceptance harness, which validates 18 customer-visible
VSTest results. `-Full -IncludeExternal` runs all 204 assembly cases before the same acceptance
harness. The external network/freshness overlay is never part of the default gate.

**Rust nightly.** JARVIS's preflight Gate 3 installs/updates and records the current session's nightly
toolchain. `test:full` validates that exact manifest and exports process-only
`RUSTUP_TOOLCHAIN=nightly` before starting VSTest, so all Cargo/rustc/test-adapter children and the
standalone harness use nightly. Missing/mismatched state is fatal; never fall back to stable.

**Zero tolerance, with one temporary exception.** Any warning or error fails the gate except
`MSB3277`, which feature 001 alone grandfathers in the lint command. Feature 002 must resolve the
assembly conflicts and remove that exception; no other warning code may be added to it. Bhaskar does
not pass a change until the gate passes under that exact policy.
