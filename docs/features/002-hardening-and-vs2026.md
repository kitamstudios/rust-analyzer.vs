# Feature: Hardening and Visual Studio 2026
**Branch:** vibe/002-hardening-and-vs2026
**Status:** In Progress

## Requirements

Six candidates, delivered in this order: (2) gate portfolio and runtime review, (3) VS 2026 and
prerequisite readiness, (4) VSSDK and library modernization, (5) VS 2022/2026 compatibility matrix,
(6) GitHub release notes in the extension. Candidate (1), the extension-architecture analysis, runs
**in parallel** from the moment candidate 2 merges; it ships no behaviour. Candidate 4 does **not**
wait on candidate 1 — cohort upgrades are reversible and independently gated, and blocking them on an
analysis that produces only a recommendation would stall modernization for no verification gain.

### Candidate 2, as stated by Sir (verbatim)

> "okay i want to revert back to previous cdp.yml. the steps there are all the steps for the quick
> gate and long gate. there is no extra steps needed. delete all the ps1 garbage created for the
> gates e.g. checking format etc (the build steps for release build checks formatting). additionally
> quick gate (dave's) runs only unit tests, bhaskar's gate runs both unit and integration tests. to
> reflect this also add another step in cdp.yml. once done raise the PR and track the PR merge gate.
> done-done for this is PR gates also pass."

### Ground truth this feature starts from

- **CI has never been green.** Run `32805906008` failed at `Lint` with
  `MSB3061 … _built\EmptyFiles\image\empty.jpc`; Quick, Full, Zip TestAdapter and both uploads were
  skipped. Run `32806128787` was cancelled. No merge gate has ever passed on this workflow.
- **The separate lint pass has no marginal analyzer coverage.** `src/KS.Common.targets:11-30` sets
  `StrictCodeAnalysisEnabled` on for Release and drives `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, and `CodeAnalysisTreatWarningsAsErrors` from it, with
  `_codeanalysis/codeanalysis.ruleset` at `<IncludeAll Action="Error"/>`. The Release build already
  fails on every StyleCop/IDE/FxCop diagnostic, including SA1028 trailing whitespace. The lint pass's
  only genuine delta was MSBuild-level `/warnAsError` — immediately re-holed by
  `/warnNotAsError:MSB3277`. Sir's "the build steps for release build checks formatting" is correct.
- **The lint failure is an artefact of the gate layer, not of the product.** `Invoke-Build.ps1`
  deliberately omits `/p:OutDir` in `-AnalyzerCheck` mode, but `cdp.yml` sets `OutDir` as a **job-level**
  env, so the analyzer `Rebuild` targeted `_built\` and its clean step tried to delete an ApprovalTests
  `EmptyFiles` payload. Nothing in the product was wrong.
- **The real content of candidate 2 is de-scripting, not reverting.** The old `cdp.yml` had
  `continue-on-error` on both test steps and no acceptance gate worth the name; restoring it verbatim
  would restore unenforced tests. What is restored is the **shape** (multi-job, inline steps, no gate
  wrappers); what is kept is fail-closed policy.
- **The acceptance harness has never gated anything in CI.** In the old workflow it ran under
  `continue-on-error: true`; in the current workflow it runs inside `Invoke-Tests.ps1 -Full`, which
  never ran because Lint failed first.

### Standing constraints

Gate-3 assistant bootstrap is retained and working and is not touched by this feature. `OutDir` stays
`${{ github.workspace }}\_built\` and is set **per step, never as job env**. The nightly toolchain is
pinned to a dated channel in exactly one source, read by both the bootstrap and the workflow. Runner
label and VS major are `config`-job knobs, not literals scattered through the file. `docs/design.md`
is corrected in the same slice that changes behaviour. Everything is fail-closed: no
`continue-on-error`, no soft failure, no quarantine. Agents never deploy (golden rule #4).

## Accepted product & UX decisions (candidate 3)

Preserved verbatim from the planning archive. These are decided requirements, not open questions.

1. Support Visual Studio 2022 17.12+ and Visual Studio 2026. Express open-ended compatibility intent
   where packaging permits and validate the actual host version/capabilities at runtime.
2. The resolver checks the current process PATH, persisted user/machine PATH, `CARGO_HOME`,
   `RUSTUP_HOME`, and `%USERPROFILE%\.cargo\bin`. It may repair the current process PATH only. It
   never writes user/machine environment or registry readiness state.
3. If resolution is still blocked, show one dialog with exactly these actions:
   **Restart Visual Studio**, **Open prerequisites**, and **Continue without Rust**.
4. Never restart automatically. Restart remains offered even when it cannot help; the dialog must
   say honestly when no changed persisted environment was found and restart is unlikely to help.
5. A browser opens only after the user explicitly selects **Open prerequisites**.
6. **Continue without Rust** sets a process-only `Suspended` state. Never persistently disable or
   unload the VSIX.
7. While suspended, hide Rust commands and gate LSP startup, downloads/updater work, Cargo, tests,
   debugging, and workspace integration.
8. After suspension, show exactly one non-modal InfoBar per Visual Studio session. Do not repeat a
   modal prompt. The InfoBar explains that Rust features are suspended and exposes the appropriate
   explicit recovery/prerequisite actions.
9. A fresh `devenv` process resets state to `Unknown` and rechecks. No readiness/suspension state is
   stored in registry, user environment, or other cross-process storage.
10. Use `AsyncLazy` or equivalent compute-once semantics so one process performs one effective
    evaluation and at most one dialog. Concurrent consumers await the same result.
11. Unexpected errors fail open: log/report the fault and leave Rust features available. Enter
    `Suspended` only for classified prerequisite failures.
12. Cancellation before prompting returns to retryable `Unknown`; it must not cache readiness,
    suspension, or a consumed-dialog state.

## Design Options (Ox)

### O1 — Faithful de-script, one job
- Description: Delete the five gate scripts, inline every step into the existing single
  `build-test-deploy` job, add the separate quick and full test steps, fix `OutDir` scoping.
- Pros: Smallest diff; one checkout; no artifact plumbing; fastest to green.
- Cons: The acceptance harness keeps pointing `/TestAdapterPath` at `_built\`, which holds the whole
  build output — a file missing from the shipped zip stays undetectable. Build and acceptance failures
  are not separable in the run UI. Does not match "revert back to previous cdp.yml", which was
  multi-job.

### O2 — De-script **and** restore the multi-job shape *(recommended, chosen by Sir)*
- Description: Delete the five gate scripts; restore `config` → `build-and-test` → `acceptance` →
  `publish`; inline every step; replace deprecated actions with shell; make the acceptance job consume
  the **published TestAdapter zip** rather than `_built\`; fail closed everywhere.
- Pros: Matches the previous topology Sir asked for. The crux: the acceptance job is the only test of
  the **shipped artefact** — today's inline harness resolves adapters out of `_built\`, so an assembly
  omitted from `KS.RustAnalyzer.TestAdapter.zip` is invisible, yet the zip is exactly what customers
  consume. Consuming the zip converts a packaging omission from a customer report into a red gate. Job
  separation also isolates a VS/VSTest-host failure from a build failure.
- Cons: Two checkouts, artifact upload/download, slightly longer wall clock; two rustup installs.

### O3 — Minimal patch
- Description: Move `OutDir` to the build step, leave the scripts in place.
- Pros: Could be green today.
- Cons: Leaves exactly the "ps1 garbage" Sir asked to delete, keeps a lint pass with no marginal
  coverage, and never tests the shipped zip. Rejected.

**Recommended: O2 — it is the only option that both removes the gate layer and puts the shipped
artefact under test. Chosen by Sir.**

## Slices (Sx)

| Slice | Outcome | Depends on |
|-------|---------|------------|
| S1 | Candidate 2: gate scripts deleted, portfolio measured, CI restored to the O2 multi-job topology with acceptance consuming the published zip, fail-closed, and **green for the first time**. | - |
| S2 | Candidate 3a: VS 2022 17.12+ **and** VS 2026 packaging plus runtime proof. | S1 |
| S3 | Candidate 3b: one process-scoped readiness evaluation, one dialog, one session InfoBar, process-only suspension. | S2 |
| S4 | Candidate 4: dependency modernization in reviewed compatibility cohorts. | S1 |
| S5 | Candidate 5: VS 2022/2026 compatibility matrix executed and recorded. | S2, S3, S4 |
| S6 | Candidate 6: GitHub release notes in the extension. | S3 |
| S7 | Candidate 1: record the decision to stay on the in-process VSSDK model, with evidence and revisit triggers. Ships no behaviour; runs in parallel with S2–S6 once S1 merges. | S1 |

## Tasks (Tx)

### S1 — Candidate 2 (execution granularity)

| #  | Slice | Task | Status | Commit |
|----|-------|------|--------|--------|
| T1 | S1 | **Pin nightly to a dated channel in exactly one source** read by both `Initialize-RustNightly.ps1` and `cdp.yml`. Default to `.github/rust-nightly-channel` (a one-line file, e.g. `nightly-2026-08-20`) unless three probes clear `rust-toolchain.toml`: **(a)** `ToolchainServiceExtensionsTests.TestGetActiveToolChainAsync` and `TestGetBinAndLibPathsAsync` resolve the active toolchain from `TestHelpers.ThisTestRoot`, a repo-relative working directory, so a repo-root `rust-toolchain.toml` becomes a **directory override** — the same suite already approves rustup text containing `active because: directory override for 'D:\src'`, so the override reason is observable and can break approvals; **(b)** the `rustup override set` product path still behaves with a toolchain file present; **(c)** a `rust-toolchain.toml` makes rustup **auto-install** the named channel on the first cargo invocation, which would let a consumer implicitly acquire a toolchain and breach the assistant-only bootstrap boundary. Any probe failing ⇒ use the channel file. Record the probe results in this file. | Pending | - |
| T2 | S1 | **Test execution mechanism and classification invariants** (Ruling A, first half). (a) Move the three assembly suites off `vstest.console.exe` to the native **xUnit console runner** (`xunit.runner.console`, `tools\net472\xunit.console.exe`), copied into `_built\` by the test projects' targets; the projects are legacy non-SDK `ToolsVersion="15.0"` / `TargetFrameworkVersion v4.8` with PackageReference, so `dotnet test` cannot drive them (`docs/design.md`: "not currently a safe `dotnet test`/Coverlet target") and the .NET Framework console runner is the runner that can. (b) Preserve gate semantics with `-trait "type=UnitTests"` (quick) and `-notrait "scope=External"` (full), `-parallel all`. (c) **Delete the PowerShell discovery preflight and its hardcoded `204/96/108/1`**; replace it with a reflection-based `TraitTaxonomyTests` xUnit test in the unit suite asserting the *invariants*: every case carries exactly one `type` trait, no case carries both, every `scope=External` case is also `type=IntegrationTests`, unit + integration = total, total > 0. Numbers stop being inputs. (d) Verify before switching: app-domain/loading behaviour for `KS.RustAnalyzer.UnitTests` (it references `src/external/vs.17.11` VS assemblies), the ApprovalTests path (`RaVsDiffReporter.INSTANCE` is `XUnit2Reporter.INSTANCE` — runner-agnostic, it fails through xUnit), and that `xunit.runner.visualstudio` is retained for in-IDE Test Explorer. (e) Accept the one loss: the console runner emits xUnit XML, not TRX; the TRX consumer was `dorny/test-reporter`, already deleted under D3, so TRX becomes an artifact-only concern. | Pending | - |
| T3 | S1 | **Re-point the Commands table and both recipes at de-scripted commands** (Ruling C). `build`, `lint`, `format:check`, `format:fix` all resolve to the single Release build command — that build *is* the C# style and analyzer enforcement. `test:quick` and `test:full` become one-line xUnit console invocations; `test:full` stays a **single process** (see N3). No `none` values, no `.ps1` wrappers, no framework divergence: `.github/skills/build-test.md` and `build-test-full.md` need **no edit**. See N2 for the exact values and the double-invocation consequence. | Pending | - |
| T4 | S1 | **Delete the five scripts and repair the cascade.** Delete `Invoke-Build.ps1`, `Invoke-Format.ps1`, `Invoke-Tests.ps1`, `Initialize-CISession.ps1`, `CIProvenance.psm1`. `RustNightly.psm1:4` imports `CIProvenance.psm1` and `Get-RustNightlyManifest` falls back to `Get-CIBootstrapProvenance`; remove both, along with the `GITHUB_ACTIONS` branch of `Get-RustNightlyHandoffMessage`. CI no longer needs provenance because it never runs the assistant bootstrap — the workflow sets `RUSTUP_TOOLCHAIN` directly. Retain `Initialize-AssistantSession.ps1`, `Initialize-RustNightly.ps1`, `AssistantBootstrap.psm1`, `RustNightly.psm1`, `SessionState.psm1`, `Test-SessionBootstrap.ps1`, and `VisualStudio.psm1`. | Pending | - |
| T5 | S1 | **Verify runner-label ↔ VS-major against `actions/runner-images`** before wiring the `config` knob. Current lead, **unverified and load-bearing**: `windows-2022` carries VS 2022 (major 17) and preinstalled rustup; `windows-2025` may now carry VS 2026 (major 18) after a mid-2026 image migration. Confirm against the image manifests and record the exact label→major mapping here. Do **not** move the default gate off VS 17 on the strength of a search result. | Pending | - |
| T6 | S1 | **Rewrite `cdp.yml` to the O2 topology**: `config` → `build-and-test` → `acceptance` → `publish`. `config` (literal runner) checks out, reads the T1 channel file, and outputs `runner`, `vs-major`, `nightly-channel`; downstream jobs use `runs-on: ${{ needs.config.outputs.runner }}` — verified legal, since `runs-on` accepts `needs.*.outputs` but **not** `env`. `build-and-test`: shell rustup install of the pinned channel, MSBuild resolved through `VisualStudio.psm1`, inline VSIX version stamp, Release build with **step-level** `OutDir`, then **two separate steps** per Sir — "Quick tests (unit)" and "Full tests (unit + integration)" — then Zip TestAdapter and upload VSIX + zip + xUnit XML. `acceptance`: downloads and expands `KS.RustAnalyzer.TestAdapter.zip` to `.\testadapter` and runs `src/TestProjects/run-integrationtests.ps1 -TestAdapterLocation .\testadapter -VisualStudioMajorVersion ${{ needs.config.outputs.vs-major }}`, i.e. **against the shipped artefact**. `publish` needs `[config, build-and-test, acceptance]`. No `continue-on-error` anywhere. All deprecated actions replaced by shell per Ruling B and N5. | Pending | - |
| T7 | S1 | **Correct `docs/design.md`**: the "Build, test, and release flow" section (script-implemented gates, the six numbered steps, the 204/96/108/203 counts, the separate lint pass, the CI-provenance paragraph, the `.github/workflows/cdp.yml` single-job description) and the `MSB3277` constraint entry, which now records that the grandfather is gone because the pass that carried it is gone. Also correct the acceptance sentence to say the harness consumes the published zip in CI. | Pending | - |
| T8 | S1 | **Drive CI to its first green run** on the pushed branch. Green means: build, quick, full, acceptance, zip, and both uploads all succeed with no soft-failure switch anywhere. | Pending | - |
| T9 | S1 | **Measure the gate portfolio on the pushed branch** and fill the table below — per-gate wall clock, trigger, what it uniquely catches, and the retained risk of every removed gate. Includes the measured cost of the repeated Release build invocation (N2). | Pending | - |
| T10 | S1 | **Raise the PR and track the merge gate to pass.** Done-done for candidate 2 is the PR gate green, not a local green. | Pending | - |

### S2 — Candidate 3a: VS 2022 17.12+ and VS 2026 packaging + runtime proof

| #  | Slice | Task | Status | Commit |
|----|-------|------|--------|--------|
| T11 | S2 | Research the supported manifest expression for **both hosts — VS 2022 17.12+ and VS 2026 — which is a hard requirement (Ruling E)**. Current `source.extension.vsixmanifest` declares three amd64 `InstallationTarget`s at `Version="[17.0, 18.0)"` and a `Microsoft.VisualStudio.Component.CoreEditor` prerequisite at `[17.0,)`. **Specifically verify the standing finding that VS 2026 exposes 17.x APIs and *ignores the upper bound* of existing `InstallationTarget` ranges** — if true, the current range already admits VS 2026 and T13 becomes a no-op or a narrower edit. Record the evidence either way; do not carry the finding forward unverified. | Pending | - |
| T12 | S2 | Separate **packaging claim** from **runtime support**: keep the runtime minimum at 17.12 in `Constants`, add explicit host-capability validation, and make an unsupported host produce a truthful classified result rather than a crash or a silent no-op. | Pending | - |
| T13 | S2 | Change the manifest **only if T11 proves a change is needed**, and **only after** T14 proves install + activation on a real VS 2026 host. If T11 confirms VS 2026 ignores the upper bound, the correct action is to leave the range alone and record why. Whatever the outcome, **VS 2022 17.12+ support must not regress** — both hosts ship (Ruling E). | Pending | - |
| T14 | S2 | **[HUMAN]** VS 2026 install and activation smoke on a real host: install the VSIX, open a Rust folder, confirm package activation and no activity-log/MEF errors. Sir has answered "unsure" on VS 2026 host availability; this is the escalation point (Ruling D). | Pending | - |

### S3 — Candidate 3b: readiness, one dialog, one InfoBar, process-only suspension

| #  | Slice | Task | Status | Commit |
|----|-------|------|--------|--------|
| T15 | S3 | Introduce the process-scoped readiness state (`Unknown` → evaluating → `Ready` \| `Suspended`) owned by one service; no registry or user-environment persistence (decisions 9, 12). | Pending | - |
| T16 | S3 | Implement a pure resolver result distinguishing found tools, repairable process PATH, classified missing prerequisites, persisted-PATH change that may benefit from restart, and unexpected faults (decisions 2, 11). | Pending | - |
| T17 | S3 | Probe process PATH, persisted user/machine PATH, `CARGO_HOME`, `RUSTUP_HOME`, `%USERPROFILE%\.cargo\bin`; validate executables; add only validated directories to the **process** PATH (decision 2). | Pending | - |
| T18 | S3 | `AsyncLazy` compute-once evaluation and dialog coordination; reset to `Unknown` only on cancellation before prompting; fail open on unexpected exceptions (decisions 10, 11, 12). | Pending | - |
| T19 | S3 | **[HUMAN]** Dialog and InfoBar copy, including the honest "restart is unlikely to help" wording and the InfoBar's recovery actions (decisions 3, 4, 8). Product copy is Sir's call. | Pending | - |
| T20 | S3 | Implement the three-action dialog, explicit-only browser launch, never-automatic restart (decisions 3, 4, 5). | Pending | - |
| T21 | S3 | Implement process-only `Suspended` and exactly one non-modal InfoBar per session; fresh `devenv` starts at `Unknown` (decisions 6, 8, 9). | Pending | - |
| T22 | S3 | Route command visibility and every LSP/updater/Cargo/test/debug/workspace entry point through the readiness result; no persistent VSIX disable or unload (decisions 6, 7). | Pending | - |
| T23 | S3 | Unit tests for resolver classification, state transitions, races, and cancellation; integration tests for one-evaluation/one-dialog/one-InfoBar and feature gating under suspension. | Pending | - |

### S4 — Candidate 4: VSSDK and library modernization

| #  | Slice | Task | Status | Commit |
|----|-------|------|--------|--------|
| T24 | S4 | Inventory every direct dependency with current and candidate versions: VSSDK, Community.VisualStudio.Toolkit, VS Threading/analyzers, Microsoft.NET.Test.Sdk 17.11.0, xunit 2.9.0 / xunit.runner.visualstudio 2.8.2 / xunit.analyzers 1.15.0 / **xunit.runner.console** (new in T2), FluentAssertions 6.12.0, Moq 4.20.70, ApprovalTests 5.8.0, StyleCop.Analyzers 1.2.0-beta.556, AutoMapper 10.1.1, Newtonsoft.Json 13.0.3, Microsoft.ApplicationInsights 2.22.0, Ensure.That, DalSoft.RestClient, System.Linq.Async, SourceLink. | Pending | - |
| T25 | S4 | **[HUMAN]** Approve the cohort plan: which packages move together, which major-version jumps are in scope, and what happens to preview pins (StyleCop beta) and licence-changed packages (FluentAssertions 8). | Pending | - |
| T26 | S4 | Execute cohort 1 (test/analyzer stack) — each cohort is its own commit, gated by the full CI portfolio. | Pending | - |
| T27 | S4 | Execute cohort 2 (VSSDK/Toolkit/Threading), the cohort most likely to interact with S2. | Pending | - |
| T28 | S4 | Execute cohort 3 (runtime libraries: AutoMapper, Newtonsoft, ApplicationInsights, REST client). | Pending | - |
| T29 | S4 | Replace checked-in `src/external/vs.17.11` host assemblies with supported package references where one exists; document provenance and hashes for whatever must remain a binary. | Pending | - |
| T30 | S4 | Record before/after versions and release-note risks per cohort in this file. | Pending | - |

### S5 — Candidate 5: VS 2022/2026 compatibility matrix

| #  | Slice | Task | Status | Commit |
|----|-------|------|--------|--------|
| T31 | S5 | Define the matrix: hosts × scenarios (install, activation, Open Folder/MEF, LSP, Cargo, test discovery/execution, run/debug, suspend/recovery, updater/offline, shutdown). | Pending | - |
| T32 | S5 | Build the repeatable clean-experimental-instance procedure with captured VS/extension/rustup/cargo/rust-analyzer versions and logs. | Pending | - |
| T33 | S5 | **[HUMAN]** Execute the matrix on VS 2022 17.12+. | Pending | - |
| T34 | S5 | **[HUMAN]** Execute the matrix on VS 2026. | Pending | - |
| T35 | S5 | Reconcile observed support with the manifest claim and `docs/design.md`; document any capability-specific degradation. | Pending | - |

### S6 — Candidate 6: GitHub release notes in the extension

| #  | Slice | Task | Status | Commit |
|----|-------|------|--------|--------|
| T36 | S6 | **[HUMAN]** Product design: what is shown, when, where, and what the user can do with it. | Pending | - |
| T37 | S6 | Define the release-data contract and the trusted source; treat all fetched content as untrusted input. | Pending | - |
| T38 | S6 | Sanitize/render safely — no arbitrary HTML or script, no navigation without explicit user action. | Pending | - |
| T39 | S6 | Cache with explicit offline behaviour; never block activation on the network. | Pending | - |
| T40 | S6 | Accessibility and theming for the surface chosen in T36. | Pending | - |
| T41 | S6 | Privacy: no identity or path data leaves the machine; respect the suspension gate from S3. | Pending | - |
| T42 | S6 | Tests: contract, sanitization, cache/offline, failure paths, and one-notification-per-session behaviour. | Pending | - |

### S7 — Candidate 1: extension-architecture decision (parallel, ships no behaviour)

**Decision taken by Sir: the extension stays on the in-process VSSDK model. No migration to
`Microsoft.VisualStudio.Extensibility`.** The analysis that produced this decision is complete, so the
slice no longer seeks a recommendation — it records one and preserves the conditions for revisiting it.

Evidence behind the decision:

- Four capabilities this extension depends on have **no verified out-of-process replacement**: Open
  Folder/workspace integration (`IWorkspaceProviderFactory`, `IFileScanner`, file contexts), debugging
  (the new model offers *visualizers*, not launch providers), the `ITestContainerDiscoverer` → Test
  Explorer bridge, and MEF (exports cannot move out-of-process).
- Migration is **not** a route to closing the editor gaps in
  [#22](https://github.com/kitamstudios/rust-analyzer.vs/issues/22),
  [#28](https://github.com/kitamstudios/rust-analyzer.vs/issues/28),
  [#35](https://github.com/kitamstudios/rust-analyzer.vs/issues/35), and
  [#46](https://github.com/kitamstudios/rust-analyzer.vs/issues/46)–[#49](https://github.com/kitamstudios/rust-analyzer.vs/issues/49).
  Microsoft issue [#426](https://github.com/microsoft/VSExtensibility/issues/426) confirms
  `workspace/configuration` was missing from the new model and was closed *because of* that gap. Those
  issues need protocol-level probes against the existing LSP broker, not an architecture change.
- Cost of the path not taken: ~3–6 engineer-weeks for prototypes alone; 8–14 engineer-months for full
  parity, at low confidence.

| #  | Slice | Task | Status | Commit |
|----|-------|------|--------|--------|
| T43 | S7 | Record the decision in `docs/design.md` as a dated architecture decision: stay on in-process VSSDK, with the evidence summary above and the revisit triggers from T44. | Pending | - |
| T44 | S7 | Capture the revisit trigger list alongside T43 — the conditions that would reopen this, chiefly **Microsoft announcing deprecation or reduced support for the 17.x VSSDK APIs this extension uses**, plus out-of-process parity arriving for the four blocking capabilities. | Pending | - |
| T45 | S7 | Update `docs/backlog.md`: retire the analysis candidate as decided, and correct the two stale premises — that the analysis "must inform dependency modernization" (resolved: it does not gate S4) and that it addresses the editor gaps (it does not; reframe those toward LSP-broker probes). | Done | - |
| T46 | S7 | ~~Cost the migration.~~ **Dropped** — decision taken; costing served a choice that is now made. | Dropped | - |
| T47 | S7 | ~~**[HUMAN]** Reconcile the analysis with the S4 cohort outcomes and decide the next program.~~ **Dropped** — the decision is independent of the S4 outcome, and S4 was never gated on it. | Dropped | - |

### Completed in feature 001 (evidence)

| # | Task | Status |
|---|------|--------|
| 001-T14 | Make build, quick, full, standalone acceptance, format, and analyzer failures block locally and in CI; remove test `continue-on-error`. | Complete (Feature 001) |
| 001-T15 | Enforce trait-based quick/full/external ownership; make classification drift fail closed. | Complete (Feature 001) |
| 001-T32 | Consume Cargo `compiler-artifact` records for test-executable/container discovery instead of display text. | Complete (Feature 001) |
| 001-T48 | Resolve the panic-ID, Duration, hash/path, and standalone approved-output failures without auto-approval or semantic suppression. | Complete (Feature 001) |
| 001-T49 | Implement explicit xUnit type traits and the external overlay; remove `.github/test-classification.json` and FQN filters. | Complete (Feature 001) |
| 001-T50 | Replace brittle nightly sysroot-layout assumptions with rustc-reported paths and semantic runtime/import-library assertions. | Complete (Feature 001) |

## Gate portfolio (measured)

Filled by T9 from the first green run. Every removed gate carries an explicit retained risk.

| Gate | Trigger | Runtime | What it uniquely catches | Retained risk |
|------|---------|---------|--------------------------|---------------|
| build (Release) | fast + full + CI | _T9_ | Compile errors; **all** StyleCop/IDE/FxCop diagnostics and SA1028 trailing whitespace, via `<IncludeAll Action="Error"/>` + `TreatWarningsAsErrors` in Release | - |
| test:quick (unit) | fast + CI | _T9_ | In-process regressions; trait-taxonomy invariants via `TraitTaxonomyTests` | - |
| test:full (unit + integration) | full + CI | _T9_ | Cargo/rustup/process-boundary regressions on the pinned nightly | - |
| acceptance (VSTest, published zip) | full + CI | _T9_ | Customer-visible adapter behaviour **and** packaging omissions in the shipped zip | - |
| external (`scope=External`) | manual/scheduled | _T9_ | Network/freshness drift | Excluded from the deterministic gate by design |
| *removed:* separate lint pass | — | — | — | MSBuild-level warnings are no longer promoted to errors. Concretely, `MSB3277` assembly conflicts stay **non-fatal** (D2) — the `/warnNotAsError:MSB3277` grandfather disappears with the pass that carried it. Compiler/analyzer/style coverage is unchanged because Release already enforces it. |
| *removed:* non-C# formatter | — | — | — | Trailing whitespace in `.ps1`/`.yml`/`.json`/`.md` is no longer normalized by a gate. `.editorconfig` (`trim_trailing_whitespace = true`) remains the IDE-level contract; C# is still enforced by SA1028 at build. |
| *removed:* PowerShell classification preflight | — | — | — | Four `vstest.console /ListTests` discovery passes per gate are gone; the invariants now run as a unit test inside both gates (T2c), so drift still fails closed but numbers are no longer hardcoded. |

## Risks (Rx)

- R1: The xUnit console runner loads the three net48 assemblies differently from the VSTest host (app domains, working directory, `Microsoft.NET.Test.Sdk` entry-point assumptions); a suite could pass under one and fail under the other. T2(d) probes this before the switch.
- R2: Loss of TRX from the assembly suites; xUnit XML is not a drop-in for any tool expecting TRX. Only consumer was `dorny/test-reporter`, already deleted (D3).
- R3: A repo-root `rust-toolchain.toml` becomes a rustup **directory override** that changes the "active because" reason the approval corpus asserts, and can implicitly auto-install a channel — breaching the assistant-only bootstrap boundary. T1's probes exist for exactly this.
- R4: The `windows-2025` ⇒ VS 2026 mapping is unverified (T5). Building the `config` knob on it without confirmation would silently move the default gate to an unproven host.
- R5: Consuming the published zip in the acceptance job will likely go red first — that is the gate working, but it means candidate 2's "first green" may require fixing the zip's file list before T10.
- R6: Two rustup installs (build-and-test, acceptance) can resolve to different channels if T1's single source is bypassed in one job.
- R7: Hand-rolled version stamping must preserve both the `source.extension.vsixmanifest` `Identity/@Version` and the `Vsix.Version` constant in the generated `source.extension.cs`, plus the `version-number` job output `publish` consumes; a mismatch produces an unpublishable or wrongly-tagged release.
- R8: `MSB3277` conflicts stay non-fatal after the lint pass is deleted; a real assembly-binding failure could reach a customer without a gate failing (D2).
- R9: `build`/`lint`/`format:*` resolving to one command means a single regression in that command removes four nominal gates at once.
- R10: VS 2026 packaging changes may not be expressible in the current manifest schema; S2 could stall on T14's human step.
- R11: The readiness redesign touches many entry points; a missed path could start Rust work while suspended.
- R12: Compute-once initialization can cache a cancellation/fault or deadlock the UI thread if state and prompting are not separated.
- R13: Dependency cohorts can compile and still fail at runtime inside VS; only S5's matrix proves otherwise.
- R14: Release-notes rendering is an untrusted-content surface (injection, navigation, privacy leakage).
- R15: The publish path cannot be validated by any PR (it runs only on `[release]` push to trunk), so a regression there surfaces only when Sir ships — see A6.

## Assumptions (Ax)

- A1: Windows amd64 remains the supported host architecture.
- A2: VS 2022 minimum stays 17.12; VS 2026 is major version 18.
- A3: rustup is preinstalled on the target runner image; if T5 finds otherwise, the shell step acquires it before installing the pinned channel.
- A4: Process-only state and process-only environment mutation are acceptable; persistent readiness, suspension, PATH repair, VSIX disable, or unload are not.
- A5: Feature 001's trait ownership is the gate contract; T2 changes the *runner*, not the taxonomy.
- A6: **"Deprecated actions" excludes the publish path.** `timheuer/openvsixpublish@v1`, `cezarypiatek/VsixPublisherAction@0.1`, and `softprops/action-gh-release@v0.1.15` are old pins, not archived actions. They run only on `[release]` push to trunk, so **no PR can validate a change to them** — a break would surface only when Sir ships, and agents never deploy (golden rule #4). They are left exactly as-is. Sir can overturn this; if he does, it becomes a supervised release-time change, not a PR-gated one.
- A7: Current public extension behaviour stays compatible except where the accepted prerequisite UX deliberately replaces the restart/disable behaviour.

## Deferrals (Dx)

- D1: Archive T46 — DRY, mutation, and CRAP redesign/re-enablement → `docs/backlog.md`. Not a prerequisite for the narrowed scope; the rows stay `none`.
- D2: Archive T47 — `MSB3277` resolution → `docs/backlog.md`. Note that deleting the lint pass also deletes the `/warnNotAsError:MSB3277` grandfather, so the conflicts simply **stay non-fatal** rather than being suppressed by an exception. Nothing is hidden; the debt is recorded here and in the portfolio table.
- D3: No `dorny/test-reporter` (or equivalent) is restored. Test outcomes are read from job status and uploaded result artifacts.
- D4: `OutDir` is **not** reverted to the old `\_built\` value; it stays `${{ github.workspace }}\_built\`, set per step.
- D5: No extension-model migration — in this feature or as a planned program. Sir has decided the
  extension stays on the in-process VSSDK model; S7 records that decision and its revisit triggers.
  Reopening requires one of the T44 triggers to fire.
- D6: SHA-pinning of actions, least-privilege `permissions:`, and release provenance → `docs/backlog.md` (CI supply chain). Candidate 2 restores a *working* gate; supply-chain hardening is its own program.
- D7: Broader cross-version ApprovalTests fixture strategy → `docs/backlog.md`.
- D8: Telemetry, updater, process ownership, async failure visibility, tool protocols, and UI performance → `docs/backlog.md` (hardening sequence).
- D9: ARM64, non-Windows hosts, project templates, and new editor features are out of scope.

## Notes & Decisions

### Sir's rulings applied

| Ruling | Decision | Applied in |
|--------|----------|-----------|
| A | "switch to xUnit entirely, away from VSTest, unless there's a good reason. update the cdp.yml appropriately." | T2, T3, T6, portfolio; **split verdict** — see N3, N4 |
| B | "for the deprecated gh actions, use your alternatives as required" / "yeah lets switch to shell completely for any deprecated actions." | T6, N5 |
| C | "these are logical steps… they dont have to [be] separate, do not necessarily need a ps1 wrapper… as long as they happen." | T3, N2 — **supersedes the earlier N2** |
| D | VS 2026 host availability: **"unsure."** | T14, T33, T34 remain `[HUMAN]`; S2 ships no widened manifest on assumption |
| E | "3 i need to support both 2022 and 2026" (2026-08-25) | Supporting **both** VS 2022 17.12+ and VS 2026 is a hard requirement, not a preference. T11 verifies the upper-bound finding; T13 must not regress 2022 |
| F | "first lets get the gates and the ci green" (2026-08-25) | **S1 has absolute priority.** No S2–S7 work starts until S1's PR gate is green. The VS 2026 host question is deferred, not dropped |
| G | Third-party actions → shell; first-party `actions/*` → version-bump (2026-08-25) | Refines Ruling B; see N5 |

### N1 — O2's crux: the acceptance job consumes the published zip

The inline harness points `/TestAdapterPath` at `_built\`, which holds the entire build output, so any
assembly missing from `KS.RustAnalyzer.TestAdapter.zip` is undetectable — yet the zip is what customers
consume. In the O2 topology the acceptance job downloads the uploaded zip, expands it to a clean
directory, and points the harness there. This is the single strongest reason to prefer O2 over O1.

### N2 — Commands table under Ruling C (supersedes the earlier N2)

The earlier N2 proposed `none` values and a framework-divergence note for `format:*`/`lint`. **Overruled.**
Every row stays Required with a real value, and neither skill file changes.

| Command | Value (T3) |
|---------|------------|
| `build` | `pwsh -NoLogo -NoProfile -NonInteractive -Command "Import-Module .\.github\scripts\VisualStudio.psm1 -Force; & (Get-VisualStudioTool -Name MSBuild) src\RustAnalyzer.sln /m /nologo /nr:false /restore /t:Build /p:Configuration=Release /p:DeployExtension=false /p:OutDir=$PWD\_built\ /verbosity:minimal"` |
| `lint` | *same command as `build`* |
| `format:check` | *same command as `build`* |
| `format:fix` | *same command as `build`* |
| `test:quick` | `pwsh -NoLogo -NoProfile -NonInteractive -Command "& .\_built\xunit.console.exe .\_built\KS.RustAnalyzer.UnitTests.dll .\_built\KS.RustAnalyzer.TestAdapter.UnitTests.dll .\_built\KS.RustAnalyzer.Remote.UnitTests.dll -trait type=UnitTests -parallel all -xml .\_built\quick.xml"` |
| `test:full` | one `pwsh -Command` that imports `RustNightly.psm1`, calls `Enable-SessionRustNightly`, runs the same three assemblies with `-notrait scope=External`, then runs `src\TestProjects\run-integrationtests.ps1` — **in one process** (N3) |

Rationale: the Release build *is* the lint and the C# format check —
`src/KS.Common.targets:11-30` turns on `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, and
`CodeAnalysisTreatWarningsAsErrors` for Release, and `codeanalysis.ruleset` is `<IncludeAll Action="Error"/>`,
so SA1028 trailing whitespace and every style rule already fail the build.

Two honest consequences, both accepted:
1. **`format:fix` does not write.** No headless auto-fixer exists for these legacy non-SDK projects
   (`dotnet format` cannot load them). The Release build reports every violation as an error and the
   agent fixes them. The row is a real command that makes the logical step happen; it is not a writer.
2. **Repeat invocation.** Dave's recipe runs `format:fix` → `build` → `lint`, i.e. the same command
   three times; Bhaskar's runs it three times too. The 2nd and 3rd are incremental no-ops (`/t:Build`,
   not `Rebuild` — the old lint pass's `Rebuild` is exactly what produced the `MSB3061` failure).
   **Decision: acceptable, no note in the skills, no second Rebuild.** T9 measures the actual cost; if
   it turns out non-trivial, that is a datum to bring back to Sir, not a reason to diverge now.

### N3 — `test:full` stays one process (constraint re-derived, Ruling A)

The earlier N3 justified this from the VSTest invocation shape. That derivation was wrong, and the
conclusion survives anyway on the correct grounds: `Enable-SessionRustNightly` sets
`$env:RUSTUP_TOOLCHAIN = "nightly"` **in the calling process** (`RustNightly.psm1`), and child processes
inherit it. The constraint is process-environment inheritance, not VSTest — so it is unaffected by the
runner change. A de-scripted `test:full` must therefore validate the manifest and run both the assembly
suites and the acceptance harness in **one** `pwsh` process. In CI the constraint does not arise: the
workflow sets `RUSTUP_TOOLCHAIN` to the T1-pinned channel as step-level env, which is why deleting
`Initialize-CISession.ps1`/`CIProvenance.psm1` costs CI nothing (T4).

### N4 — The acceptance harness stays VSTest (Ruling A's escape clause, **approved by Sir 2026-08-25**)

Ruling A has two halves with different answers.

- **The xUnit suites move.** 96 unit + 108 integration cases currently run under `vstest.console.exe`
  purely as a host. `xunit.runner.console` runs them natively, preserves trait filtering
  (`-trait` / `-notrait`), parallelism, and the ApprovalTests path, and — a real gain for S5 — decouples
  test execution from whichever Visual Studio the runner image happens to carry. Comply.
- **The standalone acceptance harness cannot move, and this is the good reason the ruling asks for.**
  The product under test **is a VSTest adapter**. `docs/design.md`: *"VSTest loads `TestDiscoverer` and
  `TestExecutor` from the packaged adapter"* and *"the standalone VSTest adapter harness is its
  acceptance gate."* Driving Rust tests through `vstest.console.exe /TestAdapterPath` is the literal
  customer scenario; `docs/meta-design.md#writing-tests` explicitly says to *"retain the stack's
  acceptance gate rather than adding an xUnit wrapper only for a trait."* Replacing it with xUnit would
  delete the only test of the shipped artefact — and under O2's own rationale (N1) that is precisely the
  gate that must stay. Rewriting it in xUnit would mean an xUnit test that shells out to
  `vstest.console.exe`: the same VSTest dependency, one indirection deeper, and a worse failure diff.

  **So: `vstest.console.exe` remains in exactly one place — the acceptance job.** `VisualStudio.psm1` is
  retained for it (`Get-VisualStudioTool -Name VSTest`, plus `Invoke-VSTestProcess` and its `windir`
  environment-casing workaround, which is VSTest-specific and load-bearing). This is stated explicitly so
  Sir sees the boundary and can overturn it deliberately.

### N5 — Deprecated actions → shell (Ruling B, refined)

**Sir's rule, stated 2026-08-25:** *all third-party actions move to shell; first-party (`actions/*`)
actions are version-bumped, not replaced.* This is a supply-chain rule, not a style rule — the goal is
to remove third-party code from the critical path, and re-implementing maintained first-party actions
in shell adds risk without removing any third-party dependency.

| Action | Replacement | Note |
|--------|-------------|------|
| `actions-rs/toolchain@v1` (archived) | shell `rustup toolchain install <T1 channel>` (+ `rustup component add rustfmt clippy`) | T5 confirms rustup is preinstalled; if not, the step acquires rustup first (A3). |
| `darenm/Setup-VSTest@v1.2` | none needed | Runners carry VS; `VisualStudio.psm1` resolves `vstest.console.exe` via `vswhere`. After N4 the acceptance job is its only consumer. |
| `timheuer/bootstrap-dotnet@v1` | inline `vswhere` MSBuild resolution through `VisualStudio.psm1` | Same resolution the local gate uses — one mechanism, not two. |
| `timheuer/vsix-version-stamp@v1` | inline shell | Must write **both** the manifest `Identity/@Version` and the `Vsix.Version` constant in the generated `src/RustAnalyzer/source.extension.cs` (a listed generated artifact, written only by CI — never hand-edited), **and** emit the `version-number` job output that `publish` consumes (R7). Base version today is `3.0` in both files. |
| `rusty-bender/vstest-action@main` | not restored | **Retained-risk record:** the old workflow pinned a third-party action to a **mutable branch**. That is exactly the class of dependency the restored file must not reintroduce. |
| `dorny/test-reporter@v1` | not restored | D3. |
| `actions/checkout@v2` | **version-bump to `@v4`** | **Approved by Sir 2026-08-25** under the first-party rule above. `v2` is an outdated *version*, not a deprecated action. Hand-rolling `git clone` would mean re-implementing credential handling, `fetch-depth`, ref resolution, and submodules — more risk, no supply-chain gain. |
| `actions/upload-artifact@v4`, `actions/download-artifact@v4` | unchanged | Current, first-party. |
| publish-path actions | unchanged | A6. |

**Shape constraint:** every replacement is **inline in the workflow** or reuses the retained
`VisualStudio.psm1`. Creating new `.ps1` wrappers under `.github/scripts` would undo candidate 2.

### N6 — Numbers stop being inputs

`Invoke-Tests.ps1` hardcoded `204/96/108/1` and ran four `/ListTests` discovery passes to defend them.
That gate is replaced by a `TraitTaxonomyTests` xUnit test asserting *invariants* by reflection
(T2c). Drift still fails closed and now fails with a stack trace instead of a PowerShell throw; adding a
test no longer requires editing a script, a skill, and a design doc. Adding a new **test assembly** must
still register it with that test — R14 from the archive persists in its new home.

### Execution rules

1. S1 is mandatory first and its done-done is the **PR merge gate green**, not a local green (Sir).
2. S7 (candidate 1) starts the moment S1 merges and runs in parallel with S2–S6. It ships no behaviour.
3. S4 (candidate 4) does not wait on S7.
4. No slice may reintroduce a gate wrapper script under `.github/scripts`, a soft-failure switch, or a
   job-level `OutDir`.
5. Every slice updates this file with actual decisions, evidence, and commit references.
6. Anything marked `[HUMAN]` stops and returns to Sir; no agent decides it by inference.
