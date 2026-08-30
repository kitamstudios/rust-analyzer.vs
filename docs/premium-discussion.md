# rust-analyzer.vs - Free and Premium Boundary

> **Status:** Product decision draft. No paid artifact, entitlement, price, or release is approved.

## Thesis

Visual Studio Rust is a niche but durable market: mixed C++/Rust teams, Windows enterprises, driver
developers, and organizations standardized on Visual Studio.

Keep the free extension dependable on VS2022 17.12+ and VS2026. Premium may add specialized
commercial workflows, but must not weaken the complete free Rust experience.

Agentic coding increases the value of dependable build, target, debug, deployment, and hardware
workflows. It does not make editor and project integration optional.

The earlier draft recorded about 41.7k Marketplace installs. Revalidate before forecasting; installs
are not active users or buyers.

## Current constraints

- The free product is one Windows amd64 VSIX for VS2022 and VS2026.
- The standalone TestAdapter is a separate free contract.
- Feature 003 retired RustDevelopmentPack; no paid artifact or publication path exists.
- `RustAnalyzer.Remote` is a stub. Cargo, path, process, and debug flows are local and Windows-only.
- Telemetry privacy and updater integrity remain unresolved.

## Boundary

### Never paywall

- Security, privacy, integrity, accessibility, compatibility, reliability, or performance fixes.
- Existing features or regressions.
- A complete local edit, build, run, debug, and test loop.
- rust-analyzer language features, diagnostics, code actions, and configuration.
- Editor parity features such as inlay hints, brace completion, Navigation Bar, and Quick Info.
- Basic Cargo operations, targets, document tests, and benchmarks.
- Basic Test Explorer, Open Folder, workspaces, and super workspaces.
- Basic `Cargo.toml` editing and vulnerability warnings.
- The standalone TestAdapter.
- Every advertised Visual Studio host and architecture.

### Premium value

Users pay for maintained orchestration across additional environments, hardware, deployment systems,
or organizational policy - not correctness or upstream feature pass-through.

Credible areas:

- WSL2/Linux workflows;
- embedded/QEMU/probe-rs workflows;
- Windows driver workflows;
- managed Wasm/container workflows;
- rich Cargo dependency productivity;
- nextest, coverage, and performance workflows; and
- enterprise policy, reporting, remediation, and support.

Existing local debugging remains free. Advanced debugging belongs inside a target workflow.

Security findings remain free. Premium may aggregate findings, enforce policy, manage exceptions, and
produce reports.

## Backlog groups

### Required and free

- Gate quality: DRY, mutation, CRAP, and nightly-pin renewal.
- Launch/workspace correctness: startup discovery, binaries/examples, toolchains, Ctrl+F5, selection,
  stale binaries, crate refresh, manifests, and duplicate test filenames.
- Process integrity: protocols, errors, locks, cancellation, waits, ownership, paths, quoting,
  environments, working directories, and fault observation.
- Updater security: verification, traversal prevention, staged activation, rollback, offline/rate
  handling, and known-good retention.
- Privacy: remove machine identity and unsafe payloads; decide telemetry policy; inject configuration.
- Supply chain: immutable action pins, least privilege, release separation, and provenance.
- Performance/supportability: readiness/workspace measurement, batched updates, responsive command
  status, memory diagnosis, and ownership attribution.
- ApprovalTests compatibility, non-affiliation, and accurate prerequisite/LTSC guidance.

### Core free product

- Editor and rust-analyzer configuration parity.
- Basic target installation, selection, and Cargo `--target`.
- ARM64 support, if approved.
- Basic document-test and benchmark discovery/execution.
- Basic `Cargo.toml` behavior.

### Premium candidates

- WSL2/Linux, embedded, driver, Wasm, and container orchestration.
- Cargo dependency workflows beyond basic editing.
- Advanced test execution, coverage, and performance analysis.
- Enterprise policy, reporting, remediation, and support.

### Pending backlog cleanup

- Remove the two resolved MSB3277 items.
- Replace the archived Feature 002 umbrella with explicit approved residuals.
- Close or narrow fire-and-forget and logging items after T16.
- Narrow prerequisite documentation to the remaining LTSC gap.
- Merge rustup parsing with machine-readable protocols.
- Merge Cargo ownership, cancellation, waits, and child-process lifetime work.
- Merge executable, environment, path, quoting, and working-directory work.
- Merge memory reproduction with upstream/extension ownership diagnosis.
- Group ARM64, telemetry, CI supply chain, updater, ApprovalTests, and cross-target work as programs.

No backlog edit is approved here.

## Candidate assessment

