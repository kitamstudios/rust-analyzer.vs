---
name: agentify
description: Install this governance framework into a target project (as a 1-pack or 4-pack), or update an existing project's agent-loop artifacts; then stamp the adopted framework version (agentify commit hash), pack, and persona, and report any remaining FILL_ME placeholders.
---

The distribution / versioning mechanism for this framework. Run from a checkout of **agentify** with
the target repo path in hand. Copilot-only scope. Do this on a `vibe/<nnn>-adopt-agentify` branch in the
target — never on its trunk — so the human reviews the diff before merge.

## Ownership model

- **Framework-owned** (refresh on update): `AGENTS.md`, `.github/agent-templates/binder.md` (the binder **template body**, installed per persona as
  `.github/agents/<Persona>.md`), `.github/agents/` (the sub-agents `anders.md` / `dave.md` / `bhaskar.md`), `.github/agent-roles/` (all role bodies) and
  `.github/personas/` (all persona overlays), all of `.github/skills/` (including the command-referencing
  recipes `build-test.md` / `build-test-full.md`), `docs/meta-design.md`,
  `docs/features/TASK_FILE_TEMPLATE.md`, and the **rules** portion of `.github/copilot-instructions.md`
  (everything *above* the `## Project profile` heading).
- **Stamped** (framework-owned files carrying per-install values that are preserved / re-derived, never
  clobbered on update): the installed binder `.github/agents/<Persona>.md` frontmatter `name` (= Persona) and `description` (= the
  pack's role-desc); each installed agent's `model` + `reasoning` (derived from the Model profile); and in
  the Project profile, the *Framework version adopted* hash, **Pack**, **Persona**, and **Model profile**.
- **Consumer-owned** (create only if absent, never clobber): `docs/design.md`, `docs/backlog.md`,
  `docs/features/<nnn>-*.md`, and the **Project profile** block of `.github/copilot-instructions.md` —
  including its **Commands** table, whose command *values* are the consumer's and must never be
  overwritten once filled.
- **Plumbing** (`.editorconfig`, `.gitignore`, `.gitattributes`, `.vscode/`): create only if absent.

## What it does

1. **Ask which pack and which persona** — pack `1-pack` (solo generalist) or `4-pack` (full team), and
   the assistant's **persona** skin. **No default** for either; the human chooses. The pack selects the
   **role** (`4-pack ⇒ conductor`, `1-pack ⇒ solo`) and the copy set (see *Packs — copy sets* below);
   the persona menu is the overlays in `.github/personas/` (today `JARVIS` | `FRIDAY`) — **validate the
   pick ∈ that set**. A persona need not match the pack's natural assistant (e.g. a 1-pack skinned `JARVIS`);
   that's allowed — behavior derives from Pack (role), never the skin. Also **ask which model
   profile** — `anthropic`, `openai`, or `both` (**default `both`**); it sets every agent's `model` +
   `reasoning: max` (see *Model profiles* below).
2. **Copy / update artifacts** into the target per the ownership model above and the chosen pack + persona.
3. **Prompt for the required commands** — ask the consumer for each required Commands value (`build`,
   `lint`, `format:fix`, `format:check`, `test:quick`, `test:full`) and write them into the Project
   profile → Commands table. (The consumer may decline and hand-fill later; preflight blocks the loop
   until every required value is set.)
4. **Ask the optional-constraints opt-in** — "Provide the optional DRY / mutation / CRAP constraints?"
   On **yes**, fill the `dry-check` / `mutation-test` / `crap-check` rows in the Project profile →
   Commands table with the provided commands; on **no**, leave them as `none` (they already ship as
   `none`). Stronger and more varied constraints ⇒ the agents are more easily supervised, so encourage
   filling in as many optional Commands as apply.
5. **Stamp the version, pack, persona, and model profile.** Record the current agentify commit hash into the target's
   `.github/copilot-instructions.md` → Project profile → *Framework version adopted*, stamp the chosen
   pack into → *Pack*, the chosen persona into → *Persona*, and the chosen **model profile** into →
   *Model profile*; set the binder `.github/agents/<Persona>.md` frontmatter `name = <persona>` and
   `description = <the pack's role-desc>` (4-pack → conductor-desc; 1-pack → solo-desc — see *Packs — copy
   sets*); and set every installed agent's `model` per *Model profiles* below, each with `reasoning: max`:

       git -C <agentify-dir> rev-parse HEAD

   On update, replace the previously stamped hash with the new one, re-derive the binder frontmatter
   from Pack/Persona, and re-stamp each agent's `model`/`reasoning` from the recorded model profile
   (leave other profile values intact).
6. **Report remaining placeholders.** Run the target's `.github/skills/preflight.md` placeholder scan
   and list every unfilled `<<FILL_ME:` placeholder the human must complete before the loop can start.

## Packs — copy sets

Every install copies the same core — `copilot-instructions.md`, all 5 skills, docs, plumbing, `AGENTS.md`,
the binder (template `.github/agent-templates/binder.md`, installed as `.github/agents/<Persona>.md`), **exactly one** role body (the pack's) and **exactly one** persona
overlay (the chosen skin). The packs differ only in **which role body** ships and whether the **3
sub-agents** ship. The chosen pack is stamped into Project profile → *Pack* (and enforced by preflight's
assistant gate); the chosen persona into → *Persona* and the binder frontmatter `name`.

### 4-pack
Role body `.github/agent-roles/conductor.md`; the **3 sub-agents** `anders.md` / `dave.md` /
`bhaskar.md`; the binder installed as `<Persona>.md` with frontmatter `name = <persona>` and `description` =
*Runs the agentic loop (hub-and-spoke). Coordinates Dave, Bhaskar, and Anders. Read-only
inspection + git/task-file management only; never designs, codes, or verifies.* Full hub-and-spoke team
with strict separation of duties.

### 1-pack
Role body `.github/agent-roles/solo.md`; the binder installed as `<Persona>.md` with frontmatter `name = <persona>` and
`description` = *Solo generalist for the 1-pack: designs, implements, verifies, and reviews in one
context; owns git + the task file. Never deploys.* **No** sub-agents. Lighter on tokens; separation of
duties is waived (the solo assistant wears every hat).

## Model profiles

Every agent runs at **maximum reasoning** (`reasoning: max`); the profile only picks the **vendor** of
each role's MAX model. `agentify` asks for one at install (**default `both`**), stamps it into Project
profile → *Model profile*, and writes each installed agent's `model` accordingly. Per-vendor MAX SKUs:
Anthropic `Claude Opus 5 (copilot)`, OpenAI `GPT-5.6 Sol (copilot)`.

| Role — file | `anthropic` | `openai` | `both` (default) |
|-------------|-------------|----------|------------------|
| Architect — `agents/anders.md`            | Opus 5 | GPT-5.6 Sol | **Opus 5**      |
| Coder — `agents/dave.md`                  | Opus 5 | GPT-5.6 Sol | **Opus 5**      |
| Verifier — `agents/bhaskar.md`            | Opus 5 | GPT-5.6 Sol | **GPT-5.6 Sol** |
| Driver — the binder `agents/<Persona>.md` | Opus 5 | GPT-5.6 Sol | **GPT-5.6 Sol** |

- **`anthropic`** — every agent on Claude Opus 5.
- **`openai`** — every agent on GPT-5.6 Sol.
- **`both`** (recommended) — Opus 5 designs & codes, GPT-5.6 Sol verifies & drives; keeps **coder ≠
  verifier vendor** so the independent check doesn't inherit the coder's blind spots. In a **1-pack**
  only the binder ships, so `both` collapses to that one agent's vendor (GPT-5.6 Sol) — the cross-vendor
  benefit is a 4-pack property.

## Install (new target) · PowerShell sketch

> **New target only.** This copies the rules + Project profile and the chosen pack's binder + role +
> persona files wholesale (overwriting any existing Project profile). For a repo that already adopted the
> framework, use **Update** below — never re-run Install.

    $src = "<agentify-dir>"; $dst = "<target-repo>"; $pack = "<1-pack | 4-pack>"; $persona = "<JARVIS | FRIDAY>"
    if ($pack -notin '1-pack','4-pack') { throw "Pick a pack explicitly: '1-pack' or '4-pack' (no default)." }
    $menu = (Get-ChildItem "$src/.github/personas/*.md" | % BaseName)          # e.g. jarvis, friday
    if ($persona.ToLower() -notin $menu) { throw "Pick a persona from .github/personas (no default): $($menu -join ', ')." }
    if ($persona.ToLower() -in 'anders','dave','bhaskar') { throw "Persona '$persona' collides with a sub-agent filename; choose another." }
    $binderName = "$($persona.ToUpper()).md"                                    # binder installs per persona, all-caps: JARVIS.md / FRIDAY.md
    $modelProfile = "both"                                                      # <anthropic | openai | both>; default both (see Model profiles)
    if ($modelProfile -notin 'anthropic','openai','both') { throw "Pick a model profile: anthropic | openai | both (default both)." }
    $maxA = 'Claude Opus 5 (copilot)'; $maxO = 'GPT-5.6 Sol (copilot)'          # per-vendor MAX SKUs; reasoning is 'max' for every role
    #   role -> model:  anthropic => all $maxA;  openai => all $maxO;
    #   both => architect(anders)+coder(dave) = $maxA, verifier(bhaskar)+driver(binder) = $maxO
    $bothModels = @{ anders = $maxA; dave = $maxA; bhaskar = $maxO; binder = $maxO } # authoritative both-profile stamp map
    Copy-Item "$src/AGENTS.md","$src/.editorconfig","$src/.gitignore","$src/.gitattributes" $dst
    New-Item "$dst/.github/agents","$dst/.github/skills", `
             "$dst/.github/agent-roles","$dst/.github/personas" -ItemType Directory -Force
    Copy-Item "$src/.github/copilot-instructions.md" "$dst/.github"
    Copy-Item "$src/.github/skills/*" "$dst/.github/skills" -Force   # all 5 skills (every install)
    $role = if ($pack -eq '4-pack') { 'conductor' } else { 'solo' }
    Copy-Item "$src/.github/agent-roles/$role.md"              "$dst/.github/agent-roles"   # ONE role body
    Copy-Item "$src/.github/personas/$($persona.ToLower()).md" "$dst/.github/personas"      # ONE persona overlay
    Copy-Item "$src/.github/agent-templates/binder.md"          "$dst/.github/agents/$binderName"   # binder template -> agents/<PERSONA>.md
    if ($pack -eq '4-pack') {
        Copy-Item "$src/.github/agents/anders.md","$src/.github/agents/dave.md", `
                  "$src/.github/agents/bhaskar.md" "$dst/.github/agents"                     # sub-agents (no jarvis/friday)
    }
    Copy-Item "$src/.vscode" $dst -Recurse
    New-Item "$dst/docs/features" -ItemType Directory -Force
    Copy-Item "$src/docs/meta-design.md" "$dst/docs"
    Copy-Item "$src/docs/features/TASK_FILE_TEMPLATE.md" "$dst/docs/features"
    if (-not (Test-Path "$dst/docs/design.md"))  { Copy-Item "$src/docs/design.md"  "$dst/docs" }
    if (-not (Test-Path "$dst/docs/backlog.md")) { Copy-Item "$src/docs/backlog.md" "$dst/docs" }
    # then: stamp Pack + Persona + Model profile in the profile; set the installed binder agents/$binderName
    #       frontmatter name=$persona + description=($role-desc); set each installed agent's model (per
    #       $modelProfile) with reasoning: max; stamp the agentify hash (step 5); run preflight (step 6)

## Update (existing target)

Refresh framework-owned files only — **including** the binder **body** (from `.github/agent-templates/binder.md` into the consumer's `.github/agents/<Persona>.md`), the role bodies in
`.github/agent-roles/`, the persona overlays in `.github/personas/`, and the now-framework-owned recipes
`build-test.md` / `build-test-full.md`; **preserve every consumer-filled value** (Project profile values
including the Commands table, `docs/design.md`, and all other consumer-owned files) **and every stamp**
(the version hash, **Pack**, **Persona**, **Model profile**, the binder frontmatter `name`/`description`,
and each agent's `model`/`reasoning` — preserved / re-derived from Pack+Persona+Model profile, never
clobbered). If the rules portion of `copilot-instructions.md` changed,
splice in the new rules. **Also splice into the consumer's existing `## Project profile` any NEW
framework-defined fields/tables the adopter lacks** — notably the **Pack**, **Persona**, and **Model
profile** fields and the **Commands** table skeleton — so an updated adopter gains them **without losing
existing values**. Then re-stamp the hash (and Pack/Persona/Model profile if they changed), re-derive the
binder frontmatter and each agent's `model`/`reasoning`, and re-run preflight.

> **Legacy binder cleanup (required on update).** Earlier installs placed the binder at a fixed
> filename (`agents/driver.md`, later `agents/assistant.md`). Identify the consumer's existing binder —
> the lone `.github/agents/*.md` whose stem is **not** `anders`/`dave`/`bhaskar` — refresh its body from
> `.github/agent-templates/binder.md` into `agents/<Persona>.md` (re-stamping `name`/`description`),
> then `git rm` every other non-sub-agent `agents/*.md` (e.g. the old `driver.md` / `assistant.md`) so
> exactly one binder remains. Idempotent: if `agents/<Persona>.md` already exists and no legacy file
> remains, it is a no-op.

> **One-time migration (inline recipes → Commands table).** A consumer who filled the *old* inline
> `build-test.md` / `build-test-full.md` migrates in this order: **(1)** splice the Commands-table
> skeleton (and the Pack field) into the Project profile; **(2)** move the command values from the old
> inline `build-test.md` / `build-test-full.md` into that table; **(3)** refresh the now-framework-owned
> recipe files, which overwrite the old inline versions.
