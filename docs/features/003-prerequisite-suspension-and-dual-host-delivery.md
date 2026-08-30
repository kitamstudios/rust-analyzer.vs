# Feature: Prerequisite Suspension and Dual-Host Delivery
**Branch:** vibe/003-prerequisite-suspension-and-dual-host-delivery
**Status:** In Progress

## Requirements

1. Run one single-flight prerequisite evaluation per `devenv.exe` process when the package activates.
   Cache readiness and complete typed failures in memory only.
2. Check the supported Visual Studio version, `rustup` through the Visual Studio process PATH, a
   configured default Rust toolchain, and operational Cargo from that toolchain and PATH. Nightly is
   not a startup prerequisite.
3. On failure, show all detected failures in a Visual Studio framework Yes/No message box. Its text
   maps `Yes` to `Disable` and `No` to `Help`, with `Yes` focused by default. `No` explicitly opens
   `PREREQUISITES.md` and re-shows the prompt; only `Yes` closes the flow. X, Escape, and equivalent
   close paths cannot dismiss it. Never restart or open a browser automatically.
4. `Disable` suspends rust-analyzer.vs for that process only. It does not unload the VSIX or change
   Extension Manager state.
5. While suspended, no extension feature performs Rust, Cargo, rustup, rust-analyzer, update,
   discovery, execution, run, or debug work. Hide extension-owned user surfaces where supported and
   otherwise disable them; hide dynamic children; make callbacks defensive no-ops. Add no placeholder
   commands or repeat prerequisite dialogs.
6. Automatic/background paths suppress without UI and emit one clear message per logical path per
   process to `Output > rust-analyzer.vs`.
7. After `Disable`, show one warning InfoBar per process:
   `rust-analyzer.vs is disabled for this Visual Studio session. Restart Visual Studio to recheck prerequisites.`
   It has a `View prerequisites` link and close X only, with no Dismiss action or persisted dismissal.
8. A new Visual Studio process starts with empty state and reruns checks. There is no same-process
   retry or automatic re-enable.
9. Support exactly Windows amd64 with Visual Studio 2022 17.12 or later and every Visual Studio
   2026/18.x release. Exact 17.12 passes, older 17.x fails, and unsupported majors fail.
10. Both `rust-analyzer.vs` and `RustDevelopmentPack` advertise the same host matrix. Reduce the pack
    to `rust-analyzer.vs` and TOML Editor; remove VSColorOutput64, Rainbow Braces, and File Icons, and
    validate the exact TOML Editor payload before publication.
11. CI builds canonical artifacts once and provides blocking evidence through deterministic 17.12
    boundary/manifest tests, actual install/startup validation on hosted VS 17.14 and VS 18.x, and
    explicitly selected host-major TestAdapter acceptance.
12. RustDevelopmentPack becomes a first-class CI/CD product: independent base version, the same
    deterministic build suffix as rust-analyzer.vs, build/validation/artifact/dual-host verification,
    and Open VSIX/Marketplace/GitHub Release parity under the main package's existing gates.
13. Rewrite prerequisite and installation documentation using official sources and copyable commands.
    Reconcile every live platform claim and update all four agent roles.
14. Audit the complete direct and transitive production, build, test, and packaging dependency
    closure. This includes VSSDK, Community.VisualStudio, threading, test platform, composition,
    acquired host assemblies, xUnit, FluentAssertions, EnsureThat, Moq, ApprovalTests, Application
    Insights, JSON, and their transitive assemblies. Select only versions proven compatible with
    VS 17.12, VS 18.x, main `net48`, pack `net472`, and TestAdapter `netstandard2.0`.
15. Do not modify `docs/features/002-hardening-and-vs2026.md`.
16. Audit every C# argument-validation site and replace manual guards with `EnsureThat` without
    changing its observable exception contract.
17. Preserve parallel MSBuild while isolating each project's output beneath `_built\projects`.
    Treat only exact named deliverables and curated file sets at owner paths as canonical; update
    every local and CI consumer of the old flat layout without cleaning `_built` or copying outputs
    through staging or promotion directories.

## Design Options (Ox)

### O1 - Process state with explicit boundary gating

- Description: Add one shared process-scoped prerequisite state and guard every inventoried
  extension-owned user and background boundary. Keep reusable TestAdapter/Cargo code independent of
  Visual Studio session state.
- Pros: Smallest design satisfying the UX; explicit visibility/suppression; preserves dependency
  direction; straightforward state, boundary, and logging tests.
- Cons: Correctness depends on a maintained entry-point inventory; guards are distributed across the
  Visual Studio integration boundaries.

### O2 - Process state plus deep Rust-execution firewall

- Description: Add O1 and decorate or replace shared process/toolchain services with a session-aware
  execution gateway.
- Pros: Additional defense against an omitted caller; central subprocess rejection.
- Cons: Broad shared Cargo/TestAdapter refactor; risks Visual Studio coupling; cannot replace
  surface/provider guards; adds complexity without a demonstrated uncovered path.

