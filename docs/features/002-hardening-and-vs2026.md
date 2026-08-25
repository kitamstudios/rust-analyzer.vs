# Feature: Hardening and Visual Studio 2026
**Branch:** vibe/002-hardening-and-vs2026
**Status:** Selected for redesign

> Planning archive awaiting Anders redesign. Feature 002 is now limited to the candidates selected in
> [`docs/backlog.md`](../backlog.md): extension-model analysis only, gate portfolio/runtime review,
> VS 2026/prerequisite readiness, dependency modernization, the VS 2022/2026 compatibility matrix,
> and GitHub release notes in the extension. The broader material below preserves evidence and
> decisions but is not the selected implementation scope.

## Requirements

Redesign this archive into the selected feature-002 scope: analyze VSSDK versus
VisualStudio.Extensibility without migrating models; review gate purpose and runtime; support Visual
Studio 2022 17.12+ and Visual Studio 2026 while replacing the prerequisite install/restart loop;
modernize VSSDK and direct libraries; validate both host generations end to end; and show trusted,
offline-tolerant GitHub release notes in the extension.

Feature 001 already aligned local and CI gates, completed durable trait ownership, and resolved the
current Cargo executable-discovery, sysroot-layout, and approval-output failures. All broader
hardening candidates below remain planning evidence in this archive and stay in `docs/backlog.md`
unless the human selects them for a later feature.

Feature 001 resolved the current approval-output failures with narrow incidental normalization.
Broader cross-version ApprovalTests resilience remains S10 work and must stay fail closed.

Feature 001 completed durable trait-based test ownership and removed its transitional FQN manifest.

## Design Options (Ox)

### O1 — Ordered vertical hardening slices
- Description: Align fail-closed CI with feature 001's green local full gate and complete remaining
  P0 gate hardening first, establish the startup state boundary second, then harden each external
  boundary in dependency order and finish with a two-version smoke matrix.
- Pros: Every later slice starts with trustworthy verification; each slice remains independently
  reviewable; the suspension gate then gives later process/network/UI work one consistent contract.
- Cons: Temporary adapters may be needed while old and new boundaries coexist.

### O2 — Big-bang platform and infrastructure rewrite
- Description: Replace startup, toolchain, updater, telemetry, process, and CI behavior together.
- Pros: No transitional state.
- Cons: High regression risk across package activation, LSP, Cargo, tests, and release; difficult to
  review or roll back; contradicts the repository's surgical-change constraint.

### O3 — Manifest-only Visual Studio 2026 enablement
- Description: Widen the VSIX range and defer startup/infrastructure work.
- Pros: Small apparent change.
- Cons: Claims compatibility without runtime proof and leaves the restart/startup loop in the first
  VS2026 experience; explicitly rejected by the accepted product decisions.

**Recommended: O1 — the human made green fail-closed verification P0, followed immediately by the
accepted startup/readiness product boundary.**

## Current evidence

Line references describe the baseline reviewed for feature 001; symbols are the durable locator.

