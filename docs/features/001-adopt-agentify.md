# Feature: Adopt agentic governance
**Branch:** vibe/001-adopt-agentify
**Status:** Complete

## Requirements

Bootstrap the minimum governance needed to execute the adopted four-agent loop: provide the factual
system design and feature records, replace all required repository commands, swap the four model
assignments, split deterministic unit tests from integration and external/freshness tests, and
install/update Rust nightly once per assistant session for explicit full-test use. Only JARVIS/the
assistant may perform that startup operation; Dave and Bhaskar only validate and consume
current-session state and hand back on failure. Keep test source unchanged: feature 001 uses a
reviewed fail-on-drift FQN manifest as a temporary unit/integration/external split. DRY, mutation,
and CRAP are disabled (`none`) and deferred to feature 002 P0. Do not change product behavior,
generated artifacts, the VSIX version, CI policy, or any feature-002 hardening implementation.

## Human scope and completion decision

Feature 001's outcome is a **functioning agentic loop**, not remediation of pre-existing product,
test, CI, dependency, or approval-baseline failures. Feature 002 P0 owns restoring a green full gate
and CI.

Feature 001 is complete when all of the following are demonstrated:

1. JARVIS runs preflight successfully and the loop reaches mode/task selection.
2. The assistant-only startup orchestrator runs once, creates valid owner/phase/token-hash
   provenance, installs/updates nightly, and produces a validated current-session artifact.
3. Dave and Bhaskar validate and consume that artifact without invoking bootstrap or rustup
   install/update.
4. Model routing is durable: JARVIS/Bhaskar use Claude Opus 5; Anders/Dave use GPT-5.6 Sol; all use
   maximum reasoning, including future `agentify` updates.
5. Build, lint, format, quick, and full commands execute end to end and report their true outcomes;
   optional DRY/mutation/CRAP rows are explicitly `none` and skipped. A red result is acceptable
   evidence for feature 001 when it is accurate, actionable, and not suppressed.
6. No generated build output, external checkout/binary, VSIX, test result, or auto-stamped version is
   committed.
7. JARVIS can begin feature 002 P0 with the known red baseline visible.

Explicit non-goals: feature 001 does not fix, quarantine, skip, auto-approve, baseline, or disguise
the current full-gate/CI failures. It adds no `KnownIssue` quarantine. Completion is not conditioned
on those pre-existing failures becoming green.

## Design Options (Ox)

### O1 — Repository scripts plus transitional FQN policy
- Description: Keep existing test assemblies/source unchanged, classify non-hermetic tests in a
  reviewed FQN/count manifest, and put small PowerShell command adapters under `.github/scripts`.
- Pros: Surgical; works with the existing VSTest/xUnit stack; commands are local, noninteractive, and
  share one `vswhere` resolver; no new dependency; classification drift fails before execution.
- Cons: Integration tests remain physically mixed with unit tests and FQN ownership is temporary
  coupling; feature 002 must design the durable taxonomy.

### O2 — Split every integration test into new assemblies
- Description: Move non-hermetic tests into dedicated integration-test projects.
- Pros: Physical separation makes accidental execution harder.
- Cons: Larger project/solution churn, duplicated fixture wiring, and unnecessary risk for a
  governance bootstrap.

**Recommended: O1 — it supplies the temporary honest gate split without modifying test source;
feature 002 P0 designs any future quality-tool acquisition/execution policy.**

## Slices (Sx)

A slice is defined in `docs/meta-design.md`.

| Slice | Outcome | Depends on |
|-------|---------|------------|
| S1 | Governance can pass preflight with factual design, stable feature records, and the requested model map. | - |
| S2 | Build, analyzer, formatting, and test commands run locally without assuming a Visual Studio edition. | S1 |
| S3 | Quick and full gates select honest unit/integration/external test groups. | S2 |
| S4 | JARVIS installs/records Rust nightly once at startup with provenance; Dave/Bhaskar only validate/use it without stable fallback. | S1, S3 |

## Tasks (Tx)

One or more tasks per slice.

