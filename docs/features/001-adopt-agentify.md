# Feature: Adopt agentic governance
**Branch:** vibe/001-adopt-agentify
**Status:** Complete

## Requirements

Bootstrap the minimum governance needed to execute the adopted four-agent loop: provide the factual
system design and feature records, replace all required repository commands, swap the four model
assignments, split deterministic unit tests from integration and external/freshness tests, and
install/update Rust nightly once per assistant session for explicit full-test use. Only JARVIS/the
assistant may perform that startup operation; Dave and Bhaskar only validate and consume
current-session state and hand back on failure. Mark every xUnit test with an explicit
`type=UnitTests` or `type=IntegrationTests` trait, retain external freshness as an opt-in overlay,
and run the standalone VSTest adapter scenario as acceptance. Run the same fail-closed configured
gates in local verification and GitHub merge validation. DRY, mutation, and CRAP are disabled
(`none`) and remain in the backlog. Do not change product behavior, generated artifacts, or the
VSIX version.

## Human scope and completion decision

Feature 001's outcome is a **functioning agentic loop with green local and merge-validation gates**.
Its approved final scope also fixes the Cargo executable-discovery, nightly sysroot-layout, narrow
approval-normalization, durable test ownership, and CI failure-propagation issues exposed while
making those gates truthful. `MSB3277` cleanup and optional quality-tool redesign remain backlogged.

Feature 001 is complete when all of the following are demonstrated:

1. JARVIS runs preflight successfully and the loop reaches mode/task selection.
2. The assistant-only startup orchestrator runs once, creates valid owner/phase/token-hash
   provenance, installs/updates nightly, and produces a validated current-session artifact.
3. Dave and Bhaskar validate and consume that artifact without invoking bootstrap or rustup
   install/update.
4. Model routing is durable: JARVIS/Bhaskar use GPT-5.6 Sol; Anders/Dave use Claude Opus 5; all use
   maximum reasoning, including future `agentify` updates.
5. Build, lint, format, quick, and full commands execute end to end and report their true outcomes;
   optional DRY/mutation/CRAP rows are explicitly `none` and skipped. Red results were acceptable
   diagnostic evidence before T23 only when accurate, actionable, and unsuppressed; completion
   requires the configured local gate to be green.
6. No generated build output, external checkout/binary, VSIX, test result, or auto-stamped version is
   committed.
7. GitHub merge validation invokes the same required format, build, lint, quick, full, and acceptance
   policy without `continue-on-error`; publishing depends on successful validation.

Explicit non-goals: feature 001 does not remove the `MSB3277` grandfather or acquire/re-enable DRY,
mutation, or CRAP tooling. It adds no `KnownIssue` quarantine and does not skip, auto-approve,
baseline, or disguise failures.

## Design Options (Ox)

### O1 — Repository scripts plus explicit xUnit traits
- Description: Mark each xUnit test as `UnitTests` or `IntegrationTests`, add an external overlay
  where needed, and put small PowerShell command adapters under `.github/scripts`.
- Pros: Ownership is visible beside each test; VSTest filters directly on traits; set and count
  validation catch missing, dual, or drifted classification before execution.
- Cons: Integration tests remain physically mixed with unit tests; the gate must explicitly inventory
  every test assembly.

### O2 — Split every integration test into new assemblies
- Description: Move non-hermetic tests into dedicated integration-test projects.
- Pros: Physical separation makes accidental execution harder.
- Cons: Larger project/solution churn, duplicated fixture wiring, and unnecessary risk for a
  governance bootstrap.

**Recommended: O1 — it supplies explicit durable ownership without restructuring legacy projects.**

## Slices (Sx)

A slice is defined in `docs/meta-design.md`.

| Slice | Outcome | Depends on |
|-------|---------|------------|
| S1 | Governance can pass preflight with factual design, stable feature records, and the requested model map. | - |
| S2 | Build, analyzer, formatting, and test commands run locally without assuming a Visual Studio edition. | S1 |
| S3 | Quick and full gates select honest unit/integration/external test groups. | S2 |
| S4 | JARVIS installs/records Rust nightly once at startup with provenance; Dave/Bhaskar only validate/use it without stable fallback. | S1, S3 |
| S5 | Every configured feature-001 gate is green without suppressing or quarantining failures. | S2-S4 |

## Tasks (Tx)