| Area | Evidence | Finding |
|------|----------|---------|
| Activation/startup | `src/RustAnalyzer/RustAnalyzerPackage.cs`, `InitializeAsync` and `OnAfterPackageLoadedAsync` (about lines 49-80) | Package load serially performs command/MEF setup, release notes, incompatibility checks, prerequisites, install/update, and notification. |
| Prerequisites/restart | `src/RustAnalyzer/Infrastructure/PreReqsCheckService.cs`, `CheckAsync`, `CheckRustupCargoAsync`, and `CheckVsVersion` (about lines 28-158) | PATH/tool checks, browser UI, and restart behavior are coupled and can repeat; resolution is incomplete. |
| Compatibility | `src/RustAnalyzer/source.extension.vsixmanifest` (about lines 13-31); `src/RustAnalyzer.TestAdapter/Constants.cs`, minimum VS constant | Manifest currently stops below version 18 while runtime minimum is 17.12. |
| LSP process | `src/RustAnalyzer/LanguageService/LanguageClient.cs`, `ActivateAsync` (about lines 63-93) | Starts a rust-analyzer process and returns its streams without a complete owned lifetime contract. |
| Process runner | `src/RustAnalyzer.TestAdapter/Common/ProcessRunner.cs`; `ToolChainServiceExtensions.RunAsync` (about lines 189-230) | Kill/dispose/cancellation behavior is distributed and callers can lose ownership. |
| Test cancellation | `src/RustAnalyzer.TestAdapter/TestExecutor.cs`, `Cancel`/`RunTests` (about lines 23-128) | Cancellation includes shared flags and child-process behavior that is not uniformly token-owned. |
| Async failures | `src/RustAnalyzer.TestAdapter/Common/TaskExtensions.cs`, `Forget` (about lines 7-14), used by `MetadataService`, `BuildOutputSink`, `TestContainerDiscoverer`, and `OutputWindowLogger` | Current fire-and-forget helper does not observe or report eventual faults. |
| Telemetry | `src/RustAnalyzer.TestAdapter/Common/TelemetryService.cs` (about lines 12-115) | Connection configuration is embedded and a machine/user-derived identifier is emitted. |
| Updater | `src/RustAnalyzer/Infrastructure/RlsInstallerService.cs` (about lines 45-175) | Startup performs GitHub download/extraction into the extension area and stores registry state without a transactional, independently verified install. |
| Cargo protocol | `src/RustAnalyzer.TestAdapter/Cargo/ToolChainService.cs`, `GetWorkspaceAsync`, build/test discovery, and test listing (about lines 118-229) | Metadata and test executable discovery use structured Cargo JSON; test listing still depends on unstable nightly JSON, and remaining text protocols are S7 work. |
| Latest-nightly gate evidence | Feature-001 full validation with rustc `1.100.0-nightly` | Feature 001 resolved build-script misselection through structured compiler artifacts and replaced fixed sysroot-layout assumptions with rustc-reported paths plus semantic runtime/import-library checks. |
| Rustup protocol | `src/RustAnalyzer.TestAdapter/Cargo/ToolChainServiceExtensions.cs`, rustup show/target methods (about lines 16-285) | Human-readable rustup output and environment assumptions are parsed directly. |
| Workspace/UI cost | `src/RustAnalyzer/Shell/RustToolsCommands.cs`, dynamic toolchain menu (about lines 135-215); `src/RustAnalyzer/Infrastructure/BuildOutputSink.cs`; `MetadataService` events | Query-status and event paths repeatedly enumerate/cache/update and dispatch fire-and-forget UI work. |
| Path behavior | `PreReqsCheckService`, `LanguageClient`, `EnvironmentExtensions`, `ToolChainServiceExtensions`, and `DebugLaunchTargetProvider` | Executable lookup, working directories, environment inheritance, quoting, and normalization do not share one explicit contract. |
| CI | `.github/workflows/cdp.yml`, unit/integration test steps and action references | Test steps use `continue-on-error`; publish can follow weakly enforced tests; dependencies include mutable or old action refs. |

## Accepted product and UX decisions

These are decided requirements, not unresolved design questions:

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

## Slices (Sx)

A slice is defined in `docs/meta-design.md`. Execution order is the table order.

| Slice | Outcome | Depends on |
|-------|---------|------------|
| S0 (P0) | Preserve feature 001's green local full gate, align CI fail closed, remove `MSB3277`, and re-enable validated quality tools. | - |
| S1 | Visual Studio 2022 17.12+/2026 packaging and runtime compatibility plus the prerequisite startup/readiness redesign. | S0 |
| S2 | Telemetry has an approved data/configuration contract with no embedded secret-like configuration or machine/user identity. | S1 |
| S3 | Remaining CI supply-chain, artifact, dependency, and release controls are immutable/auditable. | S0, S1 |
| S4 | Every LSP/Cargo/rustup/test child process has explicit ownership, cancellation, bounded shutdown, and disposal. | S1 |
| S5 | Fire-and-forget work observes and reports faults without crashing VS or hiding failure. | S2, S4 |
| S6 | Rust-analyzer update/install is safe, offline-tolerant, verified, transactional, and recoverable. | S1, S4, S5 |
| S7 | Remaining Cargo/rustup/test and path/environment boundaries use typed protocols and one correct Windows resolution/quoting contract. | S0, S1, S4 |
| S8 | Workspace/UI updates are batched and dynamic menu query paths are bounded, cached, and free of process/network work. | S1, S5, S7 |
| S9 | A repeatable VS 2022 17.12+ and VS 2026 smoke matrix validates activation through LSP, Cargo, tests, run/debug, suspend, and update/offline flows. | S2-S8 |
| S10 | Broader cross-version ApprovalTests fixture/update strategy remains deterministic and human-reviewed beyond the feature-001 fixes. | S0, S7, S9 |
| S11 | Durable trait-based unit/integration/external ownership. | Complete in feature 001 |

## Tasks (Tx)

