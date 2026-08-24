# System design

This document describes the current repository. Planned hardening and Visual Studio 2026 work is
tracked separately in [feature 002](features/002-hardening-and-vs2026.md); none of that planned
behavior should be inferred to exist today.

## Product and platform

`rust-analyzer.vs` is a Windows Visual Studio Open Folder extension for Rust. The main VSIX currently:

- targets .NET Framework 4.8 and Visual Studio's in-process VSSDK/MEF APIs;
- declares amd64 Visual Studio editions in the manifest with installation range `[17.0,18.0)`;
- rejects Visual Studio versions older than 17.12 at runtime; and
- therefore has a currently known supported baseline of Visual Studio 2022 17.12 or later, below
  version 18.

The test adapter targets .NET Standard 2.0. `RustDevelopmentPack` is a separate packaging project
targeting .NET Framework 4.7.2. Visual Studio 2026 support is not current behavior.

## Projects and dependency direction

- `src/RustAnalyzer` is the VSIX package and Visual Studio integration layer. It owns package
  activation, commands and menus, editor/workspace providers, language-client startup, debugging,
  options, updater UI, release notices, and Visual Studio logging.
- `src/RustAnalyzer.TestAdapter` contains the reusable Cargo, rustup, process, workspace metadata,
  test-container, discovery, and execution implementation. Both the VSIX and VSTest integration use
  it.
- `src/RustAnalyzer.Remote` contains the remote-target support consumed by the extension/test stack.
- `src/RustDevelopmentPack` produces the companion development-pack VSIX.
- The three `*.UnitTests` projects cover the corresponding implementation projects.
- `src/TestProjects` contains Rust workspaces and the standalone
  `run-integrationtests.ps1` VSTest adapter harness.

The Visual Studio layer depends on the test-adapter/core services. Core Cargo and test behavior does
not depend on Visual Studio UI types except where VSTest contracts are intrinsic to the adapter.

## Activation and startup

`RustAnalyzerPackage` is a background-loadable `AsyncPackage` registered for the Visual Studio
`FolderOpened` UI context. `InitializeAsync` records the VS version, establishes the package
`JoinableTaskFactory`, registers commands, and resolves MEF services such as logging, telemetry,
prerequisite checking, and installation.

After package load, the current startup path runs release-note handling, incompatible-extension
detection, prerequisite checks, rust-analyzer installation/update, and update notification. The
prerequisite implementation checks the Visual Studio version and the availability of `rustup.exe`
and `cargo.exe`; some failure paths offer browser/restart behavior. The incompatible-extension path
can disable old Rust extensions and restart Visual Studio. This startup behavior is a known
constraint and is redesigned, but not yet changed, by feature 002.

## MEF, workspace, and language server

The extension exports MEF providers for Open Folder metadata, file scanning, and file contexts.
`MetadataServiceFactory` listens to batched workspace file-system changes and forwards relevant Rust
source, manifest, and test-container changes to `MetadataService`.

`MetadataService` caches Cargo workspaces/packages and raises package and test-container change
events. File scanners and context providers translate that model into Visual Studio Open Folder
indexing, build, test, run, and debug contexts.

`LanguageClient` implements Visual Studio's language-client contracts. It starts the bundled or
installed `rust-analyzer` executable as a child process, selects the open workspace (or the
executable directory) as its working directory, and connects the server's redirected standard input
and output to Visual Studio's LSP broker.

## Cargo, toolchains, and tests

`ToolchainService` and `ToolchainServiceExtensions` are the main Rust command boundary:

- Cargo workspace discovery uses `cargo metadata --no-deps --format-version 1 --offline`.
- Build/test preparation runs Cargo and consumes JSON compiler messages where available.
- Rust test discovery builds test binaries, writes `.rusttests` containers, and invokes each binary
  with nightly-only JSON test-listing options.
- Build, clean, clippy, format, rustup target/toolchain, and toolchain install/override operations run
  as child processes with redirected output.
- Some rustup and Cargo paths still parse human-readable output. Those protocol boundaries are
  planned for hardening in feature 002.