| # | Slice | Task | Status | Commit |
|---|-------|------|--------|--------|
| T1 | S1 | Create `docs/design.md` from current implementation evidence. | Complete | - |
| T2 | S1 | Record feature 001 and the complete deferred hardening program as feature 002. | Complete | - |
| T3 | S1 | Assign JARVIS/Bhaskar to Claude Opus 5 and Anders/Dave to GPT-5.6 Sol, all at max reasoning. | Complete | - |
| T4 | S2 | Add a shared `vswhere` resolver and Release build/no-restore analyzer commands. | Complete | - |
| T5 | S2 | Add deterministic tracked-text whitespace fix/check commands without a new formatter dependency. | Complete | - |
| T6 | S3 | Classify external, toolchain, process, environment, and end-to-end tests in a reviewed transitional FQN/count manifest without modifying test source. | Complete | - |
| T7 | S3 | Add quick/full VSTest filters and include the standalone test-adapter integration harness in full. | Complete | - |
| T8 | S3 | Validate preflight, formatter idempotence, build, quick tests, and the safe extent of full tests. | Complete | - |
| T14 | S2 | Grandfather only `MSB3277` in lint while keeping every other warning/error fatal. | Complete | - |
| T16 | S4 | Add fail-closed assistant-only installation/update and session diagnostics for Rust nightly. | Complete | - |
| T17 | S4 | Require full VSTest/Cargo/harness children to inherit the validated session nightly. | Complete | - |
| T18 | S4 | Make nightly initialization an explicit assistant-only startup entrypoint, idempotent for valid same-session state. | Complete | - |
| T19 | S4 | Add validation-only nightly consumption for Dave/Bhaskar and clear JARVIS handback on absent, stale, wrong-session, modified, or invalid state. | Complete | - |
| T20 | S3 | Remove feature-001 test-source traits and enforce quick/full/external selection through a fail-on-drift transitional FQN policy. | Complete | - |
| T21 | S4 | Add JARVIS-only random-token nightly provenance; require matching assistant owner/phase/hash in the initializer and all consumers. | Complete | - |
| T22 | S4 | Run the first provenance-backed JARVIS startup, validate generated session artifacts through consumer-only checks, and confirm the loop can enter feature 002 P0. | Complete | - |

## Risks (Rx)

- R1: The existing solution emits `MSB3277` assembly-conflict warnings. Feature 001 grandfathers only
  that code; every other warning/error remains fatal. Feature 002 owns removing the exception.
- R2: Integration tests require real Cargo/rustup state, Windows child processes, and in some cases a
  nightly toolchain. Nightly install/update or network failure blocks preflight explicitly.
- R3: The transitional FQN policy couples gates to names/counts. To prevent silent escape, every test
  addition, removal, rename, prefix-count change, or overlap fails discovery until explicitly
  reviewed; feature 002 replaces this mechanism.
- R4: The custom formatter intentionally covers deterministic textual whitespace, not Roslyn syntax
  formatting. `dotnet format` cannot load the legacy solution because of `XMakeElements`.
- R5: Installing mutable nightly each session trades cross-session repeatability for freshness; the
  exact rustc/cargo diagnostics are recorded so failures remain reproducible.

## Assumptions (Ax)

- A1: `pwsh` 7.1+, Git, Visual Studio Installer/`vswhere.exe`, and a complete Visual Studio instance
  with MSBuild and VSTest are present.
- A2: Release build outputs remain in the repo-root, git-ignored `_built` directory and are never
  added.
- A3: Unclassified tests are quick only when they are pure parsing, string/path calculation, mocked
  behavior, or read-only repository fixture tests.
- A4: The transitional external FQN set is manual/scheduled evidence, not a deterministic release
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
- D2: A durable taxonomy (traits, physical integration-test assemblies, or another designed
  mechanism) is deferred to feature 002 S11.
- D3: Existing CI workflow policy is documented but not changed by this bootstrap.

## Notes & Decisions

- The model profile remains `both`: both vendors are still assigned. Its explanatory literal is
  updated to match the swapped design/code versus verify/drive roles.
- Quick validates `.github/test-classification.json` against VSTest discovery, then excludes all
  reviewed integration and external FQN prefixes.
- Full validates the same manifest, runs unit + integration FQNs, then
  `src/TestProjects/run-integrationtests.ps1`.
- `RlsReleaseTests` is the explicit external GitHub/freshness FQN. It runs only through full test
  script `-IncludeExternal` and is never represented as hermetic unit coverage.
- Real Cargo/rustup/toolchain operations, child processes/timing, process or filesystem environment
  resolution, and end-to-end VSTest discovery/execution are integration tests. Pure protocol parsing,
  path/string logic, mocked services, and fixed read-only fixtures remain quick.