### S0 (P0) — Align CI and complete mandatory gate hardening

Feature 001 completed local/CI gate alignment and durable test ownership. `MSB3277` cleanup and
optional quality-tool redesign remain in the backlog and are not prerequisites for the narrowed
feature 002 scope. Feature 002 will instead review the gate portfolio and runtime from measured
evidence.

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T14 | S0 | Make build, quick, full, standalone acceptance, format, and analyzer failures block locally and in CI; remove test `continue-on-error` behavior and make CI invoke the same policy as Bhaskar. | Complete (Feature 001) | - |
| T15 | S0 | Enforce trait-based quick/full/external ownership in local and CI execution; keep external freshness intentional and make classification drift fail closed. | Complete (Feature 001) | - |
| T32 | S0 | Consume Cargo compiler artifacts for test-executable/container discovery instead of display text, using `profile.test`, target kind, and the authoritative structured executable across Cargo layouts. | Complete (Feature 001) | - |
| T46 | S0 | Redesign and re-enable dry4csharp, mutate4csharp, and crap4csharp. Decide whether fresh `master` acquisition/build remains per session; define deterministic assistant/session ownership and provenance; validate all three against real changed production targets; prove legacy `dotnet test`/Coverlet compatibility; enforce reviewed DRY/CRAP thresholds and documented mutation/CLI exits; measure full-gate performance cost; keep every enabled gate fail closed. | Pending | - |
| T47 | S0 | Resolve every underlying `MSB3277` assembly dependency conflict, remove feature 001's `/warnNotAsError:MSB3277` grandfathering, and restore a zero-`MSB3277` fail-closed lint/build gate. | Pending | - |
| T48 | S0 | Resolve the current panic-ID, Duration, hash/path, and standalone approved-output failures without auto-approval or semantic suppression. | Complete (Feature 001) | - |
| T50 | S0 | Replace the brittle nightly sysroot-layout assumptions exercised by `ToolChainServiceExtensionsTests.TestGetBinAndLibPathsAsync` with rustc-reported paths and semantic runtime/import-library assertions. | Complete (Feature 001) | - |

Feature-001 resolutions retained as P0 evidence:

- The local configured full gate is now green: 203/203 assembly tests pass and the standalone
  18-result semantic baseline matches.
- Cargo test executable discovery consumes structured `compiler-artifact` records and no longer
  parses human-readable executable paths.
- Sysroot paths use `rustc --print sysroot` / `--print target-libdir`; tests require rustc/cargo plus
  a matching standard-library runtime DLL/import library without fixed hashes/PDB layout.
- Current ApprovalTests/standalone failures were resolved by narrow incidental normalization; no
  test was skipped, quarantined, or auto-approved.
- Broader ApprovalTests-based execution can still become brittle across future paths, Cargo/rustup
  versions, ordering, newlines, durations, or toolchain formatting.
- Likely shared/reporting surface: `src/TestsCommon/RaVsDiffReporter.cs`.
- Likely artifacts include `*.approved.txt` / ignored `*.received.txt` pairs for Cargo, rustup,
  discovery, and execution.
- Standalone approved output is owned by
  `src/TestProjects/workspace_with_tests/integrationtests.approved.txt` and
  `src/TestProjects/run-integrationtests.ps1`.
- Future nightly panic output may contain transient numeric thread/process IDs; feature 001's narrow
  normalization treats only those identifiers as incidental noise.
- Any regression that selects `target/.../build/.../out` instead of a structured test executable is
  a semantic product/protocol failure; it must never be normalized away or approved as incidental
  output.
- Feature 001 disabled DRY/mutation/CRAP and removed their bootstrap scaffolding. P0 must preserve
  prior requirements while redesigning acquisition/build cadence, deterministic session ownership,
  real-production execution, legacy Coverlet compatibility, thresholds/exits, and performance cost.

P0 acceptance criteria:

- The complete local Bhaskar full gate is green from a clean supported environment, and CI runs and
  agrees with the same gate membership, filters, manifests, tool versions, and failure policy.
- No regression is skipped, quarantined, silently ignored, converted to a baseline, or hidden behind
  `continue-on-error`.
- Quick contains only hermetic unit tests; full runs the intended integration set and standalone
  adapter harness; external freshness remains explicitly separate.
- Approval normalization covers only known incidental dimensions such as machine paths, supported
  versions, deterministic ordering/newlines, transient thread IDs, and legitimate duration noise.
  Semantic changes use stable assertions or fail with actionable diffs. Approval tests have no
  timing, network, release-freshness, or mutable external-state dependence.