VSTest loads `TestDiscoverer` and `TestExecutor` from the packaged adapter. Discovery reads generated
`.rusttests` containers and exposes Rust tests as VSTest cases; execution runs selected test
executables and translates their results back to VSTest.

## Process and threading boundaries

- Visual Studio package initialization is asynchronous. Calls that touch VS services, UI, registry,
  or output panes marshal through `JoinableTaskFactory` as required.
- The language server, Cargo, rustup, test binaries, and some test helpers are operating-system child
  processes. Standard streams are redirected for protocol and build/test output.
- Workspace and test discovery can fan out work across packages, containers, or executables.
- Metadata and output events use fire-and-forget tasks in several paths. The current
  `TaskExtensions.Forget` does not fully observe/report failures; ownership, cancellation, and async
  failure visibility are feature-002 work.

No long-running repository service exists. F5 on the `RustAnalyzer` project launches a Visual Studio
experimental instance with the VSIX deployed.

## Updater and telemetry

`RlsInstallerService` queries GitHub releases, downloads a rust-analyzer archive, extracts the
executable into the extension area, and records the installed version in the Visual Studio package
registry. Release/update notifications use process or registry state. Offline, integrity,
transaction, and rollback behavior are known constraints captured in feature 002.

`TelemetryService` uses Application Insights and is shared by the extension/test-adapter code.
Telemetry is suppressed in configured/experimental contexts, but the current implementation embeds
connection configuration and derives a machine/user-related identifier. Removing unsafe data and
configuration behavior is planned in feature 002.

## Build, test, and release flow

The repository-local gate commands are defined in `.github/copilot-instructions.md` and implemented
under `.github/scripts`:

1. Release build restores and builds `src/RustAnalyzer.sln` with `DeployExtension=false`, placing
   outputs in repository-local `_built`.
2. The analyzer/style command performs a no-restore Release rebuild with analyzers enabled and
   warnings promoted to errors. Feature 001 temporarily exempts only `MSB3277`; every other warning
   and error remains fatal. Feature 002 must resolve those assembly conflicts and remove the
   exemption.
3. The formatter normalizes trailing whitespace in tracked and non-ignored untracked C#, project,
   script, and configuration text while preserving encoding and the checkout's line-ending
   convention. Generated source and output directories are excluded.
4. The quick test gate uses the reviewed transitional FQN policy in
   `.github/test-classification.json`. It lists all built tests, validates exact prefix counts and
   classification totals, then runs only the 76 explicitly-derived hermetic unit tests.
5. The full test gate validates the current session's preflight-installed Rust nightly, exports
   process-only `RUSTUP_TOOLCHAIN=nightly`, runs the FQN-classified unit + integration set, then runs
   the standalone `src/TestProjects/run-integrationtests.ps1` adapter harness. The
   `RlsReleaseTests` FQN is an explicit manual/scheduled external opt-in because it depends on network
   freshness.
6. DRY, mutation, and CRAP are disabled (`none`) for feature 001 and skipped by Bhaskar; feature 002
   P0 owns their redesign and re-enablement.

Feature 001 establishes an executable, accurately reporting loop; it does not promise a green
pre-existing product/CI baseline. Its completion criterion is successful JARVIS startup/bootstrap,
consumer-only Dave/Bhaskar execution, durable routing, end-to-end command execution with truthful
results, and no committed generated artifacts. Feature 002 S0/P0 owns the current red full gate and
CI. No feature-001 script may suppress, quarantine, auto-approve, or disguise those failures.

Both MSBuild and VSTest are resolved from complete Visual Studio installations reported by
`vswhere.exe`; no Visual Studio edition path is assumed. Feature 001 defaults to Visual Studio major
17 (VS 2022). Gate scripts expose `-VisualStudioMajorVersion` and the module exposes `-MajorVersion`
as explicit overrides for feature-002 compatibility validation; a later completed VS major is never
selected silently.

