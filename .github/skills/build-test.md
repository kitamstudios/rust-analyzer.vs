---
name: build-test
description: Runs the fast build/test gate — Release build (which is the analyzer/style enforcement) and unit tests (excludes integration tests). Dave's fast done-done gate.
---

This repository's gate **recipe** — no placeholders. It runs the commands named in the **Project
profile → Commands** table (`.github/copilot-instructions.md`); that table is the single source of
truth for the actual shell commands, so this file never hardcodes them.

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

1. `build` — Release restore/build; Release turns on the analyzers and treats their diagnostics as errors
2. `test:quick` — unit tests only

…then any additional commands the consumer added to the Commands table.

**Run rules.** Every core row above is **required** — a required command whose Value is `none` or empty
is a **misconfiguration**: **stop and report it** (never silently skip). The skip-`none` rule applies
only to the **optional** rows (`dry-check`, `mutation-test`, `crap-check`), which run in the full gate —
the fast gate has none.

**Unit-only, trait-driven.** Test categories follow
[`docs/meta-design.md#writing-tests`](../../docs/meta-design.md#writing-tests). `Invoke-Tests.ps1` takes
one `-Mode` of `unit`, `integration`, `acceptance`, or `full`; `test:quick` is `-Mode unit`. It globs the
built `KS.*Tests.dll` assemblies — no registration step for a new one — and runs the `type=UnitTests`
cases under the xUnit console runner. The taxonomy is enforced by `TraitTaxonomyTests`, a unit test that
runs inside this gate: every discovered case must carry exactly one of `type=UnitTests`,
`type=IntegrationTests`, or `type=AcceptanceTests`, and it names any offender by assembly and case. No
count is hardcoded anywhere, and zero discovered cases fails. Cargo/process integration cases (including
the network-dependent rust-analyzer freshness case), the standalone acceptance harness, and optional
gates (DRY, mutation, CRAP) belong to Bhaskar's full gate
(`.github/skills/build-test-full.md`).

DRY, mutation, and CRAP are disabled in feature 001 (`none`) and deferred to feature 002 P0.

**Zero tolerance.** The Release build **is** the analyzer/style gate: `src/KS.Common.targets` enables
the analyzers, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`, and
`CodeAnalysisTreatWarningsAsErrors` for Release, and `src/_codeanalysis/codeanalysis.ruleset` sets
`IncludeAll Action="Error"` — so any compiler, analyzer, or StyleCop diagnostic fails `build`. There is
no separate lint pass and no MSBuild-level warning promotion, so MSBuild warnings such as the `MSB3277`
assembly conflicts are **not** fatal; that is the accepted consequence recorded as D2/R8 in
`docs/features/002-hardening-and-vs2026.md`, and warning promotion must not be reintroduced anywhere to
compensate. Dave is not done-done until the gate passes with no warning or error.