- Approved-file updates require an explicit documented command/workflow and human review. No test or
  tool auto-approves or silently overwrites unexpected output.
- Approval/Cargo/rustup fixtures cover each supported tool/host compatibility band relevant to the
  asserted protocol.
- Expected failing Rust tests are handled deterministically only when their exact semantic result set
  matches the reviewed standalone baseline; infrastructure/discovery failures remain fatal.
- Cargo test discovery selects the intended test executables across the supported Cargo/nightly
  matrix, and sysroot path logic passes supported-version fixtures.
- DRY, mutation, and CRAP are re-enabled only after all three run against real changed production
  targets under the approved acquisition/ownership policy. Missing/empty coverage, legacy-project
  incompatibility, duplication/CRAP threshold findings, surviving mutants, and documented non-zero
  tool errors are hard failures; measured cost fits the approved full-gate budget.
- Build/lint emit zero `MSB3277`, the feature-001 exception is deleted, and reintroducing the warning
  fails both local and CI gates.
- Every currently red full-gate behavior is either fixed with regression coverage or explicitly
  returned to the human as a product decision; P0 cannot pass by weakening a mandatory gate.

### S1 — Visual Studio 2026 and startup/readiness prerequisite

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T53 | S1 | Decide between the in-process VSSDK and out-of-process VisualStudio.Extensibility models using a capability/migration matrix for Open Folder, LSP, testing, debugging, commands, options, MEF, updates, deployment, and VS 2022/2026 support; prototype any uncertain critical capability before selecting the extension architecture. | Pending | - |
| T1 | S1 | Research the supported VSIX manifest/SDK expression for VS 2022 17.12+ and open-ended VS 2026 intent; update package references/manifest only with runtime proof. | Pending | - |
| T2 | S1 | Introduce a process-scoped readiness state (`Unknown`, evaluating/awaited result, `Ready`, `Suspended`) owned by one service; no registry or user-environment persistence. | Pending | - |
| T3 | S1 | Implement a pure resolver result that distinguishes found tools, repairable process PATH, classified missing/invalid prerequisites, persisted-PATH change that may benefit from restart, and unexpected faults. | Pending | - |
| T4 | S1 | Probe process PATH, persisted user/machine PATH, `CARGO_HOME`, `RUSTUP_HOME`, and `%USERPROFILE%\.cargo\bin`; validate executables; add only validated directories to process PATH. | Pending | - |
| T5 | S1 | Add compute-once asynchronous evaluation/dialog coordination. Reset to `Unknown` only when cancellation happens before prompting; fail open on unexpected exceptions. | Pending | - |
| T6 | S1 | Implement the one-dialog three-action UX, explicit-only browser launch, never-automatic restart, and honest warning copy when restart is unlikely to help. | Pending | - |
| T7 | S1 | Implement process-only Continue/Suspended behavior and exactly one session InfoBar; fresh `devenv` starts at `Unknown`. | Pending | - |
| T8 | S1 | Route command visibility and all LSP/download/Cargo/test/debug/workspace entry points through the readiness result, with no persistent VSIX disable/unload. | Pending | - |
| T9 | S1 | Add unit tests for resolver/state transitions/races/cancellation/classification and integration tests for one-dialog/one-InfoBar and feature gating. | Pending | - |

S1 acceptance criteria:

- The extension architecture decision records required capability coverage, process/isolation and
  compatibility tradeoffs, migration cost, deployment constraints, and evidence for uncertain
  critical capabilities before VSSDK/package modernization proceeds.
- Manifest/package installation and a manual experimental-instance launch work on supported VS 2022
  and VS 2026 hosts; unsupported hosts receive a truthful runtime result.
- N concurrent startup consumers cause one evaluation and no more than one modal dialog.
- Process PATH can be repaired from every accepted location without persistent writes.
- Each accepted button behavior matches decisions 3-8 above, including restart-always-offered copy.
- Cancellation before prompt is retryable; cancellation after a user choice cannot duplicate UI.
- Classified prerequisite absence suspends; resolver defects and unexpected exceptions fail open and
  are observable.
- In `Suspended`, no Rust command, LSP process, download, Cargo, test, debugger, or workspace
  integration work starts.

