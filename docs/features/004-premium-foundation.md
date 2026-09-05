# Feature: Premium Foundation
**Branch:** vibe/004-premium-foundation
**Status:** In Progress

## Requirements

1. Preserve default-on feature-usage analytics in configured release builds while removing
   arbitrary telemetry and raw exceptions. Add no consent prompt or user-facing opt-out.
2. Set telemetry user identity to:
   `ravs-v1:` + unpadded Base64URL of
   `SHA-256(UTF-8(Expand("%USERNAME%@%COMPUTERNAME%.%USERDOMAIN%")))`.
   Hash the exact expanded value without normalization or salt.
3. Emit one typed event with fixed operation, outcome, duration bucket, product version, host, and
   Visual Studio major fields. Reject every other field.
4. Consolidate duplicate telemetry, remove diagnostic/lifecycle noise, and add only missing
   user-visible feature outcomes.
5. Preserve experimental-instance and `RUSTANALYZER_TELEMETRY_DISABLED` suppression. Build and test
   gates set the latter to prevent internal telemetry. Release builds inject
   `RUSTANALYZER_TELEMETRY_CONNECTION_STRING` into generated compiled configuration. Never store it
   in source. Missing configuration produces no telemetry.
6. Retain `ITelemetryService` unchanged as a temporary no-egress T1 bridge. T2 deletes it only after
   migrating every production caller and test double to `IFeatureUsageTelemetry`; the approved
   standalone TestAdapter API break permits its removal.
7. Keep the packaged rust-analyzer as the sole recovery baseline.
8. Retain automatic updates from the latest official Windows amd64 rust-analyzer release.
9. Before download or extraction, require the SHA-256 published by official release metadata for
   the exact asset. A locally computed digest alone is not verification.
10. Extract only the expected executable into its inactive version directory. Reject absolute,
    nested, and parent-relative archive paths.
11. Validate `rust-analyzer --version` before activation. It must exit successfully and report the
    manifest release. Serialize installation across Visual Studio processes and update the registry
    pointer last.
12. If initial LSP startup fails, reset the pointer to the packaged version and retry once.
13. If the packaged retry fails, stop retrying, keep its pointer, and use the ordinary local
    initialization-failure path.
14. Store the same provenance manifest beside packaged and downloaded rust-analyzer binaries.
15. Before using a registry-selected downloaded binary, verify its adjacent manifest and executable
    digest. On failure, reset the pointer without starting it.
16. Never clean completed downloaded-version directories. Under the installation lock, remove and
    recreate only a target directory proven partial by missing or invalid files, manifest, digest,
    or version validation.
17. Add one script with `Verify`, `Check`, and `Update` modes:
    - `Verify` checks local files against their manifest without network access.
    - `Check` also resolves latest and fails when its release is newer than the packaged manifest.
    - `Update` acquires latest, verifies it, and updates all tracked package inputs.
18. Add `Check` to preflight with one 30-second total network timeout. Fail on stale, malformed,
    unavailable, or timed-out official metadata; show a precise diagnosis and refresh command.
    Never mutate tracked files during preflight.
19. After the script passes its focused tests, run `Update` once and package the then-latest verified
    rust-analyzer files and manifest.
20. Retain the existing 30-day freshness test and all other rust-analyzer tests.
21. Delete `RustAnalyzer.Remote`, `RustAnalyzer.Remote.UnitTests`, and their live repository
    references. Preserve historical feature records.
22. Publish `PRIVACY.md`, linked from README, describing the exact schema, hashed identity, no
    consent prompt or user-facing opt-out, operational suppression, and 365-day raw-event retention.
    Existing Application Insights access controls remain unchanged.
23. Make only critical product-consistency corrections caused by completed work or this feature.
24. Record final canonical-VSIX and standalone-TestAdapter smoke tests in supported Visual Studio
    2022 and 2026 versions.
25. Do not add CI supply-chain hardening, premium delivery, entitlement, or target abstractions.

## Design Options (Ox)

### O1 — Bounded foundation

- Replace unsafe telemetry with typed usage analytics.
- Harden runtime updates and record binary provenance.
- Make one script own packaged rust-analyzer verification and refresh.
- Delete the unused Remote projects.
- Correct only critical contradictions and close dual-host evidence.

**Pros:** Removes inherited risk without guessing premium architecture.

**Cons:** Premium delivery and target work remain separate.

### O2 — Broad platform foundation

