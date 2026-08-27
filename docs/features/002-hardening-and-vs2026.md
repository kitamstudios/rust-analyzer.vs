# Feature: Hardening and Visual Studio 2026
**Branch:** vibe/002-hardening-and-vs2026
**Status:** Archived

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
| T2 | S1 | **Test execution mechanism and classification invariants** (Ruling A, first half). (a) Move the three assembly suites off `vstest.console.exe` to the native **xUnit console runner** (`xunit.runner.console`, `tools\net472\xunit.console.exe`), copied into `_built\` by the test projects' targets; the projects are legacy non-SDK `ToolsVersion="15.0"` / `TargetFrameworkVersion v4.8` with PackageReference, so `dotnet test` cannot drive them (`docs/design.md`: "not currently a safe `dotnet test`/Coverlet target") and the .NET Framework console runner is the runner that can. (b) Preserve gate semantics with `-trait "type=UnitTests"` (quick) and `-notrait "scope=External"` (full), `-parallel all`. (c) **Delete the PowerShell discovery preflight and its hardcoded `204/96/108/1`**; replace it with a reflection-based `TraitTaxonomyTests` xUnit test in the unit suite asserting the *invariants*: every case carries exactly one `type` trait, no case carries both, every `scope=External` case is also `type=IntegrationTests`, unit + integration = total, total > 0. Numbers stop being inputs. (d) Verify before switching: app-domain/loading behaviour for `KS.RustAnalyzer.UnitTests` (it references `src/external/vs.17.11` VS assemblies), the ApprovalTests path (`RaVsDiffReporter.INSTANCE` is `XUnit2Reporter.INSTANCE` — runner-agnostic, it fails through xUnit), and that `xunit.runner.visualstudio` is retained for in-IDE Test Explorer. (e) Accept the one loss: the console runner emits xUnit XML, not TRX; the TRX consumer was `dorny/test-reporter`, already deleted under D3, so TRX becomes an artifact-only concern. | Done | `bf24efe` |
| T3 | S1 | **Re-point the Commands table and both recipes at de-scripted commands** (Ruling C). `build` resolves to the single Release build command — that build *is* the C# style and analyzer enforcement. **`lint`, `format:check` and `format:fix` are not re-pointed; they are deleted (Rulings N and O), along with `Invoke-Format.ps1`.** `test:quick` and `test:full` **remain `Invoke-Tests.ps1 -Mode unit|full`** — under **Ruling Q** that script is exempt from T4, because it is the single executable statement of the test-gate policy Rulings M, K and P created, not a gate wrapper. `test:full` stays a **single process** (see N3). No `none` values; the only `.ps1` wrapper removed here is `Invoke-Build.ps1`. **Both `.github/skills/build-test.md` and `build-test-full.md` DO need edits** — the earlier "no edit" claim assumed the gates survived; under Ruling N-scope those recipes are this repo's artifacts, not framework files. See N2 for the exact values and the three regressions its earlier inlined values carried. **Ruling K's local half is now T3b, not a clause here.** | Done | - |
| T2b | S1 | **Enforce the `-Force` module-import invariant** (Anders S-b). T1 fixed a latent trap: `Import-Module -Force` is remove-then-import, so a nested `.psm1` → `.psm1` forced import *unloads the caller's copy* and re-imports into the nested module's private scope, where exports are not re-exported. It silently stripped `Assert-AssistantBootstrapAuthorization` from the authorization path and surfaced as a confusing "term is not recognized" far from its cause. The invariant — **`-Force` only at entry-point `.ps1` scripts, never inside a `.psm1`** — is currently prose in N7 with nothing enforcing it. Add a `type=UnitTests` test that **globs** `.github/scripts/*.psm1` (globbing, not enumeration, so it survives T4 deleting `CIProvenance.psm1`) and asserts no `Import-Module … -Force`, with a failure message naming the remove-then-import cause. Same "convention → executable invariant" move as T2(c). **Sequence after T2** so it is written once, in the xUnit-console world. | **Reversed — Ruling V** | - |
| T4 | S1 | **Delete four scripts and repair the cascade.** Delete `Invoke-Build.ps1`, `Initialize-CISession.ps1`, `CIProvenance.psm1` (`Invoke-Format.ps1` already went in `bf24efe`). **`Invoke-Tests.ps1` is retained — Ruling Q.** `RustNightly.psm1:4` imports `CIProvenance.psm1` and `Get-RustNightlyManifest` falls back to `Get-CIBootstrapProvenance`; remove both, along with the `GITHUB_ACTIONS` branch of `Get-RustNightlyHandoffMessage`. CI no longer needs provenance because it never runs the assistant bootstrap — the workflow sets `RUSTUP_TOOLCHAIN` directly. Retain `Initialize-AssistantSession.ps1`, `Initialize-RustNightly.ps1`, `AssistantBootstrap.psm1`, `RustNightly.psm1`, `SessionState.psm1`, `Test-SessionBootstrap.ps1`, and `VisualStudio.psm1`. **S-f (Anders), same function, one edit:** `Get-RustNightlyManifest` currently folds a *channel mismatch* into the message "The Rust nightly manifest does not belong to the current session." When the pin moves mid-session the manifest is perfectly valid for that session — the pin changed. The remedy (re-bootstrap) is right but the diagnosis is wrong, and a wrong diagnosis in the bootstrap path is exactly what cost time on the `-Force` bug. Give the mismatch its own message naming the recorded channel and the current pin. Fail-closed behaviour stays byte-identical; only the diagnosis improves. | Done | - |
| T4b | S1 | **Extract the three steps CI and the loop gates must share** (Ruling S, 2026-08-26). Each becomes exactly one script under `.github/scripts`, invoked by both `cdp.yml` and the Commands table / gate recipes. No step may have one implementation in YAML and another locally. **(a) Build.** Restore a build script — this *reverses T4's deletion of `Invoke-Build.ps1`*, which was correct under the old reading of Ruling C and is wrong under Ruling S. It must contain the single Release MSBuild invocation and nothing else: **no `-AnalyzerCheck` and no second `/t:Rebuild` pass** (Ruling N — the lint gate is dead and must not return through this door), and it must keep `/p:OutDir` (D4). The Commands table `build` row points at it instead of the inline command Dave wrote for T3. **(b) Stamp VSIX version.** Currently inline in `cdp.yml`. Extract it. Note golden rule #5: the version field of `src/RustAnalyzer/source.extension.cs` is an auto-stamped generated value, so this script *is* the documented process that writes it — it is the one caller allowed to. **(c) Install pinned Rust nightly.** **Superseded by Ruling U — see T4c.** Re-scoped once by Ruling T to four touches, then obsoleted entirely: Ruling U deletes the session/token layer that was the sole reason CI could not use `Initialize-RustNightly.ps1` directly. The `-Authority` parameter, the manifest-owner branch and the CI session variable are all cancelled. | Done |  - |
| T4c | S1 | **Delete the session/token layer** (Ruling U, 2026-08-26). This *replaces* T4b(c) and collapses it: with no session scoping, CI simply calls the same installer, so Ruling S is satisfied with no parameter, no owner branch and no CI session variable. **Delete** `AssistantBootstrap.psm1`, `Initialize-AssistantSession.ps1`, `Test-SessionBootstrap.ps1`. **`SessionState.psm1` is absorbed, not merely deleted** — `Get-RepositorySessionId` and `Get-RepositorySessionRoot` die, but `Get-RepositoryRoot` and `Get-Sha256Hex` are still needed by checks being *kept* (`RustNightly.psm1:85`, and the manifest path), so move those two into `RustNightly.psm1` and delete the module; they have no other callers. **The manifest becomes checkout-scoped instead of session-scoped:** same `%LOCALAPPDATA%\ravsq\<hash16>` shape, hashing the repository root rather than the session id — it stays out of the working tree, so no `.gitignore` change. `Get-RustNightlyManifest` drops the provenance fetch and the `SessionId` check and keeps the other four; `Initialize-RustNightly.ps1` drops `-BootstrapToken`/`-Authority`, both dead imports, and the four `Bootstrap*`/`SessionId` manifest fields. **Cascade — all of it must be found, not just the list here:** `cdp.yml` (two hand-rolled rustup steps → one script call each; the session `env` var Dave added is cancelled), `.github/skills/preflight.md` Gate 3 (lines 10, 83), `.github/skills/build-test-full.md:19` (Bhaskar's `Test-SessionBootstrap.ps1` call — droppable outright, since `Invoke-Tests.ps1` already validates via `Enable-SessionRustNightly` for every mode ≠ `unit`), `.github/agents/JARVIS.md:28`, and the "Bootstrap ownership" paragraph in `.github/copilot-instructions.md`. **Diagnosis quality is the thing most likely to be lost here** (S-f): `Get-RustNightlyHandoffMessage` and the throw sites are written in "current session" language that becomes false — rewrite them to name the checkout and the remedy, and keep them naming the bootstrap. `ScriptModuleImportTests.cs` (T2b) globbed `.github/scripts/*.psm1` and so survived these deletions by construction — but **Ruling V then deleted the test itself**, on the grounds that Ruling U removed the last `.psm1` → `.psm1` import and with it the hazard T2b guarded. Both halves are required: the file *and* its `<Compile Include>` at `RustAnalyzer.UnitTests.csproj:55`, since that project enumerates compile items explicitly. | Done | - |
| T5 | S1 | **Verify runner-label ↔ VS-major against `actions/runner-images`** before wiring the `config` knob. **Done 2026-08-26 — the prior lead was REFUTED.** It claimed "`windows-2025` may now carry VS 2026 (major 18) after a mid-2026 image migration." False: `windows-2025` carries **VS Enterprise 2022 17.14.37614.0, major 17**, identical to `windows-2022`. No such migration occurred. Verified mapping, from live reads of the `actions/runner-images` READMEs on `main`: `windows-2022` → VS 17.14.37614.0 (major 17), active, not deprecated; `windows-2025` → VS 17.14.37614.0 (major 17), and this is where `windows-latest` points; **`windows-2025-vs2026` → VS Enterprise 2026 18.9.12112.369 (major 18), GA, public repos only**; `windows-11-vs2026-arm` → VS 18.9.12105.275 (arm64); `windows-2019` **fully removed** (README returns 404, absent from the docs label tables); `windows-2026` does not exist as a label. Rustup 1.29.0 / Cargo 1.97.1 / Rust 1.97.1 are preinstalled on every current Windows image under the README section "Rust Tools". **T6 uses `windows-2022` with `vs-major: 17`** — confirmed active, carries the VS major the gates target, and Ruling F scopes S1 to getting CI green rather than moving hosts. See N14 for the S2 consequence. | Done | - |
| T6 | S1 | **Rewrite `cdp.yml` to the O2 topology**: `config` → `build-and-test` → `acceptance` → `publish`. `config` (literal runner) checks out, **invokes the T1 channel reader** (`.github/scripts/Get-PinnedRustNightlyChannel.ps1` — see S-a below), and outputs `runner`, `vs-major`, `nightly-channel`; downstream jobs use `runs-on: ${{ needs.config.outputs.runner }}` — verified legal, since `runs-on` accepts `needs.*.outputs` but **not** `env`. `build-and-test`: shell rustup install of the pinned channel, MSBuild resolved through `VisualStudio.psm1`, inline VSIX version stamp, Release build with **step-level** `OutDir`, then **three separate jobs** per Ruling M — **unit**, **integration**, and **acceptance** — then Zip TestAdapter and upload VSIX + zip + xUnit XML. `acceptance`: downloads and expands `KS.RustAnalyzer.TestAdapter.zip` to `.\testadapter` and runs `src/TestProjects/run-integrationtests.ps1 -TestAdapterLocation .\testadapter -VisualStudioMajorVersion ${{ needs.config.outputs.vs-major }}`, i.e. **against the shipped artefact**. `publish` needs `[config, build-and-test, acceptance]` **and gates on the ref, not just the event — `github.ref == 'refs/heads/master'` (Ruling J); delete the ignored `branches:` key under `workflow_dispatch` (N8)**. No `continue-on-error` anywhere. All deprecated actions replaced by shell per Ruling B and N5. **S-a (Anders):** extract the read+validate of `.github/rust-nightly-channel` into a standalone `.github/scripts/Get-PinnedRustNightlyChannel.ps1` that `RustNightly.psm1` dot-sources and that `config` invokes directly. T1 left **two independent interpreters** of one file — `RustNightly.psm1` and `cdp.yml` each carry their own copy of the `^nightly-\d{4}-\d{2}-\d{2}$` regex and their own error text, and neither validates the other. That is R6's drift mode structurally. One reader, one regex, one message; CI no longer imports the bootstrap module stack merely to read a file. | Done | - |
| T6b | S1 | **Give the TestAdapter zip file list one home** (Sir, 2026-08-25). The list lives inline in `cdp.yml:99` as a six-element array — `KS.RustAnalyzer.TestAdapter.dll`/`.pdb`, `Microsoft.ApplicationInsights.dll`/`.pdb`, `System.Collections.Immutable.dll`, `Ensure.That.dll`. Under Ruling K it becomes load-bearing on **both** sides, so it cannot stay in the workflow. Sir's first suggestion was `Invoke-Tests.ps1`; under **Ruling Q** that file survives, but it is still the wrong home — it is the *test-gate policy*, not a package manifest, and the CI zip step must read the list without importing the gate. Put the list in a data file — `src/RustAnalyzer.TestAdapter/testadapter-package.txt`, one relative file name per line, comments allowed — beside the project whose output it describes, with exactly one reader script that both the local gate and the CI `acceptance`/zip steps call. **Do not derive it from project references:** the adapter references `System.ComponentModel.Composition`, `Microsoft.TestPlatform.ObjectModel` and `System.Security.Principal.Windows`, none of which ship, because the VSTest host supplies them. The list is a curated statement of *what the host does not provide* and must stay curated; a naive derivation would bloat the zip and hide the judgement. Keep `Compress-Archive`'s fail-on-missing-file behaviour — a name in the list that is not built must stay a hard error. **Sequence BEFORE T6** (corrected 2026-08-26, Anders): the old "sequence after T6 so there is only one workflow rewrite" was self-defeating — T6 *is* the rewrite, so T6b-after-T6 means writing the zip step twice. T6b first makes T6's zip step one line calling the reader. **T3b consumes this reader too.** | Done | - |
| T3b | S1 | **Ruling K's local half** (promoted from a clause inside T3 on 2026-08-26; it was real work with no owning task). The local `test:full` acceptance leg must run against an expanded copy of `KS.RustAnalyzer.TestAdapter.zip`, never `_built\`, with no fallback — see N9. Today `Invoke-Tests.ps1:98` passes `-TestAdapterLocation $outputDirectory`, i.e. `_built`, so the local half of Ruling K is currently **not implemented at all**. **Anders also found the fallback at its source:** `src/TestProjects/run-integrationtests.ps1:6` *defaults* `$TestAdapterLocation` to `..\..\_built` — literally the unguarded fallback N9 says "must not exist", and no task had named it. Make the parameter `[Parameter(Mandatory)]`; that one line closes the ruling at the source rather than at each call site. Then have the gate build/locate the zip, expand it to a scratch directory, and pass that. **Depends on T6b's reader.** | Done | - |
| T7 | S1 | **Re-home the architectural facts orphaned by Sir's `design.md` cut** (re-scoped 2026-08-26). Sir deleted 126 lines — the whole "Build, test, and release flow", "Generated and external artifacts" and "Known architectural constraints" block — rather than correcting it. That was right for the *gate-implementation* prose: it described five scripts (three now deleted) and the hardcoded counts N6 killed. But it swept out three **architectural** facts that were merely co-located with implementation detail, and one of them is a live governance hole. (a) **`src/external` acquisition rule — open hole, fix first.** The deleted paragraph was golden rule #5's *only* coverage of the packaged rust-analyzer, and N12 explicitly leaned on it. `.github/copilot-instructions.md`'s Generated-artifacts line lists `**/bin/`, `**/obj/`, `_built/`, `*.vsix` and the `source.extension.cs` version field — **not `src/external`**. As of today an agent may hand-edit a 38 MB binary and violate nothing. Do **not** restore it to `design.md`; golden rule #5 says the consuming project lists its artifacts in the **Project profile**, which is why a `design.md` edit could delete it. Add one bullet there for *acquired* (not generated) binaries under `src/external/` — packaged `rust-analyzer.exe`/`rust_analyzer.pdb` and the `vs.17.11` host assemblies — replaced only through the documented hash-verified acquisition process. Fold in the two other items the deletion dropped that the profile does not cover: `VSCommandTable.cs` and session nightly provenance under `%LOCALAPPDATA%`. (b) **"not a safe `dotnet test`/Coverlet target"** — verified still true (`RustAnalyzer.UnitTests.csproj:2` `ToolsVersion="15.0"`, `:12` `v4.8`, PackageReference via `KS.Tests.Common.targets`) and still load-bearing: it is the stated premise for **T2's runner choice** and **D1's** deferral of DRY/mutation/CRAP, and T2 quotes `docs/design.md` for it, so that citation now dangles. Re-home one sentence to `design.md` §"Projects and dependency direction" and repoint T2's citation. (c) **vswhere-based VS-major resolution** — `VisualStudio.psm1:3-14`, `:17-72` (`-MajorVersion`, default 17, requires a complete install, throws on mismatch), consumed by `run-integrationtests.ps1:8,14` and `Invoke-Tests.ps1:9-10`. The load-bearing sentence was *"a later completed VS major is never selected silently"* — a **host-binding contract**, not gate mechanics, and **Ruling E makes it more load-bearing, not less**; it is the mechanism S5's matrix (T31–T35) will use. Re-home two sentences near the VS platform section. **S-e (Anders):** also sweep the surviving text for floating-`nightly` and CI-provenance claims — the assistant-bootstrap paragraph said "installs or updates rustup's `nightly` toolchain", false after T1's pin. **Standing rule established here:** `design.md` holds **contracts and constraints**; gate mechanics live in the skill recipes and the Commands table, versioned next to the thing they describe. | Done | - |
| T8 | S1 | **Drive CI to its first green run** on the pushed branch. Green means: build, quick, full, acceptance, zip, and both uploads all succeed with no soft-failure switch anywhere. | Done | CI run `32998966472` green: unit 102, integration 108, acceptance 18 matched, `publish` skipped, no soft-failure switch |
| T9 | S1 | **Measure the gate portfolio on the pushed branch** and fill the table below — per-gate wall clock, trigger, what it uniquely catches, and the retained risk of every removed gate. Includes the measured cost of the repeated Release build invocation (N2). | Done | See "Gate portfolio (measured)" |
| T10 | S1 | **Raise the PR and track the merge gate to pass.** Done-done for candidate 2 is the PR gate green, not a local green. **If master carries branch protection, T10 also updates the required-status-check names** — O2 renames the reported checks from `Build, Test & Deploy`/`Publish` to `config`/`build-and-test`/`acceptance`/`publish`, and a required check that is never reported blocks the merge indefinitely (see N10). Evidence must include a `workflow_dispatch` from a non-`master` ref showing `publish` skipped (N8). | **Done** | N8 evidence captured (run `33000124688`). Master IS protected (N10). Sir handed the setting change to JARVIS on 2026-08-26; required checks moved from the stale `Build, Test & Deploy` to the five that actually run on a PR — `config`, `build-and-test`, `unit`, `integration`, `acceptance` — via `PATCH .../protection/required_status_checks` (targeted; `enforce_admins`, `required_conversation_resolution`, review count and force-push settings all verified unchanged). `publish` is deliberately **not** required: it never runs on a PR, so requiring it would lean on "skipped counts as passed" for no benefit (N8) |

### S2 — Candidate 3a: VS 2022 17.12+ and VS 2026 packaging + runtime proof

| #  | Slice | Task | Status | Commit |
|----|-------|------|--------|--------|
| T10c | S2 | **Close N16/N17: delete the inert `.globalconfig` include; delete the enforcement claim** (Sir's rulings Y-withdrawn / Z / AA / AC, 2026-08-26). Delete `src/KS.Common.targets:54-56` outright, leaving **nothing in its place** (Ruling AD — the explanatory comment was written, compressed, then removed; N17 plus the commit message carry the recurrence guard instead). **Then delete the entire `Language-specific conventions` bullet from `.github/copilot-instructions.md` (Ruling AC — deleted, not corrected: the build, `.editorconfig` and the analyzers are both the enforcement and the discovery mechanism).** Repoint `.github/agents/dave.md:10`, which says language-specific rules "live in the Project profile" and would otherwise dangle. **Explicitly out of scope: the 59 suffix conversions and `GenerateDocumentationFile` — those are D11.** Evidence must show the include deletion is a *no-op*: `-getItem:EditorConfigFiles` before/after showing the root `.globalconfig` still arrives, plus the `IDE0161` probe still erroring post-deletion and **zero** `MultipleGlobalAnalyzerKeys`. | Done | `007a123` |
| T11 | S2 | Research the supported manifest expression for **both hosts — VS 2022 17.12+ and VS 2026 — which is a hard requirement (Ruling E)**. Current `source.extension.vsixmanifest` declares three amd64 `InstallationTarget`s at `Version="[17.0, 18.0)"` and a `Microsoft.VisualStudio.Component.CoreEditor` prerequisite at `[17.0,)`. **Specifically verify the standing finding that VS 2026 exposes 17.x APIs and *ignores the upper bound* of existing `InstallationTarget` ranges** — if true, the current range already admits VS 2026 and T13 becomes a no-op or a narrower edit. Record the evidence either way; do not carry the finding forward unverified. **Done 2026-08-26 — VERIFIED, see N18. Microsoft documents this repository's exact range `[17.0,18.0)` as the worked both-hosts example; VS 2026 evaluates only the lower bound. One VSIX, one `Identity Id`, no manifest change required.** | Done | - |

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

Filled by T9 from the first green run on the six-job topology — CI run `32998966472`, commit `71e9d90`,
`windows-2022`. Every removed gate carries an explicit retained risk.

| Gate | Trigger | Runtime | What it uniquely catches | Retained risk |
|------|---------|---------|--------------------------|---------------|
| build (Release) | fast + full + CI `build-and-test` | **368s** (job 401s incl. checkout, stamp, zip, 3 uploads) | Compile errors; **all** StyleCop/IDE/FxCop diagnostics and SA1028 trailing whitespace, via `<IncludeAll Action="Error"/>` + `TreatWarningsAsErrors` in Release | **Resolved and re-scoped — see N17, which supersedes N16.** The root `.globalconfig` *does* load, via SDK auto-discovery; the `src/KS.Common.targets:55` include was inert and is deleted (T10c). But ~25% of the file is dormant for three unrelated reasons — 59 `:severity` suffixes inert at build, 18 naming rules at `suggestion` (Info is never promoted by `warnaserror`), and — **corrected 2026-08-26, superseding the earlier "gated off by a missing `GenerateDocumentationFile`" belief** — `IDE0005` *does* fire, enabled by the `SvSoft.MSBuild.CheckUnnecessaryUsings` workaround (N17), but only in the **2 of 7** projects that load the IDE analyzers at all (N19). What genuinely bites is StyleCop `SA*`/`SX*` + enabled-by-default `CA*`, promoted by `<IncludeAll Action="Error"/>` + `TreatWarningsAsErrors` |
| test:quick (unit) | fast + CI `unit` | **23s** job (**7.7s** xUnit run) | **102** in-process tests; trait-taxonomy invariants via `TraitTaxonomyTests` (5 tests) | - |
| test:full (unit + integration) | full + CI `unit` ∥ `integration` | **89s** critical path (integration job; its xUnit run 63.1s) | Cargo/rustup/process-boundary regressions on the pinned nightly — **108** tests | Locally `-Mode full` runs **unfiltered** in one pass, so an untagged case still executes; in CI the two jobs are trait-filtered, so an untagged case would run in **no** job. The gate holds only because the taxonomy test lives in `unit` — the jobs are therefore *not* independent |
| acceptance (VSTest, published zip) | full + CI `acceptance` | **49s** job | Customer-visible adapter behaviour **and** packaging omissions in the shipped zip | Inner VSTest exits 1 by design (4 approved failures); only the harness's approved-file comparison distinguishes that from a real break |
| *config* (not a gate) | CI only | **21s** | Resolves runner label, VS major and pinned nightly once, so no downstream job hardcodes them (T5) | - |
| *removed:* external (`scope=External`) | — | — | — | **Gate retired, not merely unmeasured.** Ruling M's three-way taxonomy has no `External` value and the string appears nowhere in `src/` or `.github/`. Network/freshness drift is now caught only by `RlsReleaseTests`' 30-day ceiling. `docs/backlog.md` D10 still proposes a `scope=External` renewal test and is therefore unimplementable as written |
| *removed:* separate lint pass | — | — | — | MSBuild-level warnings are no longer promoted to errors. Concretely, `MSB3277` assembly conflicts stay **non-fatal** (D2) — the `/warnNotAsError:MSB3277` grandfather disappears with the pass that carried it. Compiler/analyzer/style coverage is unchanged because Release already enforces it — **premise re-confirmed by N17**: the deleted `lint` gate ran the *same* Release compile with the same config, ruleset and analyzers, so a duplicate of X could never enforce more than X. N16's doubt was misdirected |
| *removed:* non-C# formatter | — | — | — | Trailing whitespace in `.ps1`/`.yml`/`.json`/`.md` is no longer normalized by a gate. `.editorconfig` (`trim_trailing_whitespace = true`) remains the IDE-level contract; C# is still enforced by SA1028 at build |
| *removed:* PowerShell classification preflight | — | — | — | Four `vstest.console /ListTests` discovery passes per gate are gone; the invariants now run as a unit test inside both gates (T2c), so drift still fails closed but numbers are no longer hardcoded |

**N2's repeated Release build no longer exists — measured, then eliminated by the topology rather than
merely costed.** `Invoke-Tests.ps1` invokes no MSBuild at all (its only archive work is the
`Compress-Archive`/`Expand-Archive` pair at `:114`/`:121` serving Ruling K), and in CI `build-and-test`
builds exactly once at 368s while `unit`, `integration` and `acceptance` consume uploaded artifacts. The
368s is paid once per run, not once per gate.

**Partition evidence (T8).** CI `unit` reported 102 and `integration` reported 108; **102 + 108 = 210**,
exactly Bhaskar's local unfiltered `-Mode full` total. The two trait-filtered jobs partition the suite
with no test lost and none double-run — an independent confirmation that
`EveryTestCaseCarriesExactlyOneTypeTrait` holds in practice, not just in assertion.

**Wall-clock shape.** Serial sum 583s; actual run ~7m30s. The three test jobs are fully parallel behind
`build-and-test`, so the critical path is 368s of build plus the slowest leg (89s). Build dominates at
**63% of serial cost** and is the only worthwhile optimisation target.

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
- D11 (Sir, 2026-08-26): **Make the advertised `.globalconfig` severities real** — convert the 59
  `option = value:severity` suffixes to `dotnet_diagnostic.IDExxxx.severity` form, and enable
  `GenerateDocumentationFile` so `IDE0005` can fire (see N17). **Scope reduced 2026-08-26:** `IDE0005`
  already fires — `SvSoft.MSBuild.CheckUnnecessaryUsings` supplies the doc-file workaround — so D11 is the
  **suffix conversion alone**, and carries the recordable fragility that removing that one package takes
  unused-usings enforcement dark repo-wide. Deferred because the conversion will surface a wave of
  new errors across the tree; that is a budget/appetite call and Sir chose "fix the docs now, backlog the
  rest". **Being priced (Sir, 2026-08-26):** *"i also need a build only run to check what would the errors
  be if i included the globalconfig and other dormant rulesets."* A build-only measurement is running in a
  disposable worktree — baseline, then the 59 suffix conversions and the 18 naming rules measured
  **separately**, since Sir may fund one and not the other and a combined total would hide that. Results to
  be recorded here as counts by rule ID with a tractability judgement per rule (mechanically auto-fixable /
  needs judgement / wrong for this codebase and better deleted than obeyed). **"Big sized work" is exactly
  the vagueness the measurement replaces.** **Two findings to fold in when D11 lands (Bhaskar, 2026-08-26):**
  (a) `.globalconfig:279` scopes the `type_parameters` symbol group to `applicable_kinds = namespace`
  rather than `type_parameter`, so the `T`-prefix rule is dead **twice over** — wrong severity *and* wrong
  symbol kind. A pure severity conversion would therefore still leave it inert; assume other symbol groups
  need the same audit. (b) **`IDE0005`'s severity is over-determined, not sourced from `.globalconfig`** —
  `src/_codeanalysis/codeanalysis.ruleset:5` carries an explicit `<Rule Id="IDE0005" Action="Error"/>`, and
  with the `.globalconfig` key forced to `none` it still errored. Enabling = SvSoft; severity = both. So
  the ruleset and the global config overlap in ways a conversion must not assume away.

### N19 — D11 priced: the conversion is worthless, and 5 of 7 projects run no IDE/CA analyzers (2026-08-26)

Sir: *"i also need a build only run to check what would the errors be if i included the globalconfig and
other dormant rulesets."* Measured by Bhaskar — six Release rebuilds in a disposable worktree at `36ae3f6`,
`.globalconfig` the only file mutated, nothing shipped. **Baseline is 0 errors / 0 warnings, so every number
below is a delta on zero.**

**Method note that matters for reading the numbers:** measurement runs set
`TreatWarningsAsErrors=false`. That is not a softened gate — under real gate semantics the first failing
project stops the graph (the confirmation run compiled only 3 of 7 before dying), so a "count the errors"
build **structurally under-reports**. Counting warnings compiles all seven. The confirmation run with the
gate on returned an identical breakdown, no compiler cap, no MSBuild truncation.

**1. The mechanism question is settled decisively — N17 stands.** Same codebase, same 59 option *values*,
only the key form differing: as `:severity` suffixes → **0** diagnostics; as `dotnet_diagnostic` keys →
**260**. Unlike the earlier single-rule `IDE0161` probe this cannot be explained by "the codebase has no
violations". Corroborated from the `csc` command lines: `/analyzerconfig:` lists the repo-root
`.globalconfig` for **all seven** projects — the config loads everywhere; the dormancy is entirely in the
key form.

**2. D11 as written costs nothing and buys nothing.** The 59 break down as **26 `:silent`, 30
`:suggestion`, 3 `:warning`**. A *faithful* conversion preserving each rule's stated severity yields
**0 new errors, 0 new warnings, exit 0**. Silent is hidden; suggestion is Info, which
`TreatWarningsAsErrors` never promotes; the three genuine warnings are clean on the merits (verified by
arming them explicitly). **56 of 59 lines document an intent, not an enforcement decision.** Blast radius
exists only if the severities are also *raised*.

**3. Raised to `warning`: 260 violations — but 211 come from seven lines.**

| Rule | Count | | Rule | Count |
|---|---:|---|---|---:|
| IDE0008 (use explicit type) | **164** | | IDE0065 (`using` placement) | 5 |
| IDE0022 (block body, method) | **34** | | IDE0060 (unused parameter) | 4 |
| IDE0058 (value never used) | **13** | | IDE0021 / IDE0032 | 3 each |
| IDE0046 (simplify `if`) | **11** | | IDE0023 / IDE0078 | 2 each |
| IDE0024 / IDE0031 | 5 each | | 9 assorted rules | 1 each |

Concentrated: 15 files carry 233 of 260, led by `ProcessRunner.cs` (35), `ToolChainService.cs` (30),
`ToolChainServiceExtensions.cs` (23), `PathExExtensions.cs` (22), `TestExecutor.cs` (18).

**The seven lines are template artefacts, not decisions:**
- `csharp_style_var_*` = false (all three) → **164 violations**. The config demands explicit types; the code
  uses `var` **470 times** repo-wide. *When a rule loses to the codebase 470-to-0, the rule is the anomaly.*
- `csharp_style_expression_bodied_methods/constructors/operators` = false → **47 violations**. The
  incoherence is the tell: the same config sets `accessors`, `indexers`, `lambdas` and `properties` to
  `true`, and those four produce **zero** violations. Four lines agree with the codebase, four disagree.

Flipping those seven leaves **49** violations: ~20 mechanically fix-all, ~29 needing judgement —
**IDE0058 (13)** (some are genuinely-discarded returns, some are swallowed results worth a look — do *not*
fix-all), **IDE0046/0045 (12)** (auto-fixable but frequently worse to read), **IDE0060 (4)** (deletion may
be illegal where an interface, delegate or VS SDK callback dictates the signature).

**4. The 18 naming rules are free.** Raised `suggestion` → `warning`: **0** violations. Re-run with
`dotnet_diagnostic.IDE1006.severity = warning` added, specifically to rule out the same inert-key-form
explanation: still **0**. The codebase already conforms. Cost to switch on: a config commit.

**5. The finding nobody asked for, which resizes everything — only 2 of 7 projects load the IDE/CA
analyzers at all.** Extracted from each `csc` invocation's `/analyzer:` arguments:

| Project | Format | CodeStyle (IDE) | NetAnalyzers (CA) | StyleCop |
|---|---|---|---|---|
| `RustAnalyzer.TestAdapter` | SDK-style | **yes** | **yes** | yes |
| `RustAnalyzer.Remote` | SDK-style | **yes** | **yes** | yes |
| `RustAnalyzer` (the VSIX) | legacy | no | no | yes |
| `RustAnalyzer.UnitTests` | legacy | no | no | yes |
| `RustAnalyzer.TestAdapter.UnitTests` | legacy | no | no | yes |
| `RustAnalyzer.Remote.UnitTests` | legacy | no | no | yes |
| `RustDevelopmentPack` | legacy | no | no | no |

The IDE code-style and NetAnalyzers assemblies ship with `Microsoft.NET.Sdk`. The five legacy-format
projects import `KS.Common.targets` and duly receive `.globalconfig`, `EnforceCodeStyleInBuild=true`,
`AnalysisLevel 6.0` and the ruleset — **and none of it can bite, because the analyzers implementing those
rules are never loaded.** Consequences:

- **`IDE0005 = error` fires in 2 projects, not 7.** N17's fragility line ("remove SvSoft and IDE0005 goes
  dark repo-wide") is *too generous* — it was never repo-wide. **The main VSIX project has never been
  checked for unused usings at build.** This does not disturb Dave's proof, which holds for the projects
  where the analyzer exists.
- **`CA*` is likewise 2-of-7**, qualifying the "default-on NetAnalyzers at `AnalysisLevel 6.0`" claim.
- **The same over-claim survives in two skill files** — `.github/skills/build-test.md:57-60` and
  `.github/skills/build-test-full.md:80-83` both state that the Release build *is* the analyzer/style gate
  and that "any compiler, analyzer, or StyleCop diagnostic fails `build`". True for StyleCop; **false for
  IDE and CA in 5 of 7 projects.** Found by Anders during the T10c design review, 2026-08-26. This is worse
  sited than the `copilot-instructions.md` bullet Ruling AC deleted: Dave and Bhaskar reload these files on
  *every gate run*, so a green gate is currently read as "the main VSIX project passed code-style" — which
  it has never once been checked for. **Ruling AC removed the honest-but-redundant statement while the
  actively-false one remained.** Candidate task under decision (c); no edit made, as it is Sir's to rule on.
- **The 260 is the cost for 2 of 7.** `RustAnalyzer` alone has 173 `var`-bearing lines against
  TestAdapter's 164; extrapolating TestAdapter's 259/164 = 1.58 multiplier across the 309 `var`-bearing
  lines in uncovered projects suggests **~490 further diagnostics, order-of-magnitude total near 750**.
  **Explicitly an extrapolation, not a measurement** — measuring it requires SDK-style conversion or adding
  analyzer `PackageReference`s, i.e. project-file changes, which the probe was forbidden.

**Three decisions returned to Sir, none taken by an agent** (golden rule #6): (a) whether to fund the real
legacy-project measurement; (b) whether the `var`/expression-bodied lines get flipped to match the codebase
or the codebase gets rewritten to match them — a product/style call; (c) whether the 2-of-7 coverage gap
becomes its own note and backlog item. **Presented 2026-08-26; Sir has not yet ruled.** Until he does, D11
stays deferred and nothing in the config changes. **The docs half is not deferred, but Ruling AC changed its shape** — T10c *deletes* the enforcement
  claim rather than correcting it, so there is no prose left to drift. The Ruling W principle that motivated
  fixing it (a false statement in the file every agent reloads first is its own hazard) is satisfied more
  cheaply by having no statement at all.
  Carries three cheap Bhaskar probes when it lands: (1) one rule, two builds — suffix-only vs
  `dotnet_diagnostic` — to pin the suffix claim on *this* toolchain; (2) add an unused `using`, build
  Release, then repeat with `GenerateDocumentationFile=true`, to settle IDE0005; (3) whether
  `MultipleGlobalAnalyzerKeys` is promoted by `TreatWarningsAsErrors` (needed only if anyone revives
  option B or C).

## Notes & Decisions

### Sir's rulings applied

| Ruling | Decision | Applied in |
|--------|----------|-----------|
| A | "switch to xUnit entirely, away from VSTest, unless there's a good reason. update the cdp.yml appropriately." | T2, T3, T6, portfolio; **split verdict** — see N3, N4 |
| B | "for the deprecated gh actions, use your alternatives as required" / "yeah lets switch to shell completely for any deprecated actions." | T6, N5 |
| C | "these are logical steps… they dont have to [be] separate, do not necessarily need a ps1 wrapper… as long as they happen." | T3, N2 — **supersedes the earlier N2** |
| D | VS 2026 host availability: **"unsure."** | ~~T14~~, T33, T34 remain `[HUMAN]`; S2 ships no widened manifest on assumption. **Partly overtaken by T5's byproduct (2026-08-26):** `windows-2025-vs2026` is a GA hosted runner carrying VS Enterprise 2026 (major 18), available to this repo because it is public. A VS 2026 host no longer requires Sir to own the hardware. See N14. **Largely spent as of 2026-08-26:** N18 splits T14 into T14a/T14b/T14d (automatable) and **T14c, which is all of this ruling that survives** — the genuinely interactive half CI cannot do |
| E | "3 i need to support both 2022 and 2026" (2026-08-25), restated verbatim 2026-08-26: **"this extension needs to support both 2022 and 2026"** | Supporting **both** VS 2022 17.12+ and VS 2026 is a hard requirement, not a preference. **T11 has now verified the upper-bound finding (N18): one VSIX covers both, and the current `[17.0,18.0)` range is Microsoft's own worked example of the both-hosts case.** T13 collapses to "leave it alone and record why", and must not regress 2022. Two artifacts are an escalation, not a design choice |
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
| P | **"update the rust-analyser package along with the r1 and r2 fixes"** (2026-08-25) | Overrides the earlier "binary after the fixes" sequencing: the rust-analyzer bump ships in the same commit as B-R1 and B-R2. Landed in `bf24efe` |
| Q | **`Invoke-Tests.ps1` is exempt from T4** (2026-08-26) | Narrows Ruling C. The other four scripts still die. Ruling C removed a *gate abstraction layer*; `build` genuinely collapses to one line and `lint`/`format` genuinely vanish, but `test:*` cannot — it carries N3 (one process for `RUSTUP_TOOLCHAIN`), Ruling K/N9 (zip-only acceptance, no fallback), Ruling M's four modes, B-R1's `-parallel assemblies`, B-R2's zero-test guard, and "acceptance runs even when the assembly leg failed". De-scripting it would relocate ~100 lines of policy into five undiffable string literals, not delete it. Sir had already narrowed Ruling C himself: Ruling M is written as *"`Invoke-Tests.ps1` takes unit, integration, acceptance, full"*, and T6's S-a and T6b each mandate a new single-purpose script. **The operative rule is "no gate *orchestration* wrappers; single-purpose readers and assertions are fine."** Reconciles N5's blanket "no new `.ps1`" and execution rule #4 |
| R | **F1 gets no guard** (2026-08-26) | Sir: *"we dont need to do this."* The three-way coupling between `Constants.RlsLatestInPackageVersion` and the two `<Link>` folder names stays unguarded. Residual risk stated and accepted: `RlsReleaseTests` enforces a **30-day** freshness ceiling, so the bump is now monthly rather than the historical 441 days, and that test **cannot** detect the break — it compares the constant to upstream, so a constant-only bump goes green while the shipped extension can no longer resolve its binary |
| S | **"pull out Build, Stamp VSIX version, and Install pinned Rust nightly as scripts that are also used by the loop gates"** (2026-08-26) | **One implementation per step, shared by CI and the local gates.** Completes the arc Rulings C and Q started: Ruling C removed *duplicated gate abstraction*, Ruling Q kept the one script that was a genuine policy statement, and Ruling S now says the converse — any step that must behave identically in CI and locally lives in exactly one script under `.github/scripts`, invoked by both. Supersedes T4's deletion of `Invoke-Build.ps1`. See T4b. **Sir's #3 also resolves the CI-nightly blocker** described below, and it does so better than the `-ToolchainExternallyProvisioned` parameter I was about to recommend: rather than teaching the validator to accept an unprovenanced toolchain, CI provisions the nightly through the *same* installer the assistant uses, so there is one install path and one provenance shape instead of two parallel ones |

| T | **"this doesn't have to be secure. what is the least required change"** (2026-08-26) | Settles the T4b(c) escalation. The assistant-only bootstrap rule is a **guardrail against a casual or accidental install, not a security boundary** — it never was one, since any sub-agent could already run `Initialize-AssistantSession.ps1 -AssistantStartup` and mint itself a token; nothing verifies the caller *is* the assistant. What the handshake genuinely prevents is role spoofing via a caller-supplied string, and that stays intact. So **no exclusion guard, no discriminator, no new mechanism** — Dave's `-Authority workflow` refuses-when-agent-session-present proposal is rejected as over-engineering, not as wrong. Take the smallest change that makes CI's provenance exist. See the T4b(c) row for the resulting four touches |

| U | **"delete it"** — the session/token layer goes (2026-08-26) | Sir's answer to *"why is this required? sessionstate.psm1"*. The follow-on from Ruling T: once the assistant-only rule is a guardrail rather than a boundary, the session layer does no work the other checks don't already do. `Get-RustNightlyManifest` runs five checks; **four are session-independent and cover every real failure** — manifest exists (forbids stable fallback), `Toolchain` equals the pinned channel (catches a pin move), `RepositoryRoot` matches (catches a different checkout), and `Enable-SessionRustNightly` re-probes live `rustc`/`cargo` against the manifest (catches an uninstalled or altered toolchain). The session layer's only unique contribution is forcing one `rustup toolchain install` per session — **and `Get-PinnedRustNightlyChannel` enforces `^nightly-\d{4}-\d{2}-\d{2}$`, a dated nightly, which rustup treats as immutable, so that reinstall is a no-op.** It guards against drift that cannot occur. The hazard genuinely worth preventing — a sub-agent silently self-healing or falling back to stable — is prevented by check 1 plus the sub-agent scripts containing no install code, not by the session hash. The rule survives as prose in `.github/copilot-instructions.md`, which is an honest statement of what it always was. See T4c |

| V | **"`ScriptModuleImportTests.cs` is over engineering. its not necessary"** (2026-08-26) | Reverses **T2b** and declines the command-resolution test Dave proposed after the export bug. Delete the file and its `<Compile Include>` at `RustAnalyzer.UnitTests.csproj:55`. **The justification is Ruling U, and it is verified, not assumed:** T2b enforced "no `Import-Module -Force` inside a `.psm1`", a trap that requires a nested `.psm1` → `.psm1` import to exist. After Ruling U there are **zero `Import-Module` statements in any `.psm1`** — `RustNightly.psm1:5` only dot-sources `Get-PinnedRustNightlyChannel.ps1`, and `VisualStudio.psm1` imports nothing at all. The module stack that created the hazard is the module stack Ruling U deleted, so the test now guards a shape the tree cannot express. Same reasoning declines the resolution test: `.github/scripts` is stable now that de-scripting is done, and the two live breaks it would have caught were both *artefacts of the deletion cascade itself*, not a standing risk. **Consistent with the slice's theme** — S1 removes invented machinery, and a test guarding a deleted hazard is exactly that. Residual risk accepted and stated: the scripts have no compiler behind them, so a future symbol or import error in `.github/scripts` surfaces at first execution rather than at `test:quick` |

| W | **"proceed. ignore this"** (2026-08-26) | Overrides Bhaskar's second blocking finding. `docs/design.md:9` calls the product a "Visual Studio **2022/2026** Open Folder extension" while `:5` forbids inferring planned VS 2026 behaviour today, `:14` declares installation range `[17.0,18.0)` which excludes major 18, and `:20` states outright that "Visual Studio 2026 support is not current behavior." Bhaskar was **correct to stop on it** — `design.md` is the file every agent reloads first under golden rule #1, so a false claim there propagates. Sir has read the contradiction and accepted it as-is. **Recorded so no later agent re-raises it as a defect, and so nobody "corrects" `:14`/`:20` to match `:9`** — the range and the runtime rejection are the truth about today's behaviour; only the product line is written in the wrong tense. T5 and N14 make the destination genuinely reachable (VS 2026 runners exist, this repo is public), so S2 closes the gap by making `:9` true rather than by making it smaller. Bhaskar's first finding — the stale VSTest claim at `.github/copilot-instructions.md:87` — was **not** overridden and was fixed |

| X | **"make the least possible changes to remove dangling references and bring back anything critical missed or nuked"** (2026-08-26) | Ratifies Sir's governance consolidation and repairs its fallout. **What Sir changed:** deleted `.github/agent-roles/conductor.md` and folded it into `.github/agents/JARVIS.md` (every agent is now one self-contained file — the binder/role-body split is gone); reduced `AGENTS.md` to a one-line redirect; cut the framework-adoption scaffolding from `.github/copilot-instructions.md`, which also removed golden rule #8 (writing tests → `meta-design.md`, renumbering #9–#11 → #8–#10) and the **entire Project profile**. **Two things the profile deletion broke, both restored:** (1) **Preflight Gate 1 reads `Project profile → Pack`** — with the profile gone the gate evaluated to "Pack unset" and the loop could not legally start; (2) **golden rule #5 delegates the never-edit list to the Project profile**, so deleting it silently reopened the `src/external/` hole **T7(a) closed in `71e9d90`** — an agent could hand-edit the packaged 38 MB `rust-analyzer.exe` and violate nothing. Restored the nine profile bullets that are actually referenced (Pack, Persona, Trunk, Addressing, generated + acquired artifacts, liveness, conventions, name); **deliberately not restored** — *Framework version adopted*, *Model profile*, *Build/test skills*, *CI/CD pipeline*: nothing dangles on them and `cdp.yml` is the CI SSOT. **Stale text removed:** the fold-in carried `conductor.md`'s pre-Ruling-U bootstrap paragraph ("assistant owner/phase/token-hash provenance", "plaintext authorization token") into JARVIS.md, where it became *active assistant instruction* rather than a dormant role body — Ruling U's cascade had missed `conductor.md`. Repointed the `agent-roles` references in `skills/retrospective.md`, and fixed JARVIS.md's self-reference to "the binder" (it *is* the binder now) and its dangling *Roles & responsibilities* cross-ref. **Sir then deleted `.github/personas/` too**, collapsing the binder/role-body/overlay triad entirely — `.github/agents/<name>.md` is now the single home for every agent. **The ASCII banner was the one casualty**: it lived only in the overlay and is JARVIS's mandated first action, so it is restored **inline** in JARVIS.md's *JARVIS etiquette* section (no new file, per Sir's constraint); the voice/etiquette prose had already been folded in by Sir. **Left alone: `.github/skills/agentify.md`** — it targets `.github/agent-roles/`, `.github/personas/` *and* `.github/agent-templates/`, and the last has never existed in this checkout, so it was already a source-side skill describing the framework repo, not this consumer; realigning it to the one-file layout is a framework change, not a dangling-reference fix. Feature files 001 and 002 keep their `agent-roles`/token references — they are historical logs, corrected by superseding notes, never rewritten |

| Y | **~~"fix by using msbuild properties referring to the current file"~~ — WITHDRAWN by Sir the same day (2026-08-26)** | Sir ruled the N16 remedy, then withdrew it when Dave declined to implement it and produced disconfirming evidence, independently verified by Anders. **The ruling was technically impeccable for the problem as we described it; the problem as we described it did not exist** — N16 was our note, so the error is ours. Applying it would have emitted 240 × `MultipleGlobalAnalyzerKeys` and unset every key in the file while the build stayed green. **Confirmed empirically by Bhaskar on the final tree: 239 `MultipleGlobalAnalyzerKeys`,
all emitted as *warnings*, `EXIT=0`, with `TreatWarningsAsErrors=true` verified via `-getProperty`.** So the
withdrawn fix was **silent sabotage, not a loud break** — Anders' "no `NotConfigurable` tag ⇒ `/warnaserror`
should promote it" reading is refuted: the diagnostic is emitted by `CSC` with no location and a
non-numeric ID during analyzer-config *setup*, outside the compilation-diagnostic pipeline that
`TreatWarningsAsErrors` filters. **239, not 240** — the 240th line is the `is_global = true` header
directive, which is not a config key and cannot conflict. **Replaced by Ruling Z.** Recorded rather than deleted so the withdrawal is visible: the failure here was an agent handing the human a confident conclusion built on one unchecked inference |
| Z | **Option D — delete the include, leave a comment** (2026-08-26) | Delete `src/KS.Common.targets:54-56` outright: SDK auto-discovery already supplies the root `.globalconfig`, so the line is **dead, not broken**. Rejected alternatives: `DiscoverGlobalAnalyzerConfigFiles=false` (trades one invisible magic for another, and silently breaks any future `.globalconfig` elsewhere in the tree), and `Remove`/`Condition` dedupe (permanent MSBuild complexity to preserve a line whose entire content is "do what the SDK already did"). The comment is the whole delta from a bare deletion and it exists to stop the next agent re-deriving this session and re-proposing Ruling Y. See T10c, N17 |
| AA | **"fix the docs only" on dormant enforcement** (2026-08-26) | Correct `.github/copilot-instructions.md` now to describe what actually enforces what; **backlog** converting the 59 `:severity` suffixes and enabling `GenerateDocumentationFile` (D11), because that surfaces a wave of new errors across the tree and the appetite for that churn is Sir's call. The docs half is not deferrable on the Ruling W principle — a false claim in the file every agent reloads first is its own hazard, and we already knowingly swallowed one |
| AB | **The VS 2026 CI leg gates on PR; no report-only mode** (2026-08-26) | Answers the question N14 explicitly reserved for Sir. A report-only leg is `continue-on-error` wearing a different hat, and execution rule #4 forbids it; the whole S1 thesis is that soft failure is how a stale test hid for 441 days. If a leg is too flaky to gate, the correct response is **don't add it yet** — not add it soft. Accepted risk, stated: `windows-2025-vs2026` is public-repo-only, so if this repository ever went private a required check on that label would never report and would deadlock every PR (N10, new axis). See T14b, N18 |
| AC | **"i dont want '**Language-specific conventions:**' called out in the copilot instructions. build failures should discover all of it. language conventions are already enforced by build, editorconfig, analysers etc."** (2026-08-26) | **Supersedes the documentation half of Ruling AA: the bullet is deleted, not corrected.** Dave's replacement text was *accurate* — Sir's point is that an accurate prose copy of what the build already enforces is still a liability, because it drifts and because maintaining it costs exactly the archaeology N17 records. The build, `.editorconfig` and the analyzers are both the enforcement **and** the discovery mechanism; a build failure names the rule better than a profile bullet can. The taxonomy of what bites versus what is decorative survives in **N17**, which is where a historical finding belongs — not in the file every agent reloads on every invocation. **One cascade, handled in T10c:** `.github/agents/dave.md:10` said language-specific rules "live in the Project profile", which the deletion would have turned into a dangling pointer — the Ruling X failure mode exactly. The least-privilege and `internal`-needs-flagging guidance is a **behavioural instruction to Dave, enforced by no analyzer**, so it survives, self-contained in his own agent file. **Nothing was migrated** — not to `design.md`, not to a new file. Golden rule #5 and Preflight Gate 1 both read *other* profile bullets and are unaffected; Bhaskar re-confirms that rather than assuming it |
| AD | **"remove the comment."** (2026-08-26) | **Supersedes Ruling Z's "delete plus comment" and Anders' review points (a)1-3.** T10c's deletion of the `GlobalAnalyzerConfigFiles` ItemGroup leaves **nothing in its place** — no explanatory comment at the site, none relocated beside the analyzer `PropertyGroup`. Arrived at over three iterations: fourteen lines → five (compressed on Sir's "super terse" edit, preserving Bhaskar's *inert, not misresolved* distinction) → **zero**. Sir saw the five-line version and ruled against it. **Context:** he hand-edited `.github/agents/dave.md` item 2 three times in twenty minutes to reach *"Err on the side of not writing comments. The intent should be readable from the code. Exception is some super non-obvious case. If comments need to be written, they need to be super terse."* — settled policy, not a one-off. **The recurrence guard moves, it does not vanish:** N17 is the record, and the T10c commit message carries the refutation so that `git log -S GlobalAnalyzerConfigFiles` and `git blame` surface it for precisely the reader who goes looking. Arguably its proper home all along — a comment explaining an *absence* is an odd artefact, and this one had already proved it by being wrong in its first draft. **Residual risk, accepted knowingly:** a future agent editing `KS.Common.targets` sees no in-file warning; the guard now depends on that agent consulting history or the feature file rather than tripping over the answer |

### N16 — ~~the `.globalconfig` include points one directory short~~ **SUPERSEDED by N17 (2026-08-26)**

> **Do not act on this note.** Its MSBuild reasoning is correct and its conclusion is false. The remedy
> it proposed — and which Sir ruled for on that basis — would have unset all 240 keys in the file. The
> note is retained only so the record shows how the error was made. **Read N17 instead.**

`src/KS.Common.targets:55` declares `<GlobalAnalyzerConfigFiles Include="..\.globalconfig" />`. MSBuild
resolves relative item `Include` paths against the **project** directory, not the directory of the
imported `.targets` file — that is what `$(MSBuildThisFileDirectory)` exists to work around. So from
`src/RustAnalyzer/RustAnalyzer.csproj` the path resolves to `src/.globalconfig`, and **the only
`.globalconfig` in the repository is at the root**. The include has been pointing one directory short.

**Why this matters beyond a stray path: it undercuts Ruling N.** The lint gate was deleted on Sir's
statement that *"lint is part of release build"*, and `.github/copilot-instructions.md` claims style and
quality are "enforced at build via StyleCop.Analyzers, … `.globalconfig`, and
`_codeanalysis/codeanalysis.ruleset`". If the global config never loads, the severity mapping in it —
including the `IDE0005` unused-usings-are-errors rule the profile advertises — is not being applied, and
part of what justified deleting the gate is not actually running.

**Not evidence either way yet:** the build reports 0 non-`MSB3277` warnings, which is equally consistent
with "config applied and the code is clean" and "config never loaded". Deciding it needs an experiment —
introduce a known violation and observe whether it errors — which is verification work, not conductor
work.

**Deliberately not fixed in S1.** It is pre-existing, unrelated to de-scripting, and Sir's instruction
was to close S1. The fix is one of two choices and therefore a decision: change the include to
`$(MSBuildThisFileDirectory)..\.globalconfig`, or move `.globalconfig` into `src/`. Carry to S2 and pair
it with re-examining Ruling N's premise.

### N17 — the `.globalconfig` loads; the dormancy is real but elsewhere (2026-08-26, supersedes N16)

Sir ruled on N16: *"fix by using msbuild properties referring to the current file"* — i.e.
`$(MSBuildThisFileDirectory)..\.globalconfig`. **Dave declined to implement it and brought evidence;
Anders independently verified every load-bearing claim; the ruling was withdrawn.** The instrument Sir
named was exactly right for the problem as we described it. The problem as we described it did not exist.
**N16 was our note, so the correction is ours to own.**

**Three findings, all verified without a build:**

1. **The root `.globalconfig` has always loaded.** `Microsoft.Managed.Core.targets:131-140` appends
   SDK-discovered configs *into `GlobalAnalyzerConfigFiles` itself* and then filters the **whole item** —
   project-declared entries included — through `->Exists()`. So `src\.globalconfig` is silently dropped:
   it is not a broken include producing wrong behaviour, it is an include producing **no** behaviour. The
   repo-root file arrives on every project by discovery, because every `Compile` item sits beneath it.
   N16's MSBuild reasoning was right; it then took "the path is wrong" to mean "the config never loads"
   without checking whether anything else supplied it. One inference too many. N16 itself named the honest
   tell — a 0-warning build was consistent with both its stories, *and with a third*, which is the true one.
2. **The ordered fix was actively harmful.** With the property applied, the same physical file reaches
   `csc` twice under different item identities (the `Distinct()` at `:138` applies to `_AllDirectoriesAbove`
   *before* the combine, so MSBuild does not dedupe). Roslyn's `AnalyzerConfigSet.MergeSection` then
   removes conflicting keys **with no value comparison** — identical values from two configs at equal
   `global_level` still conflict, and both files being named `.globalconfig` makes them `global_level = 100`
   by construction. Result: `warning MultipleGlobalAnalyzerKeys … It has been unset.`, **one per key, 240
   of them**, and the build **exits 0** because these setup-phase warnings are not promoted by
   `TreatWarningsAsErrors`. Behaviourally confirmed: an `IDE0161` probe that errored pre-fix produced no
   error post-fix. A change made in the name of enforcement that deletes the enforcement and leaves no red
   — precisely the failure mode N16 existed to close. Anders' key count (240 total, 59 severity-suffixed)
   matches Dave's observed warning count to the unit.
3. **Ruling N stands, untouched.** The deleted `lint` gate ran the *same* Release compile as `build`, with
   the same config, ruleset and analyzers. A duplicate of X cannot enforce more than X. Even had N16's
   premise been true it would have argued that the *build* gate is weaker than advertised — never that
   deleting its clone lost anything. **N16 coupled two things that were never coupled.**

**But N16's wider worry survives, sharper and for different reasons.** The config loads fine and roughly a
quarter of it is dormant anyway:

- **59 `option = value:severity` suffixes are not driving build severity.** The option *values* are read
  (proven: `IDE0161` fired on block-scoped code only once `dotnet_diagnostic.IDE0161.severity = error` was
  added, while the option line still read `file_scoped:warning` — so the value was honoured and the suffix
  was not). Microsoft documents this family of behaviour for .NET 8 and earlier. **Not established:** that
  the inertness is specific to *global* configs rather than the toolchain or `AnalysisLevel 6.0`; two probes
  cannot separate those, and this repo cannot A/B it locally because the root `.editorconfig` carries **no**
  code-style options at all — every style option lives in the global config and nowhere else.
- **18 `dotnet_naming_rule.*.severity = suggestion` entries are dead at build even if fully honoured** —
  suggestion is Info, and Info is never promoted by `warnaserror`. The naming layer is IDE guidance, full stop.
- **`IDE0005` fires today, but only by a third-party workaround.** *Anders concluded it was "very likely
  dormant" — Dave refuted him, and the refutation holds.* IDE0005 does require XML documentation comments,
  and `GenerateDocumentationFile`/`DocumentationFile` appear in **no** `.csproj`/`.targets`/`.props` under
  `src/` — Anders' mechanism was right. But `SvSoft.MSBuild.CheckUnnecessaryUsings` exists to close exactly
  that hole: its `__TriggerUnnecessaryUsingsCheck` target injects a dummy `DocFileItem` before `CoreCompile`
  when `GenerateDocumentationFile != true`, and the artefact is on disk in all six projects. Dave's direct
  build evidence: an unused `using System.Text;` produced `error IDE0005 … EXIT=1`. **The enabling
  mechanism is SvSoft; the severity is ours** — SvSoft's own config is `global_level = 10` and sets only
  `warning`, losing to the repo file's default 100. **Recordable fragility: remove that one package and
  IDE0005 goes dark repo-wide.** Consequence for D11 — it need not carry `GenerateDocumentationFile`.
- **File-scoped namespaces**, previously advertised as "(enforced)", are uniform by **habit, not by gate**.
- **What genuinely bites (Release only):** StyleCop `SA*` (default Warning → promoted by
  `<IncludeAll Action="Error"/>` + `TreatWarningsAsErrors`), incl. SA1028 trailing whitespace; `SX1309`
  (`_` private-field prefix) and `SX1101` (no `this.`) at explicit `Action="Error"` — **so the profile's
  `_camelCase` claim is true, but enforced by StyleCop, not by the naming rules a reader would look at**;
  enabled-by-default `CA*` at `AnalysisLevel 6.0`; VS SDK/Threading, xunit and FluentAssertions analyzers;
  ordinary compiler warnings; the two `dotnet_diagnostic … = none` suppressions; and `SvSoft`.
  `StrictCodeAnalysisEnabled` is false outside Release, which is why *"the Release build is the lint"* is
  the correct framing rather than *"the build is the lint"*.

**Sir's rulings (2026-08-26), both applied:** (a) **Option D** — delete `src/KS.Common.targets:54-56` and
leave a comment recording why nothing is there, so the next agent does not re-derive this session and
re-propose the harmful fix; (b) **fix the docs now, backlog the rest** — correct the enforcement claim in
`.github/copilot-instructions.md`; do **not** convert the 59 suffixes or enable `GenerateDocumentationFile`
in this task. Both are T10c. The conversion work is D11.

### N18 — T11 answered: one VSIX covers both hosts, verified by the vendor (2026-08-26)

Sir restated the constraint today, verbatim: *"this extension needs to support both 2022 and 2026."* That
confirms Ruling E rather than changing it. **T11 is answered, and by the strongest available form of
evidence — Microsoft documenting this repository's exact expression as the both-hosts case.**

Microsoft Learn, **"Extension compatibility model for Visual Studio"** (`visualstudio` 2026 moniker,
`ms.date 2026-01-09`): if a VSIX works in VS 2022, no changes are required for VS 2026, which *supports
API version 17.x*, *evaluates compatibility using **only the lower bound*** of the installation-target
range, and *ignores the upper bound*. Its worked example is byte-for-byte the range at
`source.extension.vsixmanifest:18,21,24`:

```xml
<InstallationTarget Id="Microsoft.VisualStudio.Community" Version="[17.0,18.0)" />
```

Corroborated by **"Upgrade a Visual Studio extension"** (`ms.date 2025-11-05`), whose *only* named manifest
breaking change is removal of the `IntegratedShell` install target — this repo declares `Community`/`Pro`/
`Enterprise` only, so it does not apply. **Verified no-op.** The standing finding the feature file refused
to carry forward unverified is now confirmed and may be relied on.

**One artifact, emphatically — two would be a mistake, and the cost is not build time.** The 2019→2022
precedent does not transfer: that split existed because of a hard ABI break (32→64-bit and a distinct set
of reference assemblies). No equivalent break exists 17→18; VS 2026 supports 17.x, which is exactly the
surface this repo binds (`Microsoft.VisualStudio.SDK 17.11.40262`, `VS.Threading 17.11.20`,
`Community.VisualStudio.Toolkit.17 17.0.522`, `src/external/vs.17.11/*`). Two artifacts would force **two
`Identity Id`s** — Microsoft is explicit that per-host VSIXes must differ in `Identity` — meaning two
Marketplace listings, split install counts and ratings, no in-place upgrade for existing 2022 users, and
live breakage in this codebase: `RustAnalyzerPackage.cs:112` indexes `allExtensionIds[Vsix.Id]` and the
incompatible-extension table at `:129-133` is Id-keyed; `RustDevelopmentPack` would have to fork too.
**Two artifacts are therefore an escalation to Sir triggered only by an experimental failure — not a
design choice available to us.**

**The real S2 risk is not packaging; it is that a green build proves nothing about VS 2026.** The extension
compiles against frozen 17.11 assemblies checked into `src/external/vs.17.11`. On an 18 host those bind
through platform unification, and a mismatch surfaces as a runtime **`TypeLoadException` / MEF composition
error**, never a compiler error. **Any evidence of the form "it builds" must be rejected outright.** Hence
the T14a/T14b/T14d proof legs, in increasing order of signal.

**Three rules for those legs, written into the tasks rather than into prose nobody executes:**

1. **Every new leg must be demonstrated red once, by construction, before its green is accepted, with the
   red run id recorded beside the green one.** Point the 18 acceptance leg at an adapter zip missing
   `KS.RustAnalyzer.TestAdapter.dll`, or at a manifest with `[19.0,)`. **A green leg with no proven red is
   not evidence** — the repo already holds this discipline (Ruling M, N11).
2. **No assertion a zero can satisfy** (the B-R2 lesson) and **no filter that excludes the case that
   matters** (the `scope=External`/`RlsReleaseTests` lesson — the most expensive mistake in this feature's
   history).
3. **The resolved host must be asserted and recorded, not assumed** — log the vswhere-resolved instance
   path and `installationVersion` for the 18 leg into this file's evidence line, or "passed on 2026" is
   unfalsifiable.

**Two facts behind Sir's gate/report ruling.** (a) `windows-2025-vs2026` is **public-repo-only**; if
`kitamstudios/rust-analyzer.vs` ever went private, a required check on that label would never report and
would block every PR indefinitely — **N10's deadlock on a new axis.** (b) Cost is small: the 18 leg reuses
the existing build artifact and adds one parallel job (slowest test leg measured at 89s against a 368s
build), so it should not move the critical path. **Sir ruled: gate, from the first commit.**

**Two items flagged but not moved, both outside S2:** VSIX v3 / SDK-style extension projects are an
*authoring* modernization, **S4 cohort 2 (T27)**, not a 2026 compatibility requirement — do not let it leak
in. And S7's T44 revisit trigger is *"Microsoft announcing deprecation or reduced support for the 17.x
VSSDK APIs"*: Microsoft shipping a 2026 compatibility model headlined *"Supports API version 17.x"* is a
**dated data point that the trigger has not fired**, and T43/T44 should record it with the citation.
`ProductArchitecture amd64` stays correct; VS 2026 also ships ARM64 (`windows-11-vs2026-arm`) and we declare
no arm64 target — correct under **D9**, but recorded as a knowing omission rather than an oversight.

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

**EVIDENCE CAPTURED (T10, 2026-08-26).** Run `33000124688`, dispatched deliberately from the feature
branch to exercise exactly this hole: `event: workflow_dispatch`, `headBranch:
vibe/002-hardening-and-vs2026`, jobs `config`/`build-and-test`/`unit`/`integration`/`acceptance` all
**success**, `publish` **skipped**. The ref assertion holds under the one trigger that could bypass it,
so the path from a `vibe/*` dispatch to a Marketplace publish is closed and proven, not merely
reasoned about. `cdp.yml:10-11` also carries the comment explaining why no `branches:` key appears
under `workflow_dispatch`, so the ignored-key trap cannot be reintroduced by someone "fixing" the
omission.

### N10 — Branch protection and the check-rename deadlock

**CORRECTED 2026-08-26 — master IS protected, and the predicted deadlock has actually happened.**

The paragraph below recorded master as unprotected on 2026-08-25. That was **wrong**, and it was my
error: I asserted it rather than re-reading it, and carried it forward across several turns. Read
directly from `gh api repos/kitamstudios/rust-analyzer.vs/branches/master/protection` on 2026-08-26:

- `required_status_checks.contexts` = **`["Build, Test & Deploy"]`** — the pre-T6 job name
- `strict` = true, `enforce_admins` = **true**, `required_conversation_resolution` = true
- `required_approving_review_count` = 0, `allow_force_pushes` = false, `allow_deletions` = false

So golden rule #3 *is* mechanically enforced, and the hazard this note predicted is now live rather
than hypothetical. After `71e9d90`, PR #71 reports `mergeStateStatus: BLOCKED` — the six new jobs all
report (`config`, `build-and-test`, `unit`, `integration`, `acceptance` succeeded; `publish` skipped),
but **not one of them is named `Build, Test & Deploy`**, so the required check sits permanently
"Expected — waiting for status to be reported". `enforce_admins: true` means it cannot be clicked past.

**Resolution: Sir owns the setting change** (2026-08-26, `checks=mine`, `strict` retained). The
conductor does not modify branch protection — it changes the merge gate for every contributor, not just
this PR. Recommendation on record was the **five** checks that actually execute on a pull request —
`config`, `build-and-test`, `unit`, `integration`, `acceptance` — deliberately excluding `publish`,
which never runs on a PR and would make the gate depend on GitHub treating a skipped check as
satisfying a requirement. That is true today but is a subtlety carrying no benefit, since `publish`
only executes *after* the merge it would be gating.

**Superseded original assessment, retained for the record:**

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
| `test:quick` | `pwsh -NoLogo -NoProfile -NonInteractive -File .\.github\scripts\Invoke-Tests.ps1 -Mode unit` |
| `test:full` | `pwsh -NoLogo -NoProfile -NonInteractive -File .\.github\scripts\Invoke-Tests.ps1 -Mode full` |

**The two `test:*` rows above were rewritten on 2026-08-26 under Ruling Q.** They previously carried
inlined `xunit.console.exe` command lines, which Anders showed contained three regressions — the row
read as an instruction, so implementing T3 verbatim would have silently undone two of Sir's own rulings:

1. **B-R1 would reopen.** The old value said `-parallel all`. N12 changed this to `-parallel assemblies`
   (`Invoke-Tests.ps1:63`, with the *why* in the comment above it); the N2 row was never updated.
   `-parallel all` overrides an assembly's own `CollectionBehavior` and restores the ~37 % cold-tree flake.
2. **B-R2 would reopen.** No zero-test guard. xUnit v2's console runner has no `--minimum-expected-tests`
   equivalent and the xUnit team declined to add one, so the guard cannot be a runner flag here.
3. **Ruling L would reverse.** The old value hardcoded the three assembly names. A literal list *is* the
   registration step Ruling L abolished; the script globs `KS.*Tests.dll` instead.

Two further leaks in the old values: they dropped `RUSTANALYZER_TELEMETRY_DISABLED=1`
(`Invoke-Tests.ps1:31`) and wrote `-xml .\_built\quick.xml`, which does not match `cdp.yml`'s
`TestResults/*.xml` upload path.

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

**Superseded by Ruling X (2026-08-26).** Sir folded `conductor.md` into `.github/agents/JARVIS.md` and
deleted `.github/agent-roles/` outright. Every agent is now self-contained in one file, so the dispatch
rule collapses to: **always cite `.github/agents/<agent>.md`** — there is no longer a second location to
get wrong.

### N15 — Ruling Q broke T4's premise, and Ruling S repairs it (2026-08-26)

Found by Dave while implementing T6; he stopped rather than working around it, which was correct.

**The collision.** T4's justification for deleting `Initialize-CISession.ps1` and `CIProvenance.psm1`
reads: *"CI no longer needs provenance because it never runs the assistant bootstrap — the workflow
sets `RUSTUP_TOOLCHAIN` directly."* That was written when T3 was going to inline the xUnit runner into
YAML, so CI would never call the gate script. **Ruling Q kept `Invoke-Tests.ps1`**, and CI now calls it.
The script calls `Enable-SessionRustNightly` for every mode except `unit` (`Invoke-Tests.ps1:24-28`),
which requires the token-backed assistant provenance under `%LOCALAPPDATA%\ravsq\`. So the CI
`integration` and `acceptance` jobs would fail. T4's premise died the moment we saved the script, and
nobody noticed at the time.

**Options considered and rejected.** (b) Branching on `$env:GITHUB_ACTIONS` inside the script is
environment-sniffing, and is precisely the implicit branch T4 is *removing* from
`Get-RustNightlyHandoffMessage`. (c) Inlining the runner in YAML reopens all three of N2's regressions.
(d) Restoring the deleted CI provenance keeps two parallel provenance mechanisms whose own design notes
conceded the CI half *"is not an attestation and can be reproduced locally"* — ceremony, not a boundary.
I was about to recommend (a), an explicit `-ToolchainExternallyProvisioned` parameter that asserts the
pinned channel is installed but skips the provenance requirement.

**Sir overrode all four with Ruling S, and it is a better answer.** Extracting the nightly install into
one script used by *both* CI and the assistant means CI does not need to be taught to accept an
unprovenanced toolchain — it provisions through the same path, so the provenance simply exists. One
install path, one provenance shape, two authorities. The parameter I was going to propose would have
added a second *kind* of acceptable state; Ruling S removes the need for one.

**The invariant that must survive T4b(c)** — as settled by **Ruling T**. Dave escalated correctly: making
CI's provenance exist means `Get-RustNightlyManifest` must accept a `workflow`-owned manifest with no
token, after which a sub-agent could mint one locally. He proposed refusing `-Authority workflow`
whenever an agent-session variable is present. Sir's ruling: *"this doesn't have to be secure."*

The reasoning that makes that safe to accept: the assistant-only rule was **never** a security boundary.
Any sub-agent could already run `Initialize-AssistantSession.ps1 -AssistantStartup` and mint a token —
nothing verifies the caller *is* the assistant. What the handshake genuinely prevents is role spoofing
via a caller-supplied string, and Ruling T leaves that untouched. What the rule genuinely buys is that a
casual or accidental install is impossible, and an explicit `-Authority workflow` is not casual. Both
routes remain visible, deliberate violations rather than silent fallbacks — which is the property worth
keeping. A real boundary is a larger piece of work than S1 and no part of it.

### N14 — T5's byproduct: VS 2026 is reachable in CI, which unblocks S2 (2026-08-26)

T5 was scoped to picking S1's runner label. It also answered a question that has been blocking S2
since Ruling D.

**Ruling D recorded Sir's "unsure" on VS 2026 host availability**, which is why T14, T33 and T34 are
marked `[HUMAN]` — they assume proving VS 2026 requires Sir to own a VS 2026 machine. T5 shows that
assumption is no longer true: **`windows-2025-vs2026` is a GA hosted runner carrying Visual Studio
Enterprise 2026, version 18.9.12112.369 (major 18)**, at `C:\Program Files\Microsoft Visual Studio\18\Enterprise`,
with Rustup 1.29.0 preinstalled.

The one restriction is that the label appears in the **public-repo** runner table only, and is absent
from the private-repo table. **`kitamstudios/rust-analyzer.vs` is public** (verified via
`gh repo view`: `visibility: PUBLIC`), so the label is available to this repository today.

Consequences, none of which are S1 work and none of which are actioned in this commit (Ruling F):

- **T11** can be answered by experiment rather than by documentation research. The standing unverified
  finding — that VS 2026 exposes 17.x APIs and ignores the upper bound of an existing
  `InstallationTarget` range — is directly testable by installing the current VSIX on a
  `windows-2025-vs2026` runner and observing whether it activates.
- **T14** ("[HUMAN] VS 2026 install and activation smoke on a real host") is very likely no longer
  `[HUMAN]`. A CI job on that label is a real VS 2026 host. What CI cannot do is the *interactive* half
  of S5 — anything requiring a human at a running IDE — so T33/T34 need re-reading rather than blanket
  reclassification.
- **S5's matrix (T31–T35)** gains a second real host without hardware. This is exactly why T6's `config`
  job emits `runner` and `vs-major` as outputs instead of hardcoding a label: fanning out across
  `windows-2022` and `windows-2025-vs2026` later becomes a matrix change, not a workflow rewrite.

**Not decided here.** Whether to add a VS 2026 CI leg, and whether it gates or merely reports, is a
product call for Sir and belongs to S2. Recorded so the option is not lost.

### N13 — Anders' S1 architecture review (2026-08-26): Ruling Q, Ruling R, and six unslotted fixes

Triggered by the B-R2/Ruling C collision. Anders confirmed the collision and found it wider than
stated — see the rewritten N2 for the three regressions T3's old command values carried. Rulings Q
and R came out of this review. The rest is work with no prior owner.

**Taxonomy fixes — `src/RustAnalyzer.UnitTests/TraitTaxonomyTests.cs`:**

- **B-R6 — delete `TypeTraitsPartitionEveryTestCase` (`:82-92`). Do not fix it.**
  `EveryTestCaseCarriesExactlyOneTypeTrait` (`:53`) already asserts `Types.Length == 1 && TypeTraitValues.Contains(Types[0])`
  **per case, naming offenders**, which strictly implies the sum equality. "Fixing" the sum test would
  produce a duplicate of the exactly-one test. Bhaskar's demonstration — it passed with two live
  violations, because offsetting errors cancel — is the proof that a weaker redundant assertion is
  worse than none: it is where the false comfort came from.
- **B-R3 — make the two globs provably equal rather than narrowing either.** The runner globs
  `KS.*Tests.dll` (`Invoke-Tests.ps1:41`); the taxonomy test globs `*Tests.dll` (`TraitTaxonomyTests.cs:14`).
  Do **not** narrow the taxonomy to `KS.*` — a stray assembly would then be invisible to *both*, which
  is strictly worse. Do **not** widen the runner — `ApprovalTests.dll` would need a second exclusion
  list. Add two ~3-line facts: every discovered non-excluded assembly matches `KS.*Tests.dll`, naming
  any offender; and no entry in `ExcludedAssemblies` (`:25-29`) matches `KS.*Tests.dll`. Together they
  prove *governed ≡ run* and turn the naming convention into an executable invariant.
- **B-R4 — close it inside the B-R2 guard, now that Ruling Q keeps the script.** Assert the result XML
  contains at least one case from `TraitTaxonomyTests`, only in modes selecting `type=UnitTests`
  (`unit`, `full`) — ~3 lines beside the count check at `Invoke-Tests.ps1:78-87`. Costs one deliberate
  class-name literal, cheaper than an expected-assembly list, which Ruling L abolished.
- *Robustness nit, not a demand:* `GetTestCases()` (`:106`) calls `Assembly.LoadFrom(path).GetTypes()`.
  A future test assembly with an unresolvable dependency turns a clean taxonomy failure into an opaque
  `ReflectionTypeLoadException`. Catching it and reporting `LoaderExceptions` with the assembly name
  is 4 lines.

**Unrecorded hole that T6 opens.** Today `-Mode full` runs **unfiltered** (`Invoke-Tests.ps1:49-52`
has no `full` branch), so an untagged case still executes. Post-Ruling-M, CI runs
`-trait type=UnitTests` + `-trait type=IntegrationTests` + the harness — so **an untagged case executes
in no CI job at all**. The gate still fails closed, but *only* because
`EveryTestCaseCarriesExactlyOneTypeTrait` runs in the `unit` job. **The three jobs are therefore not
independent:** `integration`'s green is meaningless if the `unit` job's taxonomy facts did not run.
This makes B-R4 materially more load-bearing after T6 than it is today.

**Two production defects, neither introduced by this branch. Blast radius is larger than N11 recorded:**

1. `src/RustAnalyzer.TestAdapter/Common/EnvironmentExtensions.cs:18-21` builds the merged dictionary
   under the default **ordinal** comparer, while `:26` correctly uses `OrdinalIgnoreCase`. Windows
   environment names are case-insensitive, so a user override whose casing differs is silently ignored
   *and* a duplicate name is emitted into the child's environment block. This is **not** adapter-internal:
   `TestExecutor.cs:82` passes user-supplied `TestExecutionEnvironment` for **every Rust test run**, and
   `DebugLaunchTargetProvider.cs:90-95` hands the block to the **debug engine**. Fix: one
   `StringComparer.OrdinalIgnoreCase` applied to all three dictionary producers (`:21`, `:37`, `:53`).
   `PrependToPathInEnviroment` then collapses from `Keys.First(k => k.Equals("PATH", …))` — which
   **throws** when no PATH key exists — to a total `@this["PATH"]`. Anders verified the existing tests
   still pass (`GroupBy` keeps the first key). **Sharper form of B-R5:** `EnvironmentExtensionsTests.cs:63`
   asserts `ContainSingle` on `windir`, but **no `InlineData` row overrides `windir`** (`:54-58` override
   `OS`, `SYSTEMRoot`, `USERDOMAIN`, `ProgramFiles(x86)`) — the uniqueness assertion is on a variable
   nobody overrides, so it cannot fail. Add the assertion that would fail today when the fix lands.
2. `RlsInstallerService.cs:92-95` — a bare `catch { return null; }` swallowing `HttpRequestException`,
   `InvalidOperationException`, `FormatException`, `TaskCanceledException` and everything else. The
   swallow exists structurally: the method is `public static` and has no access to `_tl`. **Two changes,
   not one** — fixing only the catch leaves the real misdiagnosis: on `null`, `InstallLatestAsync:49-52`
   falls into `DownloadAsync(null)` → `ArgumentNullException`, caught at `:65` and logged as
   `"Download failed. StatusCode System.ArgumentNullException…"`, so a transient network blip produces
   a log line actively misleading about both cause and operation. (a) Typed catches that throw a
   classified exception; the instance caller logs with `_tl` and decides — keeps the static testable and
   puts logging where the logger is. (b) Handle `latestRel == null` explicitly in `InstallLatestAsync`:
   log "latest release could not be determined; keeping the packaged version" and return. Note
   `RlsReleaseTests.cs:19` has **no** `NotBeNull` band-aid — N11's warning has held; keep it that way.

**MSB3026 — take the free half only.** Root cause confirmed structurally: `CopyTestProjects` is
triplicated verbatim (`RustAnalyzer.UnitTests.csproj:58-63`, `RustAnalyzer.Remote.UnitTests.csproj:47-52`,
`RustAnalyzer.TestAdapter.UnitTests.csproj:78-83`) all copying to the same `$(TargetDir)\Cargo\TestData`,
and `CopyXunitConsoleRunner` (`KS.Tests.Common.targets:31-37`) is imported by all three copying the same
files to the same `$(TargetDir)`. With `/m` and a shared `/p:OutDir=_built\`, three projects race on
identical destinations. **But consolidation removes only two of N races** — the shared `OutDir` also makes
all three copy `KS.RustAnalyzer.TestAdapter.dll` and every transitive reference concurrently via
`ResolveAssemblyReference`, which is inherent to `/m` + shared `OutDir` and no target edit touches.
The complete fix is a build-topology change (per-project `bin` plus a staging step) that collides with
**D4** and every gate command, and is against **Ruling F** — backlog it as "shared-`OutDir` staging
redesign", noting it would also give Ruling K the curated staging directory it actually wants.

**Same family, observed 2026-08-26 (Bhaskar, during T10c):** two `MSB3061 "Access to the path … denied"`
on `_built\EmptyFiles\*` recur under `/m /t:Rebuild`, **naming different files each run** (`ppm`/`pdf`,
then `avif`/`jpe`) — several test projects copy the same `EmptyFiles` package content into the shared
`_built` OutDir and collide on delete. Non-fatal, pre-existing, absent from `Invoke-Build.ps1`'s own runs.
Recorded as a **latent CI flake** and as further evidence that the shared-`OutDir` race is broader than the
three `CopyTestProjects` duplicates: it reaches package content nobody wrote a target for. Do take
the free half: move `CopyTestProjects` into `KS.Tests.Common.targets` (kills 3 copies of the same 6
lines) and gate both copy targets on a declared opt-in property such as `<OwnsSharedTestPayload>true</OwnsSharedTestPayload>`
set in exactly one test csproj, rather than a hardcoded project name. Build order is irrelevant —
nothing reads those payloads until after the build completes.

**Two corrections Anders made to my framing, both verified:** the `dotnet test`/Coverlet premise is
cited by **T2** and **D1**, not T9; and the vswhere premise underpins **T6 + N5**, with T5 only choosing
*which* major.

### Execution rules

1. S1 is mandatory first and its done-done is the **PR merge gate green**, not a local green (Sir).
2. S7 (candidate 1) starts the moment S1 merges and runs in parallel with S2–S6. It ships no behaviour.
3. S4 (candidate 4) does not wait on S7.
4. No slice may reintroduce a gate **orchestration** wrapper under `.github/scripts`, a soft-failure
   switch, or a job-level `OutDir`. **Single-purpose readers and assertion scripts are permitted**
   (Ruling Q) — `Invoke-Tests.ps1`, T6's `Get-PinnedRustNightlyChannel.ps1`, and T6b's package-list
   reader are all in scope of this exemption. This reconciles N5's blanket "no new `.ps1` under
   `.github/scripts`", which contradicted T6's S-a and T6b as written.
5. Every slice updates this file with actual decisions, evidence, and commit references.
6. Anything marked `[HUMAN]` stops and returns to Sir; no agent decides it by inference.
