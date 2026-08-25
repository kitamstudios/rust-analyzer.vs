# Backlog

> Running list of candidate features / ideas, newest-relevant first. The human prioritises;
> a feature graduates from here into `docs/features/<nnn>-<feature_name>.md` when picked up.

Detailed evidence, dependencies, accepted prerequisite UX decisions, and unresolved decisions from
the original hardening review remain in
[`002-hardening-and-vs2026.md`](features/002-hardening-and-vs2026.md). That document is a planning
archive; this backlog owns selection of the next feature.

## Selected for feature 002 design

| Candidate | Scope | Priority signal |
|-----------|-------|-----------------|
| Extension architecture analysis | Compare in-process VSSDK and out-of-process VisualStudio.Extensibility using a capability/migration matrix and targeted prototypes; produce a recommendation only. Do not migrate extension models in this feature. | Analysis must inform dependency modernization and future architecture work. Editor capability gaps recur in issues [#22](https://github.com/kitamstudios/rust-analyzer.vs/issues/22), [#28](https://github.com/kitamstudios/rust-analyzer.vs/issues/28), [#35](https://github.com/kitamstudios/rust-analyzer.vs/issues/35), [#46](https://github.com/kitamstudios/rust-analyzer.vs/issues/46), [#47](https://github.com/kitamstudios/rust-analyzer.vs/issues/47), [#48](https://github.com/kitamstudios/rust-analyzer.vs/issues/48), and [#49](https://github.com/kitamstudios/rust-analyzer.vs/issues/49). |
| Gate portfolio and runtime review | Measure every local and CI gate, identify redundant or misplaced work, and redesign quick/full/PR execution for fast feedback with equivalent failure coverage. Record each gate's purpose, trigger, cost, and retained risk before merging, moving, or removing it. | Explicit maintainer concern; public feedback contains no demand signal. Optimize from evidence without silently weakening merge protection. |
| Visual Studio 2026 and prerequisite readiness | Support VS 2022 17.12+ and VS 2026; replace the repeated install/restart loop with one process-scoped evaluation, one explicit dialog, Continue without Rust, and one session InfoBar. | Highest public-feedback priority: two one-star Marketplace reviews plus issues [#26](https://github.com/kitamstudios/rust-analyzer.vs/issues/26), [#39](https://github.com/kitamstudios/rust-analyzer.vs/issues/39), [#56](https://github.com/kitamstudios/rust-analyzer.vs/issues/56), and [#68](https://github.com/kitamstudios/rust-analyzer.vs/issues/68) report repeated restart/lockout behavior. |
| VSSDK and library modernization | Upgrade dependencies required by the current extension architecture and VS 2026 support, including VSSDK, Community.VisualStudio.Toolkit, VS Threading/analyzers, Test Platform, .NET/NuGet, ApprovalTests/xUnit, and other direct libraries. | Explicitly selected; issues [#33](https://github.com/kitamstudios/rust-analyzer.vs/issues/33), [#50](https://github.com/kitamstudios/rust-analyzer.vs/issues/50), and [#57](https://github.com/kitamstudios/rust-analyzer.vs/issues/57) provide activation/dependency evidence. Architecture migration remains out of scope. |
| VS 2022/2026 compatibility matrix | Exercise installation, activation, Open Folder/MEF, LSP, Cargo, tests, run/debug, suspend/recovery, updater/offline, and shutdown in clean experimental instances. | Final acceptance evidence for the compatibility and prerequisite work. |
| GitHub release notes in the extension | Show release notes with designed UX, trusted/sanitized data, caching/offline behavior, accessibility, navigation, privacy, and failure handling. | Explicitly selected by the maintainer; public feedback contains no demand signal. |

## Remaining backlog

| Candidate | Scope | Priority signal |
|-----------|-------|-----------------|
| Dependency conflicts and quality gates | Resolve all `MSB3277` conflicts and remove the sole warning exception; redesign and, if still desired, re-enable DRY, mutation, and CRAP against real production targets. | Deferred from feature 001 as P0. No later product feature should weaken the now-green mandatory gates. |
| Run/debug target and toolchain reliability | Make startup-target discovery and rustup parsing typed and actionable across fresh binary crates, examples, multiple binaries, missing active toolchains, Ctrl+F5, and invalid launch selections. | Largest separate unresolved bug cluster: [#29](https://github.com/kitamstudios/rust-analyzer.vs/issues/29), [#43](https://github.com/kitamstudios/rust-analyzer.vs/issues/43), [#52](https://github.com/kitamstudios/rust-analyzer.vs/issues/52), [#55](https://github.com/kitamstudios/rust-analyzer.vs/issues/55), [#58](https://github.com/kitamstudios/rust-analyzer.vs/issues/58), [#59](https://github.com/kitamstudios/rust-analyzer.vs/issues/59), and post-fix recurrence [#69](https://github.com/kitamstudios/rust-analyzer.vs/issues/69). |
| Cargo process and build coordination | Prevent stale incremental-debug binaries and indefinite Cargo build-directory locks; define ownership, cancellation, bounded waits, working directory, and environment behavior. | Open issues [#44](https://github.com/kitamstudios/rust-analyzer.vs/issues/44) and [#45](https://github.com/kitamstudios/rust-analyzer.vs/issues/45), plus residual reports in closed [#34](https://github.com/kitamstudios/rust-analyzer.vs/issues/34). |
| Editor and LSP configuration parity | Design coherent settings for inlay hints, brace completion, Navigation Bar, rust-analyzer flags/environment, suggestions, Quick Info timing/size/color, and Code Definition Window support. | Repeated Marketplace and GitHub demand: [#22](https://github.com/kitamstudios/rust-analyzer.vs/issues/22), [#28](https://github.com/kitamstudios/rust-analyzer.vs/issues/28), [#35](https://github.com/kitamstudios/rust-analyzer.vs/issues/35), [#46](https://github.com/kitamstudios/rust-analyzer.vs/issues/46), [#47](https://github.com/kitamstudios/rust-analyzer.vs/issues/47), [#48](https://github.com/kitamstudios/rust-analyzer.vs/issues/48), and [#49](https://github.com/kitamstudios/rust-analyzer.vs/issues/49). Extension-model analysis should identify what is feasible before implementation. |
| ARM64 packaging feasibility | Determine whether the extension and every bundled/native dependency can support Visual Studio ARM64; document blockers or add packaging/matrix coverage. | One Marketplace request; current manifest is amd64-only. Lower evidence than VS2026/amd64 work. |
| Workspace refresh without restart | Detect newly added crates and refresh indexing without restarting Visual Studio. | Marketplace Q&A 64790; publisher-confirmed limitation with no GitHub tracker. |
| rust-analyzer memory investigation | Reproduce reported extreme memory growth, distinguish extension process-lifetime ownership from upstream rust-analyzer behavior, and document or fix the responsible layer. | One Marketplace report of 14 GB usage; investigate before assigning implementation ownership. |
| README identity and troubleshooting accuracy | Restore the non-affiliation clarification and review current prerequisite/LTSC troubleshooting guidance. | Closed [#11](https://github.com/kitamstudios/rust-analyzer.vs/issues/11) appears regressed; closed [#57](https://github.com/kitamstudios/rust-analyzer.vs/issues/57) found missing compatibility guidance. |

## Hardening sequence

| Candidate | Scope | Priority signal |
|-----------|-------|-----------------|
| Safe telemetry | Remove machine/user-derived identity and unsafe payloads; decide remove, opt-in, or a minimal allow-list with injected configuration. | High-priority privacy finding from the architecture review; follows readiness work. |
| CI supply chain and release provenance | Pin actions, minimize permissions, separate verified artifacts from release authorization, and add dependency/tool provenance. | Merge validation is now fail closed; immutable dependencies and release authorization were deliberately left for the next program. |
| Process ownership and cancellation | Give LSP, Cargo, rustup, build, discovery, and test children one owned lifetime/cancellation/disposal contract with race coverage. | Architecture-review priority; prerequisite for updater and protocol hardening. |
| Asynchronous failure visibility | Replace unobserved fire-and-forget work with awaited operations or a fault-observing, logging boundary. | Architecture-review priority; depends on safe telemetry/logging and process ownership. |
| Safe offline updater | Separate check/acquire/verify/stage/activate/rollback; verify artifacts, prevent traversal, tolerate offline/rate-limit failures, and retain a known-good version. | Security/reliability priority from the architecture review; depends on readiness, process ownership, and observable failures. |
| Cargo, rustup, test, path, and environment protocols | Prefer typed machine-readable protocols; define nightly degradation; centralize Windows executable lookup, environment merge, paths, working directories, and quoting. | Architecture-review priority; structured Cargo executable discovery is complete, while rustc sysroot failure validation still needs hardening. |
| Workspace and UI performance | Measure and then cache/batch readiness, workspace, test-container, menu, query-status, and output updates without process/network work on UI query paths. | Lower than correctness/security work; follows stable readiness, async, and protocol contracts. |
| Cross-version ApprovalTests resilience | Define fixture-version bands, semantic assertions versus snapshots, reusable normalizers, actionable diffs, and an explicit human-reviewed update workflow. | Deferred after narrow feature-001 normalization; follows the supported tool/host matrix. |

## Product ideas

| Candidate | Scope | Priority signal |
|-----------|-------|-----------------|
| Cross compilation and remote execution | Support wasm/Linux cross compilation, WSL2 build/run, and potentially container-based workflows. | Existing README “Upcoming” item; no newer priority decision. |
| Test experience enhancements | Improve Rust test UX, including document and benchmark tests and the known duplicate integration-test filename limitation. | Existing README item plus issues [#4](https://github.com/kitamstudios/rust-analyzer.vs/issues/4), [#30](https://github.com/kitamstudios/rust-analyzer.vs/issues/30), and [#41](https://github.com/kitamstudios/rust-analyzer.vs/issues/41); lower urgency than IDE lockout and run/debug reliability. |

Project templates remain an explicit non-goal despite Marketplace feedback and closed issues
[#9](https://github.com/kitamstudios/rust-analyzer.vs/issues/9) and
[#66](https://github.com/kitamstudios/rust-analyzer.vs/issues/66); direct users to `cargo new` unless
product direction changes.

## Feedback snapshot

As of 2026-08-24, the
[Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustAnalyzer)
reports 41,740 installs and 4.8/5 from 50 ratings (35 textual). The complete public review set
contains two severe restart-loop reports, two
syntax/editor-readability requests, one ARM64 request, one project-template request, and one
rust-analyzer memory report; other textual reviews are praise or non-actionable. Repository research
covered all 58 GitHub issues (21 open, 37 closed), all nine Marketplace Q&A threads, and all four
GitHub Discussions. Closed items were retained above only when closure was unclear, the behavior
recurred, or a documented limitation remains.

Specialized performance and backward-compatibility suites are part of the relevant feature, not
substitutes for unit, integration, acceptance, or exploratory testing.