One or more tasks per slice.

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T1 | S1 | Create `docs/design.md` from current implementation evidence. | Complete | - |
| T2 | S1 | Record feature 001 and the complete deferred hardening program as feature 002. | Complete | - |
| T3 | S1 | Assign JARVIS/Bhaskar to GPT-5.6 Sol and Anders/Dave to Claude Opus 5, all at max reasoning, including future `agentify` updates. | Complete | - |
| T4 | S2 | Add a shared `vswhere` resolver and Release build/no-restore analyzer commands. | Complete | - |
| T5 | S2 | Add deterministic tracked-text whitespace fix/check commands without a new formatter dependency. | Complete | - |
| T6 | S3 | Classify every xUnit test with reviewed `UnitTests` or `IntegrationTests` traits and an external overlay where required. | Complete | - |
| T7 | S3 | Add quick/full trait filters and include the standalone test-adapter acceptance harness in full. | Complete | - |
| T8 | S3 | Validate preflight, formatter idempotence, build, quick tests, and the safe extent of full tests. | Complete | - |
| T14 | S2 | Grandfather only `MSB3277` in lint while keeping every other warning/error fatal. | Complete | - |
| T16 | S4 | Add fail-closed assistant-only installation/update and session diagnostics for Rust nightly. | Complete | - |
| T17 | S4 | Require full VSTest/Cargo/harness children to inherit the validated session nightly. | Complete | - |
| T18 | S4 | Make nightly initialization an explicit assistant-only startup entrypoint, idempotent for valid same-session state. | Complete | - |
| T19 | S4 | Add validation-only nightly consumption for Dave/Bhaskar and clear JARVIS handback on absent, stale, wrong-session, modified, or invalid state. | Complete | - |
| T20 | S3 | Remove the transitional FQN manifest and enforce fail-closed trait ownership, overlap, external-subset, count, and filtered-selection validation. | Complete | - |
| T21 | S4 | Add JARVIS-only random-token nightly provenance; require matching assistant owner/phase/hash in the initializer and all consumers. | Complete | - |
| T22 | S4 | Run the first provenance-backed JARVIS startup, validate generated session artifacts through consumer-only checks, and confirm the loop can enter feature 002 P0. | Complete | - |
| T23 | S5 | Fix the current Cargo/nightly discovery, sysroot-layout, and approval-output root causes; add regression coverage and run every configured gate green in documented order. | Complete | - |
| T24 | S5 | Make GitHub merge validation run the same configured format, build, lint, quick, full, and acceptance gates fail closed before publishing. | Complete | - |

## Risks (Rx)

- R1: The existing solution emits `MSB3277` assembly-conflict warnings. Feature 001 grandfathers only
  that code; every other warning/error remains fatal. Feature 002 owns removing the exception.
- R2: Integration tests require real Cargo/rustup state, Windows child processes, and in some cases a
  nightly toolchain. Nightly install/update or network failure blocks preflight explicitly.
- R3: Trait ownership is explicit, but the gate's test-assembly inventory is manual. Adding a new test
  project requires updating the inventory or its tests will not be discovered.
- R4: The custom formatter intentionally covers deterministic textual whitespace, not Roslyn syntax
  formatting. `dotnet format` cannot load the legacy solution because of `XMakeElements`.
- R5: Installing mutable nightly each session trades cross-session repeatability for freshness; the
  exact rustc/cargo diagnostics are recorded so failures remain reproducible.

## Assumptions (Ax)

- A1: `pwsh` 7.1+, Git, Visual Studio Installer/`vswhere.exe`, and a complete Visual Studio instance
  with MSBuild and VSTest are present.
- A2: Release build outputs remain in the repo-root, git-ignored `_built` directory and are never
  added.
- A3: Unit tests run fast and do not cross a process boundary. Integration tests may cross process or
  network boundaries. Acceptance tests perform and verify critical scenarios as a customer would.
- A4: The external trait is an opt-in overlay on integration ownership, not a deterministic release
  prerequisite.
- A5: The assistant runtime exposes `AGENCY_SESSION_ID` or `COPILOT_AGENT_SESSION_ID`. A manual shell
  can explicitly set `RUST_ANALYZER_VS_SESSION_ID`; all commands in a gate use the same value.
- A6: rustup and network access are available at assistant session start. Installing/updating nightly
  through rustup is acceptable, but changing the default toolchain or adding a persistent directory
  override is not.

## Deferrals (Dx)

- D1: Every reconciled product, architecture, security, CI, updater, process, protocol, performance,
  and Visual Studio 2026 change is deferred as one ordered program in
  [feature 002](002-hardening-and-vs2026.md).
