---
name: preflight
description: The pack's loop assistant (`.github/agents/<Persona>.md`) runs this at session/loop start. Enforces assistant identity, required placeholders, and session Rust nightly before the loop. Halts and reports otherwise.
---

Run by the pack's loop assistant (`.github/agents/<Persona>.md`) at the start of every session
and before entering the loop. If any gate fails, **do not start the loop** — report and stop.

**Separation of duties:** Gate 3 is an assistant-owned startup operation. Only JARVIS/the
assistant invokes `Initialize-AssistantSession.ps1`, exactly once at session startup, before
delegating any work. That orchestrator creates a cryptographically random in-memory token, persists
only its hash/provenance, and alone passes the token to the nightly initializer. Never delegate
installing/updating nightly to Dave or Bhaskar. Their gate commands are validation/consumption only.

## Gate 1 — Assistant-only loop (role-based, persona-agnostic)

The loop is driven by a single **assistant** — the agent defined by `.github/agents/<Persona>.md`. Its Copilot
invocation name is the stamped **Persona** (e.g. JARVIS, FRIDAY); the **name does not gate**. Read
**Project profile → Pack**:

- **Pack unset** — "unset" means the Pack value is **not** one of the recognized tokens `1-pack` or
  `4-pack` (line missing, blank, still-unfilled, or any unrecognized string); a recognized token ⇒
  set ⇒ proceed. When unset, stop and ask the human to set it (the `agentify` skill stamps it; no
  default):
  > "Preflight: Project profile → Pack is unset. Set it to 1-pack or 4-pack (via the `agentify` skill), then re-run."

  **Guarantee note:** because the menu ships a valid literal (`4-pack`), Gate 1 cannot distinguish an
  explicitly-chosen pack from one inherited by a raw copy; the no-default guarantee is enforced at
  install time by `agentify` (the only supported install path), which rejects any pack ∉ {`1-pack`,
  `4-pack`}.
- **A non-assistant agent invoked the loop** — in a 4-pack the non-assistant agents are the fixed-named
  **Anders / Dave / Bhaskar**. If the invoker is one of these (or any agent other than the assistant),
  refuse and hand back:
  > "Preflight: the agentic loop is assistant-only. Handing back — please invoke the assistant (`.github/agents/<Persona>.md`)."
- **The assistant invoked** — the agent running `.github/agents/<Persona>.md` proceeds. (`Persona` may be
  unset; that does **not** block — the assistant falls back to a plain banner. Identity is enforced by
  role/sub-agent name, never by persona name.)

Any agent that is not the assistant stops here.

## Gate 2 — Required placeholders filled

The framework ships placeholders written as `<<FILL_ME: ...>>`. Scan the required files for the opening
sentinel `<<FILL_ME:` (a filled file has none; prose must never reproduce that sentinel). The loop must
not run while any remain — scan from the repo root:

    # PowerShell
    Select-String -Path .github/copilot-instructions.md, docs/design.md, `
      .github/skills/build-test.md, .github/skills/build-test-full.md -Pattern '<<FILL_ME:' -SimpleMatch

    # or ripgrep (any shell) — fixed-strings so << is literal
    rg -F -n "<<FILL_ME:" .github/copilot-instructions.md docs/design.md .github/skills/build-test.md .github/skills/build-test-full.md

> **Maintainer note:** inside those four files — including the **Commands** table in
> `copilot-instructions.md` — always write the token as the bare word `FILL_ME` in prose or comments,
> and keep the literal opening sentinel only in genuinely fillable cells/values. Never reproduce the
> sentinel in prose, or the scan will match your text and silently re-block the gate.

> **Source-vs-consumer note:** the agentify **source/menu** checkout is itself the distributable
> template — it intentionally keeps its consumer `FILL_ME` fields (the adopter's own prompts), so it
> is **expected** to report Gate 2 findings. Gate 2 is a **consumer-completion** gate, not an
> install-shape check. This is documentation, **not** a bypass — every checkout runs the same scan.

Required files (must contain no `<<FILL_ME:` sentinel before the loop runs):

- `.github/copilot-instructions.md` — Project profile, **including the Commands table** (where the
  per-command placeholders live)
- `docs/design.md` — real system design
- `.github/skills/build-test.md` — Dave's fast-gate recipe (framework-owned; references the Commands
  table, carries no placeholders — scanned harmlessly)
- `.github/skills/build-test-full.md` — Bhaskar's full-gate recipe (framework-owned; references the
  Commands table, carries no placeholders — scanned harmlessly)

If any match is found, list each file + placeholder and stop:

> "Preflight: N placeholder(s) still need filling before the loop can start: <list>. Fill them
> (see README → Getting Started, or the `agentify` skill), then re-run."

## Gate 3 — Session Rust nightly

Only after Gates 1 and 2 pass, JARVIS starts the single assistant-owned bootstrap orchestration:

    pwsh -NoLogo -NoProfile -NonInteractive -File .\.github\scripts\Initialize-AssistantSession.ps1 -AssistantStartup

The orchestrator creates the authorization handshake and invokes `Initialize-RustNightly.ps1` with
the in-memory token. The plaintext token is never written to the manifest or handed to a sub-agent.
Direct invocation without the matching token/phase fails before rustup.

The script uses rustup's normal toolchain installation, without changing the default or adding a
directory override. It installs/updates `nightly` with the minimal profile, rustfmt, clippy, and the
Windows amd64 MSVC target, then records rustc release/host/commit and cargo version in the current
session state. A rustup, network, component, install, or version-probe failure blocks the loop and
writes no nightly manifest.

Bhaskar's full test command validates that manifest and sets process-only
`RUSTUP_TOOLCHAIN=nightly` before starting VSTest. Cargo, rustc, test discovery, test executables, and
the standalone harness inherit that environment. Missing or changed nightly state fails explicitly;
stable fallback and silent integration-test skips are forbidden. Bhaskar never runs rustup install or
update; he stops and hands back to JARVIS when validation fails. Existing invalid nightly state is
never repaired or updated in-session.

After successful nightly bootstrap, the orchestrator records phase `ready`. The nightly manifest
records `BootstrapOwner=assistant` plus the authorization token hash. Consumer scripts require
matching current-session, repository, owner, phase, and hash provenance before using nightly.

## Pass

All three gates clean ⇒ proceed to mode selection (trunk ⇒ new-feature, `vibe/<nnn>-*` ⇒ WIP).