- Add CI hardening, generic process/remote abstractions, entitlement, and premium delivery now.

**Pros:** More infrastructure exists before the first premium feature.

**Cons:** Large speculative surface with no selected delivery or target contract.

**Selected: O1.**

## Scope

### Included

| Area | Why |
|---|---|
| Usage analytics | Product decisions need trustworthy feature adoption and completion data without user content. |
| Updater integrity | Downloaded executables require verified identity, safe extraction, validation, and deterministic fallback. |
| Provenance and freshness | Every packaged or downloaded binary must identify its source and bytes. |
| Remote deletion | Both projects are unused stubs and falsely imply an existing remote platform. |
| Critical product truth | Current architecture, privacy, and delivery claims must match shipped behavior. |
| Baseline closure | Premium work should start from recorded VS2022, VS2026, VSIX, and TestAdapter evidence. |

### Excluded

| Area | Why |
|---|---|
| CI supply-chain trust | The human explicitly excluded action pinning, permission changes, and release-authorization redesign. |
| Launch/workspace correctness | Important free-product work, but unrelated to this minimum foundation. |
| Toolchain/protocol UX | Nightly degradation, parsing, and actionable errors need a focused feature. |
| Generic process platform | Cargo, rustup, LSP, and test lifetimes differ; a shared abstraction would encode guesses. |
| Durable diagnostics | Retention, redaction, export, support bundles, drain, and flush require separate decisions. |
| Product expansion | Editor parity, Cargo/TOML, ARM64, performance, and quality-gate work are independent. |
| Premium architecture | Targets, delivery, licensing, entitlement, pricing, and support follow a selected paid product. |

## Flows

### Packaged rust-analyzer

```text
╔══════════════╗
║ Verify files ║
╚══════╤═══════╝
       ▼
╔══════════════╗     stale      ┌────────────────┐
║ Check latest ║ ─────────────▶ │ Stop; show fix │
╚══════╤═══════╝                └────────────────┘
       │ current
       ▼
╔════════════════╗
║ Pass preflight ║
╚════════════════╝

┌─────────────────┐   ┌─────────────┐   ┌──────────────────┐   ╔═══════════════╗
│ Official latest │ → │ Verify hash │ → │ Files + manifest │ → ║ Reviewed VSIX ║
└─────────────────┘   └─────────────┘   └──────────────────┘   ╚═══════════════╝
```

`Check` runs `Verify` first, then resolves latest. It fails if official metadata is unavailable or
the manifest release is stale. Preflight uses `Check`; normal builds use `Verify`. Only explicit
`Update` changes tracked artifacts. CI never downloads a replacement.

### Runtime update

```text
┌─────────────┐   ┌─────────────┐   ┌──────────────┐   ┌──────────┐
│ Find latest │ → │ Verify hash │ → │ Safe extract │ → │ Validate │
└─────────────┘   └─────────────┘   └──────────────┘   └────┬─────┘
                                                            ▼
                                                  ╔══════════════╗
                                                  ║ Point + start║
                                                  ╚══════╤═══════╝
                                                         │ failure
                                                         ▼
                                                  ╔══════════════╗
                                                  ║ Packaged copy║
                                                  ╚══════════════╝
```

The existing version directory is the inactive staging location. No second staging directory or
downloaded fallback version is required. The installation lock covers inspection, partial-directory
replacement, validation, pointer commit, and rollback.

## Slices (Sx)

| Slice | Outcome | Depends on |
|---|---|---|
| S1 | Typed, bounded feature-usage telemetry replaces unsafe and noisy telemetry. | - |
| S2 | Packaged and downloaded rust-analyzer binaries are verified, attributable, and recoverable. | - |
| S3 | Unused Remote projects and critical product contradictions are removed. | S1, S2 |
| S4 | Canonical VSIX and standalone TestAdapter behavior is recorded on both supported Visual Studio generations. | S3 |

## Tasks (Tx)

Execute one task at a time.