- D2: Physical separation of integration tests into dedicated assemblies is unnecessary for this
  feature; explicit traits are the durable ownership mechanism.
- D3: Immutable action pinning, minimum workflow permissions, and release provenance remain
  backlogged; merge-validation failure propagation is included here.

## Notes & Decisions

- The model profile remains `both`: both vendors are still assigned. Its explanatory literal is
  updated to match the swapped design/code versus verify/drive roles.
- Quick validates complete, disjoint trait ownership and runs `type=UnitTests`.
- Full excludes only `scope=External`, runs unit + integration, then
  `src/TestProjects/run-integrationtests.ps1` as the acceptance harness.
- `RlsReleaseTests` is `type=IntegrationTests` plus `scope=External`. It runs only through full test
  script `-IncludeExternal`.
- Real Cargo/rustup/toolchain operations, child processes/timing, process or filesystem environment
  resolution, and end-to-end VSTest discovery/execution are integration tests. Pure protocol parsing,
  path/string logic, mocked services, and fixed read-only fixtures remain quick.
- Every xUnit case has exactly one type trait. Set membership, overlap, external-subset, expected
  totals, and filtered discovery checks make classification drift fatal.
- Feature 001 adds no `KnownIssue` trait/quarantine. The approved scope extension fixed the four
  full-gate root causes rather than suppressing or reclassifying them.
- `dry-check`, `mutation-test`, and `crap-check` are `none`; Bhaskar skips them under the optional
  command rule. Feature 002 P0 retains and redesigns all prior acquisition, ownership, real-target,
  Coverlet, threshold/exit, and performance requirements before re-enablement.
- Lint promotes every warning to an error except `MSB3277`, which is passed through
  `/warnNotAsError:MSB3277` until feature 002 resolves the dependency conflicts and removes the
  exception.
- Preflight installs/updates `nightly`, records its rustc commit/release/host and cargo version, and
  writes a session manifest only after successful probes. Only JARVIS performs that operation. Full
  tests validate it and set process-only `RUSTUP_TOOLCHAIN=nightly`; stable fallback is forbidden.
- Dave and Bhaskar never call `Initialize-RustNightly.ps1` or run rustup install/update. Their
  scripts validate/consume only; any state failure stops and hands back to JARVIS without repair.
- Valid same-session bootstrap state is reused without network work. Invalid existing state is never
  self-healed in-session; JARVIS must begin a new assistant-session bootstrap.
- JARVIS's startup orchestrator generates a random token in memory and persists only its hash with
  `Owner=assistant` and phase. The nightly initializer requires that token; consumers require
  matching current-session/repository owner, `ready` phase, and hash provenance. Direct sub-agent
  invocation without the token fails before install or update work.
- Cargo test executable discovery now consumes `compiler-artifact` JSON with `profile.test=true`,
  a non-null `executable`, and non-`custom-build` target kind. Cargo's structured executable path is
  authoritative across both legacy `deps` and current build-directory layouts; no path is inferred
  from human-readable stderr.
- Rust debugger paths come from `rustc --print sysroot` and `rustc --print target-libdir`.
  Regression coverage requires tool binaries plus a matching standard-library runtime DLL/import
  library, without assuming a fixed hash, PDB, or duplicated bin-directory layout.
- Approval normalization removes StartTime/EndTime, canonicalizes every Duration, panic thread ID,
  build-directory hash, and executable hash while preserving test names, outcomes, source lines, and
  semantic error text. The three approved files changed only for UTF-8 BOM consistency, Cargo's
  normalized structured executable paths, and canonical long-test Duration punctuation/value; the
  standalone approved file did not change.
- `RaVsDiffReporter.INSTANCE` is unconditionally `XUnit2Reporter.INSTANCE`. Approval mismatches still
  write received output and fail through xUnit/VSTest, but no environment can launch Visual Studio or
  a graphical diff reporter. No CI environment policy is required.
- **Feature-001-only gate exception (final human decision):** known baseline gate failures do not
  block completing this bootstrap if and only if (a) loop/gate machinery executes correctly,
  (b) every failure is accurately captured and assigned to feature 002 P0, and (c) no failure is
  newly caused by feature-001 implementation. The final acceptance did not use this exception: T23
  restored the configured local gate to green. This exception expires with feature 001; feature 002
  P0 must align CI with that green baseline and complete the remaining MSB/quality-tool work before
  any subsequent product slice proceeds. It never authorizes suppressing tests, altering approvals,
  quarantining failures, or changing exit codes to manufacture green.
