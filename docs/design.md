# System design

This document describes current behavior and architecture. Planned work lives in
[`backlog.md`](backlog.md) and must not be inferred as implemented.

## Repository operations

- Resolve trunk from `origin/HEAD`; use `master` only when it is unavailable.
- **Generated — never hand-edit:** `**/bin/`, `**/obj/`, `_built/`, `*.vsix`, build-generated
  `.g.cs`, and `src/RustAnalyzer/VSCommandTable.cs` (generated from `VSCommandTable.vsct`). Only
  `.github/scripts/Set-VsixVersion.ps1` writes `Identity/@Version` in
  `src/RustAnalyzer/source.extension.vsixmanifest` and `Version` in
  `src/RustAnalyzer/source.extension.cs`.
- **Acquired — never hand-edit:** packaged `rust-analyzer.exe` and `rust_analyzer.pdb`, plus
  `src/external/vs.17.11` host assemblies. Replace only from official assets after hash verification.
  Only `Initialize-RustNightly.ps1` writes manifests under `%LOCALAPPDATA%\ravsq\`.

## Product and platform

`rust-analyzer.vs` is a Windows Visual Studio Open Folder extension for Rust. It introduces a super
workspace: one folder containing multiple Cargo workspaces.

- targets .NET Framework 4.8 and Visual Studio's in-process VSSDK/MEF APIs;
- declares amd64 Visual Studio editions in the manifest with installation range `[17.0,18.0)`;
- rejects Visual Studio versions older than 17.12 at runtime; and
- is currently validated for Visual Studio 2022 17.12 or later.

The test adapter targets .NET Standard 2.0. `RustDevelopmentPack` is a separate packaging project
targeting .NET Framework 4.7.2. Visual Studio 2026 compatibility is not yet validated.

Build and test tooling binds to a Visual Studio host explicitly rather than by ambient discovery:
`.github/scripts/VisualStudio.psm1` resolves MSBuild and `vstest.console.exe` through `vswhere`,
filtered to a caller-supplied major version that defaults to 17 and to instances reporting a complete
install. Resolution throws when no complete install of that major version is present, so a later
completed Visual Studio major is never selected silently.

## Projects and dependency direction

- `src/RustAnalyzer` is the VSIX package and Visual Studio integration layer. It owns package
  activation, commands and menus, editor/workspace providers, language-client startup, debugging,
  options, updater UI, release notices, and Visual Studio logging.
- `src/RustAnalyzer.TestAdapter` contains the reusable Cargo, rustup, process, workspace metadata,
  test-container, discovery, and execution implementation. Both the VSIX and VSTest integration use
  it.
- `src/RustAnalyzer.Remote` contains the remote-target support consumed by the extension/test stack.
- `src/RustDevelopmentPack` produces the companion development-pack VSIX.
- The three legacy-named `*.UnitTests` projects contain unit and integration assembly tests for the
  corresponding implementation projects; xUnit traits, not project names, classify their boundaries.
  They are legacy non-SDK projects (`ToolsVersion="15.0"`, `TargetFrameworkVersion v4.8`) that take
  their packages through `PackageReference` in `src/KS.Tests.Common.targets`, and are therefore not
  currently a safe `dotnet test`/Coverlet target.
- `src/TestProjects` contains Rust workspaces and the standalone
  `run-integrationtests.ps1` VSTest adapter harness.

The Visual Studio layer depends on the test-adapter/core services. Core Cargo and test behavior does
not depend on Visual Studio UI types except where VSTest contracts are intrinsic to the adapter.

## Build and canonical project outputs

`.github/scripts/Invoke-Build.ps1` is the sole local and CI build entry point. Its custom
`RaVsProjectOutputRoot` property activates `src/Directory.Build.props`, which gives every project one
writer-owned `_built\projects\<project>` closure.

Each project output is canonical. Consumers use exact owning-project paths. In particular:

- `_built\projects\RustAnalyzer\RustAnalyzer.vsix`
- `_built\projects\RustDevelopmentPack\RustDevelopmentPack.vsix`
- `_built\projects\RustAnalyzer.TestAdapter\` for curated TestAdapter package inputs
- `_built\projects\<test-project>\` for each independently probeable test closure

## Activation and startup

`RustAnalyzerPackage` is a background-loadable `AsyncPackage` registered for the Visual Studio
`FolderOpened` UI context. `InitializeAsync` records the VS version, establishes the package
`JoinableTaskFactory`, registers commands, and resolves MEF services such as logging, telemetry,
prerequisite checking, and installation.

After package load, the current startup path runs release-note handling, incompatible-extension
detection, prerequisite checks, rust-analyzer installation/update, and update notification. The
prerequisite implementation checks the Visual Studio version and the availability of `rustup.exe`
and `cargo.exe`; some failure paths offer browser/restart behavior. The incompatible-extension path
can disable old Rust extensions and restart Visual Studio.

Every new automatic/background Rust execution path must have a finite `AutomaticRustPath` member and
pass `PrerequisiteAvailabilityPolicy` before side effects. Every prerequisite state except `Ready`
disables the path.

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
- Build/test preparation runs Cargo and consumes JSON compiler messages. Test executable discovery
  uses `compiler-artifact` records (`profile.test`, target kind, and `executable`) rather than
  human-readable path text, supporting Cargo's legacy `deps` and current build-directory layouts.
- Rust test discovery builds test binaries, writes `.rusttests` containers, and invokes each binary
  with nightly-only JSON test-listing options.
- Build, clean, clippy, format, rustup target/toolchain, and toolchain install/override operations run
  as child processes with redirected output.
- Some rustup and remaining Cargo paths still parse human-readable output.

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
  `TaskExtensions.Forget` does not fully observe or report failures.

No long-running repository service exists. F5 on the `RustAnalyzer` project launches a Visual Studio
experimental instance with the VSIX deployed.

## Updater and telemetry

`RlsInstallerService` queries GitHub releases, downloads a rust-analyzer archive, extracts the
executable into the extension area, and records the installed version in the Visual Studio package
registry. Release/update notifications use process or registry state. Offline operation, integrity
verification, transactional activation, and rollback are unsupported.

`TelemetryService` uses Application Insights and is shared by the extension/test-adapter code.
Telemetry is suppressed in configured/experimental contexts, but the current implementation embeds
connection configuration and derives a machine/user-related identifier.

## Sample Rust Projects

`src\TestProjects` contains the list of various Rust project scenarios (positive & negative) that this extension
supports. More will be added here on demand.