| Candidate | Buyer | Risk | Differentiation | Disposition |
|---|---|---:|---:|---|
| WSL2/Linux build and run | Windows enterprise; mixed C++/Rust teams | High | High | Recommended paid v1 |
| Embedded/QEMU/probe-rs | Firmware, IoT, device teams | Very high | High | Later, with design partners |
| Windows drivers | OEM, security, kernel teams | Very high | High | Later, with design partners |
| Wasm/container workflows | Web, backend, edge teams | High | Medium | Research after WSL |
| Cargo dependency productivity | Application/library maintainers | Medium | Low-medium | Not paid v1 |
| nextest/coverage/performance | Teams with large suites or CI cost | Medium-high | Medium | Later |
| Enterprise governance/support | Regulated organizations; platform teams | High | Unproven | Validate demand first |

## Smallest paid v1

Ship one vertical: **WSL2 Build & Run**.

### Included

- Select one installed WSL2 distribution.
- Validate its stable Rust toolchain.
- Map Windows-hosted Cargo workspaces into WSL.
- Build and run one selected Cargo binary.
- Map diagnostics and paths back to Visual Studio.
- Preserve local free build and run.
- Support VS2022 17.12+ and VS2026.

### Excluded

- F5 debugging;
- Test Explorer and TestAdapter changes;
- Linux-filesystem workspaces;
- multiple distributions per workspace;
- SSH, containers, Wasm, embedded, and drivers;
- Cargo dependency UI;
- templates;
- enterprise services; and
- agentic workflows.

This is the smallest credible differentiator. Cargo version decorations are too close to table stakes
to validate willingness to pay.

## Earlier Phase 1 disposition

| Earlier item | Disposition |
|---|---|
| Cargo dependency decorations | Candidate; insufficient for paid v1 |
| One-click version update/list | Candidate; safety and vulnerability information stay free |
| Toolchain display and rustup helpers | Free |
| Small QEMU runner set | Premium later; not low risk |
| Better rust-analyzer configuration | Free |
| Existing debug/test polish | Free |

Replace "anything helping paid work is paid" with:

> Free provides a complete, dependable local Rust loop. Premium removes specialized target,
> deployment, hardware, or organizational complexity.

## Licensing and entitlement

`LICENSE.txt` applies CC BY-NC-SA 4.0. A proprietary paid derivative cannot rely only on that grant.
Before implementation:

1. Confirm commercial rights to every contribution.
2. Audit bundled assets and dependencies.
3. Obtain legal review of the commercial or dual-license model.
4. Choose shared, source-available, or proprietary premium source.

Entitlement rules:

- Free use requires no account or entitlement.
- Gate only premium surfaces.
- Failure leaves every free feature operational.
- Do not derive entitlement from telemetry identity.
- Define offline and grace behavior.
- Minimize and document collected data.
- Keep entitlement out of the TestAdapter unless separately approved.
- Never embed private signing or licensing secrets.

## Delivery decision

The earlier draft preferred a paid extension that subsumes free. Preserve that preference; it is not
approved.

| Option | Main risk |
|---|---|
| Replacement VSIX | New identity, migration, split installs/reviews/settings, duplicated delivery |
| Companion VSIX | Cross-extension API and version coupling |
| One freemium VSIX | Entitlement code enters every free installation |

Do not revive RustDevelopmentPack. Paid delivery needs a new feature design.

## Risks

- Free-product stagnation weakens adoption and the paid funnel.
- Paywalling parity or safety damages trust.
- Multiple VSIX identities can create upgrade and MEF conflicts.
- WSL, hardware, driver, and container matrices create sustained support cost.
- Upstream tool changes create recurring maintenance.
- Paid customers expect support, not only feature flags.
- Install count does not prove willingness to pay.

## Decisions required

1. Approve or reject WSL2 Build & Run as paid v1.
2. Choose replacement, companion, or freemium delivery.
3. Confirm commercial rights and source-license strategy.
4. Choose entitlement provider, identity, offline behavior, and privacy contract.
5. Decide whether Cargo dependency work is a later premium candidate.
6. Resolve the README's conflicting template claims.
7. Choose pricing only after active-use, buyer, support-cost, and willingness-to-pay evidence.

## Should the extension target emulators?

Yes. First. Then probes.

Emulators give:

- zero hardware
- reproducible CI
- easy onboarding
- safe default "Run"

Probes give:

- the paid commercial workflow
- flash / RTT / real peripherals

## What the Embedded Rust Book covers beyond hello world

It is a patterns book, not a product book.

After hello world it covers, mostly on STM32F3DISCOVERY:

- LED / GPIO
- memory-mapped registers
- panics, exceptions, interrupts
- singletons, typestate, HAL design
- concurrency patterns
- C/Rust FFI

It does not build a complete product (logger, motor controller, USB device, radio node).

For project-style learning, the community's Discovery book is the better analogue.

## 7. Windows drivers

### Should you target them?

- Windows drivers: yes
- Linux drivers: no as a first-class target

### Debugging Rust Windows drivers from VS 2022/2026

