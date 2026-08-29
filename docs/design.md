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
- Thread-affinity tests bind `JoinableTaskContext` to an explicit owner and invoke off-thread
  callbacks on a distinct joined `Thread`; `Task.Run` does not prove thread identity.

The Visual Studio layer depends on the test-adapter/core services. Core Cargo and test behavior does
not depend on Visual Studio UI types except where VSTest contracts are intrinsic to the adapter.

## Build and canonical project outputs

`.github/scripts/Invoke-Build.ps1` is the sole local and CI build entry point. Its custom
`RaVsProjectOutputRoot` property activates `src/Directory.Build.props`, which gives every project one
writer-owned `_built\projects\<project>` output namespace.

Canonical output means an exact named deliverable or explicitly curated file set at its owning
project path, never every file in a project directory:

- `_built\projects\RustAnalyzer\RustAnalyzer.vsix`
- `_built\projects\RustDevelopmentPack\RustDevelopmentPack.vsix`
- the six `_built\projects\RustAnalyzer.TestAdapter\` inputs named by
  `src/RustAnalyzer.TestAdapter/testadapter-package.txt`
- `_built\projects\<test-project>\KS.<test-project>.dll` for the three test assemblies
- `_built\projects\RustAnalyzer.UnitTests\xunit.console.exe` as the sole test runner

Consumers resolve those exact owner paths. Directory enumeration is not a consumer contract;
unreferenced stale siblings are noncanonical and cannot satisfy a gate.

## Dependency and payload closure

The extension compiles against the stable Visual Studio 17.12 API floor. Visual Studio 2026 supports
API version 17.x for VSIX extensions and preserves stable APIs under its
[API-version compatibility model](https://learn.microsoft.com/en-us/visualstudio/extensibility/migration/extension-compatibility?view=visualstudio).
No Visual Studio 18 runtime or API package is part of the product closure.

| Dependency | Previous | Current ownership |
|---|---:|---|
| `Microsoft.VisualStudio.SDK` | 17.11.40262 in main and pack | [17.12.40392](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) in main; removed from the code-free pack |
| `Microsoft.VSSDK.BuildTools` | 17.11.435 | [18.9.820](https://www.nuget.org/packages/Microsoft.VSSDK.BuildTools/18.9.820), build-only, in main and pack |
| `Microsoft.VisualStudio.SDK.Analyzers` | 17.7.41 in main and pack | [17.7.122](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK.Analyzers/17.7.122), direct in main and BuildTools-owned in pack |
| Visual Studio Threading and analyzers | 17.11.20 | [17.12.19](https://www.nuget.org/packages/Microsoft.VisualStudio.Threading/17.12.19); host runtime, build-owned analyzers |
| TestPlatform ObjectModel and Test SDK | 17.11.0 | [17.12.0](https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/17.12.0); host ObjectModel and test-only SDK |
| `Community.VisualStudio.Toolkit.17` | 17.0.522 | [17.0.551](https://www.nuget.org/packages/Community.VisualStudio.Toolkit.17/17.0.551), extension-owned |
| `Community.VisualStudio.VSCT` | 16.0.29.6 | [16.0.29.14](https://www.nuget.org/packages/Community.VisualStudio.VSCT/16.0.29.14), build-only |
| `System.ComponentModel.Composition` | 9.0.0 preview | [8.0.0](https://www.nuget.org/packages/System.ComponentModel.Composition/8.0.0), stable host/framework contract |
| `System.Collections.Immutable` | 7.0.0 | [8.0.0](https://www.nuget.org/packages/System.Collections.Immutable/8.0.0), SDK-aligned and TestAdapter-owned |
| Language Server Client | loose 17.11 assembly | [17.12.48](https://www.nuget.org/packages/Microsoft.VisualStudio.LanguageServer.Client/17.12.48), host contract |
| Workspace contracts | five loose 17.11 assemblies | 17.12.19 official Workspace package family; host contracts |

### Restored dependency classification

The seven current `project.assets.json` graphs contain 246 distinct package/version entries. The
ledger below applies its sections in order, so each entry maps once: 33 direct, 81 transitive host
contracts, 11 transitive build/analyzer tools, 7 conflict-sensitive transitive versions, 12
delivered transitives, and 102 ordinary grouped-family entries. The sum is 246 and the unclassified
count is zero. Shared
transitives count once, not once per project.

Consumer abbreviations are `M` = `RustAnalyzer/net48`, `A` =
`RustAnalyzer.TestAdapter/netstandard2.0`, `R` = `RustAnalyzer.Remote/netstandard2.0`, `U` = all
three `*.UnitTests/net48` projects, and `P` = `RustDevelopmentPack/net472`. Evidence abbreviations
are `A` = restored assets and selected asset paths, `B` = zero-warning Release build and conflict
log, `I` = compiled IL references, `V` = VSIX/TestAdapter archive entries, and `T` = assembly and
acceptance tests. Linked package names are the official NuGet metadata.

#### Direct package entries (33)

| Package/version | Consumers | Role; owner; delivery | Disposition and evidence |
|---|---|---|---|
| [ApprovalTests 5.8.0](https://www.nuget.org/packages/ApprovalTests/5.8.0) | U | test; test; excluded | Unchanged test contract; net48 passes. A/T |
| [AutoMapper 10.1.1](https://www.nuget.org/packages/AutoMapper/10.1.1) | M/A/R/U | compile/runtime; extension and TestAdapter; main VSIX | Unchanged application contract; compatible net48/netstandard2.0 assets. A/B/V/T |
| [Community.VisualStudio.Toolkit.17 17.0.551](https://www.nuget.org/packages/Community.VisualStudio.Toolkit.17/17.0.551) | M and dependent U | compile/runtime; extension; main VSIX | Selected current VS17 toolkit compatible with the 17.12 floor. A/B/I/V/T |
| [Community.VisualStudio.VSCT 16.0.29.14](https://www.nuget.org/packages/Community.VisualStudio.VSCT/16.0.29.14) | M | build; build; excluded | Selected stable VSCT generator; build-only assets execute under MSBuild. A/B |
| [DalSoft.RestClient 4.4.1](https://www.nuget.org/packages/DalSoft.RestClient/4.4.1) | M/A/R/U | compile/runtime; extension and TestAdapter; main VSIX | Unchanged application contract; compatible assets pass. A/B/V/T |
| [Ensure.That 9.2.0](https://www.nuget.org/packages/Ensure.That/9.2.0) | M/A/R/U | compile/runtime; extension and TestAdapter; both payloads | Unchanged validation contract; compatible assets pass. A/B/V/T |
| [FluentAssertions 6.12.0](https://www.nuget.org/packages/FluentAssertions/6.12.0) | U | test; test; excluded | Unchanged assertion contract; net48 passes. A/T |
| [FluentAssertions.Analyzers 0.33.0](https://www.nuget.org/packages/FluentAssertions.Analyzers/0.33.0) | U | analyzer; build/test; excluded | Unchanged analyzer; Release build passes. A/B |
| [Microsoft.ApplicationInsights 2.22.0](https://www.nuget.org/packages/Microsoft.ApplicationInsights/2.22.0) | M/A/R/U | compile/runtime; extension and TestAdapter; both payloads | Unchanged telemetry contract; compatible assets pass. A/B/V/T |
| [Microsoft.NET.Test.Sdk 17.12.0](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.12.0) | U | test/build; test; excluded | Aligned with TestPlatform 17.12; discovery and execution pass. A/B/T |
| [Microsoft.SourceLink.GitHub 8.0.0](https://www.nuget.org/packages/Microsoft.SourceLink.GitHub/8.0.0) | M/A/R/U | build; build; excluded | Unchanged deterministic-source tooling. A/B |
| [Microsoft.TestPlatform.ObjectModel 17.12.0](https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/17.12.0) | M/A and dependent U | compile host contract; host; excluded | Selected 17.12 contract; runtime excluded and IL remains ObjectModel 15.0. A/B/I/V/T |
| [Microsoft.VisualStudio.LanguageServer.Client 17.12.48](https://www.nuget.org/packages/Microsoft.VisualStudio.LanguageServer.Client/17.12.48) | M and dependent U | compile host contract; host; excluded | Replaces loose 17.11 binary at the API floor. A/B/I/V/T |
| [Microsoft.VisualStudio.SDK 17.12.40392](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) | M and dependent U | compile host contracts; host; excluded | Selected exact 17.12 SDK closure; runtime excluded. A/B/I/V/T |
| [Microsoft.VisualStudio.SDK.Analyzers 17.7.122](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK.Analyzers/17.7.122) | M; transitively P | analyzer; build; excluded | Selected stable analyzer owned directly by M and by BuildTools in P. A/B |
| [Microsoft.VisualStudio.Threading 17.12.19](https://www.nuget.org/packages/Microsoft.VisualStudio.Threading/17.12.19) | M and dependent U | compile host contract; host; excluded | Selected exact 17.12 contract; runtime excluded. A/B/I/V/T |
| [Microsoft.VisualStudio.Threading.Analyzers 17.12.19](https://www.nuget.org/packages/Microsoft.VisualStudio.Threading.Analyzers/17.12.19) | M | analyzer; build; excluded | Aligned analyzer; Release build passes. A/B |
| [Microsoft.VisualStudio.Workspace.Extensions.VS 17.12.19](https://www.nuget.org/packages/Microsoft.VisualStudio.Workspace.Extensions.VS/17.12.19) | M and dependent U | compile host contract; host; excluded | Replaces loose 17.11 contract at the API floor. A/B/I/V/T |
| [Microsoft.VisualStudio.Workspace.VSIntegration 17.12.19](https://www.nuget.org/packages/Microsoft.VisualStudio.Workspace.VSIntegration/17.12.19) | M and dependent U | compile host contract; host; excluded | Supplies the Contracts assembly; private implementation is not exposed. A/B/I/V/T |
| [Microsoft.VSSDK.BuildTools 18.9.820](https://www.nuget.org/packages/Microsoft.VSSDK.BuildTools/18.9.820) | M/P | build/packaging; build; excluded | Build-only CLR4 tasks run on VS17 MSBuild; no product lib/runtime asset. A/B/V |
| [Moq 4.20.70](https://www.nuget.org/packages/Moq/4.20.70) | U | test; test; excluded | Unchanged mocking contract; net48 passes. A/T |
| [NETStandard.Library 2.0.3](https://www.nuget.org/packages/NETStandard.Library/2.0.3) | A/R | compile framework contract; framework; excluded | Unchanged netstandard2.0 reference closure. A/B |
| [Newtonsoft.Json 13.0.3](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3) | M/A/R/U | compile/runtime; extension and TestAdapter; excluded from curated payloads | Unchanged contract; no selected payload requires private delivery. A/B/V/T |
| [StyleCop.Analyzers 1.2.0-beta.556](https://www.nuget.org/packages/StyleCop.Analyzers/1.2.0-beta.556) | M/A/R/U | analyzer; build; excluded | Unchanged repository analyzer; Release build passes. A/B |
| [SvSoft.MSBuild.CheckUnnecessaryUsings 1.0.1](https://www.nuget.org/packages/SvSoft.MSBuild.CheckUnnecessaryUsings/1.0.1) | M/A/R/U | analyzer/build task; build; excluded | Unchanged Release enforcement tool. A/B |
| [System.Collections.Immutable 8.0.0](https://www.nuget.org/packages/System.Collections.Immutable/8.0.0) | M/A and dependent U | compile/runtime; TestAdapter; TestAdapter ZIP only | SDK-aligned selection; standalone adapter owns runtime delivery. A/B/I/V/T |
| [System.ComponentModel.Composition 8.0.0](https://www.nuget.org/packages/System.ComponentModel.Composition/8.0.0) | M/A and dependent U | compile host/framework contract; host; excluded | Stable contract replaces preview 9; runtime excluded and IL remains 4.0. A/B/I/V/T |
| [System.Linq.Async 6.0.1](https://www.nuget.org/packages/System.Linq.Async/6.0.1) | M/A/R/U | compile/runtime; extension and TestAdapter; main VSIX | Unchanged application contract; compatible assets pass. A/B/V/T |
| [System.Security.Principal.Windows 5.0.0](https://www.nuget.org/packages/System.Security.Principal.Windows/5.0.0) | A and dependent U | compile host/framework contract; host; excluded | Unchanged TestAdapter contract; runtime excluded. A/B/I/V/T |
| [xunit 2.9.0](https://www.nuget.org/packages/xunit/2.9.0) | U | test; test; excluded | Unchanged test framework; assembly gate passes. A/T |
| [xunit.analyzers 1.15.0](https://www.nuget.org/packages/xunit.analyzers/1.15.0) | U | analyzer; build/test; excluded | Unchanged analyzer; Release build passes. A/B |
| [xunit.runner.console 2.9.0](https://www.nuget.org/packages/xunit.runner.console/2.9.0) | U | test runner; test; sole curated runner | Unchanged runner; only `RustAnalyzer.UnitTests` copies `tools/net472`. A/T |
| [xunit.runner.visualstudio 2.8.2](https://www.nuget.org/packages/xunit.runner.visualstudio/2.8.2) | U | test adapter/build; test; excluded from product payloads | Unchanged in-IDE runner; discovery passes. A/B/T |

#### Other material entries (111)

Every package/version entry in the next table is named exactly once. Compact SDK-family rows share
identical consumers, role, ownership, delivery, disposition, and evidence. Consumers are M and its
dependent `RustAnalyzer.UnitTests/net48` unless shown otherwise.

| Package/version entries | Role; owner; delivery | Disposition and evidence |
|---|---|---|
| `Microsoft.Extensions.Configuration.Abstractions` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Abstractions/2.2.0) |
| `Microsoft.Extensions.Configuration.Binder` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Binder/2.2.0) |
| `Microsoft.Extensions.Configuration` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.Configuration/2.2.0) |
| `Microsoft.Extensions.DependencyInjection.Abstractions` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/2.2.0) |
| `Microsoft.Extensions.DependencyInjection` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/2.2.0) |
| `Microsoft.Extensions.Http` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.Http/2.2.0) |
| `Microsoft.Extensions.Logging.Abstractions` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/2.2.0) |
| `Microsoft.Extensions.Logging` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.Logging/2.2.0) |
| `Microsoft.Extensions.Options` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.Options/2.2.0) |
| `Microsoft.Extensions.Primitives` 2.2.0 | compile/runtime; extension; main VSIX | Toolkit/RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/Microsoft.Extensions.Primitives/2.2.0) |
| `System.ComponentModel.Annotations` 4.5.0 | compile/runtime; extension; main VSIX | RestClient-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/System.ComponentModel.Annotations/4.5.0) |
| `System.Text.Encodings.Web` 8.0.0 | compile/runtime; extension; main VSIX | Toolkit-owned compatible asset. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/System.Text.Encodings.Web/8.0.0) |
| `envdte`, `envdte100`, `envdte80`, `envdte90`, `envdte90a` 17.12.40391 | compile/runtime; host; excluded | SDK contracts retained at 17.12. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.ServiceHub.Framework` 4.7.36; `Microsoft.ServiceHub.Resources` 4.4.14194 | compile/runtime; host; excluded | SDK-owned ServiceHub contracts; deterministic 4.7 selection removes the ambient 4.8 conflict. A/B/I/V/T; official [Framework metadata](https://www.nuget.org/packages/Microsoft.ServiceHub.Framework/4.7.36) |
| `Microsoft.VisualStudio.CommandBars` 17.12.40391; `ComponentModelHost` 17.12.215; `Composition` 17.12.18; `CoreUtility` 17.12.215 | compile/runtime; host; excluded | SDK-owned 17.12 contracts. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.Debugger.Interop.10.0`, `.12.0`, `.14.0`, `.15.0`, `.16.0`, `InteropA` 17.12.40391 | compile/runtime; host; excluded | SDK-owned debugger contracts. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.Designer.Interfaces` 17.12.40391; `Editor` 17.12.215; `Extensibility.Editor.Contracts` 17.12.215; `GraphModel` 17.12.40391 | compile/runtime; host; excluded | SDK/editor contracts; explicit suppression covers Extensibility.Editor.Contracts. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.ImageCatalog` 17.12.40391; `Imaging.Interop.14.0.DesignTime` 17.12.40390; `Imaging` 17.12.40391; `Interop` 17.12.40391 | compile/runtime; host; excluded | SDK-owned imaging/interops. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.Language`, `.Intellisense`, `.NavigateTo.Interfaces`, `.StandardClassification` 17.12.215 | compile/runtime; host; excluded | SDK-owned language/editor contracts. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.Linux.ConnectionManager.Store` 17.12.40390; `OLE.Interop` 17.12.40391; `Package.LanguageService.15.0` 17.12.40392; `ProjectAggregator` 17.12.40390 | compile/runtime; host; excluded | SDK-owned contracts; explicit suppression covers ConnectionManager.Store. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.RemoteControl` 16.3.52; `RpcContracts` 17.12.12; `Setup.Configuration.Interop` 3.12.2149 | compile/runtime, Setup build; host/build; excluded | Versions are the exact SDK-owned closure, not independent upgrades. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.Shell.15.0` 17.12.40392; `Shell.Design` 17.12.40392; `Shell.Framework` 17.12.40391 | compile/runtime; host; excluded | SDK-owned shell contracts. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.Shell.Interop`, `.8.0`, `.9.0`, `.10.0`, `.11.0`, `.12.0` 17.12.40391 | compile/runtime; host; excluded | SDK-owned shell interops. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.TaskRunnerExplorer.14.0` 14.0.0; `Telemetry` 17.12.48 | compile/runtime; host; excluded | Exact SDK-owned compatibility contracts. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.Text.Data`, `.Logic`, `.UI`, `.UI.Wpf` 17.12.215 | compile/runtime; host; excluded | SDK-owned editor contracts. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.TextManager.Interop`, `.8.0`, `.9.0`, `.10.0`, `.11.0`, `.12.0` 17.12.40391 | compile/runtime; host; excluded | SDK-owned text-manager interops. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.TextTemplating.VSHost` 17.12.40392; `Utilities` 17.12.40391; `Utilities.Internal` 16.3.90; `Validation` 17.8.8 | compile/runtime; host; excluded | Exact SDK-owned closure; older component versions are upstream SDK selections. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.VCProjectEngine` 17.12.40390; `VSHelp`, `VSHelp80`, `WCFReference.Interop` 17.12.40391; `Web.BrowserLink.12.0` 12.0.0 | compile/runtime; host; excluded | Exact SDK-owned compatibility closure. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `Microsoft.VisualStudio.Workspace`, `.Extensions` 17.12.19 | compile host contracts; host; excluded | Official Workspace family replaces loose binaries; explicitly suppressed. A/B/I/V/T; official [Workspace metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.Workspace/17.12.19) |
| `stdole`, `VSLangProj`, `VSLangProj2`, `VSLangProj80`, `VSLangProj90`, `VSLangProj100`, `VSLangProj110`, `VSLangProj140`, `VSLangProj150`, `VSLangProj157`, `VSLangProj158`, `VSLangProj165` 17.12.40391 | compile/runtime; host; excluded | SDK-owned automation contracts. A/B/I/V/T; official [SDK metadata](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK/17.12.40392) |
| `StreamJsonRpc` 2.20.17 | compile/runtime; host; excluded | Language Server/ServiceHub-owned host contract. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/StreamJsonRpc/2.20.17) |
| `Community.VisualStudio.Toolkit.Analyzers` 1.0.551; `Microsoft.ServiceHub.Analyzers` 4.7.36; `Microsoft.VisualStudio.Composition.Analyzers` 17.12.18 | analyzer; build; excluded | Owning package analyzers execute in Release. A/B; official owning-package metadata above |
| `Microsoft.Build` 17.10.4; `Microsoft.Build.Framework`, `Microsoft.NET.StringTools` 17.12.6 | build contracts/tasks; build; excluded | BuildTools-owned CLR4 task closure. A/B; official [BuildTools metadata](https://www.nuget.org/packages/Microsoft.VSSDK.BuildTools/18.9.820) |
| `Microsoft.Build.Tasks.Git`, `Microsoft.SourceLink.Common` 8.0.0 | build; build; excluded | SourceLink-owned build closure. A/B; official [SourceLink metadata](https://www.nuget.org/packages/Microsoft.SourceLink.GitHub/8.0.0) |
| `Microsoft.CodeCoverage` 17.12.0 | test/build; test; excluded | Test SDK-owned test tooling; not a product dependency. A/B/T; official [metadata](https://www.nuget.org/packages/Microsoft.CodeCoverage/17.12.0) |
| `Microsoft.VsSDK.CompatibilityAnalyzer` 18.9.795 | analyzer; build; excluded | BuildTools-owned analyzer; Release passes at the 17.12 API floor. A/B; official [metadata](https://www.nuget.org/packages/Microsoft.VsSDK.CompatibilityAnalyzer/18.9.795) |
| `StyleCop.Analyzers.Unstable` 1.2.0.556 | analyzer; build; excluded | Implementation package owned by direct StyleCop.Analyzers. A/B; official [metadata](https://www.nuget.org/packages/StyleCop.Analyzers.Unstable/1.2.0.556) |
| `System.Buffers` 4.5.1 | runtime; SDK/TestAdapter; excluded | Selected assembly 4.0.3.0; ambient 4.0.5.0 rejected. A/B/I/V/T; official [metadata](https://www.nuget.org/packages/System.Buffers/4.5.1) |
| `System.Memory` 4.5.4 and 4.5.5 | runtime; owning family/TestAdapter; excluded | Asset graphs retain both family versions; selected product closure is 4.5.5/assembly 4.0.1.2. A/B/I/V/T; official [4.5.5 metadata](https://www.nuget.org/packages/System.Memory/4.5.5) |
| `System.Numerics.Vectors` 4.4.0 and 4.5.0 | runtime; owning family/TestAdapter; excluded | Asset graphs retain both family versions; selected product closure is 4.5.0/assembly 4.1.4.0. A/B/I/V/T; official [4.5.0 metadata](https://www.nuget.org/packages/System.Numerics.Vectors/4.5.0) |
| `System.Runtime.CompilerServices.Unsafe` 5.0.0 and 6.0.0 | runtime; owning family/TestAdapter; excluded | Asset graphs retain both family versions; selected product closure is 6.0.0/assembly 6.0.0.0. A/B/I/V/T; official [6.0 metadata](https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/6.0.0) |

The delivered rows above comprise 12 package/version entries, the host rows 81, the build/analyzer
rows 11, and the four conflict-family rows 7 versions. Each name/version is stated explicitly;
family text only avoids repeating identical ownership and evidence.

#### Grouped transitive families (102)

These ordered boundaries exclude every entry already listed above. Each row is one classification
group with the exact restored version set.

| Boundary and count | Consumers | Role; owner; delivery | Disposition and evidence |
|---|---|---|---|
| ApprovalTests descendants `ApprovalUtilities`, `DiffEngine`, `EmptyFiles`, `TextCopy` (4 entries; 5.8.0, 11.0.0, 4.1.0, 6.2.0) | U | test; test; excluded | Unchanged subtree; net48 tests pass. A/T |
| Moq descendant `Castle.Core` (1 entry; 5.1.1) | U | test/runtime; test; excluded | Unchanged Moq subtree; tests pass. A/T |
| xUnit descendants matching `xunit.*` except the four direct rows (5 entries; 2.0.3, 2.9.0) | U | test; test; excluded | Unchanged xUnit subtree and sole-runner policy pass. A/T |
| ServiceHub serialization descendants `MessagePack`, `MessagePack.Annotations`, `Nerdbank.Streams` (3 entries; 2.5.168, 2.11.79) | M and dependent U | compile/runtime; host; excluded | SDK/ServiceHub-owned compatible assets. A/B/I/V/T |
| Remaining `Microsoft.Bcl.AsyncInterfaces` 6.0.0 (1 entry) | A/R | compile/runtime; owning root; excluded | Compatible netstandard2.0 asset. A/B/I/V/T |
| Remaining `Microsoft.Bcl.AsyncInterfaces` 7.0.0 (1 entry) | Remote and TestAdapter U | test runtime; test; excluded | Compatible net48 test asset. A/B/T |
| Remaining `Microsoft.Bcl.AsyncInterfaces` 8.0.0 and `System.Text.Json` 8.0.5 (2 entries) | M and its U | compile/runtime; host/owning root; excluded | Compatible SDK closure; no private delivery. A/B/I/V/T |
| Remaining `Microsoft.Extensions.DependencyInjection.Abstractions` 7.0.0 (1 entry) | U | test runtime; test; excluded | Compatible net48 test asset. A/B/T |
| Every other unlisted transitive in `Microsoft.CSharp`, `Microsoft.IO.Redist`, `Microsoft.NETCore.*`, `Microsoft.Win32.*`, `runtime.*`, and `System.*`; M/A/R/U consumer set (9 entries; 4.3.0, 4.3.4, 4.5.4) | M/A/R/U | framework support; framework/owning root; excluded | Compatible common support assets. A/B/I/V/T |
| Same boundary; M/A/R and main U consumer set (3 entries; 1.1.1, 4.7.0) | M/A/R and main U | framework support; framework/owning root; excluded | Compatible shared support assets. A/B/I/V/T |
| Same boundary; M and U consumer set (1 entry; 5.0.0) | M/U | framework support; framework/owning root; excluded | Compatible net48 support asset. A/B/I/V/T |
| Same boundary; M and main U consumer set (23 entries; 1.1.3, 4.3.0, 4.3.2, 4.5.0, 6.0.0, 6.0.1, 8.0.0, 8.0.1) | M and main U | framework support; framework/owning root; excluded | Compatible SDK support assets. A/B/I/V/T |
| Same boundary; A/R and their U consumer set (1 entry; 5.0.0) | A/R and their U | framework support; framework/owning root; excluded | Compatible shared support asset. A/B/I/V/T |
| Same boundary; A/R consumer set (40 entries; 1.1.0, 4.3.0, 4.3.2, 4.7.0) | A/R | framework support; framework/owning root; excluded | Compatible netstandard2.0 reference closure. A/B/I/V/T |
| Same boundary; A and Remote/TestAdapter U consumer set (1 entry; 1.6.0) | A and Remote/TestAdapter U | framework support; framework/owning root; excluded | Compatible reflection metadata asset. A/B/I/V/T |
| Same boundary; Remote/TestAdapter U consumer set (2 entries; 4.5.0, 5.0.0) | Remote/TestAdapter U | test runtime; test; excluded | Compatible net48 test assets. A/B/T |
| Same boundary; all U consumer set (4 entries; 4.3.0, 4.8.5, 7.0.0) | U | test runtime; test; excluded | Compatible net48 test assets. A/B/T |

The complete audit retained the unrelated proven application and test contracts: Ensure.That 9.2.0,
AutoMapper 10.1.1, DalSoft.RestClient 4.4.1, System.Linq.Async 6.0.1, Newtonsoft.Json 13.0.3,
Application Insights 2.22.0, System.Security.Principal.Windows 5.0.0, xUnit 2.9.0 with Visual Studio
runner 2.8.2 and analyzers 1.15.0, FluentAssertions 6.12.0 with analyzers 0.33.0, Moq 4.20.70, and
ApprovalTests 5.8.0. Their resolved target assets build and pass the existing behavior gates, none
owns an ambient conflict, and no newer version was adopted solely for recency.

`RustAnalyzer` remains `net48`. It selects the Toolkit's `net48` asset, the SDK, threading, language
server, Workspace, ServiceHub, and Visual Studio Composition `net472` assets, and the TestPlatform
and Immutable `net462` assets. `RustAnalyzer.TestAdapter` remains `netstandard2.0` and selects only
compatible `netstandard2.0` contract assets; ObjectModel, Composition, and Windows Principal are
compile-only, while Immutable is its runtime-owned dependency. `RustDevelopmentPack` remains
`net472` and consumes only BuildTools build assets. BuildTools 18.9.820 has no product `lib` or
runtime asset; its MSBuild tasks target CLR 4 and the Microsoft.Build 15.1 contract.

Compiled main IL references Visual Studio assemblies at stable 17.0 contracts except Threading and
Language Server Client at 17.12; it references TestPlatform ObjectModel 15.0 and framework
Composition 4.0. TestAdapter IL references ObjectModel 15.0, Composition 4.0, and Immutable 8.0.
There is no post-17.12 host assembly reference.

The SDK owns `Microsoft.VisualStudio.Composition` 17.12.18, `Microsoft.ServiceHub.Framework` 4.7.36,
`System.Buffers` 4.5.1, `System.Memory` 4.5.5, `System.Numerics.Vectors` 4.5.0, and
`System.Runtime.CompilerServices.Unsafe` 6.0.0. Direct Immutable 8.0.0 agrees with the SDK,
Workspace, and Language Server requirements and owns its Memory and Unsafe dependencies in the
standalone TestAdapter closure. The pack has no runtime dependency closure.

MSBuild's installed `AssemblyFolders.config` added the selected Visual Studio installation's
`PublicAssemblies` directory to ResolveAssemblyReference. That mixed the 17.12 package graph with
ambient 17.14 host binaries and produced 18 project-and-assembly `MSB3277` signatures. The repository
disables only that ambient search path through
`AssemblySearchPath_UseAssemblyFoldersConfigFileSearchPath`; explicit references, NuGet assets,
framework references, the GAC, and project outputs remain available. This is dependency-source
isolation, not warning suppression.

| Assembly family | Former package/ambient conflict | Current package and assembly version |
|---|---|---|
| `Microsoft.ServiceHub.Framework` | 4.6.0.0 / 4.8.0.0 | 4.7.36 / 4.7.0.0 |
| `System.Buffers` | 4.0.3.0 / 4.0.5.0 | 4.5.1 / 4.0.3.0 |
| `System.Collections.Immutable` | 7.0.0.0 or 8.0.0.0 / 10.0.0.8 | 8.0.0 / 8.0.0.0 |
| `System.Memory` | 4.0.1.2 / 4.0.5.0 | 4.5.5 / 4.0.1.2 |
| `System.Numerics.Vectors` | 4.1.4.0 / 4.1.6.0 | 4.5.0 / 4.1.4.0 |
| `System.Runtime.CompilerServices.Unsafe` | 6.0.0.0 / 6.0.3.0 | 6.0.0 / 6.0.0.0 |

Each former family appeared in `RustAnalyzer`, `RustAnalyzer.UnitTests`, and
`RustDevelopmentPack`. Removing the pack's unnecessary SDK reference removed all six pack
signatures; deterministic package resolution removed the remaining 12.

Visual Studio, ServiceHub, TestPlatform, Workspace, and Language Server assemblies are host-owned
and absent from both VSIXes. The main package explicitly suppresses the four Workspace contracts and
the two SDK dependencies not covered by VSSDK's standard host-assembly list. The main VSIX owns the
extension, TestAdapter, Community Toolkit, application dependencies, and rust-analyzer payload. The
pack VSIX owns only its seven existing container/manifest/resource entries. The standalone
TestAdapter archive remains exactly:

- `KS.RustAnalyzer.TestAdapter.dll`
- `KS.RustAnalyzer.TestAdapter.pdb`
- `Microsoft.ApplicationInsights.dll`
- `Microsoft.ApplicationInsights.pdb`
- `System.Collections.Immutable.dll`
- `Ensure.That.dll`

### Acquired Visual Studio assemblies

Before this closure, the six removed assemblies had no acquisition URL recorded beyond repository
commit `55a4780`. Their exact legacy identities and hash-verified replacements are:

| Legacy file | Assembly / file / product version | Legacy SHA-256 | Disposition |
|---|---|---|---|
| `Microsoft.VisualStudio.LanguageServer.Client.dll` | 17.11.0.0 / 17.11.32.10169 / 17.11.32+27b96b1bb5.RR | `61155656C882558B576F97A656C53D5CCF7880812B087AC117819226AD884A47` | Deleted; replaced by [NuGet 17.12.48](https://www.nuget.org/packages/Microsoft.VisualStudio.LanguageServer.Client/17.12.48), nupkg SHA-256 `0EEBFE163696740306182C82A199FFB87FC820FEC0414684500ACE3B7D1BD223` |
| `Microsoft.VisualStudio.Workspace.dll` | 17.0.0.0 / 17.11.9.58141 / 17.11.9-preview.1+1de32b53b2.RR | `F569A0E4CCD4072F3EDB2AD284B66A8B81FEBE898C1BB8C09A67916B120C9BE5` | Deleted; replaced by [Workspace 17.12.19](https://www.nuget.org/packages/Microsoft.VisualStudio.Workspace/17.12.19), nupkg SHA-256 `619C472A65F888A74BE352D42E21B278C8EDE15F7BDD2A3604E1CB2FE649D0D9` |
| `Microsoft.VisualStudio.Workspace.Extensions.dll` | 17.0.0.0 / 17.11.9.58141 / 17.11.9-preview.1+1de32b53b2.RR | `CB12DB173257185094BAAA8B686DA8F1406B31F95D922ACFFA13BA66606804E9` | Deleted; replaced by [Workspace.Extensions 17.12.19](https://www.nuget.org/packages/Microsoft.VisualStudio.Workspace.Extensions/17.12.19), nupkg SHA-256 `388E79EF9980704F7393D89EFA54E7B273FF29F896FFD5E2014676E7E46B0E07` |
| `Microsoft.VisualStudio.Workspace.Extensions.VS.dll` | 17.0.0.0 / 17.11.9.58141 / 17.11.9-preview.1+1de32b53b2.RR | `097639DBAF67EDB5525FB96B6CF86AA33E929967E10476D2C8B0B753BA83A145` | Deleted; replaced by [Workspace.Extensions.VS 17.12.19](https://www.nuget.org/packages/Microsoft.VisualStudio.Workspace.Extensions.VS/17.12.19), nupkg SHA-256 `E91836D440BCC169C797E29F5F4D440E99180628B722907BC4F54A3A1199A975` |
| `Microsoft.VisualStudio.Workspace.VSIntegration.Contracts.dll` | 17.0.0.0 / 17.11.9.58141 / 17.11.9-preview.1+1de32b53b2.RR | `D48558304E0FBB3F0813F308E4B2AEA462B687FEA39A44C74E5C3B0FC4181097` | Deleted; replaced by [Workspace.VSIntegration 17.12.19](https://www.nuget.org/packages/Microsoft.VisualStudio.Workspace.VSIntegration/17.12.19), nupkg SHA-256 `2443B8828EBEB85B78BAA0679EEBD8BC7C4E496C5031A99664331C9A375630BA` |
| `Microsoft.VisualStudio.Workspace.VSIntegration.dll` | 17.0.0.0 / 17.11.9.58141 / 17.11.9-preview.1+1de32b53b2.RR | `FFA9ADCC2A53C17788F58A3CBA450F3296D2CC2103D1BBB0E18D5C7402A52783` | Deleted as an unused private implementation; the official VSIntegration package exposes only its Contracts assembly |

`Microsoft.VisualStudio.TestWindow.Interfaces.dll` is the sole retained acquired assembly because no
current Microsoft-owned public NuGet contract exists. Its official source is the
[Visual Studio 2022 17.11.0 fixed Enterprise bootstrapper](https://download.visualstudio.microsoft.com/download/pr/394f0f54-a258-4a53-9479-0356ed9778f6/3f993138caa59984ffa2ffeb53c2dba70dff0f26d239dbd0f7957df4e105cce6/vs_Enterprise.exe)
(SHA-256 `3F993138CAA59984FFA2FFEB53C2DBA70DFF0F26D239DBD0F7957DF4E105CCE6`),
installer package
[`Microsoft.VisualStudio.TestTools.TestPlatform.IDE/17.11.0.2436103`](https://download.visualstudio.microsoft.com/download/pr/0eeac6cc-ba3d-4506-9ad7-935008a2ece2/cab89f22b61e63b248d5046dbd71490a2b8080dddc7424e5bb583edb227075bb/Microsoft.VisualStudio.TestWindow.Setup.vsix)
(SHA-256 `CAB89F22B61E63B248D5046DBD71490A2B8080DDDC7424E5BB583EDB227075BB`),
installed as
`Common7\IDE\CommonExtensions\Microsoft\TestWindow\Microsoft.VisualStudio.TestWindow.Interfaces.dll`.
The retained file is assembly 17.0.0.0, file 17.1100.24.36103, product
17.11.0-beta.24361.3+57c1b9cc225c05f834e9f44f38e417d38ab07877, 60,448 bytes, and SHA-256
`3FF86D869A1ABA4F207CFAC44C26D9DD605071BAB8339E229EFB930AE7AAF6C4`. Its Microsoft Authenticode
signature is valid (signer thumbprint `C2048FB509F1C37A8C3E9EC6648118458AA01780`). `RustAnalyzer`
uses it with copy-local disabled; `RustAnalyzer.UnitTests` copies it for execution; neither VSIX
contains it.

Post-17.12 Visual Studio SDK, threading, TestPlatform, Language Server, and Workspace runtime
packages are rejected because they raise or fail to prove the exact 17.12 API floor. Visual Studio
18 runtime packages are unnecessary under the API compatibility model. BuildTools 18.9.820 is the
only 18.x package because it is build-only and runs under Visual Studio 2022 MSBuild; the conservative
17.12.2069 fallback was therefore not selected. The preview Composition 9 package and the
unverified, obsolete `Microsoft.VisualStudio.TestWindow.Interfaces` 11.0.61030 NuGet package are
rejected. Visual Studio Composition remains the SDK-owned stable 17.12.18 closure rather than an
independent upgrade.

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