- Feature 001 adds no test-source traits or helper classification attributes. Exact discovered and
  unit/integration/external totals plus per-prefix expected counts make FQN drift fatal.
- Feature 001 adds no `KnownIssue` trait/quarantine. The four current full-gate failures remain
  visible until the human authorizes product/test remediation; the transitional FQN policy classifies
  them but never suppresses them.
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
- **Feature-001-only gate exception (final human decision):** known baseline gate failures do not
  block completing this bootstrap if and only if (a) loop/gate machinery executes correctly,
  (b) every failure is accurately captured and assigned to feature 002 P0, and (c) no failure is
  newly caused by feature-001 implementation. This exception expires with feature 001. Feature 002
  P0 must restore green local/CI full gates before any subsequent product slice proceeds. It never
  authorizes suppressing tests, altering approvals, quarantining failures, or changing exit codes to
  manufacture green.
- No commit, push, deploy, generated-artifact edit, or VSIX version change belongs to this feature.

## Validation evidence

### Current acceptance status

Complete under the final human-approved, feature-001-only gate exception above. JARVIS ran the first
provenance-backed startup successfully, consumer validation passed, and the loop can begin feature
002 P0. The known red full gate below remains visible and does **not** become green by declaration.

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
- Quick tests: 76/76 passed after transitional discovery validated 195 total tests as 76 unit,
  118 integration, and 1 external.
- Test source diff contains no feature-001 trait-attribute additions. Trait-only staged changes were
  removed without resetting unrelated files.
- Focused VSTest discovery validated the transitional filters end to end: quick selected exactly 76
  tests; default full selected exactly 194; explicit `-IncludeExternal` selected 195, proving the
  single external freshness FQN remains intentional.
- Rust-nightly preflight: installed/updated `nightly` without changing the default/override and
  recorded rustc `1.100.0-nightly`, commit
  `fb6531d550e0075b9eb9a51464f404805eec87d9`, in the session manifest. Full tests validated and used
  that exact manifest through process-only `RUSTUP_TOOLCHAIN=nightly`.
- Child environment normalization: all 5
  `EnvironmentExtensionsTests.OverrideWithEnvironmentBlockTests` cases passed after VSTest launched
  with a lowercase `windir` copied from the process/SystemRoot value.
- Full assembly tests: 190/194 passed. Two Cargo test-suite cases select build-script `out`
  executables instead of `deps` executables, `TestGetBinAndLibPathsAsync` assumes an older nightly
  sysroot file layout, and one ApprovalTests executor snapshot remains version/timing brittle. These
  remain visible; no product or approval snapshot was blindly accepted.
- Standalone adapter harness: parsed/validated 18 TRX results safely under StrictMode, then failed
  with an actionable approved-output diff because current nightly panic text includes transient
  process IDs. Matching approved content follows the null-safe success path, but feature 001 does not
  normalize or auto-approve this brittle output; feature 002 P0 owns that design.
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
- Repository revalidation after the transitional classification change: Release build exited 0 with
  existing `MSB3277`/copy-retry warnings, and quick tests remained 76/76.

### Precise red baseline handed to feature 002 P0

- Release build: exit 0, with existing `MSB3277` assembly conflicts.
- Lint: exit 0 under feature 001's sole `MSB3277` grandfather; every other warning/error remains
  fatal.
- Quick: 76/76 passed; transitional discovery is 76 unit / 118 integration / 1 external.
- Full command: exit 1.
  - Assembly tests: 190/194 passed.
  - Failures:
    `ToolchainServiceTests.GetTestSuiteTestsAsync(hello_world)`,
    `ToolchainServiceTests.GetTestSuiteTestsAsync(hello_library)`,
    `ToolchainServiceExtensionsTests.TestGetBinAndLibPathsAsync`, and
    `TestExecutorTests.RunTestsTestsAsync(hello_library)`.
  - Standalone VSTest produced 18 results (11 passed, 4 expected failures, 3 skipped), then the
    approved-output comparison failed on transient nightly panic thread IDs.
- DRY/mutation/CRAP: disabled as `none`; no feature-001 acquisition, execution, or baseline claim.
- CI remains fail-open in the pre-existing workflow (`continue-on-error`) and does not yet match the
  local Bhaskar gate.

These outcomes must remain visible and unquarantined. Feature 002 S0/P0, not feature 001, owns making
them green.