| Type | In Visual Studio? | Reality |
|---|---|---|
| UMDF | Reasonably yes | Attach to WUDFHost.exe; closest to normal debug |
| KMDF / WDM | Limited | Still mostly WinDbg + test VM + test signing |

Microsoft stack:

- windows-drivers-rs
- cargo-wdk
- build story is ahead of the debug story

Extension value:

- templates
- cargo-wdk integration
- packaging / INF / signing helpers
- UMDF launch configs
- WinDbg helper commands for KMDF
- do not promise a C++-quality F5 kernel debugger in VS

## 8. Pricing

Recommended Phase 2 price: $129–$179 / seat / year

Best default: $149 / seat / year

Suggested ladder:

- Individual / indie: $129
- Professional default: $149
- Company / team: $149–$179, volume discount at 5–10 seats

Phase pricing:

| Phase | Suggested |
|---|---|
| Phase 1 | $79–$99 |
| Phase 2 | $129–$179 |
| Phase 3 | $199–$299+ or custom |

Comparables used:

- RustRover commercial often lands roughly $69–$229/year depending on SKU
- Visual Assist personal ~$129/year
- Specialized embedded/driver tools often $200–$600+/year

## 9. Revenue projections

Assumptions:

- ~41.7k free installs
- active users much lower, roughly 5k–12k
- conversion 1.5–4% normally, higher if Embedded + Drivers ship early
- $149/year

### Base Phase 2 (no accelerated Embedded+Drivers promise)

| Period | Pessimistic | Realistic | Optimistic |
|---|---|---|---|
| Month 1 | $1k–$3k | $3k–$7k | $8k–$15k |
| 6 months | $6k–$15k | $15k–$35k | $40k–$70k |
| Year 1 | $15k–$40k | $40k–$90k | $100k–$160k |

Realistic seats:

- Month 1: 20–50
- 6 months: 100–230
- Year 1: 270–600

### Accelerated case: Embedded + Windows Drivers ship within 1 month

| Period | Pessimistic | Realistic | Optimistic |
|---|---|---|---|
| Month 1 | $2.5k–$6k | $6k–$12k | $15k–$25k |
| 6 months | $15k–$30k | $35k–$70k | $80k–$130k |
| Year 1 | $35k–$70k | $80k–$150k | $170k–$250k+ |

Realistic seats:

- Month 1: 40–80
- 6 months: 230–470
- Year 1: 540–1,000

These are planning ranges, not guarantees. Marketplace conversion is usually slow in month 1 and
compounds in months 3–12.

## 10. CAPEX / OPEX / Net if you work in agentic mode

Assumptions:

- machine already owned
- only new purchase is Copilot or Claude Code
- heavy agentic-loop style work
- Embedded + Drivers ship in month 1
- Marketplace + payment friction ~8%

### CAPEX

$0 new.

### AI OPEX (2026 individual pricing, heavy agent use)

| Tool | Typical heavy-use plan | Monthly | Yearly |
|---|---|---|---|
| GitHub Copilot Max | heavy agent | $100 | $1,200 |
| Claude Code Max 5x | common heavy | $100 | $1,200 |
| Claude Code Max 20x | very intensive | $200 | $2,400 |

Budget $100–$200/month ($1,200–$2,400/year).

Note: unbounded API-style agent use can cost much more than the subscription cap. Stay on Max-style
plans rather than raw API if you want predictable OPEX.

### Net revenue — realistic accelerated case

| Period | Gross | AI | Fees ~8% | Net |
|---|---|---|---|---|
| Month 1 | $6k–$12k | $100–$200 | $0.5k–$1.0k | $5.3k–$10.7k |
| 6 months | $35k–$70k | $0.6k–$1.2k | $2.8k–$5.6k | $31k–$63k |
| Year 1 | $80k–$150k | $1.2k–$2.4k | $6.4k–$12k | $70k–$135k |

Central year-1 planning number:

- Gross ~$110k
- AI ~$1.8k
- Fees ~$8.8k
- Net ~$99k

AI cost is small relative to revenue if conversion works. The risk is not the license. The risk is
conversion and feature quality.

## 11. Recommended near-term build order

1. Stabilize free extension on VS 2022 + 2026.
2. Ship paid shell + license check.
3. Cargo Intelligence MVP.
4. QEMU MVP (lm3s6965evb + one MPS2 + RISC-V virt).
5. probe-rs / RTT path.
6. Windows driver templates + cargo-wdk + UMDF debug helpers.
7. Security/testing extras (audit, nextest).
8. Only then Phase 3 specialty work.

## 12. Caveats

- Visual Studio Rust is a niche inside a niche.
- Install count ≠ active users ≠ paying users.
- Kernel-mode Rust debug in VS will not match C++ WDK polish soon.
- QEMU coverage is architectural, not a substitute for the exact customer MCU.
- Revenue ranges assume competent marketing into embedded and Windows-driver communities, not
  "publish and wait."