### S2 — Safe telemetry

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T10 | S2 | Inventory every event/property/exception call and classify necessary operational data versus identity, path, command, source, or environment data. | Pending | - |
| T11 | S2 | Remove machine/user/domain-derived identifiers and redact paths, arguments, source content, environment values, and exception payloads under an approved policy. | Pending | - |
| T12 | S2 | Remove embedded connection configuration; inject approved configuration at build/deploy/runtime or disable telemetry safely when absent. | Pending | - |
| T13 | S2 | Add deterministic tests proving disablement, redaction, bounded event schemas, and no telemetry from tests/experimental instances. | Pending | - |

S2 acceptance criteria:

- No credential, connection string, stable user/machine identifier, source text, full local path, or
  environment value is present in source or emitted telemetry.
- Missing/invalid telemetry configuration is a no-op and never blocks activation.
- The human approves the final event allow-list and retention/consent posture before enablement.

### S3 — Remaining CI supply-chain and release hardening

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T16 | S3 | Pin third-party actions to reviewed immutable commits, minimize workflow permissions, and replace unsupported/deprecated setup actions. | Pending | - |
| T17 | S3 | Separate build/test artifacts from release authorization; publish only exact verified artifacts from the successful run. | Pending | - |
| T18 | S3 | Define dependency/update review for NuGet, Rust toolchains, bundled binaries, and action pins; produce provenance/checksum evidence where feasible. | Pending | - |
| T52 | S3 | Inventory and upgrade the VSSDK, Community.VisualStudio.Toolkit, VS Threading/analyzers, Test Platform, .NET/NuGet, ApprovalTests/xUnit, and other direct libraries in reviewed compatibility cohorts; remove obsolete/preview dependencies where supported replacements exist. | Pending | - |

S3 acceptance criteria:

- Workflow actions are immutable, permissions least-privilege, and publish consumes only artifacts
  built and verified by the same approved run.
- Dependency/action/toolchain updates have an auditable review and provenance/checksum policy.
- Library upgrades record before/after versions and release-note risks, keep related VSSDK packages
  compatible, and avoid unreviewed major-version jumps or permanent preview dependencies.
- Each upgrade cohort passes the local/CI gates plus VS 2022 17.12+ and VS 2026 VSIX install,
  MEF-composition, package-activation, LSP, Open Folder, Test Explorer, and debugging smoke coverage
  relevant to the changed APIs. Supported package references replace checked-in host assemblies
  where practical; unavoidable binaries retain documented provenance and hashes.
- Publishing authorization cannot bypass or substitute for P0's already-green local/CI verification.

### S4 — Process ownership and cancellation

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T19 | S4 | Define one child-process abstraction with owner, cancellation token, output completion, exit result, timeout policy, and async disposal. | Pending | - |
| T20 | S4 | Move LSP process lifetime under the language client's shutdown/disposal contract, including process-tree cleanup. | Pending | - |
| T21 | S4 | Migrate Cargo, rustup, build, test discovery, test execution, and helper processes from shared flags/manual kill paths to token-owned operations. | Pending | - |
| T22 | S4 | Add race tests for cancellation-before-start, during output, natural exit, kill failure, VS shutdown, and concurrent operations; assert no orphan processes. | Pending | - |

S4 acceptance criteria:

- Every spawned process has exactly one owner and awaited output/exit/disposal path.
- Cancellation is token-based, idempotent, bounded, kills owned descendants when required, and never
  kills unrelated processes.
- VS shutdown, workspace close, test cancellation, and LSP restart leave no owned process or pipe
  behind.

### S5 — Asynchronous failure visibility

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T23 | S5 | Replace `TaskExtensions.Forget` with an exception-observing helper that requires a named logger/error sink and deliberately handles cancellation. | Pending | - |
| T24 | S5 | Audit every fire-and-forget call; await work where ordering matters and use the helper only at true event/UI boundaries. | Pending | - |
| T25 | S5 | Add tests proving eventual faults are observed once, cancellations are not reported as faults, and reporting cannot recursively fail. | Pending | - |

S5 acceptance criteria:

- No task is discarded through `ConfigureAwait(false)` or an equivalent no-op.
- Every non-awaited task has an explicit owner and fault sink; unexpected asynchronous failure appears
  once in safe logs/telemetry and does not crash Visual Studio.

### S6 — Safe/offline transactional updater

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T26 | S6 | Separate update check, acquisition, verification, staging, activation, rollback, and notification into testable boundaries. | Pending | - |
| T27 | S6 | Define and implement trusted version/integrity metadata; reject redirects, archives, paths, hashes/signatures, or executable identities outside policy. | Pending | - |
| T28 | S6 | Download to a unique staging location, prevent archive traversal, verify before activation, atomically switch versions, and retain a known-good rollback. | Pending | - |
| T29 | S6 | Make offline/timeouts/rate limits/non-success responses non-blocking; continue with the packaged or last-known-good analyzer and avoid repeated session prompts. | Pending | - |
| T30 | S6 | Add fault-injection integration tests for interrupted download/extract/swap, corrupt or malicious archive, locked files, no network, and rollback. | Pending | - |

