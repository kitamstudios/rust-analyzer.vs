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

- ~~**CI has never been green.**~~ **Corrected 2026-08-25 — this was true when written and is now
  false, and the correction matters more than the original claim.** Run `32805906008` failed at `Lint`
  with `MSB3061 … _built\EmptyFiles\image\empty.jpc`; run `32806128787` was cancelled. But run
  `32916996580` (commit `29eb9c1`, PR event) **passed**. That green is **partly false** and must not be
  treated as S1's done-done: its own log reads `Classification: unit=96, integration=108 (external
  subset=1), assembly total=204` followed by `Total tests: 203`. The `−1` is `RlsReleaseTests` — the
  rust-analyzer freshness alarm. The gate counted the alarm and then subtracted it by name via
  `-notrait scope=External`, so the one test that would have reported a **441-day-stale** packaged
  rust-analyzer was the single test excluded. T8's "first green run" is therefore restated: green must
  mean green **with the alarm armed**. Ruling M arms it.
- **The separate lint pass has no marginal analyzer coverage.** `src/KS.Common.targets:11-30` sets
  `StrictCodeAnalysisEnabled` on for Release and drives `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, and `CodeAnalysisTreatWarningsAsErrors` from it, with
  `_codeanalysis/codeanalysis.ruleset` at `<IncludeAll Action="Error"/>`. The Release build already
  fails on every StyleCop/IDE/FxCop diagnostic, including SA1028 trailing whitespace. The lint pass's
  only genuine delta was MSBuild-level `/warnAsError` — immediately re-holed by
  `/warnNotAsError:MSB3277`. Sir's "the build steps for release build checks formatting" is correct
  **for compiled `.cs`, and only for compiled `.cs`** — see the next bullet, which is the part that was
  missing.
