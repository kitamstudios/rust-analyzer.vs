# Feature: Prerequisite Suspension and Dual-Host Delivery
**Branch:** vibe/003-prerequisite-suspension-and-dual-host-delivery
**Status:** In Progress

## Requirements

1. Run one single-flight prerequisite evaluation per `devenv.exe` process when the package activates.
   Cache readiness and complete typed failures in memory only.
2. Check the supported Visual Studio version, `rustup` through the Visual Studio process PATH, a
   configured default Rust toolchain, and operational Cargo from that toolchain and PATH. Nightly is
   not a startup prerequisite.
3. On failure, show all detected failures in one modal dialog with exactly `Disable` and `Help`.
   `Help` explicitly opens `PREREQUISITES.md` and leaves the dialog open. Only `Disable` closes the
   flow. X, Escape, and equivalent close paths cannot dismiss it. Never restart or open a browser
   automatically.
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
10. Both `rust-analyzer.vs` and `RustDevelopmentPack` advertise the same host matrix. Remove
    VSColorOutput64 from the pack and validate every remaining Marketplace constituent.
11. CI builds canonical artifacts once and provides blocking evidence through deterministic 17.12
    boundary/manifest tests, actual install/startup validation on hosted VS 17.14 and VS 18.x, and
    explicitly selected host-major TestAdapter acceptance.
12. RustDevelopmentPack becomes a first-class CI/CD product: independent base version, the same
    deterministic build suffix as rust-analyzer.vs, build/validation/artifact/dual-host verification,
    and Open VSIX/Marketplace/GitHub Release parity under the main package's existing gates.
13. Rewrite prerequisite and installation documentation using official sources and copyable commands.
    Reconcile every live platform claim and update all four agent roles.
14. Audit VSSDK, Community.VisualStudio, threading, test platform, composition, and acquired
    host-assembly dependencies. Select only versions proven compatible with VS 17.12, VS 18.x, main
    `net48`, and pack `net472`.
15. Do not modify `docs/features/002-hardening-and-vs2026.md`.

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
| S1 | Ship process-safe prerequisite suspension, truthful dual-host packages, reconciled documentation, and blocking VS17/VS18 compatibility evidence. | - |
| S2 | Ship RustDevelopmentPack as an independently versioned, first-class artifact across the same publication channels and gates as rust-analyzer.vs. | S1 |

S1 can ship through the existing main-package delivery path. S2 adds distribution of the
already-compatible pack without changing S1 runtime behavior.

## Tasks (Tx)

Execute one task at a time in order.

| # | Slice | Task | Status | Commit |
|---|---|---|---|---|
| T1 | S1 | Add the shared process-scoped prerequisite state, immutable cached result, single-flight completion, and state-transition unit tests. | Done | `c87922d` |
| T2 | S1 | Implement complete prerequisite probe classification and the exact VS17/VS18 predicate with unit and process-boundary integration tests. | Done | - |
| T3 | S1 | Replace prerequisite failure UX with the non-dismissible `Disable`/`Help` dialog and focused UI integration tests. | Pending | - |
| T4 | S1 | Add the one-shot warning InfoBar, explicit navigation, non-persisted close behavior, and tests. | Pending | - |
| T5 | S1 | Make prerequisite evaluation the first product operation after package activation and defer all normal startup work until readiness. | Pending | - |
| T6 | S1 | Gate all automatic/background Rust paths and implement first-suppression-per-path Output logging with tests. | Pending | - |
| T7 | S1 | Hide or disable every extension-owned user surface while unavailable and make execution callbacks defensive no-ops. | Pending | - |
| T8 | S1 | Audit and apply the newest proven dual-compatible dependency closure and acquired-artifact provenance policy. | Pending | - |
| T9 | S1 | Align both VSIX manifests and metadata, remove VSColorOutput64, and validate all remaining Development Pack constituents. | Pending | - |
| T10 | S1 | Rewrite prerequisite/readme material, reconcile live support claims, update `docs/design.md`, and update all four agent roles. | Pending | - |
| T11 | S1 | Add canonical-artifact VS17/VS18 validation and the complete blocking behavior/platform evidence matrix. | Pending | - |
| T12 | S2 | Extend the approved stamper to produce independent main/pack versions with the same deterministic build suffix. | Pending | - |
| T13 | S2 | Build, validate, and upload RustDevelopmentPack as a named canonical artifact and verify that artifact on both hosts. | Pending | - |
| T14 | S2 | Add pack publication to Open VSIX, Marketplace, and GitHub Releases under the main package's existing fail-closed gates. | Pending | - |

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
- **R5:** Marketplace extension-pack constituents can change independently. Revalidate their
  then-current payload manifests before publication and fail closed.
- **R6:** Hosted-image labels or installed versions can drift. Assert the resolved host major and
  minimum version with `vswhere`; never accept an ambient fallback.
- **R7:** External registries are not transactional. Prevalidate both products before the first
  external write, publish the dependency before the pack, and fail visibly on partial publication.
- **R8:** VS2026's compatibility model may ignore a manifest upper bound. Keep the runtime major
  predicate authoritative and test unsupported-major rejection.
- **R9:** Extension-pack resolution is gallery-based and not version-pinned. Publish
  rust-analyzer.vs before RustDevelopmentPack and record constituent versions in publication evidence.

## Assumptions (Ax)

- **A1:** Microsoft's documented VS2026 model continues to support the stable VS 17.x extension API
  and evaluates the installation target by its lower bound.
- **A2:** `windows-2022` supplies current VS 17.14 and `windows-2025-vs2026` supplies VS 18.x; CI
  verifies rather than assumes those versions.
- **A3:** Remaining pack entries - rust-analyzer.vs, TOML Editor, Rainbow Braces, and File Icons -
  retain compatible Marketplace payloads at implementation time.
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

## Notes & Decisions

**Pull request:** [#73](https://github.com/kitamstudios/rust-analyzer.vs/pull/73)

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

The dialog lists all failures, explains both actions, has exactly `Disable` and `Help`, cannot be
dismissed by close gestures, and invokes neither navigation nor restart before an explicit action.
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
| rust-analyzer.vs | Retain; S1 establishes its dual-host contract. |
| TOML Editor | Retain; current Marketplace payload supports both hosts. |
| Rainbow Braces | Retain; current Marketplace payload supports both hosts. |
| File Icons | Retain; current Marketplace payload supports both hosts. |
| VSColorOutput64 | Remove; its current VS2026 support is not truthful. |

Before publication, resolve each remaining Marketplace ID, record the selected version, inspect its
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