| # | Slice | Task | Status | Commit |
|---|---|---|---|---|
| T1 | S1 | Add the typed telemetry boundary, hashed identity, injected release configuration, strict allow-list, temporary no-egress migration bridge, and focused contract tests. | Done | pending |
| T2 | S1 | Replace current telemetry calls with one terminal event per approved operation; remove duplicates, diagnostics, unsafe payloads, and lifecycle noise. | Pending | - |
| T3 | S2 | Add the rust-analyzer `Verify`/`Check`/`Update` script, shared provenance manifest, preflight freshness gate, build verification, and focused script tests. Preserve existing tests, then run `Update` once. | Pending | - |
| T4 | S2 | Harden runtime acquisition, verification, safe extraction, validation, cross-process activation, packaged fallback, local provenance, and focused failure tests. | Pending | - |
| T5 | S3 | Delete both Remote projects and reconcile the solution, build/test gates, dependency ledger, architecture, and premium boundary. | Pending | - |
| T6 | S3 | Correct only a super-critical current-product fact made false by T1–T5. Defer unrelated README, historical-feature, build-skill, and backlog edits. | Pending | - |
| T7 | S4 | Human-test the canonical VSIX and packaged standalone TestAdapter in one supported VS2022 17.x host and one VS2026 18.x host; record exact host/artifact versions plus install/load, LSP, Cargo, and test discovery/execution outcomes. | Pending | - |

## Task Contracts

### T1 — Typed telemetry boundary

**Work**

- Add `IFeatureUsageTelemetry`, closed operation/outcome enums, duration buckets, and one wire event.
- Hash the exact expanded identity with SHA-256 and unpadded Base64URL.
- Generate compiled release configuration from an environment variable.
- Keep experimental, unconfigured, test, CI, and internally disabled runs silent.
- Fail closed on every non-allowed event, value, property, or SDK context field.
- Make legacy generic methods no-op until T2 removes them.

**Acceptance**

- Configured release telemetry is on without consent or a user-facing opt-out.
- Only the documented event and fields reach a recording channel.
- Unresolved identity placeholders omit `user.id`.
- Experimental, unconfigured, and operationally disabled contexts construct no active client.
- Repository test commands set `RUSTANALYZER_TELEMETRY_DISABLED`.
- Contract tests cover every enum mapping, duration boundary, identity rule, and suppression path.

### T2 — Telemetry reconciliation

**Work**

- Apply the audit dispositions below at canonical terminal boundaries.
- Emit at most one event per operation invocation.
- Delete `ITelemetryService`, generic event methods, exception telemetry, and migrated test doubles.
- Add `PRIVACY.md`; link it from README.

**Acceptance**

- Approved operations report `succeeded`, `failed`, or `cancelled`.
- Test execution does not inflate discovery usage.
- No path, name, command, argument, environment, setting, source, test, update, URL, exception, or
  arbitrary property reaches telemetry.
- Privacy text states default-on collection, hashed identity, exact schema, operational suppression,
  365-day raw retention, and unchanged existing access controls.
- Focused boundary tests prove one event and correct outcome for each feature family.

### T3 — Packaged acquisition and provenance

**Work**

- Add one script with `Verify`, `Check`, and `Update`.
- Make the script own binary/PDB acquisition, version/link synchronization, and manifest generation.
- Run `Verify` in normal builds and `Check` in preflight.
- Give `Check` one 30-second total network timeout and fail closed.
- Preserve every existing freshness and package test.
- After focused script tests pass, run `Update` once.

**Acceptance**

- `Verify` is offline and fails on any local manifest or hash mismatch.
- `Check` verifies locally before querying official latest metadata; it never writes.
- `Update` resolves latest, requires its official digest, validates files/version, and changes every
  coupled tracked input together.
- Normal CI packages reviewed files without downloading replacements.
- The refreshed VSIX contains the then-latest manifest and verified files.

### T4 — Runtime update integrity

**Work**

- Resolve the exact official Windows amd64 asset and published digest.
- Hold one cross-process lock from target inspection through pointer commit or rollback.
- Verify before extraction; accept only the expected executable path.
- Reuse a complete validated version directory; recreate only one proven partial.
- Validate version, write local provenance, and set the registry pointer last.
- Verify a selected downloaded binary and manifest before every returned path.
- On initial LSP failure, point to packaged and retry once.

**Acceptance**

- Metadata, digest, traversal, partial-install, version, or manifest failure never activates or
  executes the download.
- Concurrent installers cannot write or activate the same target simultaneously.
- Failed packaged retry stops without a loop and uses existing local failure reporting.
- Completed downloaded directories remain untouched.
- Focused tests cover mismatch, unsafe path, partial state, pointer ordering, selection verification,
  and one packaged fallback.

### T5 — Remote deletion

**Work**

