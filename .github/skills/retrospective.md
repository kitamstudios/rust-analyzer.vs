---
name: retrospective
description: Periodic cross-feature governance retrospective. Every ~5 completed features, distill durable learnings from docs/agents/skills/commits into all-agent guardrails + per-agent notes, then apply a minimal governance update. Count-based, not time-based; the pack's assistant reminds the human when it's due.
---

A recurring, minimal-footprint review that turns accumulated delivery experience into durable
guardrails. Event/count-based, **not** time-based.

## Packs

In a **4-pack**, the assistant reminds, Anders (architect) distills, and Dave (coder) applies. In a
**1-pack**, the assistant (solo) performs all three roles. The human always approves any guardrail change.

## When

After a feature completes, the pack's assistant compares the current
feature-doc count to the `features=N` value on the last `## Log` line below; when it has grown by
**≥ 5**, the assistant **reminds the human** to run this skill before the next feature.

Feature-doc count (excludes the template):

    (Get-ChildItem docs/features/*.md | ? { $_.Name -ne 'TASK_FILE_TEMPLATE.md' }).Count

## Sources (read-only)

`docs/features/*.md` (especially post-review / post-test-fix logs), `docs/design.md`,
`docs/backlog.md`, `.github/copilot-instructions.md`, `.github/agents/*.md`,
`.github/skills/*`, and `git log` / commit diffs since the last Log entry.

## Produce (Anders)

Distill cross-cutting, durable lessons (skip feature specifics; verify claims against docs/commits)
into two parts, prioritising high-signal, recurring issues:

- **(A) All-agent guardrails** — candidate golden-rule additions/refinements for `copilot-instructions.md`.
- **(B) Per-agent learnings** — short sections for Anders / Dave / Bhaskar and the **assistant's own agent
  file** (`.github/agents/<Persona>.md`).

Main thing: Don't overdo this.

## Then (minimal governance update)

1. Architect proposes exact redlines. Keep edits minimal.
2. **Human approves** any guardrail/golden-rule change (guardrails are the human's call).
3. Coder applies the approved set: promote strong learnings to golden rules in
   `copilot-instructions.md`, add per-agent notes to the agent files, and fix stale cross-references.
   Cite guardrails **by number**; keep the numbering **stable** and update references on any insert/reorder.

## Log (append one line per run)

    - YYYY-MM-DD · features=N · <1-line summary of what changed>

- 2026-08-03 · features=0 · Framework seeded; retrospective process created, not yet run.