S6 acceptance criteria:

- Startup never requires network access and never executes an unverified or partially installed
  binary.
- A failed update leaves the previous/package analyzer usable and reports one actionable non-modal
  result.
- Staging cannot write outside its root; activation is transactional; recovery works after process
  termination at every phase.

### S7 — Machine-readable protocols and path/environment correctness

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T31 | S7 | Inventory each Cargo, rustup, rustc, and Rust test command; prefer stable JSON/message-format output and retain typed versioned adapters only where no machine format exists. | Pending | - |
| T33 | S7 | Encapsulate rustup/toolchain/target parsing with invariant culture, explicit version probes, fixture tests, and actionable unsupported-version errors. | Pending | - |
| T34 | S7 | Decide and implement the nightly test-listing compatibility policy, including capability detection and a truthful degraded mode. | Pending | - |
| T35 | S7 | Centralize Windows executable resolution, PATH composition, environment block merge, working-directory validation, path normalization, and argument quoting without invoking a shell. | Pending | - |
| T36 | S7 | Add spaces/Unicode/UNC/long-path/mixed-case/missing-variable/duplicate-PATH tests and real-toolchain integration coverage. | Pending | - |

S7 acceptance criteria:

- Machine-readable output is used wherever the tool offers it; remaining text parsers are isolated,
  version-probed, fixture-tested, culture-invariant, and fail with bounded diagnostics.
- Paths and arguments round-trip correctly without shell interpretation. Process, persisted, Cargo,
  rustup, and default-user locations obey the S1 resolver precedence and do not persist repairs.
- Tool capability absence yields a classified/degraded result rather than malformed test discovery.

### S8 — UI batching and menu performance

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T37 | S8 | Measure package activation, workspace-change bursts, command query-status, dynamic toolchain menu, and output-pane dispatch with repeatable traces. | Pending | - |
| T38 | S8 | Cache immutable readiness/toolchain/menu snapshots; invalidate on explicit workspace/settings/toolchain changes, not every query. | Pending | - |
| T39 | S8 | Batch/coalesce workspace and test-container events, deduplicate affected packages, and marshal one minimal UI update per batch. | Pending | - |
| T40 | S8 | Ensure `QueryStatus`/visibility performs no process, network, blocking wait, or unbounded enumeration and immediately hides commands while suspended. | Pending | - |
| T41 | S8 | Add burst/load tests and UI-thread assertions with agreed latency/allocation budgets. | Pending | - |

S8 acceptance criteria:

- Dynamic menu/status reads a bounded in-memory snapshot and reflects suspension immediately.
- A burst of equivalent filesystem changes produces one deduplicated model/UI update.
- No package/menu path blocks the UI thread or starts process/network work; measured budgets are met
  on both supported Visual Studio generations.

### S9 — Visual Studio 2026 smoke validation

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T42 | S9 | Automate or document a repeatable clean experimental-instance matrix for latest supported VS 2022 and VS 2026 with captured versions/logs. | Pending | - |
| T43 | S9 | Smoke install/activation, Open Folder/MEF, LSP, metadata, build/clippy/fmt, test discovery/execution, run/debug, menus/options, update/offline, and shutdown. | Pending | - |
| T44 | S9 | Exercise every S1 resolver/dialog/suspend/restart/InfoBar path once per fresh process and verify no state survives a new `devenv`. | Pending | - |
| T45 | S9 | Gate release compatibility claims on the matrix and document any capability-specific degradation. | Pending | - |

S9 acceptance criteria:

- Both host generations complete the critical smoke path without activity-log/MEF composition errors,
  leaked processes, repeated prompts, hidden async faults, or unsupported API use.
- Evidence identifies exact VS, extension, rustup, Cargo, toolchain, and rust-analyzer versions.
- Compatibility documentation and manifest claims match observed runtime support.

### S10 — Broader ApprovalTests resilience

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T51 | S10 | Design the longer-term cross-supported-version fixture matrix, semantic-assertion versus snapshot boundaries, reusable normalizer APIs, actionable reporter diffs, and explicit human-reviewed approved-file update workflow. Never auto-approve unexpected output. | Pending | - |

