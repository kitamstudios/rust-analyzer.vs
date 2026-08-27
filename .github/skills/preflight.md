---
name: preflight
description: The JARVIS runs this at session/loop start. Enforces assistant identity, required placeholders, and the pinned Rust nightly before the loop. Halts and reports otherwise.
---

Run by the JARVIS at the start of every session and before entering the loop. If any gate fails,
**do not start the loop** — report and stop.

**Separation of duties:** Gate 3 is an assistant-owned startup operation. Only JARVIS/the
assistant invokes `Initialize-RustNightly.ps1`. Never delegate installing/updating nightly to Dave
or Bhaskar. Their gate commands are validation/consumption only.

## Gate 1 — JARVIS-only loop

- No one else other than JARVIS should run this. Otherwise refuse and hand back:

## Gate 2 — Required placeholders filled

Verify that none of the agent governances files, skills & docs have `<<FILL_ME: ...>>` marker. This line is the only
exception.

If any match is found, list each file + placeholder and stop.

## Gate 3 — Pinned Rust nightly

Only after Gates 1 and 2 pass, JARVIS runs the single assistant-owned bootstrap:

    pwsh -NoLogo -NoProfile -NonInteractive -File .\.github\scripts\Initialize-RustNightly.ps1

## Pass

All three gates clean ⇒ proceed to mode selection (trunk ⇒ new-feature, `vibe/<nnn>-*` ⇒ WIP).