**Recommended and selected: O1.** The hidden/disabled-surface ruling removes the need for proxies,
placeholder commands, repeat UX, or a general execution firewall. Direct process-launching services
still receive defensive guards at their owning integration boundary.

## Slices (Sx)

| Slice | Outcome | Depends on |
|---|---|---|
| S1 | Ship process-safe prerequisite suspension across automatic, background, and user-triggered extension paths. | - |
| S2 | Ship collision-free project-owned build outputs, a proven dual-host main package, reconciled documentation, and blocking VS17/VS18 evidence. | S1 |
| S3 | Ship one minimal, independently versioned RustDevelopmentPack artifact for both Visual Studio 2022 and Visual Studio 2026. | S2 |
| S4 | Apply the `EnsureThat` argument-validation rule consistently across the C# codebase without behavioral changes. | S3 |

S1 completes the runtime behavior. S2 makes isolated project outputs canonical and proves the main
package on both hosts. S3 distributes one canonical pack containing only
`rust-analyzer.vs` and TOML Editor, without host-specific builds. S4 keeps repository-wide validation
cleanup separate from product delivery.

## Tasks (Tx)

Execute one task at a time in order.

| # | Slice | Task | Status | Commit |
|---|---|---|---|---|
| T1 | S1 | Add the shared process-scoped prerequisite state, immutable cached result, single-flight completion, and state-transition unit tests. | Done | `c87922d` |
| T2 | S1 | Implement complete prerequisite probe classification and the exact VS17/VS18 predicate with unit and process-boundary integration tests. | Done | `3350c13` |
| T3 | S1 | Replace prerequisite failure UX with the non-dismissible mapped Yes/No Visual Studio message box and focused UI tests. | Done | `ee6c813`, `d4125c7` |
| T4 | S1 | Add the one-shot warning InfoBar, explicit navigation, non-persisted close behavior, and tests. | Done | `d4125c7` |
| T5 | S1 | Make prerequisite evaluation the first product operation after package activation and defer all normal startup work until readiness. | Done | `3301b78` |
| T6 | S1 | Gate all automatic/background Rust paths and implement first-suppression-per-path Output logging with tests. | Done | `e84fa0a` |
| T7 | S1 | Hide or disable every extension-owned user surface while unavailable and make execution callbacks defensive no-ops. | Done | `c0b60df` |
| T7b | S2 | Isolate parallel outputs; make exact owner-path deliverables and curated sets canonical; update consumers without staging copies. | Done | `c0b60df` |
| T8 | S2 | Audit and apply the complete newest proven dual-compatible production/build/test/package dependency closure and acquired-artifact provenance policy. | Done | 8b343ea |
| T9 | S2 | Align the main VSIX manifest and metadata and establish the shared `[17.12,19.0)` dual-host validation contract. | Done | `e4db364` |
| T10 | S2 | Rewrite prerequisite/readme material, reconcile live support claims, update `docs/design.md`, and update all four agent roles. | Pending | - |
| T11 | S2 | Add canonical-artifact VS17/VS18 validation and the complete blocking behavior/platform evidence matrix. | Pending | - |
| T12 | S3 | Extend the approved stamper to produce independent main/pack versions with the same deterministic build suffix. | Pending | - |
| T13 | S3 | Reduce RustDevelopmentPack to `rust-analyzer.vs` and TOML Editor, align its manifest, build one canonical artifact, and verify it on both hosts. | Pending | - |
| T13b | S3 | Add a RustDevelopmentPack README covering its two constituents, dual-host support, installation, versioning, source, and licensing. | Pending | - |
| T14 | S3 | Add pack publication to Open VSIX, Marketplace, and GitHub Releases under the main package's existing fail-closed gates. | Pending | - |
| T15 | S4 | Inventory every C# argument-validation site, replace manual guards with `EnsureThat`, and prove exception-contract and build/test parity. | Pending | - |

## Risks (Rx)

- **R1:** An omitted activation boundary could still perform Rust work. Mitigate with an explicit
  inventory, unavailable-by-default state, boundary tests, and defensive guards at direct
  process-launching services.
- **R2:** No hosted image provides exact VS 17.12. Mitigate with a pure host predicate,
  deterministic manifest tests, a 17.12 API baseline, and actual startup on current VS 17.14.
- **R3:** Some Visual Studio surfaces, notably registered option pages, might not support dynamic
  hiding. Disable their controls and prevent initialization work; inability to hide or disable is a
  blocker.
- **R4:** Newer SDK/runtime packages could introduce post-17.12 APIs, binding failures, or framework
  changes. Reject versions without package, analyzer, build, payload, and host evidence.
- **R5:** TOML Editor can change independently. Revalidate its then-current payload manifest before
  publication and fail closed.
- **R6:** Hosted-image labels or installed versions can drift. Assert the resolved host major and
  minimum version with `vswhere`; never accept an ambient fallback.