Feature 001 resolved the currently red panic-ID, duration, build-hash/path, and standalone output
cases. S10 is limited to broader future resilience across supported Visual Studio/Rust/Cargo/rustup
bands and must not reopen those fixes as permissive normalization.

### S11 — Durable unit/integration/external test taxonomy

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T49 | S11 | Implement explicit xUnit type traits, external integration overlay, direct trait filters, and fail-closed ownership/drift checks; remove `.github/test-classification.json` and FQN filters. | Complete (Feature 001) | - |

S11 requirements and acceptance criteria:

- Every xUnit test has explicit, reviewable `UnitTests` or `IntegrationTests` ownership.
- Quick contains only tests that do not cross a process boundary.
- Full runs unit + integration coverage, including the standalone adapter acceptance harness.
- External freshness/network checks remain intentional, separately invokable, and excluded from the
  deterministic default full release gate unless the human changes that policy.
- Added, removed, renamed, moved, or reclassified tests produce an actionable classification-drift
  failure rather than silently changing gate membership.
- The chosen mechanism works with VSTest/xUnit and the mutation/CRAP coverage path; no test is dropped
  through incompatible filter keys.
- Feature 001 removed the FQN manifest. Verified discovery is 96 unit and 108 integration, with one
  external integration overlay; default full selects 203 and explicit full+external selects 204.
- Missing, dual, overlapping, external-outside-integration, count, and filtered-selection drift fail
  before execution.

## Execution rules and dependencies

1. S0/P0 is mandatory first. No product slice starts while local or CI full verification is red.
2. S1 (VS2026 + prerequisite startup/readiness) starts immediately after P0. No later slice may
   invent a separate prerequisite check or suspension rule.
3. S2 and S3 can proceed independently after S1. S4 establishes the lifetime primitive consumed by
   updater/protocol work.
4. S5 follows S2/S4 so its fault sink is safe and process completion is observable.
5. S6 and S7 then harden remaining network/tool boundaries in parallel only if they share the S1/S4
   contracts rather than duplicating them.
6. S8 consumes stable readiness, async, and protocol snapshots.
7. S9 is the final release claim, not a substitute for slice-level unit/integration tests.
8. S10 handles broader cross-version approval resilience after the supported matrix is known.
9. S11 was completed in feature 001; later gate work preserves its trait ownership and drift checks.
10. Every slice updates this file with actual decisions, tests, evidence, and commit references.

## Unresolved decisions requiring the human

- U1: Exact VSIX SDK/package upgrades and manifest syntax that best express open-ended VS 2022
  17.12+/VS 2026 intent while remaining accepted by both installers.
- U2: Final dialog and InfoBar copy, icons, command placement, and what the explicit Restart action
  does when VS reports unsaved/blocking state. It must never restart automatically.
- U3: Telemetry posture: remove entirely, explicit opt-in, or approved minimal operational allow-list;
  configuration injection, endpoint ownership, consent, and retention.
- U4: Rust-analyzer trust policy: publisher signature versus maintained checksums/attestation, trusted
  release hosts, retention count, and whether automatic acquisition remains enabled by default.
- U5: Nightly test protocol policy: minimum supported nightly, stable fallback when available, and
  which degraded test features are acceptable without nightly.
- U6: CI release authorization/provenance mechanism and whether external freshness failures block a
  scheduled maintenance signal but never a deterministic PR build.
- U7: Quantitative activation, query-status, workspace-batch, cancellation, and updater timeout
  budgets.
- U8: Whether to make the legacy test projects safely consumable by `dotnet test`/Coverlet, introduce
  a dedicated coverage-compatible test assembly, or adopt another explicit bridge for mutation/CRAP.
  No wrapper may fake coverage or reinterpret a compatibility failure as success.
- U11: P0 quality-tool policy: whether fresh `master` acquisition/build remains per assistant
  session, the deterministic ownership/provenance mechanism, reviewed DRY/CRAP thresholds, mutation
  scope/worker limits, and acceptable full-gate time budget.
- U12: S10's long-term boundary between versioned fixtures, semantic assertions, and approved
  snapshots, plus the reviewed update tooling; feature-001's narrow normalizers are not blanket
  precedent.

Implementation must stop for the relevant human decision rather than silently choosing.

## Risks (Rx)

- R1: VS 2026 may have VSSDK/MEF/LSP/Test Window behavioral changes that compile against existing
  references but fail at runtime.