- Delete both Remote projects and all tracked contents.
- Remove solution, build, test, and live documentation references.
- Reconcile only dependency-ledger and canonical-output facts changed by deletion.

**Acceptance**

- No live project, gate, or architecture claim references Remote.
- T5 adds no further main-VSIX or TestAdapter runtime, identity, or packaging change.
- Historical feature records remain unchanged.

### T6 — Critical consistency

**Work**

- Add README non-affiliation guidance; preserve T2's privacy link.
- Update current design facts for telemetry, updater, provenance, and Remote deletion.
- Update only the current-constraints section of the premium discussion when made false.

**Acceptance**

- Current product documents agree with implemented behavior.
- No broad README, build-skill, `MSB3277`, backlog, or historical-feature rewrite occurs.

### T7 — Human baseline closure

**Work**

- Produce the canonical VSIX and standalone TestAdapter package.
- Give the human exact install and smoke-test steps for one supported VS17 and VS18 host.
- Record exact host/artifact versions and outcomes.

**Acceptance**

- Both hosts install/load the VSIX and initialize rust-analyzer.
- Cargo build/run and TestAdapter discovery/execution complete on both hosts.
- Evidence names the exact artifacts and host versions.
- Feature 003 and Feature 004 status reflect the recorded outcome.

## Risks (Rx)

- **R1:** The identity hash is pseudonymous and guessable from candidate environment values. Never
  use it for authentication, entitlement, enforcement, or support identity.
- **R2:** Telemetry can still expose data through SDK defaults. The final processor must remove
  every non-allowed context field.
- **R3:** A stale or unavailable release endpoint can block preflight. Use a hard timeout and precise
  failure; never retry indefinitely.
- **R4:** GitHub's digest proves byte identity, not upstream safety or publisher-account integrity.
- **R5:** Concurrent Visual Studio processes can target the same version directory. Hold one
  cross-process installation lock and recheck after acquisition.
- **R6:** A healthy executable can still fail LSP initialization. Only startup/handshake failure
  triggers the single packaged-version retry.
- **R7:** Removing Remote changes build/test and dependency-ledger counts. Update only facts made
  false by its deletion.
- **R8:** Always-on pseudonymous telemetry and 365-day retention require an applicable legal basis.
  `PRIVACY.md` provides notice; legal approval remains human-owned.
- **R9:** Deleting `ITelemetryService` can break external standalone-TestAdapter consumers. The human
  explicitly accepts that API break.
- **R10:** The generated client telemetry connection string is recoverable from shipped binaries and
  can be used to submit false events. Treat ingestion as untrusted and enforce service-side controls.

## Assumptions (Ax)

- **A1:** Release builds inject telemetry configuration from an environment variable into generated
  compiled configuration. Source control contains no connection string. Local and unconfigured
  builds remain no-op.
- **A2:** The official release exposes a SHA-256 digest for the expected Windows amd64 asset.
- **A3:** The runtime archive contains one expected executable.
- **A4:** The registry pointer remains the activation authority.
- **A5:** The main VSIX and standalone TestAdapter retain their identities and delivery formats.
- **A6:** Existing Application Insights access controls need no repository or Azure change.

## Deferrals (Dx)

- **D1:** GitHub Action pinning, workflow-permission changes, and release-authorization redesign.
- **D2:** Independent signatures, signing services, SBOM platforms, or artifact databases.
- **D3:** Retaining or selecting a previous downloaded rust-analyzer version.
- **D4:** General cleanup of completed downloaded version directories.
- **D5:** Durable logs, support bundles, export, retention, retry, drain, or flush contracts.
- **D6:** Generic process, remote-target, transport, or deployment abstractions.
- **D7:** WSL, Linux, Wasm, containers, embedded, drivers, ARM64, and other target verticals.
- **D8:** Premium SKU identity, delivery, licensing, entitlement, pricing, or support architecture.
- **D9:** Editor parity, Cargo/TOML product features, and unrelated quality-gate work.
- **D10:** Broad documentation or backlog rewrites.
- **D11:** Launch/workspace correctness, toolchain/protocol UX, and process-lifetime redesign.
- **D12:** Performance investigations and ApprovalTests/tool-version hardening.

## Notes & Decisions

### Telemetry audit

The audit found 23 custom-event calls and 26 exception-telemetry calls. No current call remains
unchanged.