At every assistant session start, JARVIS/the assistant runs
`.github/scripts/Initialize-AssistantSession.ps1 -AssistantStartup` exactly once after the blocking
identity/placeholder checks pass. The orchestrator generates a cryptographically random token in
memory, writes only its hash with `Owner=assistant` and bootstrap phase, then authorizes the nightly
initializer with the in-memory token. It installs or updates rustup's `nightly` toolchain
without changing the user's default or adding a directory override, then records rustc release, host,
and commit plus cargo version and matching bootstrap provenance. Repeated same-session startup checks
validate/reuse ready state without running rustup install/update; invalid existing state requires a
new assistant-session bootstrap rather than repair.

Dave and Bhaskar never invoke the initializer or install/update nightly. Their recipes only
validate/consume existing state (`Test-SessionBootstrap.ps1` and `Invoke-Tests.ps1`). Missing, stale,
wrong-session, modified, or invalid state fails clearly and returns control to JARVIS; gate scripts
perform no self-healing, acquisition/update, or stale fallback. Consumer validation requires matching
assistant owner, `ready` phase, and token hash; a caller-supplied role string cannot authorize an
initializer.

Feature 001 intentionally leaves test source unchanged. Its FQN manifest is transitional and fails
on any discovered-test total, prefix count, rename, addition, or overlap drift. The legacy
test-project/package import shape is not currently a safe `dotnet test`/Coverlet target. Feature 002
P0 owns redesigning/re-enabling mutation/CRAP and validating that path; S11 owns a durable test
taxonomy and removal of the FQN policy.

`.github/workflows/cdp.yml` currently builds on `windows-2022`, installs nightly Rust, builds and
packages the VSIX/test adapter, runs assembly and standalone integration tests, publishes artifacts,
and conditionally publishes releases. Its current test steps are fail-open and several action
dependencies are not immutable; feature 002 owns making CI and supply-chain enforcement fail closed.

## Generated and external artifacts

Do not hand-edit or add:

- repo-root `_built` (explicitly git-ignored), `**/bin`, `**/obj`, Rust `target`, VSIX, test result,
  or other build outputs;
- session Rust-nightly provenance/manifests under `LOCALAPPDATA`;
- generated command/package sources such as `VSCommandTable.cs`, generated `.g.cs` files, and
  build-generated package files;
- the auto-stamped version field in `src/RustAnalyzer/source.extension.cs`; or
- binaries under `src/external`, including the packaged rust-analyzer executable/PDB, except through
  the repository's intended acquisition/update process.

`source.extension.vsixmanifest` and `VSCommandTable.vsct` are source inputs. MSBuild generates package
metadata, command code, pkgdef, VSIX files, test assemblies, and copied test fixtures.

## Known architectural constraints

- Current manifest/runtime compatibility does not include Visual Studio 2026.
- Startup prerequisite/restart paths can repeat or take persistent action rather than exposing a
  single process-scoped state.
- Rust test listing requires a nightly toolchain.
- Some Cargo/rustup/test protocols are based on human-readable output.
- Child-process lifetime and cancellation ownership are inconsistent.
- Fire-and-forget failures may be lost.
- Updater download/extraction is not transactional or independently integrity-verified.
- Telemetry configuration and identity handling are unsafe.
- Dynamic menu/status and workspace updates can do more repeated work than necessary.
- The current CI test/publish chain is not uniformly fail closed.
- Session nightly install/update is an assistant-only startup responsibility. Dave/Bhaskar gate code
  is local validation/consumption only and cannot authorize the token-protected initializer.
- `MSB3277` is the only lint warning temporarily grandfathered by feature 001; unresolved assembly
  conflicts remain architectural debt until feature 002 removes the exemption.
- Latest-nightly full validation currently exposes Cargo text-protocol and sysroot-layout
  incompatibilities in three integration cases; feature 002 P0 owns those gate-restoring fixes.
- ApprovalTests and standalone approved output remain brittle to incidental version/timing/process
  text; feature 002 P0 owns resilience and forbids automatic approval.
- Unit/integration/external ownership is temporarily encoded as reviewed FQN prefixes and exact
  discovery counts, not test-source traits. Feature 002 S11 must replace it with a durable taxonomy.

The accepted remediation sequence and product decisions for every item above are preserved in
[feature 002](features/002-hardening-and-vs2026.md).