- No commit, push, deploy, generated-artifact edit, or VSIX version change belongs to this feature.

## Validation evidence

### Current acceptance status

Complete. JARVIS startup and consumer validation pass, and all configured feature-001 gates are
green through root-cause fixes—not through the historical exception, suppression, quarantine,
auto-approval, or exit-code changes.

- Required-placeholder preflight: passed.
- `vswhere` resolution: selected a complete Visual Studio major 17 installation for both MSBuild and
  VSTest without encoding the edition or installation path. Major 17 is the feature-001 default even
  if a later installation becomes complete; feature-002 validation must opt into another major
  explicitly.
- Format: check found three existing tracked whitespace drifts, fix normalized them, and the second
  check passed with no drift.
- Release build/restore: completed with exit code 0 and wrote `_built`; existing `MSB3277`
  assembly-version conflict warnings remain.
- No-restore analyzer gate: exited 0 with only `MSB3277` warnings. `/warnAsError` remained enabled and
  `/warnNotAsError:MSB3277` was the sole exception; no other warning/error code was observed.
- Trait discovery found 204 cases: 96 unit and 108 integration, with 1 external integration overlay;
  the sets are complete and disjoint.
- Test-source taxonomy changes are trait attributes only; assertions and behavior are unchanged.
- Quick selected and passed 96/96. Default full selected 203 assembly cases by excluding the external
  overlay; full plus external validated selection of all 204 without executing network freshness.
- Rust-nightly preflight: installed/updated `nightly` without changing the default/override and
  recorded rustc `1.100.0-nightly`, commit
  `fb6531d550e0075b9eb9a51464f404805eec87d9`, in the session manifest. Full tests validated and used
  that exact manifest through process-only `RUSTUP_TOOLCHAIN=nightly`.
- Child environment normalization: all 5
  `EnvironmentExtensionsTests.OverrideWithEnvironmentBlockTests` cases passed after VSTest launched
  with a lowercase `windir` copied from the process/SystemRoot value.
- Full assembly tests: 203/203 passed on the recorded nightly.
- Standalone adapter harness: produced 18 reviewed results (11 passed, 4 expected failures, 3
  skipped); incidental nightly thread IDs were normalized, semantic output matched the approved
  baseline, and the harness returned success without altering VSTest's underlying exit code.
- `RlsReleaseTests`: not executed, by design. It remains an explicit network/freshness opt-in.
- Separation-of-duties validation: the JARVIS orchestrator requires `-AssistantStartup`; the nightly
  initializer requires its in-memory token. `Test-SessionBootstrap.ps1` is validation-only
  and performs no download, install, or update work.
- Provenance hardening validation: direct nightly initialization without JARVIS's in-memory random
  token failed before install/update work; the orchestrator rejected calls without its
  assistant-startup authorization; invalid owner/phase/token-hash provenance was rejected and handed
  back to JARVIS.
- JARVIS operational acceptance: blocking placeholder scan passed; the assistant orchestrator
  completed; the nightly manifest validated with `Owner=assistant`, phase `ready`, and matching
  hash-backed current-session/repository provenance; `Test-SessionBootstrap.ps1` passed for
  Dave/Bhaskar consumption.
- Reduced-scope acceptance: JARVIS reran the nightly-only orchestrator against ready provenance; both
  initializer and consumer validation exited 0 with no install, update, network, external build, or
  quality-tool activity.
- Consumer-only acceptance: Dave reran `Test-SessionBootstrap.ps1` successfully without bootstrap or
  rustup install/update work.
- Repository revalidation after the green-gate slice: format fix/check, Release build, lint, quick,
  and full all exited 0 in documented order. Only the pre-approved `MSB3277` grandfather remains.
- Reporter regression: `ApprovalReporterIsAlwaysXUnit` passed, and source/reference search found no
  `DiffReporter.INSTANCE` or CI-based reporter selection.

### Green configured-gate baseline

- Format fix: exit 0; no drift.
- Format check: exit 0.
- Release build: exit 0; existing `MSB3277` remains visible.
- Lint: exit 0 with `/warnAsError` and only the feature-001 `MSB3277` exception.
- Quick: 96/96 passed.
- Full: exit 0; 203/203 assembly tests passed, then the 18-result standalone approved baseline
  matched.
- DRY/mutation/CRAP: disabled as `none` and skipped.

The backlog still owns removing the `MSB3277` grandfather and deciding whether to redesign/re-enable
the three optional quality tools. Merge validation is aligned with the local gate in this feature.