| Flow | Current boundary | Decision |
|---|---|---|
| Transport and identity | `TelemetryService` | Replace with the closed typed contract. |
| Generic commands | `BaseRustAnalyzerCommand<T>.Execute` | Remove request-only command-class events. |
| Comment commands | `CommentSelectionCommandHandler` | Remove minor editor-action telemetry. |
| Language server | `LanguageClient` activation/failure | Emit one terminal activation event without path or exception. |
| Debug/run | `DebugLaunchTargetProvider`, `LaunchConfigWrapper` | Emit one terminal launch event without target configuration. |
| Cargo operations | `ToolchainService.ExecuteOperationAsync` | Emit terminal build, clean, Clippy, or format outcome. |
| Test preparation | `ToolchainService.GetTestSuiteInfoAsync` | Remove duplicate path/profile/argument events. |
| Test discovery | `TestDiscovererCommon`, `TestDiscoverer` | Emit once from the top-level discovery invocation. |
| Test execution | `TestExecutor` | Emit once per top-level execution invocation. |
| Workspace lifecycle | `TestContainerDiscoverer`, `TestContainer`, `MetadataService` | Remove host-churn telemetry. |
| Workspace providers | `FileContextProviderFactory`, `FileScannerFactory` | Remove construction telemetry. |
| Settings | `SettingsService` | Remove setting/path/value telemetry. |
| Toolchain actions | `InstallToolchainCommand`, `SwitchToolchainCommand` | Add terminal completion outcomes. |
| Updater and package UX | `RlsInstallerService`, package remediation, release links | Remove non-feature telemetry. |
| Diagnostics and parsing | Prerequisites, sinks, loggers, Cargo parser, nested catches | Keep local logging; remove exception egress. |

Add telemetry only when a distinct user-visible capability has one completion boundary, its
aggregate usage informs a product decision, and one event represents it without user content.

### Telemetry contract

Emit only `rustanalyzer.feature_usage`.

| Scope | Fields |
|---|---|
| Context | `schema_version`, `host_kind`, `extension_version`, `visual_studio_major`, `user.id` |
| Event | `feature`, `action`, `outcome`, `duration_bucket` |

`UsageOperation` is a closed enum. Its only wire mappings are:

- `language_server/activate`;
- `cargo/build`, `cargo/clean`, `cargo/clippy`, `cargo/format`;
- `test_adapter/discover`, `test_adapter/execute`;
- `launch/debug`, `launch/run`; and
- `toolchain/install`, `toolchain/switch`.

Callers never supply `feature` or `action` strings. `schema_version` is `1`; `host_kind` is `vsix`
or `test_adapter`; `visual_studio_major` is `17`, `18`, or `unknown`; and `extension_version` is
`Vsix.Version`.

Outcomes are `succeeded`, `failed`, or `cancelled`. Duration buckets are `<1s`, `1–5s`, `5–30s`,
`30–120s`, and `≥120s`.

If environment expansion leaves any identity placeholder unresolved, omit `user.id`. Equal expanded
values aggregate; renamed machines or accounts split identity.

Track one terminal event for:

- language-server activation;
- Cargo build, clean, Clippy, and format;
- test discovery and execution;
- debug and no-debug launch; and
- toolchain installation and switching.

Remove telemetry for generic command clicks, comment commands, settings, workspace/scanner/container
lifecycle, updater activity, release links, prerequisites, local faults, parser failures, and raw
exceptions. Telemetry is usage analytics, not diagnostics.

Do not send paths, names, commands, arguments, environments, settings, source, Cargo output, test
data, URLs, update metadata, exception details, device context, or arbitrary properties.

### Provenance manifest

Store `rust-analyzer.provenance.json` beside each binary:

- schema version;
- upstream repository, release, target, asset name, and URL;
- official published SHA-256 and metadata source;
- archive SHA-256 verified during acquisition;
- executable and PDB SHA-256 when present; and
- reported rust-analyzer version.

The packaged manifest is reviewed and shipped in the VSIX. Runtime updates generate the same schema
locally. Telemetry may report feature usage but is never provenance evidence.

### Critical consistency

Update only a current-product fact made false by implemented telemetry/updater behavior, Remote
deletion, or recorded S4 evidence. Preserve historical feature records. Do not perform broad README,
build-skill, `MSB3277`, or backlog rewrites.

Premium product, licensing, pricing, revenue, CAPEX, and OPEX decisions remain in
[`premium-discussion.md`](../premium-discussion.md); this feature does not duplicate them.
