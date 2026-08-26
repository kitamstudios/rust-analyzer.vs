---
name: build-test-full
description: Runs the full build/test gate — Release build (which is the analyzer/style enforcement), the complete test suite (unit + integration + acceptance), plus every optional quality gate that is set. Bhaskar's full done-done gate.
---

This repository's gate **recipe** — no placeholders. It runs the commands named in the **Project
profile → Commands** table (`.github/copilot-instructions.md`); that table is the single source of
truth for the actual shell commands, so this file never hardcodes them.

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

1. `build` — Release restore/build; Release turns on the analyzers and treats their diagnostics as errors
2. `test:full` — unit + integration + acceptance
3. `dry-check` — duplication/DRY checker _(if set)_
4. `mutation-test` — mutation tester _(if set)_
5. `crap-check` — CRAP metric, complexity × coverage _(if set)_

…plus any additional commands the consumer added to the Commands table.

**Run rules.** The skip-`none` rule applies **only to the optional rows** (`dry-check`, `mutation-test`,
`crap-check`): when their Value is `none` they don't run. A **required** command (`build`, `test:full`)
whose Value is `none` or empty is a **misconfiguration**: **stop and report it** — never silently skip.

**Every optional gate that is set runs on EVERY change.** The stronger and more varied these
constraints, the more tightly the agents' work can be supervised — so a consumer who fills in more
optional rows gets a stricter gate.

**Deferred quality tools.** `dry-check`, `mutation-test`, and `crap-check` are all `none` in feature
001, so Bhaskar skips them under the optional-command rule. Feature 002 P0 owns redesign and
re-enablement.

**Trait-driven test classification.** Test categories follow
[`docs/meta-design.md#writing-tests`](../../docs/meta-design.md#writing-tests). `Invoke-Tests.ps1` takes
one `-Mode` of `unit`, `integration`, `acceptance`, or `full`; `test:full` is `-Mode full`, which runs
all three legs in **one** process (`RUSTUP_TOOLCHAIN` is exported into it and inherited by every child).
Full globs the built `KS.*Tests.dll` assemblies and runs them **unfiltered** — so a case that carries no
type trait still runs — then the standalone `src/TestProjects/run-integrationtests.ps1` acceptance
harness, which validates 18 customer-visible VSTest results. The acceptance leg runs even when the
assembly leg has already failed, and both failures are reported. The taxonomy itself is enforced by
`TraitTaxonomyTests` inside the run: every discovered case must carry exactly one of `type=UnitTests`,
`type=IntegrationTests`, or `type=AcceptanceTests`, with any offender named by assembly and case. No
count is hardcoded anywhere, and zero discovered cases fails.

**Network dependency.** Full runs every integration case, including the rust-analyzer release-freshness
case, which reaches GitHub. A genuine network outage therefore **fails** this gate rather than skipping
it; there is no opt-out mode. Re-run when connectivity is restored.

**Rust nightly.** JARVIS's preflight Gate 3 installs/updates and records the current session's nightly
toolchain — the dated channel pinned in `.github/rust-nightly-channel`. `test:full` validates that
exact manifest and exports process-only
`RUSTUP_TOOLCHAIN` set to that pinned channel before starting VSTest, so all Cargo/rustc/test-adapter children and the
standalone harness use nightly. Missing/mismatched state is fatal; never fall back to stable.

**Zero tolerance.** The Release build **is** the analyzer/style gate: `src/KS.Common.targets` enables
the analyzers, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`, and
`CodeAnalysisTreatWarningsAsErrors` for Release, and `src/_codeanalysis/codeanalysis.ruleset` sets
`IncludeAll Action="Error"` — so any compiler, analyzer, or StyleCop diagnostic fails `build`. There is
no separate lint pass and no MSBuild-level warning promotion, so MSBuild warnings such as the `MSB3277`
assembly conflicts are **not** fatal; that is the accepted consequence recorded as D2/R8 in
`docs/features/002-hardening-and-vs2026.md`, and warning promotion must not be reintroduced anywhere to
compensate. Bhaskar does not pass a change until the gate passes with no warning or error.