- R2: Readiness gates cross many entry points; a missed path could start Rust work while suspended.
- R3: Compute-once initialization can accidentally cache cancellation/fault/suspension or deadlock the
  UI thread if state and prompting are not separated.
- R4: Process-tree termination can kill unrelated reused PIDs if ownership identity is weak.
- R5: Protocol output differs by Cargo/rustup/toolchain version and locale; degraded-mode behavior must
  not silently lose tests.
- R6: Transactional replacement is complicated by antivirus, locked executable files, VS shutdown,
  and power loss.
- R7: Telemetry redaction after serialization is insufficient; sensitive fields must be excluded at
  the event boundary.
- R8: Fail-closed CI will expose the current warning/test debt and can halt releases until that debt is
  explicitly fixed rather than suppressed.
- R9: Caching/batching can serve stale command or workspace state unless invalidation ownership is
  explicit and tested.
- R10: Open-ended manifest compatibility can overclaim future support; runtime capability validation
  and release smoke evidence are required.
- R11: DRY/mutation/CRAP are disabled in feature 001. Re-enabling without real-target, legacy
  Coverlet, ownership, threshold/exit, and performance proof could create false confidence or an
  unusably slow gate; P0 must resolve all of them together.
- R12: The feature-001 `MSB3277` grandfather can conceal runtime assembly-binding risk if it outlives
  its temporary scope; P0 must remove it rather than expand the exempt warning set.
- R13: Over-broad approval normalization or automatic baseline updates can hide real regressions;
  feature 001 normalized only proven incidental dimensions, and S10 must keep unexpected semantic
  output fail closed.
- R14: Trait ownership is fail closed for the inventoried assemblies, but adding a new test project
  requires adding that assembly to discovery or it can escape classification checks.

## Assumptions (Ax)

- A1: Windows amd64 remains the supported host architecture unless the human expands scope.
- A2: Visual Studio 2022 minimum remains 17.12, and Visual Studio 2026 is major version 18 in the
  target environment.
- A3: A packaged or last-known-good rust-analyzer remains available so offline mode can fail open.
- A4: Process-only state and environment mutation are acceptable; persistent readiness, suspension,
  PATH repair, VSIX disable, or unload are not.
- A5: Feature 001's trait ownership and fail-on-drift checks are the current gate contract.
- A6: Current public extension behavior remains compatible except where the accepted prerequisite UX
  deliberately replaces restart/disable behavior.

## Deferrals (Dx)

- D1: New Rust editor features, project templates, package management, and unrelated UI redesign are
  outside this hardening program.
- D2: Non-Windows and non-amd64 hosts are outside scope unless separately approved.
- D3: A wholesale project-system rewrite or migration away from Visual Studio Open Folder is outside
  scope.
- D4: No reconciled review finding listed in Requirements is deferred beyond feature 002; deferrals
  apply only to unrelated product expansion.
- D5: **GitHub release notes in the extension UI.** Selected for feature 002 design. Planning must define the detailed UX,
  release-data contract, caching/offline behavior, trust/sanitization, navigation, accessibility,
  telemetry/privacy, and failure behavior before implementation. Likely current touchpoints include
  `ReleaseSummaryNotification`, package startup release-summary handling, `RlsInstallerService`, and
  the GitHub release/update notification surfaces. This entry records only the desired product
  outcome and likely evidence locations; it deliberately makes no UI, data, cache, or security design
  decision now.
- D7: **Durable test taxonomy:** completed in feature 001 with explicit traits and fail-closed
  discovery/filter validation.

## Notes & Decisions

- The prerequisite result is a session capability boundary, not an installation switch. `Suspended`
  means "this `devenv` process will not start Rust functionality."
- Restart is an explicit user action and remains visible even when diagnostics indicate it is
  unlikely to help. Honest copy is required; automatic restart is forbidden.
- **Open prerequisites** is the only action that opens a browser.
- A fresh process is the only automatic reset. No registry or user-environment sentinel may suppress
  future checks.
- Unexpected resolver/programming errors fail open; only enumerated prerequisite failures may
  suspend. All such errors must still be observable through the safe S2/S5 channels.
- External/freshness tests remain separate from deterministic release evidence throughout CI
  hardening.
- Mutation and CRAP remain fail closed. Feature 002 must validate their real production-target path;
  it must not weaken exits, skip required coverage, or treat legacy test/Coverlet incompatibility as
  a clean result.
- `MSB3277` is the only feature-001 lint grandfather. Feature 002 must resolve the conflicts, remove
  the command-line exception, and make any recurrence fatal.