- **R7:** External registries are not transactional. Prevalidate both products before the first
  external write, publish the dependency before the pack, and fail visibly on partial publication.
- **R8:** VS2026's compatibility model may ignore a manifest upper bound. Keep the runtime major
  predicate authoritative and test unsupported-major rejection.
- **R9:** Extension-pack resolution is gallery-based and not version-pinned. Publish
  rust-analyzer.vs before RustDevelopmentPack and record constituent versions in publication evidence.
- **R10:** `EnsureThat` substitutions can alter exception type, parameter name, validation order, or
  message. Preserve each public contract and add focused regression coverage where behavior differs.
- **R11:** Parallel projects writing common dependencies into one flat output directory can race or
  lock files. Isolate project outputs so every canonical path has exactly one project writer.

## Assumptions (Ax)

- **A1:** Microsoft's documented VS2026 model continues to support the stable VS 17.x extension API
  and evaluates the installation target by its lower bound.
- **A2:** `windows-2022` supplies current VS 17.14 and `windows-2025-vs2026` supplies VS 18.x; CI
  verifies rather than assumes those versions.
- **A3:** TOML Editor retains one compatible Marketplace payload for both supported hosts at
  implementation time.
- **A4:** Logging, diagnostics, prerequisite UX, and explicit prerequisite navigation may operate
  while suspended.
- **A5:** Existing release/tag semantics remain based on the main product's version; the independently
  versioned pack attaches to that release.
- **A6:** Existing branch, ref, release, and manual-publication predicates are safe and extended, not
  redefined.
- **A7:** This feature changes no public data schema or reusable TestAdapter contract.
- **A8:** The approved test boundary is extension-owned Rust discovery/execution; generic Visual
  Studio test commands remain untouched.

## Deferrals (Dx)

- **D1:** Persistent disablement, VSIX unloading, or Extension Manager changes.
- **D2:** Automatic prerequisite installation or automatic PATH mutation.
- **D3:** Automatic or user-requested Visual Studio restart from prerequisite UX.
- **D4:** Same-process prerequisite retry or automatic re-enable.
- **D5:** Background modal prompts, repeated prerequisite dialogs, or placeholder commands.
- **D6:** A pinned VS 17.12 hosted runner.
- **D7:** A general Rust-execution firewall or deferred runtime-composition architecture.
- **D8:** Interception of generic Visual Studio Test Explorer commands or Visual Studio state in
  reusable adapter code.
- **D9:** Optional dependency modernization not needed for dual-host correctness.
- **D10:** Changes to the existing incompatible-extension restart path on a prerequisite-ready startup.
- **D11:** Support claims for Visual Studio majors other than 17 and 18.
- **D12:** Any edit to the archived prior feature document.
- **D13:** The real-Visual-Studio E2E harness analyzed below. The human chose to record the analysis
  but not adopt its companion VSIX, UI Automation, scenarios, or publication gate in this feature.

## Notes & Decisions