- **The format gate was never a C# formatter, and the build never covered what it actually did.**
  `Invoke-Format.ps1` did exactly two things — normalize line endings to the per-file majority, and
  strip trailing whitespace — across 18 extensions and 4 config filenames (`.ps1`, `.psm1`, `.yml`,
  `.sln`, `.props`, `.targets`, `.json`, `.toml`, `.resx`, `.vsct`, `.gitignore`, …). Roslyn/StyleCop
  see **only compiled `.cs`**, so they never covered `.github/scripts/*.ps1` or `cdp.yml`. What *did*
  cover the rest: `.gitattributes` is `* text=auto`, so git normalizes line endings at commit
  regardless. Residual coverage lost by Ruling O is therefore **trailing whitespace in non-`.cs`
  files** — cosmetic, and Sir has ruled it is not worth the loop time. Recorded so the removal is not
  later mistaken for an oversight.
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
| T1 | S1 | **Pin nightly to a dated channel in exactly one source** read by both `Initialize-RustNightly.ps1` and `cdp.yml`. Default to `.github/rust-nightly-channel` (a one-line file, e.g. `nightly-2026-08-20`) unless three probes clear `rust-toolchain.toml`: **(a)** `ToolchainServiceExtensionsTests.TestGetActiveToolChainAsync` and `TestGetBinAndLibPathsAsync` resolve the active toolchain from `TestHelpers.ThisTestRoot`, a repo-relative working directory, so a repo-root `rust-toolchain.toml` becomes a **directory override** — the same suite already approves rustup text containing `active because: directory override for 'D:\src'`, so the override reason is observable and can break approvals; **(b)** the `rustup override set` product path still behaves with a toolchain file present; **(c)** a `rust-toolchain.toml` makes rustup **auto-install** the named channel on the first cargo invocation, which would let a consumer implicitly acquire a toolchain and breach the assistant-only bootstrap boundary. Any probe failing ⇒ use the channel file. Record the probe results in this file. **Outcome: probes (a) and (c) failed; the channel file was adopted. See N7 for the evidence, the resolved channel `nightly-2026-08-25`, and the latent `-Force` module-import bug the pin exposed and fixed.** | Done | - |
| T2 | S1 | **Test execution mechanism and classification invariants** (Ruling A, first half). (a) Move the three assembly suites off `vstest.console.exe` to the native **xUnit console runner** (`xunit.runner.console`, `tools\net472\xunit.console.exe`), copied into `_built\` by the test projects' targets; the projects are legacy non-SDK `ToolsVersion="15.0"` / `TargetFrameworkVersion v4.8` with PackageReference, so `dotnet test` cannot drive them (`docs/design.md`: "not currently a safe `dotnet test`/Coverlet target") and the .NET Framework console runner is the runner that can. (b) Preserve gate semantics with `-trait "type=UnitTests"` (quick) and `-notrait "scope=External"` (full), `-parallel all`. (c) **Delete the PowerShell discovery preflight and its hardcoded `204/96/108/1`**; replace it with a reflection-based `TraitTaxonomyTests` xUnit test in the unit suite asserting the *invariants*: every case carries exactly one `type` trait, no case carries both, every `scope=External` case is also `type=IntegrationTests`, unit + integration = total, total > 0. Numbers stop being inputs. (d) Verify before switching: app-domain/loading behaviour for `KS.RustAnalyzer.UnitTests` (it references `src/external/vs.17.11` VS assemblies), the ApprovalTests path (`RaVsDiffReporter.INSTANCE` is `XUnit2Reporter.INSTANCE` — runner-agnostic, it fails through xUnit), and that `xunit.runner.visualstudio` is retained for in-IDE Test Explorer. (e) Accept the one loss: the console runner emits xUnit XML, not TRX; the TRX consumer was `dorny/test-reporter`, already deleted under D3, so TRX becomes an artifact-only concern. | Done | *(pending, rides with t2c)* |
| T3 | S1 | **Re-point the Commands table and both recipes at de-scripted commands** (Ruling C). `build` resolves to the single Release build command — that build *is* the C# style and analyzer enforcement. **`lint`, `format:check` and `format:fix` are not re-pointed; they are deleted (Rulings N and O), along with `Invoke-Format.ps1`.** `test:quick` and `test:full` become one-line xUnit console invocations; `test:full` stays a **single process** (see N3). No `none` values and no `.ps1` wrappers. **Both `.github/skills/build-test.md` and `build-test-full.md` DO need edits** — the earlier "no edit" claim assumed the gates survived; under Ruling N-scope those recipes are this repo's artifacts, not framework files. See N2 for the exact values and the double-invocation consequence. **Ruling K applies here:** whatever `test:full` becomes, its acceptance leg must run against an expanded copy of `KS.RustAnalyzer.TestAdapter.zip` — never `_built\` — with no fallback. See N9. | Pending | - |
| T2b | S1 | **Enforce the `-Force` module-import invariant** (Anders S-b). T1 fixed a latent trap: `Import-Module -Force` is remove-then-import, so a nested `.psm1` → `.psm1` forced import *unloads the caller's copy* and re-imports into the nested module's private scope, where exports are not re-exported. It silently stripped `Assert-AssistantBootstrapAuthorization` from the authorization path and surfaced as a confusing "term is not recognized" far from its cause. The invariant — **`-Force` only at entry-point `.ps1` scripts, never inside a `.psm1`** — is currently prose in N7 with nothing enforcing it. Add a `type=UnitTests` test that **globs** `.github/scripts/*.psm1` (globbing, not enumeration, so it survives T4 deleting `CIProvenance.psm1`) and asserts no `Import-Module … -Force`, with a failure message naming the remove-then-import cause. Same "convention → executable invariant" move as T2(c). **Sequence after T2** so it is written once, in the xUnit-console world. | Pending | - |
| T4 | S1 | **Delete the five scripts and repair the cascade.** Delete `Invoke-Build.ps1`, `Invoke-Format.ps1`, `Invoke-Tests.ps1`, `Initialize-CISession.ps1`, `CIProvenance.psm1`. `RustNightly.psm1:4` imports `CIProvenance.psm1` and `Get-RustNightlyManifest` falls back to `Get-CIBootstrapProvenance`; remove both, along with the `GITHUB_ACTIONS` branch of `Get-RustNightlyHandoffMessage`. CI no longer needs provenance because it never runs the assistant bootstrap — the workflow sets `RUSTUP_TOOLCHAIN` directly. Retain `Initialize-AssistantSession.ps1`, `Initialize-RustNightly.ps1`, `AssistantBootstrap.psm1`, `RustNightly.psm1`, `SessionState.psm1`, `Test-SessionBootstrap.ps1`, and `VisualStudio.psm1`. **S-f (Anders), same function, one edit:** `Get-RustNightlyManifest` currently folds a *channel mismatch* into the message "The Rust nightly manifest does not belong to the current session." When the pin moves mid-session the manifest is perfectly valid for that session — the pin changed. The remedy (re-bootstrap) is right but the diagnosis is wrong, and a wrong diagnosis in the bootstrap path is exactly what cost time on the `-Force` bug. Give the mismatch its own message naming the recorded channel and the current pin. Fail-closed behaviour stays byte-identical; only the diagnosis improves. | Pending | - |
| T5 | S1 | **Verify runner-label ↔ VS-major against `actions/runner-images`** before wiring the `config` knob. Current lead, **unverified and load-bearing**: `windows-2022` carries VS 2022 (major 17) and preinstalled rustup; `windows-2025` may now carry VS 2026 (major 18) after a mid-2026 image migration. Confirm against the image manifests and record the exact label→major mapping here. Do **not** move the default gate off VS 17 on the strength of a search result. | Pending | - |
| T6 | S1 | **Rewrite `cdp.yml` to the O2 topology**: `config` → `build-and-test` → `acceptance` → `publish`. `config` (literal runner) checks out, **invokes the T1 channel reader** (`.github/scripts/Get-PinnedRustNightlyChannel.ps1` — see S-a below), and outputs `runner`, `vs-major`, `nightly-channel`; downstream jobs use `runs-on: ${{ needs.config.outputs.runner }}` — verified legal, since `runs-on` accepts `needs.*.outputs` but **not** `env`. `build-and-test`: shell rustup install of the pinned channel, MSBuild resolved through `VisualStudio.psm1`, inline VSIX version stamp, Release build with **step-level** `OutDir`, then **three separate jobs** per Ruling M — **unit**, **integration**, and **acceptance** — then Zip TestAdapter and upload VSIX + zip + xUnit XML. `acceptance`: downloads and expands `KS.RustAnalyzer.TestAdapter.zip` to `.\testadapter` and runs `src/TestProjects/run-integrationtests.ps1 -TestAdapterLocation .\testadapter -VisualStudioMajorVersion ${{ needs.config.outputs.vs-major }}`, i.e. **against the shipped artefact**. `publish` needs `[config, build-and-test, acceptance]` **and gates on the ref, not just the event — `github.ref == 'refs/heads/master'` (Ruling J); delete the ignored `branches:` key under `workflow_dispatch` (N8)**. No `continue-on-error` anywhere. All deprecated actions replaced by shell per Ruling B and N5. **S-a (Anders):** extract the read+validate of `.github/rust-nightly-channel` into a standalone `.github/scripts/Get-PinnedRustNightlyChannel.ps1` that `RustNightly.psm1` dot-sources and that `config` invokes directly. T1 left **two independent interpreters** of one file — `RustNightly.psm1` and `cdp.yml` each carry their own copy of the `^nightly-\d{4}-\d{2}-\d{2}$` regex and their own error text, and neither validates the other. That is R6's drift mode structurally. One reader, one regex, one message; CI no longer imports the bootstrap module stack merely to read a file. | Pending | - |
| T6b | S1 | **Give the TestAdapter zip file list one home** (Sir, 2026-08-25). The list lives inline in `cdp.yml:99` as a six-element array — `KS.RustAnalyzer.TestAdapter.dll`/`.pdb`, `Microsoft.ApplicationInsights.dll`/`.pdb`, `System.Collections.Immutable.dll`, `Ensure.That.dll`. Under Ruling K it becomes load-bearing on **both** sides, so it cannot stay in the workflow. Sir's first suggestion was `Invoke-Tests.ps1`; **that file is deleted by T4**, so it is not a viable home. Put the list in a data file — `src/RustAnalyzer.TestAdapter/testadapter-package.txt`, one relative file name per line, comments allowed — beside the project whose output it describes, with exactly one reader script that both the local gate and the CI `acceptance`/zip steps call. **Do not derive it from project references:** the adapter references `System.ComponentModel.Composition`, `Microsoft.TestPlatform.ObjectModel` and `System.Security.Principal.Windows`, none of which ship, because the VSTest host supplies them. The list is a curated statement of *what the host does not provide* and must stay curated; a naive derivation would bloat the zip and hide the judgement. Keep `Compress-Archive`'s fail-on-missing-file behaviour — a name in the list that is not built must stay a hard error. **Sequence after T6** so there is only one workflow rewrite. | Pending | - |
| T7 | S1 | **Correct `docs/design.md`**: the "Build, test, and release flow" section (script-implemented gates, the six numbered steps, the 204/96/108/203 counts, the separate lint pass, the CI-provenance paragraph, the `.github/workflows/cdp.yml` single-job description) and the `MSB3277` constraint entry, which now records that the grandfather is gone because the pass that carried it is gone. Also correct the acceptance sentence to say the harness consumes the published zip in CI. **S-e (Anders):** scope extends beyond the "Build, test, and release flow" section. `design.md:160` ("It installs or updates rustup's `nightly` toolchain") sits in the **assistant-bootstrap paragraph**, outside the original enumeration, and is now false after T1's pin. Golden rule #1 makes `design.md` the SSOT, so a stale floating-`nightly` claim there is a live inconsistency with no other owner. Sweep the whole file for floating-channel and CI-provenance claims, not just the enumerated section. | Pending | - |
| T8 | S1 | **Drive CI to its first green run** on the pushed branch. Green means: build, quick, full, acceptance, zip, and both uploads all succeed with no soft-failure switch anywhere. | Pending | - |
| T9 | S1 | **Measure the gate portfolio on the pushed branch** and fill the table below — per-gate wall clock, trigger, what it uniquely catches, and the retained risk of every removed gate. Includes the measured cost of the repeated Release build invocation (N2). | Pending | - |
| T10 | S1 | **Raise the PR and track the merge gate to pass.** Done-done for candidate 2 is the PR gate green, not a local green. **If master carries branch protection, T10 also updates the required-status-check names** — O2 renames the reported checks from `Build, Test & Deploy`/`Publish` to `config`/`build-and-test`/`acceptance`/`publish`, and a required check that is never reported blocks the merge indefinitely (see N10). Evidence must include a `workflow_dispatch` from a non-`master` ref showing `publish` skipped (N8). | Pending | - |

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
- R9: ~~`build`/`lint`/`format:*` resolving to one command means a single regression in that command removes four nominal gates at once.~~ **Moot (Rulings N/O):** the rows were deleted, not merged, so there is no shared-command amplification. Replaced by **B-R2** in N11 — the live version of this risk is a gate that exits 0 having run zero tests.
- R10: VS 2026 packaging changes may not be expressible in the current manifest schema; S2 could stall on T14's human step.
- R11: The readiness redesign touches many entry points; a missed path could start Rust work while suspended.
- R12: Compute-once initialization can cache a cancellation/fault or deadlock the UI thread if state and prompting are not separated.
- R13: Dependency cohorts can compile and still fail at runtime inside VS; only S5's matrix proves otherwise.
- R14: Release-notes rendering is an untrusted-content surface (injection, navigation, privacy leakage).
- R15: The publish path cannot be validated by any PR (it runs only on `[release]` push to trunk), so a regression there surfaces only when Sir ships — see A6.
- R16 (Anders S-d): **`TestGetActiveToolChainAsync` and `TestGetBinAndLibPathsAsync` are not hermetic.** They resolve from `TestHelpers.ThisTestRoot`, a repo-relative directory, and therefore answer from *ambient rustup configuration*. Under the gate they are pinned, because `Enable-SessionRustNightly` sets `RUSTUP_TOOLCHAIN` in the calling process and that outranks every directory override and toolchain file in rustup's resolution order — so the channel-file design leaves them correct **by construction, not by luck**. But run from Test Explorer in the IDE they resolve to the developer's machine default, and a `rust-toolchain.toml` in any *parent* directory of a contributor's checkout, or a stray `rustup override set`, flips them without touching this repository. They pass today only because their assertions are shape-based (`EndWith("-x86_64-pc-windows-msvc")`, `std-*.dll`/`.lib` existence) rather than identity-based. **Anyone strengthening those assertions must make the resolution hermetic first.** This is the underlying reason T1's probe (a) mattered.

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
- D10 (Anders S-c): **The pin's staleness has no owner.** A dated nightly with no renewal mechanism fails in the classic way — untouched until something forces it, then six months of nightly churn lands in one commit at the worst moment with no bisect surface. Availability is *not* the risk (rust-lang archives dated nightlies indefinitely); big-bang drift is. Pinning remains the right trade; this is its cost. Deferred out of S1 under Ruling F — S1's done-done is a green PR gate, and a renewal program is not that. ~~Suggested shape, recorded in `docs/backlog.md`: a `scope=External` test asserting the pin is younger than N days. That trait is already defined as manual/scheduled and excluded from the deterministic gate, and its stated purpose is network/freshness drift — this is that category exactly, reusing machinery that already exists.~~ **Corrected 2026-08-25:** `scope=External` no longer exists. Ruling M retired it precisely *because* "excluded from the deterministic gate" is how `RlsReleaseTests` stayed silent for 441 days — the trait was a hiding place, not a category. The renewal check must therefore be a plain `type=IntegrationTests` case that runs in the normal gate. Reusing the old machinery is exactly the mistake to avoid.

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
| H | ~~CI test-step shape: **keep Quick (unit, 96) + Full (unit + integration, 204) as designed**~~ (2026-08-25) | **Superseded by Ruling M (same day).** Two steps become **three jobs** — unit, integration, acceptance — so the duplication this ruling accepted disappears rather than being measured. Its counts are also stale twice over: the taxonomy tests added 4 cases, and Ruling M re-armed the excluded external case, so 96→100 and 204→208 |
| I | Stranded bootstrap provenance cleared by deletion, not session restart (2026-08-25) | One-off, authorized by Sir. The file was stuck in phase `authorized` because the pre-fix module bug prevented the `failed` marking. Not a precedent: the no-self-healing rule stands |
| J | **`publish` runs against `master` only** (2026-08-25) | The job condition must assert the ref, not just the event. See N8 |
| K | **Acceptance runs against the zip contents alone — locally *and* in CI** (2026-08-25) | Extends N1 from a CI-only property to an invariant of the harness. The local `test:full` gate must not resolve adapters out of `_built\` either. See N9 |
| L | **"`RegisteredTestAssemblies` should be exclusion list."** (2026-08-25) | Overturns R14/N6's registration model. Discovery is the default: `TraitTaxonomyTests` globs the built assemblies and enforces the taxonomy on all of them, with an **exclusion** list carrying a per-entry reason. Adding a test assembly requires **zero** registration. Zero discovery must fail closed, and an offender is named by assembly **and** case. Proven by Dave with a throwaway probe assembly and independently re-proven by Bhaskar with three. See N6 |
| M | **Four test modes, three CI jobs, three-way taxonomy** (2026-08-25) | `Invoke-Tests.ps1` takes `unit`, `integration`, `acceptance`, `full`. Dave runs `unit`; Bhaskar runs `full`; CI gets one job each for u/i/a. The taxonomy test must assert every case carries **exactly one** of the three type traits. Sir: *"do this now. so bhaskar's next run should fail."* — the failure is the point. **Supersedes Ruling H's two-step shape and its 96/204 counts** |
| N | **"for the last time we do not need a second gate for linting. make this change uniformly."** (2026-08-25) | Third statement of this ruling. The separate `lint` gate is **deleted**, not re-pointed — this overrides N2's "same command as `build`" row. The Release build already is the lint. Consequence accepted: the MSB3277 grandfather dies with the pass that carried it, so conflicts stay non-fatal (D2/R8) |
| N-scope | **"agentify framework should be dead once this project has been agentified. only the artifact it installs should be active."** (2026-08-25) | `agentify.md` and the agentify repo are **off limits** — never edited, never re-run. Corollary 1: the "regeneration vector" concern I raised is void; there is no re-stamp, so no accepted debt. Corollary 2: `build-test.md`/`build-test-full.md` are **this repository's artifacts** despite `copilot-instructions.md` calling them "framework-owned", and are edited freely |
| O | **Remove the format concept entirely** (2026-08-25) | `format:check`, `format:fix`, **and** `.github/scripts/Invoke-Format.ps1` are deleted. Sir: *"we dont need normalization of line endings and trailing strips. remove that step. it just adds to the loop time."* I flagged that his stated premise ("part of the release build") holds only for compiled `.cs`; he confirmed removal anyway on loop-time grounds. See the corrected ground-truth bullet |

### N1 — O2's crux: the acceptance job consumes the published zip

The inline harness points `/TestAdapterPath` at `_built\`, which holds the entire build output, so any
assembly missing from `KS.RustAnalyzer.TestAdapter.zip` is undetectable — yet the zip is what customers
consume. In the O2 topology the acceptance job downloads the uploaded zip, expands it to a clean
directory, and points the harness there. This is the single strongest reason to prefer O2 over O1.

**Superseded in scope by Ruling K** — the zip-only property is no longer CI-only; it now binds the
local gate as well. N9 is authoritative.

### N8 — `publish` must assert the branch, not the event (Ruling J)

Today `publish` is guarded only by `if: github.event_name == 'push' || github.event_name ==
'workflow_dispatch'` (`cdp.yml:124`). Push is confined to `master` by the trigger
(`on.push.branches: [master]`), so the push path is already master-only **by accident of the trigger,
not by the job's own condition**.

`workflow_dispatch` is the live hole. **`on.workflow_dispatch.branches` is not a supported key —
GitHub ignores it**, and the workflow can be dispatched from any ref. A dispatch from a `vibe/*`
branch therefore reaches `publish`, and the Marketplace step fires on `workflow_dispatch` alone
without even requiring `[release]` (`cdp.yml:161`). That is a path to publishing a feature branch to
the Marketplace.

Under O2, `publish` gains `if: ${{ github.ref == 'refs/heads/master' }}` combined with the existing
event condition, and the ignored `branches:` key under `workflow_dispatch` is deleted so it stops
implying a guarantee it never provided. T6 owns this; T10's green-gate evidence must include a
dispatch-from-branch check that `publish` is skipped.

### N9 — Acceptance is zip-only on both sides (Ruling K)

`Invoke-Tests.ps1:181` passes `-TestAdapterLocation $outputDirectory`, i.e. `_built\`. The local full
gate has exactly the blind spot N1 describes for CI: an assembly omitted from the zip still resolves
out of the build output, so the packaging bug cannot be reproduced locally even once CI catches it.

Both sides converge on one rule: **the harness only ever sees an expanded copy of
`KS.RustAnalyzer.TestAdapter.zip`, never `_built\`.** Locally that means `Invoke-Tests.ps1 -Full`
builds the zip (or reuses the built one), expands it to a scratch directory, and points
`-TestAdapterLocation` there. In CI the `acceptance` job downloads the uploaded artefact and does the
same. The zip's file list is currently hand-maintained (`cdp.yml:98`), which is precisely why the
omission risk is real — this is the check that makes that list honest.

Consequence to design deliberately: the zip build step moves onto the local gate's critical path.
Whoever implements this must decide whether `-Full` builds the zip itself or fails with a clear
message when it is absent — **an unguarded fallback to `_built\` defeats the entire ruling and must
not exist.**

**The list and the ruling reinforce each other (T6b).** The zip's contents are enumerated by hand at
`cdp.yml:99` and encode a judgement recorded nowhere: of the adapter's four package references, only
`System.Collections.Immutable` ships — `System.ComponentModel.Composition`,
`Microsoft.TestPlatform.ObjectModel` and `System.Security.Principal.Windows` are omitted because the
VSTest host supplies them. `Ensure.That` and `Microsoft.ApplicationInsights` ship transitively. Today
nothing verifies that judgement is still true. Once acceptance runs against the zip alone, **the
acceptance run itself becomes the proof that the list is sufficient** — a missing entry stops being a
silent customer-facing packaging bug and becomes a red gate. So T6b need not make the list
self-deriving (which would be wrong — see T6b); it need only give it a single home, because Ruling K
supplies the check.

### N10 — Branch protection and the check-rename deadlock

Master is currently **unprotected** — no protection rule, no rulesets (verified 2026-08-25 via
`gh api`; both collaborators hold admin). Golden rule #3 ("never commit to the trunk branch") is
therefore honoured by convention alone, with nothing enforcing it.

The hazard when adding protection: **required status checks are matched by literal check name.**
Today the run reports `Build, Test & Deploy` and `Publish`. T6 renames those to `config`,
`build-and-test`, `acceptance`, `publish`. A required check that stops being reported does not fail —
it stays permanently *"Expected — waiting for status to be reported"*, blocking merge until an admin
edits the protection settings. Pinning the current names would therefore deadlock the very PR that
lands T6.

Two orderings are viable and the choice is Sir's: (a) protect now with the current check names and
make updating them part of T10's definition of done, or (b) defer required checks until T6 has merged
and the O2 names are stable. **Open — awaiting Sir.**

Note also that `enforce_admins` interacts with Ruling F: with S1 unmerged and both humans holding
admin, an over-tight rule can only be relieved by the same admins it constrains.

### N2 — Commands table under Ruling C (supersedes the earlier N2)

The earlier N2 proposed `none` values and a framework-divergence note for `format:*`/`lint`. **Overruled.**
Then **overruled again, in the opposite direction, by Rulings N and O (2026-08-25)**: the `lint`,
`format:check` and `format:fix` rows are **deleted outright** rather than pointed at `build`, and
**both skill files do change** — the claim below that "neither skill file changes" is void. Under
Ruling N-scope the recipes are this repository's own artifacts, so editing them is not divergence.
Surviving required rows: `build`, `test:quick`, `test:full`.

| Command | Value (T3) |
|---------|------------|
| `build` | `pwsh -NoLogo -NoProfile -NonInteractive -Command "Import-Module .\.github\scripts\VisualStudio.psm1 -Force; & (Get-VisualStudioTool -Name MSBuild) src\RustAnalyzer.sln /m /nologo /nr:false /restore /t:Build /p:Configuration=Release /p:DeployExtension=false /p:OutDir=$PWD\_built\ /verbosity:minimal"` |
| ~~`lint`~~ | **Row deleted (Ruling N).** Not re-pointed at `build` — removed from the table entirely |
| ~~`format:check`~~ | **Row deleted (Ruling O).** Script deleted too |
| ~~`format:fix`~~ | **Row deleted (Ruling O).** Script deleted too |
| `test:quick` | `pwsh -NoLogo -NoProfile -NonInteractive -Command "& .\_built\xunit.console.exe .\_built\KS.RustAnalyzer.UnitTests.dll .\_built\KS.RustAnalyzer.TestAdapter.UnitTests.dll .\_built\KS.RustAnalyzer.Remote.UnitTests.dll -trait type=UnitTests -parallel all -xml .\_built\quick.xml"` |
| `test:full` | one `pwsh -Command` that imports `RustNightly.psm1`, calls `Enable-SessionRustNightly`, runs the same three assemblies with `-notrait scope=External`, then runs `src\TestProjects\run-integrationtests.ps1` — **in one process** (N3) |

Rationale: the Release build *is* the lint and the C# format check —
`src/KS.Common.targets:11-30` turns on `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, and
`CodeAnalysisTreatWarningsAsErrors` for Release, and `codeanalysis.ruleset` is `<IncludeAll Action="Error"/>`,
so SA1028 trailing whitespace and every style rule already fail the build.

~~Two honest consequences, both accepted:~~ **Both consequences are void as of Rulings N and O — the
rows that produced them no longer exist.** Retained as a record of what the removal bought:

1. ~~**`format:fix` does not write.**~~ Moot: the row is gone. The observation that no headless
   auto-fixer exists for these legacy non-SDK projects (`dotnet format` cannot load them) still
   stands, and is now simply a fact about the repo rather than a defect in a gate.
2. ~~**Repeat invocation.**~~ **This was the real cost, and removing it is the real gain.** Dave's
   recipe would have run the same Release build three times (`format:fix` → `build` → `lint`) and
   Bhaskar's three times too. Rulings N and O collapse both recipes to **one** build invocation. Sir's
   stated reason for Ruling O — *"it just adds to the loop time"* — is this line item. T9 no longer
   needs to measure the duplication; it measures the single build.

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
test no longer requires editing a script, a skill, and a design doc. ~~Adding a new **test assembly** must
still register it with that test — R14 from the archive persists in its new home.~~ **Overturned by
Ruling L (2026-08-25):** registration is exactly the manual step that rots. `TraitTaxonomyTests` now
**discovers** the built assemblies by glob and enforces the taxonomy on all of them, with an
**exclusion** list that carries a per-entry reason. Adding a test assembly requires **zero**
registration; forgetting to exclude one fails loudly rather than silently skipping it. Zero discovery
is itself a failure. **R14 is retired, not relocated.**

### N7 — T1 probe results: `rust-toolchain.toml` rejected, channel file adopted

Pin location: **`.github/rust-nightly-channel`**, one line, `nightly-2026-08-25`. This is the repository's
single **active pin** — the only dated channel that any script, workflow, or toolchain resolution reads.
`RustNightly.psm1`'s exported `Get-PinnedRustNightlyChannel` is the only reader, and `cdp.yml` reads the
same file in a `config`-style step.

**Precision, per Bhaskar's T1 verification:** this is *not* the only dated-channel literal in the
repository, and the earlier wording claiming so was wrong. `nightly-2024-03-27` appears in
`src/RustAnalyzer.TestAdapter.UnitTests/Cargo/ToolChainServiceExtensionsTests.cs:70,72,109,111` and in the
`TestGetInstalledToolchainsBasicAsync.same_active_and_default` /
`.separate_active_and_default` approval files. Those are **inert fixture and approval data** — simulated
`rustup show` output feeding a parser test — with no effect on toolchain resolution. They are correct as
they stand and must not be deleted to make a tidier claim true. The invariant that matters is narrower
and is the one to hold: **exactly one configured pin, read from exactly one file.**

Channel resolution (not guessed): the current session manifest records
`rustc 1.100.0-nightly (e7769602a 2026-08-24)`, commit `e7769602aca3770e8d8ea55716becb22e839a579`.
`https://static.rust-lang.org/dist/2026-08-25/channel-rust-nightly.toml` reports
`[pkg.rust] version = "1.100.0-nightly (e7769602a 2026-08-24)"` with that exact
`git_commit_hash`; the `2026-08-24` manifest is the previous nightly (`fb6531d55 2026-08-23`). So the
dated channel for the nightly in use is **`nightly-2026-08-25`** — rustup's channel date is the
publish date, one day after `rustc -Vv`'s `commit-date`.

| Probe | Result | Evidence |
|-------|--------|----------|
| (a) override-reason drift | **FAIL** | With a repo-root `rust-toolchain.toml`, `rustup show` reports `active because: overridden by 'D:\src\gh\rust-analyzer.vs\rust-toolchain.toml'` instead of `active because: it's the default toolchain`. The reason string the corpus asserts (`directory override for 'D:\src'`) is a *different* rustup reason and is not what a toolchain file produces. Blast radius is narrower than feared: those strings are **simulated inline input** to `TestGetInstalledToolchainsBasicAsync` (a `RustupShowOutput.Simulated` unit test), and `IsActive` is parsed from the `(active)` marker in the *installed toolchains* list, not from the reason line — so the approvals themselves would not drift. What does change is the live `TestGetActiveToolChainAsync` / `TestGetBinAndLibPathsAsync` pair: they resolve from `TestHelpers.ThisTestRoot`, a repo-relative directory, so their answer silently flips from the machine default to the pinned channel. The pin becomes an invisible input to two integration tests. |
| (b) `rustup override set` product path | Not reached | Probe (c) is disqualifying on its own; (b) was not exercised because the toml is rejected regardless. |
| (c) implicit auto-install | **FAIL — boundary breach** | With `rust-toolchain.toml` naming an uninstalled `nightly-2020-01-01` and `RUSTUP_DIST_SERVER` pointed at an unreachable host, `cargo --version` emitted `info: syncing channel updates for nightly-2020-01-01-x86_64-pc-windows-msvc` and then failed on the download. rustup **does** attempt to acquire the channel on the first cargo invocation. That is an install path outside the `-AssistantStartup` token handshake, i.e. exactly the breach the working agreement forbids. `RUSTUP_TOOLCHAIN` *does* pre-empt it (confirmed: with `RUSTUP_TOOLCHAIN=nightly` set, the same invocation returned `cargo 1.100.0-nightly (e8cb624d5 2026-08-22)` and never touched the network) — but that only protects processes the gate scripts launch. Any bare `cargo`/`rustc` in the checkout, including one launched by the extension under VS, would auto-install. |
| (extra) product pickup of a repo-root toml | **Not a hazard — corrected after review** | Originally recorded as a live scanner input. **That overstated it.** `FileScannerFactory.cs:15` registers the scanner with `supportedFileExtensions: new[] { Constants.ManifestFileName, Constants.RustFileExtension }` — `ManifestFileName` is the **name-exact** `"Cargo.toml"`, not an extension. VS therefore never hands this scanner an arbitrary `.toml`. `FileScanner.cs:74`'s broad `ext.Equals(Constants.ManifestFileExtension)` (`".toml"`) lives only in `IsUpToDateAsync`, reached solely for files the scanner was already registered against, and `ScanContentAsync` returns `null` unless `GetContainingPackageAsync` finds a real package. So line 74 is a filter **broader than its caller's contract** — an internal inconsistency that is dead in practice with no reachable behaviour change. Tightening it to a name-exact `IsManifest` check is a one-line consistency cleanup belonging to the Cargo/discovery-hardening candidate in `docs/backlog.md`, not to T1. Verified independently against both files. |

**Decision:** probe (c) alone is disqualifying, and (a) makes the pin an invisible input to two live
tests. R3 is confirmed as written. The documented fallback is adopted: a channel file that only the
bootstrap scripts read, with no rustup-visible side effect anywhere in the tree.

**Latent module-resolution bug fixed alongside.** T1 added an `Import-Module RustNightly.psm1` to
`Initialize-RustNightly.ps1`, which exposed a pre-existing trap: the `.psm1` files imported each other
with `-Force`, and `Import-Module -Force` is remove-then-import. A nested `-Force` import therefore
*unloaded the caller's copy* of the same module and re-imported it into the nested module's private
scope, where its exports are not re-exported. `Initialize-AssistantSession.ps1` lost
`Assert-AssistantBootstrapAuthorization` and `Set-AssistantBootstrapPhase` mid-run. Fix: nested
`.psm1` → `.psm1` imports (`RustNightly.psm1`, `AssistantBootstrap.psm1`, `CIProvenance.psm1`) drop
`-Force`; top-level `.ps1` → `.psm1` imports keep it. The invariant is now: **`-Force` only at the
entry-point script, never inside a module.** No authorization logic changed.

### N11 — Bhaskar's Ruling M verification: red confirmed, six rejects

**Headline: the red is the right red.** `-Mode full` → **208 tests, exactly 1 failed, exit 1**, and that
one failure is `RlsReleaseTests.LastUpdateShouldNotBeOlderThan30DaysAsync` on the genuine staleness
assertion — real dates, real URL, `RlsLatestInPackageVersion` still `2025-06-09`, `src/external`
byte-untouched, no `Skip=` anywhere, nothing retagged. Counts reconcile as a clean partition:
`unit 100 + integration 108 = 208 = full`; `204 + 4 taxonomy facts = 208`, and `203 → 204` is exactly
`RlsReleaseTests` re-entering the gate. All four modes behave; no silent default (`-Mode` omitted,
`-Mode quick`, and legacy `-Full` all exit 1).

Six rejects, numbered `B-Rn` to avoid collision with this file's risk register:

- **B-R1 — `-parallel all` defeats an assembly's declared serialization. HIGH, and it is new.**
  `src/RustAnalyzer.TestAdapter.UnitTests/Properties/AssemblyInfo.cs:41` declares
  `[assembly: CollectionBehavior(DisableTestParallelization = true)]` — the only assembly that does.
  VSTest honoured it; `-parallel all` overrides it. `TestExecutorTests` (profile `bench`) and
  `ToolchainServiceTests` (profile `release`) are different classes, therefore different collections,
  and cargo emits **both profiles into the same `target/release`**. Measured on cold trees:
  `-parallel all` 3 failures in 8 runs (**~37%**); attribute honoured 0/4; `-parallel assemblies` 0/4.
  **This refutes the earlier "pre-existing latent flake" attribution recorded against T8** — Dave's
  "1-in-5, five clean reproductions" was measured warm, and warm trees close the window. CI is always
  cold, so expect ~1 CI run in 3 to redden on an unrelated case. Fix before T8: `-parallel assemblies`.
- **B-R2 — zero matched tests exits 0. Fail-open regression, the most important item.** `-Mode unit`
  can pass having executed nothing (`GRAND TOTAL: 0 0 0 0`, exit 0), proven against the real `_built`.
  The old script asserted the selected count and threw; T2 deleted that and replaced it with nothing.
  `TraitTaxonomyTests` cannot cover this — if the filter selects nothing, the taxonomy test does not
  run either. A typo in a trait name yields a green gate that ran zero tests. **This is the same
  failure class as `RlsReleaseTests`: a gate reporting success without executing the thing it guards.**
- **B-R3 — glob asymmetry.** The runner globs `KS.*Tests.dll`; `TraitTaxonomyTests` globs `*Tests.dll`.
  Proven: a correctly-tagged `Probe.NotKs.Tests.dll` with a deliberately failing case passed the
  taxonomy and was **never executed**. A test assembly not named `KS.*` appears governed but does not run.
- **B-R4 — the taxonomy can be silently skipped.** The script throws only on *zero* assemblies. If the
  assembly hosting `TraitTaxonomyTests` fails to land in `_built` while others do, nothing asserts the
  invariant ever ran. The deleted hardcoded `204` would have caught it. Same hole as B-R2.
- **B-R5 — an undisclosed test assertion edit.** `EnvironmentExtensionsTests.cs` was relaxed to match
  `windir` case-insensitively. **Necessary and substantively defensible** (a `pwsh`-rooted process tree
  yields `WINDIR`; the new form also adds a uniqueness assertion) but it was not disclosed, and it masks
  the production defect below.
- **B-R6 — `TypeTraitsPartitionEveryTestCase` is a sum, not a partition.** With an untagged probe
  (contributes 0) and a dual-tagged probe (contributes 2) both live, the sum still equalled the case
  count and **the test passed with two real violations present**. Offsetting errors cancel. Only
  `EveryTestCaseCarriesExactlyOneTypeTrait` caught them. Redundant at best, false comfort at worst.

**Synthesis worth keeping — and its limit.** ~~Fixing B-R2 subsumes the
`NoTestCaseCarriesTheAcceptanceTypeTrait` question entirely.~~ **Corrected by Bhaskar (F3) after the fix
landed:** that would hold only for a count-*comparison* assertion. The implemented guard is `-eq 0`,
correctly so — hardcoded counts are exactly what N6 killed — and therefore a filter selecting 1 of 208
still exits 0 with no mismatch surfaced. **`NoTestCaseCarriesTheAcceptanceTypeTrait` remains
load-bearing and must not be deleted on the strength of the original sentence.** The narrower true claim
survives: zero-match no longer passes silently.

**Ruling M is not fully implemented.** The 3-job u/i/a CI topology is not built; CI still has one
`build-test-deploy` job and currently runs the unit cases twice (quick, then unfiltered full). Deferring
to T6 is defensible sequencing, but Ruling M must not be recorded as done.

**Production defects surfaced, both routed to Anders, neither Dave's:**
1. `src/RustAnalyzer.TestAdapter/Common/EnvironmentExtensions.cs:16-21` builds the child environment with
   `GroupBy(...).ToDictionary(...)` under the **default ordinal comparer**, but Windows environment names
   are case-insensitive. `OverrideProcessEnvironment` therefore silently fails to override any variable
   whose casing differs and emits a duplicate into the child's env block. Line 36's
   `PrependToPathInEnviroment` gets this right with `OrdinalIgnoreCase` — the inconsistency is inside one
   file. **This is the real content of the N4 `windir` refutation.**
2. `RlsInstallerService.cs:92-95` ends in a bare `catch { return null; }`, conflating "GitHub unreachable"
   with "the redirect segment did not parse as a date". Fail-closed still holds — the null path throws
   `ArgumentNullException: String reference not set to an instance of a String` — but the diagnosis is
   opaque. A test-side `NotBeNull` guard is a band-aid over a production swallow; record the swallow as
   the defect and do not let the band-aid close it. Same category as S-f.

**Process:** the working tree changed under Bhaskar mid-run (JARVIS's doing — Dave's Ruling N/O edits at
20:39/20:44). Every artifact his findings depend on was untouched and both gates completed before those
edits landed, so A–I stand. But concurrent edits during verification are how a false green gets recorded.
**Rule adopted: Dave holds still while Bhaskar runs.**

### N12 — B-R1/B-R2 fixed and the packaged rust-analyzer updated (Ruling P)

Sir authorized these three together (Ruling P), overriding the earlier "update the binary after the
fixes" sequencing. **B-R3, B-R4 and B-R6 were deliberately left unfixed** and remain open.

- **B-R1 closed structurally, not probabilistically.** `-parallel all` → `-parallel assemblies` in
  `Invoke-Tests.ps1`. Cross-assembly parallelism is kept and **collection-level parallelism is now off
  for every assembly** — the runner's own help is explicit: `assemblies - only parallelize assemblies`.
  So this is strictly *more* serial than `DisableTestParallelization` asks for, not "each assembly's
  declaration honoured" (Bhaskar F2 corrects the looser wording I first used here — stated precisely so
  a future optimizer does not "restore" `all` believing declarations are respected). The race is closed
  because `TestExecutorTests` (`bench`) can no longer share `target/release` with `ToolchainServiceTests`
  (`release`). Measured cost is visible but acceptable: `TestAdapter.UnitTests` is 82.0s of a 91.2s wall.
  A comment in the script records *why*.
- **B-R2 closed by counting what actually ran.** The gate now sums the `total` attribute across
  `<assembly>` nodes in the xUnit XML and fails closed on zero, naming the mode, the filter, and the
  assemblies scanned. It **defers rather than throwing**, so the acceptance leg still runs and reports.
  Scoped inside `if ($runsAssemblyTests)`, so `acceptance` — which legitimately runs no assembly tests —
  does not trip it. No count is hardcoded; the assertion is `-eq 0`.
  **Demonstrated firing, not merely asserted:** with the trait mistyped to `type=UnitTestsTypo` the
  runner still printed `GRAND TOTAL: 0 0 0 0` and still exited 0, while the gate exited 1. A second
  probe proved the same for `full`, with the acceptance leg still running afterwards. A negative control
  on real `-Mode acceptance` exited 0 and emitted no count line at all.
- **Packaged rust-analyzer updated to `2026-08-24`.** `rust-analyzer.exe` 40,887,808 → 38,472,704 bytes;
  `rust_analyzer.pdb` 17,211,392 → 14,413,824. Zip SHA256 verified against the published asset
  **before** extraction. Binary reports `rust-analyzer 0.3.3025-standalone (5c156cdfb0 2026-08-23)`.
  Golden rule #5 is satisfied by the design-doc exception: the binaries were replaced *through the
  intended acquisition process* (official release asset, hash-verified), never hand-edited.

**The three-way version coupling — the part that could have shipped broken.**
`RlsInstallerService.GetVersionedExePath` resolves to
`<assembly dir>\<Constants.RlsLatestInPackageVersion>\rust-analyzer.exe`, and the two `<Link>` folder
names in `src/RustAnalyzer/RustAnalyzer.csproj` are what place the binary at that path. All three must
carry the same date string. A mismatch **builds green and fails only at runtime inside Visual Studio** —
no gate in this repository would catch it. Verified at the artifact level by opening the built VSIX:
`2026-08-24/rust-analyzer.exe` and `2026-08-24/rust_analyzer.pdb` are present at exactly the path the
resolver will look in.

**Gate result: `208 total, 1 failed, exit 1` → `208 total, 0 failed, exit 0`.** The alarm cleared because
the thing it was alarming about was fixed — `RlsLatestInPackageVersion` moved from a 441-day-stale
`2025-06-09` to `2026-08-24`. No test, threshold, trait, or constant was adjusted to force it.

**Standing rule adopted:** Dave holds still while Bhaskar runs. The previous round's concurrent edits did
not invalidate any finding, but that was luck, and concurrent mutation during verification is how a false
green gets recorded.

**Note for T7:** `docs/design.md` §"Build, test, and release flow" still describes the deleted
format/lint steps and cites `204`/`96`/`203`/`108`, `scope != External`, and `-Full -IncludeExternal` —
all superseded. Untouched here; it is T7's.

**Open gap this round did NOT close (Bhaskar F1, routed to Anders).** The very coupling described above
is still **unguarded**. `LastUpdateShouldNotBeOlderThan30DaysAsync` compares the *constant* to the
*remote release date*; nothing asserts that the packaged binary or the two `<Link>` folder names agree
with the constant. A future bump of `RlsLatestInPackageVersion` alone would pass all 208 cases and fail
only at runtime inside Visual Studio — the same "builds green, breaks in VS" class this note calls out.
It is correct today only because all three edits were made together, and nothing makes that true next
time. Suggested close: a ~10-line unit test parsing the `<Link>` names out of `RustAnalyzer.csproj` and
asserting equality with `Constants.RlsLatestInPackageVersion`.

**Process defect found by Bhaskar (F6), and it was JARVIS's, not the framework's.** My briefs told Dave
to load `.github/agent-roles/coder.md` and Bhaskar `.github/agent-roles/verifier.md`. **Neither file
exists and neither ever did.** `.github/agent-roles/` holds exactly one role body — `conductor.md`, the
assistant's — because `agentify` copies only the assistant's role (`agentify.md:129`, "ONE role body").
The sub-agents are self-contained in `.github/agents/dave.md`, `bhaskar.md`, `anders.md`. Bhaskar flagged
the dangling pointer and substituted the right file; Dave worked around it silently, which is the more
concerning half. **Correct dispatch: cite `.github/agents/<agent>.md` for sub-agents; `agent-roles/` is
the assistant's alone.**

### Execution rules

1. S1 is mandatory first and its done-done is the **PR merge gate green**, not a local green (Sir).
2. S7 (candidate 1) starts the moment S1 merges and runs in parallel with S2–S6. It ships no behaviour.
3. S4 (candidate 4) does not wait on S7.
4. No slice may reintroduce a gate wrapper script under `.github/scripts`, a soft-failure switch, or a
   job-level `OutDir`.
5. Every slice updates this file with actual decisions, evidence, and commit references.
6. Anything marked `[HUMAN]` stops and returns to Sir; no agent decides it by inference.
