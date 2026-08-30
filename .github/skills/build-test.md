---
name: build-test
description: Runs the fast build/test gate — Release build (which is the analyzer/style enforcement) and unit tests (excludes integration tests). Dave's fast done-done gate.
---

This repository's gate **recipe** — no placeholders. It runs the commands in the **Commands** table
in `.github/copilot-instructions.md`; that table is the single source of truth for shell commands.

This recipe is **authoritative for gate membership and order**; the Commands table's `Gate` column is a
hint. Reorder for your stack if needed (e.g. type-aware linters that require compiled output should run
after `build`).

## The fast gate (Dave)

**Bootstrap boundary.** JARVIS/the assistant owns Rust-nightly install/update at session
startup. Dave never invokes `Initialize-RustNightly.ps1` or rustup install/update. If any
nightly check fails, stop and hand back to JARVIS; do not download, update, repair,
or fall back to stale state. This is a working agreement rather than something the scripts enforce —
the consumer path only validates that the pinned nightly is installed and matches this checkout's
manifest, so honouring the boundary is on you.

Run these commands in order, resolving each name from the Commands table:

1. `build` — Release restore/build; Release turns on the analyzers and treats their diagnostics as errors
2. `test:quick` — unit tests only

…then any additional commands the consumer added to the Commands table.

**One implementation per step.** `build` is `.github/scripts/Invoke-Build.ps1`, the same script `cdp.yml`
invokes (Ruling S) — a step that must behave identically in CI and locally has exactly one implementation,
never one in YAML and another here. It performs one Release solution MSBuild invocation with `/m`,
does not clean or manipulate outputs, and writes each project directly to its canonical
`_built\projects\<project>` closure. There is no analyzer switch or second `/t:Rebuild` pass because
the Release build *is* the analyzer enforcement.

**Run rules.** Every core row above is **required** — a required command whose Value is `none` or empty
is a **misconfiguration**: **stop and report it** (never silently skip). The skip-`none` rule applies
only to the **optional** rows (`dry-check`, `mutation-test`, `crap-check`), which run in the full gate —
the fast gate has none.

**Unit-only, trait-driven.** Test categories follow
[`docs/meta-design.md#writing-tests`](../../docs/meta-design.md#writing-tests). `Invoke-Tests.ps1` takes
one `-Mode` of `unit`, `integration`, `acceptance`, or `full`; every mode first runs the TestAdapter
packager regression once, and `test:quick` is `-Mode unit`. It then reads
the three exact isolated test-project outputs beneath `_built\projects`, takes the xUnit console
runner from its owning `RustAnalyzer.UnitTests` closure, and runs the `type=UnitTests` cases in one
process with assembly parallelism. The taxonomy is enforced by `TraitTaxonomyTests`, a unit test that
receives that exact canonical assembly set from the gate:
every discovered case must carry exactly one of `type=UnitTests`,
`type=IntegrationTests`, or `type=AcceptanceTests`, and it names any offender by assembly and case. It
also asserts that what it governs is what the runner runs — every discovered assembly matches the
runner's `KS.*Tests.dll` glob, and no excluded assembly does. The gate itself fails closed on zero
executed tests, and on a run that did not execute `TraitTaxonomyTests`. No count is hardcoded anywhere.
Cargo/process integration cases (including
the network-dependent rust-analyzer freshness case), the standalone acceptance harness, and optional
gates (DRY, mutation, CRAP) belong to Bhaskar's full gate
(`.github/skills/build-test-full.md`).

DRY, mutation, and CRAP are disabled (`none`) and tracked in `docs/backlog.md`.

**Zero tolerance.** The Release build **is** the analyzer/style gate: `src/KS.Common.targets` enables
the analyzers, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`, and
`CodeAnalysisTreatWarningsAsErrors` for Release, and `src/_codeanalysis/codeanalysis.ruleset` sets
`IncludeAll Action="Error"` — so any compiler, analyzer, or StyleCop diagnostic fails `build`. There is
no separate lint pass and no MSBuild-level warning promotion, so MSBuild warnings such as the `MSB3277`
assembly conflicts are **not** fatal; that is the accepted consequence recorded as D2/R8 in
`docs/features/002-hardening-and-vs2026.md`, and warning promotion must not be reintroduced anywhere to
compensate. Dave is not done-done until the gate passes with no error or new warning code/signature;
the established MSB3277 signature baseline remains T8 work.
