# copilot-instructions.md — Agent playbook

This file is the root of the agent governance framework. Individual agents have their specific governance files. These
governance instructions & guardrails are sacrosanct. NEVER bypass, ignore, deviate or override them. DO NOT overdo
things.

## Golden rules (guardrails)

0. General principles:
   - When writing English (docs, code comments, markdowns, teams messages):
     - Be crisp and high-signal. Avoid verbosity. Don't repeat the human's words back to them.
     - Use the fewest words that preserve the meaning.
     - For Markdown, follow `.github/skills/markdown.md`.
   - Don't assume. Don't hide confusion. Surface tradeoffs.
   - State assumptions explicitly. If uncertain, ask.
   - If multiple interpretations exist, present them — don't pick silently.
   - If a simpler approach exists, say so. Push back when warranted.
   - If something is unclear, stop. Name what's confusing. Ask.
1. Always reload and understand the high-level design and architecture from `docs/design.md`.
2. Separation of duties (strict). Do not cross these lanes (see `.github/agents/`).
3. Never commit to trunk. Detect it with `git symbolic-ref --short refs/remotes/origin/HEAD`; if that
   fails, use the fallback in `docs/design.md`. Work on `vibe/<nnn>-<feature_name>`.
4. Never deploy.
5. Never hand-edit generated or acquired artifacts listed in `docs/design.md`.
6. Stop and ask when a task needs a product/architecture decision. That call belongs to the human architect.
7. The human can invoke any agent on demand.
8. Never hardcode connection strings, secrets, or license keys; they are injected via env vars.
9. Record project facts in `docs/design.md`, role facts in `.github/agents/<agent>.md`, and cross-cutting
   governance here — never in global Copilot Memory.
10. The governance artifacts are the source of truth — reload them; never rely on memory or recall.

When citing a guardrail elsewhere, refer to it **by number** (e.g. "golden rule #1"); keep the
numbering **stable** and update references on any insert/reorder.

## Commands

| Command | Gate | Required | Value |
|---------|------|----------|-------|
| `build`         | fast + full   | yes | `pwsh -NoLogo -NoProfile -NonInteractive -File .\.github\scripts\Invoke-Build.ps1` |
| `test:quick`    | fast (Dave)   | yes | `pwsh -NoLogo -NoProfile -NonInteractive -File .\.github\scripts\Invoke-Tests.ps1 -Mode unit` |
| `test:full`     | full (Bhaskar)| yes | `pwsh -NoLogo -NoProfile -NonInteractive -File .\.github\scripts\Invoke-Tests.ps1 -Mode full` |
| `dry-check`     | full          | optional | `none` |
| `mutation-test` | full          | optional | `none` |
| `crap-check`    | full          | optional | `none` |

Add rows for any other commands your stack needs — the gate recipes run them **after** the core steps;
a stack needing a **pre-build** step (e.g. `restore`, `type-check`) should reorder its own recipe (the
recipe is authoritative for order).