**Pull request:** [#73](https://github.com/kitamstudios/rust-analyzer.vs/pull/73)

### Development Pack composition ruling

- The pack contains only `rust-analyzer.vs` and
  [TOML Editor](https://marketplace.visualstudio.com/items?itemName=MadsKristensen.TomlEditor).
- Rainbow Braces is excluded because its current provider does not target Rust and conflicts with
  built-in brace colorization. File Icons is excluded because it duplicates this extension's Rust
  icon registrations. VSColorOutput64 remains excluded because publisher-backed VS2026 support is
  unproven.
- General-purpose and terminal extensions remain optional user choices. T13 revalidates the exact
  TOML Editor payload and the one canonical pack artifact on both supported hosts.

### T7b build-output design ruling

- A conditional `src/Directory.Build.props` gives every project one output directory beneath
  `_built\projects`; ordinary IDE builds retain their existing layout.
- `Invoke-Build.ps1` performs one parallel Release solution build. It neither cleans `_built` nor
  copies project outputs through `_built\stage` or `_built\artifacts`.
- Canonical output means an exact named deliverable or explicitly curated file set at its owning
  `_built\projects\<project>` path. The directory is an output namespace, not a consumer contract;
  consumers never enumerate it or fall back to the former flat layout.
- Test closures remain separated by project for dependency probing. One project owns the xUnit
  runner; TestAdapter packaging reads the TestAdapter project's output directly.
- T7b changes no dependency, package composition, version, host matrix, or MSB3277 policy.

### T1 outcome

- The state, failure, result, and status types are public because the human forbids C# `internal`;
  helper implementation remains private and no friend assembly is used. Their CLR surface is now a
  compatibility concern.
- The first caller owns the shared evaluator and cancellation token. T5 uses the package-lifetime
  token, never a transient provider token.
- T2/T5 keep package activation through the prerequisite service as the sole production evaluation
  initiator. T2 converts ordinary probe failures into typed completed results; completed failures do
  not retry in-process.
- T5/T6 add a read-only completion path only if wiring proves background callers need one. They do not
  join by supplying substitute evaluators.
- T7 consumes the state directly and adds no readiness facade unless a Visual Studio boundary
  requires it.
- T11 still proves process reset in real Visual Studio; T1 unit coverage is not a substitute.
- The Release build added no warning code or MSB3277 conflict signature. The unchanged 18-signature
  baseline remains visible and T8 owns its resolution.

### T2 outcome

- The evaluator returns stable typed failures for host, rustup, default-toolchain, and Cargo outcomes,
  suppresses only dependent failures, excludes nightly, and admits exactly VS 17.12+ and VS 18.x.
- Prerequisite child processes resolve through the Visual Studio PATH and force child-only
  `RUSTUP_AUTO_INSTALL=0`; probes never install, acquire from the network, or mutate the parent
  environment.
- A fail-closed isolated integration helper proves the enabled-parent override through the production
  probe without mutating xUnit process state or adding a test seam to production.
- T3/T5 preserve cancellation but convert unexpected non-cancellation probe faults into a cached typed
  diagnostic failure before passing the result to process state.
- When the new evaluator becomes live, remove the legacy prerequisite dictionary, checks, service
  dependency, and commented toolchain check together. Do not revive or delete only the historical
  fragment.
- T5 keeps package activation as the sole production evaluation initiator; all other boundaries only
  observe or await cached state.

### T3 outcome

- The human superseded the custom `DialogWindow` after T3's first commit. The Visual Studio framework
  Yes/No message box owns DPI, theme, sizing, accessibility, modality, and shell integration.
- Dialog text maps `Yes` to process-only Disable and `No` to Help. `Yes` is the default. `No`
  explicitly opens prerequisites and then re-shows the prompt, so only `Yes` releases Visual Studio.
- The immutable model owns ordered content and action explanations; the controller owns
  state/navigation and the framework message-box loop.
- T4 shows the InfoBar only after the modal returns with state `Suspended`; shutdown closure leaves
  state `Failed` and creates no InfoBar.
- T5 performs no startup or Rust work after the dialog unless state is `Ready`. Shutdown-driven
  closure terminates activation, converts unexpected non-cancellation probe faults to typed failures,
  and removes the complete legacy check/restart path when the new flow becomes authoritative.
- T11 still validates shell ownership, theming, modal behavior, and shutdown closure in real VS17 and
  VS18 processes.

### T4 outcome

- The process singleton creates at most one suspended-session InfoBar attempt. Ineligible early calls
  do not consume it; success, close, or failure cannot recreate it.
- The InfoBar contains only the exact warning text, warning icon, identity-bound
  `View prerequisites` link, and framework X. Close or show failure detaches handlers, unadvises the
  COM sink, and disposes the adapter.
- T5 evaluates through process state, shows the prompt for typed failure, shows the InfoBar only after
  `Suspended`, and stops startup for both `Suspended` and exceptional/shutdown `Failed`.
- T5 catches and logs InfoBar failures without undoing suspension, retrying UI, showing another
  modal, or withholding control from Visual Studio.
- T6 folds InfoBar failure diagnostics into the same non-spamming Output policy. T11 validates real
  VS17/VS18 prompt close behavior, InfoBar placement/action routing, and close cleanup.

### T5 outcome

- Package activation is the sole production evaluation initiator. Prerequisites run before release,
  incompatible-extension, installer, or update work; only `Ready` continues that ordered sequence.
- Typed failure enters the framework prompt. `Suspended` attempts one InfoBar and stops; exceptional
  or shutdown `Failed` stops without InfoBar. InfoBar failure is logged/reported once and cannot
  retry, undo suspension, repeat the modal, or resume startup.
- The Visual Studio host lookup occurs exactly once inside the authoritative probe. Its result
  supplies process-only `RAVsVersion` telemetry metadata; readiness depends only on process state.
- Unexpected faults, including `OperationCanceledException` with an uncanceled package token, become
  logged and telemetered typed diagnostic failures. Genuine package-token cancellation performs no
  UI/startup work, caches no terminal state, and remains retryable.
- The complete legacy prerequisite dictionary/check/browser/restart path and obsolete MEF imports are
  removed together. The separate ready-path incompatible-extension restart remains unchanged.
- T6 only observes or awaits existing state and logs the first suppression per named background path.
  T7 treats every non-Ready state as unavailable and rechecks immediately before Rust work.

### T6 outcome

- A process-shared availability policy treats every non-`Ready` state as unavailable and records one
  suspension transition, one first suppression per finite `AutomaticRustPath`, and one InfoBar
  failure diagnostic.
- Package activation remains the sole evaluator initiator. Attempt completion is immutable, while
  long-lived observers follow later status generations so pre-evaluation construction and
  canceled/faulted retries cannot permanently miss `Ready`.
- Language, metadata, watcher, scanner, Open Folder, test-discovery, debug/run, installer/updater,
  node/property, toolchain-enumeration, and package follow-on boundaries guard before Rust-related
  effects. Reusable TestAdapter/Cargo code remains independent of Visual Studio prerequisite state.
- Metadata and test-discovery services remain inert until `Ready`, initialize once, detach external
  handlers on disposal, and drain admitted callbacks before disposing owned state. Language-client
  stop likewise prevents late activation.
- The human removed the T6 source-scanning inventory test. Direct compiled behavior proves
  toolchain status enumeration has no downstream effect in every non-`Ready` state and preserves the
  `Ready` path; `docs/design.md` records the maintenance invariant for future boundaries.
- Full verification passed 338 assembly tests and all 18 acceptance expectations. The Release build
  added no warning code or MSB3277 project/assembly signature; T8 still owns the existing baseline.
- T7 owns user-surface visibility and defensive explicit command callbacks.

### T7 outcome

- Only `Ready` advertises or executes extension-owned user surfaces. Status queries synchronously
  hide or disable unavailable commands, dynamic children are empty, and callbacks independently
  recheck readiness before telemetry, service resolution, mutation, or external effects.
- Persistent command objects restore every status bit on `Ready`; Cargo Clippy and Fmt cannot remain
  unsupported after an unavailable query.
- The registered Options page remains a standard Visual Studio `DialogPage`. It is read-only and
  effect-free while unavailable, promotes its cached property grid exactly once on `Ready`, and
  prevents promotion after disposal.
- Editor, Open Folder, test, run/debug, node/property, update, release, installer, and keybinding
  routes fail closed without repeating prerequisite UI. Prerequisite Help and InfoBar navigation
  remain available.
- S1 is complete. Full verification passed 397 assembly tests and all 18 acceptance expectations
  without adding a warning code or MSB3277 conflict signature.

### T7b outcome

- One parallel Release solution build writes each project directly to its
  `_built\projects\<project>` output namespace through conditional `Directory.Build.props`; normal
  IDE output paths remain unchanged.
- `Invoke-Build.ps1` performs no cleanup, staging, promotion, archive creation, or second build.
  Tests, CI, package, and publication paths consume the two named VSIXes, three exact test
  assemblies, sole xUnit runner, and six TestAdapter files named by `testadapter-package.txt` at
  their owner paths, with no flat-layout or staging fallback.
- Directory enumeration is not a consumer contract. Unreferenced stale siblings are noncanonical
  and cannot satisfy a gate; project output directories need not be pristine.
- Cargo fixtures are copied only by the two consuming test projects. TestAdapter packaging performs
  owner-local compression and acceptance expansion from its curated file set.
- The normal full gate's parallel build emitted no copy-retry warning. Hosted artifact transport
  remains T11 evidence rather than a T7b claim.

### T8 outcome

- The main extension now uses the exact Visual Studio 17.12 SDK/runtime closure: SDK 17.12.40392,
  threading and Workspace 17.12.19, Language Server Client 17.12.48, and TestPlatform 17.12.0.
  BuildTools 18.9.820 is retained only as build tooling; no Visual Studio 18 runtime/API package was
  introduced. Main `net48`, pack `net472`, and TestAdapter `netstandard2.0` are unchanged.
- Six undocumented loose assemblies were removed in favor of official NuGet contracts. The sole
  retained TestWindow contract has an official fixed-installer source, exact identity and hash,
  copy-local ownership, and compatibility rationale recorded in `docs/design.md`.
- ResolveAssemblyReference no longer mixes the package closure with ambient installed
  `PublicAssemblies`. This removed all 18 former `MSB3277` project-and-assembly signatures without
  suppression, binding redirects, direct compatibility pins, serial builds, or output changes.
- A parallel Release build on complete Visual Studio 2022 17.14.37531.7 produced zero warnings and
  zero errors. The installed Visual Studio 2026 18.8.12105.206 instance is incomplete and therefore
  intentionally unresolved; T11 retains real-host validation.
- The main and pack VSIXes contain 37 and 7 entries respectively, with no Visual Studio or ServiceHub
  assembly. The TestAdapter archive remains its exact six-file contract. Verification passed all
  411 assembly tests (292 unit and 119 integration) and all 18 standalone acceptance expectations.
- Compiled unit validation opens both exact owner-path VSIXes and rejects Visual Studio, ServiceHub,
  or TestPlatform assemblies. Local and CI TestAdapter packaging use one script that reads only
  `testadapter-package.txt` and compares the completed ZIP with that exact six-entry set.
- All 246 distinct restored package/version entries are classified exactly once in
  `docs/design.md`: 33 direct, 81 host-contract, 11 build/analyzer, 7 conflict-sensitive, 12
  delivered-transitive, and 102 ordinary family entries. The classification has zero unclassified
  entries; shared transitives
  are counted once rather than once per project.
- Final architecture review approved T8. Non-blocking follow-ups are to table-drive additional
  invalid manifest spellings and broaden the synthetic VSIX denylist if those guards are next
  changed; the current normalized owner-path guards and built payloads are proven clean.

### T9 outcome

- One identity and amd64 VSIX targets Community, Pro, and Enterprise at `[17.12,19.0)`; the Core
  Editor prerequisite uses the same range.
- Manifest, generated constants, and Marketplace metadata share the exact dual-host description.
  The repository synchronizer validates and mirrors all seven manifest fields and supports
  deterministic `-Check`; only `Set-VsixVersion.ps1` calculates or changes the manifest version.
- Built-artifact tests prove stable identity, targets, ranges, metadata, and host-binary exclusion.
  The full gate passed 412 assembly tests and all 18 acceptance expectations.
- Only JARVIS may spawn agents. Web-backed agents and web tool calls run serially.
- T10 owns transitional host-matrix prose, T11 real-host evidence, and T13 pack alignment. Final
  architecture review approved T9.

### Runtime flow

1. Necessary package wiring and logging may initialize, but status surfaces begin unavailable.
2. Package activation starts the sole prerequisite evaluation.
3. Async background callers arriving during evaluation await that same completion; they never
   initiate another check.
4. User status queries cannot await and therefore hide/disable their surface until `Ready`.
5. Success transitions to `Ready`, then runs release notes, incompatible-extension handling,
   downloader/update work, and normal activation.
6. Failure caches every typed failure and enters the activation-owned modal flow.
7. `Help` explicitly navigates to the instructions and changes no state.
8. `Disable` transitions to `Suspended`, releases waiting paths as unavailable, shows the InfoBar
   once, and returns control to Visual Studio.
9. No registry, settings, file, roaming store, or dismissal state records readiness or suspension.

Unexpected probe exceptions fail closed and become diagnostic failure details rather than enabling
the product.

### Prerequisite classification

| Failure | Required behavior |
|---|---|
| Unsupported host | Pass only `(major == 17 && version >= 17.12) || major == 18`. |
| rustup unavailable | Distinguish not found through inherited PATH from failure to execute. |
| Default toolchain unavailable | Verify a default rustup toolchain without relying on a workspace override. |
| Cargo unavailable | Verify Cargo is PATH-visible and operational for the default toolchain. |

Run every independent feasible probe and aggregate failures. Do not create cascading duplicate
failures when a prerequisite makes a dependent probe impossible. Nightly remains an optional feature
prerequisite for nightly-only behavior, including Rust Test Explorer discovery/execution.

### Dialog and InfoBar

The Visual Studio Yes/No message box lists all failures and explains `Yes = Disable` and `No = Help`.
`Yes` is the default; `No` opens prerequisites and re-shows the prompt. Close gestures cannot dismiss
the flow, and neither navigation nor restart occurs before an explicit action.
After `Disable`, the one warning InfoBar may navigate to prerequisite documentation but cannot retry.

### Boundary inventory

| Boundary | Suspension behavior |
|---|---|
| Static commands and menus | Hidden or disabled during checking/suspension; callbacks no-op. |
| Dynamic toolchain/target children | Return no children without invoking rustup or Cargo. |
| Open Folder build, clean, run, and debug contexts | Not offered; providers return without Rust work. |
| Editor-owned commands | Hidden/disabled even if they do not normally launch Rust. |
| Options, toolchain, node, and property surfaces | Hidden where possible; otherwise disabled without toolchain initialization. |
| Language client | Await activation; never start rust-analyzer unless ready. |
| Metadata, watchers, scanners, and context factories | Perform no Cargo discovery or indexing while suspended. |
| Rust test discovery/execution handoff | Stop extension-owned subscriptions/requests; do not intercept generic VS test commands. |
| Debug/run preparation | Return unavailable before toolchain resolution or launch. |
| Installer, updater, release follow-on, and package startup | Skip after failed prerequisites. |
| Direct Rust subprocess paths | Guard at their owning integration boundary against callback races. |

`SwitchToolchainCommand.BeforeQueryStatus` is gated before enumerating rustup toolchains.

### Suppression logging

- Write one general transition message when the process becomes suspended.
- For each named automatic path, write its first suppression once per process.
- Subsequent callbacks from the same path remain silent.
- Include the path name, session-only state, and restart-to-recheck guidance.
- Use no timers, persisted throttles, or modal fallback.

Required logical paths cover package follow-on startup, language activation, workspace
metadata/watchers, Open Folder providers, Rust test discovery/execution handoff, debug/run
preparation, and updater/download work.

### Host and manifest contract

Both manifests use identical Community, Pro, and Enterprise amd64 targets:

```xml
Version="[17.12,19.0)"
```

The Core Editor prerequisite uses the same range. Runtime validation remains authoritative for
supported product majors. This produces one VSIX per product, admits VS 17.x only from 17.12,
supports VS2026 through Microsoft's compatibility model, and introduces no host-specific binaries.

Regenerate `src/RustAnalyzer/source.extension.cs` from the source manifest with the existing VSIX
Synchronizer mechanism; never hand-edit its derived description. Extend only the approved stamper
for generated version fields.

### Dependency disposition

Use the newest dependency version with positive evidence for the exact 17.12 floor. No 18.x runtime
dependency is required merely because the host is VS2026.

| Area | Current | Candidate or constraint |
|---|---:|---|
| `Microsoft.VisualStudio.SDK` | 17.11.40262 | Researched 17.12 baseline `17.12.40392`; do not compile against post-17.12 APIs. |
| `Microsoft.VSSDK.BuildTools` | 17.11.435 | Try build-only `18.9.820`; otherwise newest passing version, with `17.12.2069` as conservative baseline. |
| SDK analyzers | 17.7.41 | Build-only candidate `17.7.122`, subject to both builds. |
| Visual Studio Threading + analyzers | 17.11.20 | Prefer `17.12.19`; do not take 18.x without exact-floor evidence. |
| TestPlatform ObjectModel/Test SDK | 17.11.0 | Align host-facing references to `17.12.0`; do not take 18.x solely because its TFMs compile. |
| Community Toolkit.17 | 17.0.522 | Candidate `17.0.551`; retains VS17/net48 baseline. |
| Community VSCT | 16.0.29.6 | Build-only candidate `16.0.29.14`. |
| Composition | preview 9.0 package | Do not modernize independently; use the framework/SDK closure or a stable package proven in both hosts. |
| Acquired VS assemblies | `src/external/vs.17.11` | Prefer official 17.12 NuGet contracts; retain loose assemblies only with official source, version, SHA-256, and rationale. Never substitute VS18 host binaries. |

The audit includes `Microsoft.VisualStudio.TestWindow.Interfaces` and every acquired DLL. Ensure
host-provided assemblies are not copied into the VSIX. Main remains `net48`, RustDevelopmentPack
remains `net472`, and TestAdapter remains `netstandard2.0`. If a candidate fails any target, host,
package-load, or payload check, select the newest lower passing version and record why in
`docs/design.md`.

### RustDevelopmentPack contents

| Entry | Decision |
|---|---|
| rust-analyzer.vs | Retain; S2 establishes its dual-host contract. |
| TOML Editor | Retain; current Marketplace payload supports both hosts. |
| Rainbow Braces | Remove; its current provider does not target Rust and conflicts with built-in brace colorization. |
| File Icons | Remove; it duplicates this extension's Rust icon registrations. |
| VSColorOutput64 | Remove; its current VS2026 support is not truthful. |

Before publication, resolve the TOML Editor Marketplace ID, record the selected version, inspect its
payload manifest, and fail if the listing is unavailable or incompatible.

### Compatibility evidence and CI topology

Blocking evidence covers successful startup, classified/aggregated failures, exact dialog actions,
explicit Help navigation, no automatic restart/navigation, blocked close gestures, single-flight
state, no persistence, complete user/background gating, suppression logging, one InfoBar, host
predicate boundaries, both manifests, pack contents, version/artifact/publication wiring, and real
host generations.

CI:

1. Stamp and build both canonical VSIXes and the TestAdapter once on the VS17 build host.
2. Run host-independent tests once and upload canonical outputs.
3. Run parallel blocking host jobs on `windows-2022` and `windows-2025-vs2026`; assert resolved host
   versions and majors.
4. Download rather than rebuild the exact artifacts; install both VSIXes, exercise startup and
   installability, and run host-bound TestAdapter acceptance with its explicit major.
5. Require both host jobs before publication.

### Deferred real-Visual-Studio E2E analysis

**Finding:** feasible with limitations on current GitHub-hosted Windows runners. VS 2022 and VS 2026
images currently expose an interactive desktop and usable UI Automation, but GitHub does not
guarantee those semantics. A two-label pilot would be required before making rendered-UI evidence a
blocking publication gate.

| Option | Shape | Assessment |
|---|---|---|
| O1 | Thin external net48 controller, test-only observer VSIX, nonce-bound current-user named pipe, and HWND-rooted semantic UI Automation. | Recommended; medium effort/risk and no production changes. |
| O2 | Adopt the Microsoft/Roslyn Visual Studio integration-test harness. | Proven shape but large, custom, and currently Dev18-oriented; high integration cost. |
| O3 | External DTE/ROT plus UI Automation or WinAppDriver only. | No companion VSIX, but modal-sensitive, weak for dynamic command status, and likely flaky. |

The recommended O1 observer would never ship, replace product assemblies, intercept product
MessageBox/InfoBar services, mutate prerequisite state, or expose a production endpoint. It would
report shell readiness and the owned PID/HWND, open the fixture, query real OLE command status, read
the Output pane, and request graceful shutdown. External UI Automation would inspect and invoke only
the real framework MessageBox and InfoBar; it would use no coordinates, pixels, or menu traversal.

If adopted later, retain exactly two logical scenarios on both VS17 and VS18:

1. **Failed prerequisites -> suspension:** launch the canonical VSIX with Rust tools removed only from
   the child PATH; verify the real mapped Yes/No prompt, blocked close gestures, same-process
   suspension, real InfoBar, representative static/workspace/dynamic command unavailability, no
   rust-analyzer child, and one non-repeating Output suppression message.
2. **Fresh process -> ready:** reuse the same experimental profile with restored Rust PATH but a new
   `devenv` PID; verify prerequisites rerun, no failure UI appears, representative commands return,
   normal activation resumes, and no suspension state persisted.

Both host jobs would consume the same hashed canonical VSIX, use a unique experimental root suffix,
resolve an exact host major with `vswhere`, install through that host's `VSIXInstaller.exe`, and own
only the PID/descendants reported by the observer. No rebuild, `/DeployExtension=true`,
`/shutdownprocesses`, process-name termination, scenario retry, or browser inspection would be
allowed. Diagnostics would include installer/configuration logs, `ActivityLog.xml`, controller/RPC
transcript, UIA tree, Output/command/state/process reports, and a best-effort screenshot on failure.

Open decisions if revisited:

- approve the test-only observer VSIX and local named-pipe endpoint;
- accept real shell/OLE `QueryStatus` as command-visibility evidence;
- keep browser launch outside E2E while deterministic tests prove exact URL/action behavior;
- pilot both hosted labels before making the two host legs blocking.

Research basis:

- [Visual Studio experimental instance](https://learn.microsoft.com/en-us/visualstudio/extensibility/the-experimental-instance?view=visualstudio)
- [CreateExpInstance utility](https://learn.microsoft.com/en-us/visualstudio/extensibility/internals/createexpinstance-utility?view=visualstudio)
- [`devenv /Log`](https://learn.microsoft.com/en-us/visualstudio/ide/reference/log-devenv-exe?view=visualstudio)
- [Microsoft UI Automation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/invoke-a-control-using-ui-automation)
- [Roslyn Visual Studio integration-test harness](https://github.com/dotnet/roslyn/tree/main/src/VisualStudio/IntegrationTest)
- [GitHub Windows 2022 image](https://github.com/actions/runner-images/blob/main/images/windows/Windows2022-Readme.md)
- [GitHub Windows 2025 VS2026 image](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-VS2026-Readme.md)

**Decision (2026-08-27):** record this analysis only. Do not implement it or change T11/current gates.

### Versioning and publication

Extend `Set-VsixVersion.ps1` as the only version writer for the main manifest, generated main
constant, pack manifest, and pack `Extensions.vsext`. Retain independent checked-in base versions
and append the same normalized CI build suffix. Emit both named versions and validate consistency.

Create separate Marketplace publish metadata for RustDevelopmentPack. Before external writes,
validate both VSIXes, IDs, versions, metadata, third-party compatibility, and credentials.

- Eligible `master` publish: main, then pack, to Open VSIX Gallery.
- Release/manual publish: main, then pack, to Visual Studio Marketplace.
- After successful Marketplace publication, attach `RustAnalyzer.vsix`,
  `RustDevelopmentPack.vsix`, and the TestAdapter archive to the existing main-version GitHub
  Release.

Use no `continue-on-error`; partial external publication fails visibly.

### Documentation command baseline

`PREREQUISITES.md` provides official sources and copyable PowerShell commands for:

- install/upgrade of Community, Professional, or Enterprise VS 2022 via their
  `Microsoft.VisualStudio.2022.*` WinGet IDs;
- install/upgrade of the corresponding VS 2026 edition via its current official WinGet ID;
- `vswhere` verification of a complete Core Editor installation in `[17.12,19.0)`;
- secure `rustup-init.exe` download and stable default-toolchain installation;
- existing rustup/stable update and `rustup default stable`;
- adding `%USERPROFILE%\.cargo\bin` to user and current-process PATH when absent;
- `Get-Command`, rustup, active-toolchain, rustc, and Cargo verification;
- optional nightly install/update and a workspace-local nightly override for Test Explorer.

Users run commands only for their applicable Visual Studio edition. After installation or PATH
changes, they close every Visual Studio process and start a fresh one.

Official sources include Microsoft Visual Studio command-line installation, WinGet install,
Visual Studio extension compatibility, rust-lang installation, the rustup book, and Cargo
installation.

### Documentation and governance

Update `PREREQUISITES.md`, root `README.md`, `src/RustDevelopmentPack/README.md`, both manifests,
Marketplace publication metadata, every live VS2022-only claim, and `docs/design.md`. Regenerate the
main derived description through the approved mechanism. Exclude the archived previous feature
document from all searches and replacements.

Role duties:

- **JARVIS:** scope planning, gates, and delivery across both hosts and products.
- **Anders:** design every product/platform change for both hosts.
- **Dave:** implement without breaking either host or product.
- **Bhaskar:** verify both hosts, both VSIXes, and publication evidence.
