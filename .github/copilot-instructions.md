# copilot-instructions.md — Agent playbook

This file is the root of the agent governance framework. Individual agents have their specific governance files. These
governance instructions & guardrails are sacrosanct. NEVER bypass, ignore, deviate or override them. DO NOT overdo
things.

## Golden rules (guardrails)

0. All agents:
   - Be crisp and high-signal. Avoid verbosity. Don't repeat the human's words back to them.
   - Don't assume. Don't hide confusion. Surface tradeoffs.
   - State your assumptions explicitly. If uncertain, ask.
   - If multiple interpretations exist, present them — don't pick silently.
   - If a simpler approach exists, say so. Push back when warranted.
   - If something is unclear, stop. Name what's confusing. Ask.
1. Always reload and understand the high-level design and architecture from `docs/design.md`.
2. Separation of duties (strict). Do not cross these lanes (see `.github/agents/`).
3. Never commit to the trunk branch. Work on a branch named `vibe/<nnn>-<feature_name>`. (Trunk is
   auto-detected — see Working agreement; `master`/`main` are only examples.)
4. Never deploy.
5. Never edit auto-generated files directly. Each consuming project lists its generated artifacts
   (path or glob) in the **Project profile** so all agents leave them alone.
6. Stop and ask when a task needs a product/architecture decision. That call belongs to the human architect.
7. The human can invoke any agent on demand.
8. Never hardcode connection strings, secrets, or license keys; they are injected via env vars.
9. Record durable facts in the relevant `.github/agents/<agent>.md` (or this file if cross-cutting),
    not in global Copilot Memory.
10. The governance artifacts are the source of truth — reload them; never rely on memory or recall.

When citing a guardrail elsewhere, refer to it **by number** (e.g. "golden rule #1"); keep the
numbering **stable** and update references on any insert/reorder.

## Working agreement (all agents)

- On every invocation, reload this file and `docs/design.md` before acting.
- **Trunk is auto-detected** as the origin default branch:
  `git symbolic-ref --short refs/remotes/origin/HEAD` (strip the `origin/` prefix). If detection fails
  (no remote yet), fall back to **Project profile → Trunk branch**.
- Determine your mode from the current branch: **trunk ⇒ new-feature mode**; **`vibe/<nnn>-*` ⇒ WIP
  mode**; otherwise defer to the human. Each agent file details its behaviour per mode.
- **The loop is driven by a single agent — the assistant (`.github/agents/<Persona>.md`).** Its Copilot
  invocation name is the stamped **Persona**, and its agent file carries the loop governance directly.
  Only the assistant starts/runs the loop; any other agent — in a 4-pack, the sub-agents
  Anders / Dave / Bhaskar — asked to run the loop **refuses and hands back to the assistant**.
- **Preflight before the loop.** The assistant runs `.github/skills/preflight.md` at session/loop start; if
  the caller isn't the assistant, Pack is unset, any required FILL_ME placeholder remains, or the
  Rust-nightly bootstrap fails, the loop **does not start**.
- **Bootstrap ownership.** Only the assistant performs the one-time rustup nightly install/update, by
  running `Initialize-RustNightly.ps1`; CI runs the same script as its own authority. The nightly
  manifest is scoped to the checkout and records the pinned channel, the repository root, and the
  rustc/cargo versions installed. Dave and Bhaskar only validate and consume existing nightly state;
  invalid state fails and returns to the assistant without self-healing, network access, update, or
  stale fallback.
- The feature file `docs/features/<nnn>-<feature_name>.md` is the source of truth for in-flight work.

---

## Project profile

- **Project name:** `rust-analyzer.vs`
- **Addressing the human:** Sir
- **Trunk branch (fallback only; normally auto-detected):** `master`
- **Pack:** `4-pack` _(team shape — assistant plus Anders / Dave / Bhaskar. Preflight Gate 1 **blocks** if the value is neither `1-pack` nor `4-pack`.)_
- **Persona:** JARVIS _(assistant skin; identity, banner and voice live in `.github/agents/JARVIS.md`.)_
- **Generated artifacts (never edit):** `**/bin/`, `**/obj/`, `_built/` (build outputs); `*.vsix`; the version values in `src/RustAnalyzer/source.extension.vsixmanifest` (`Identity/@Version`) and `src/RustAnalyzer/source.extension.cs` (the `Version` constant) — both auto-stamped by `.github/scripts/Set-VsixVersion.ps1`, the only caller allowed to write them; `src/RustAnalyzer/VSCommandTable.cs` (generated from `VSCommandTable.vsct`). Do not hand-edit `.g.cs`/build-generated files.
- **Acquired artifacts (never hand-edit; replace only through the documented acquisition process):** the binaries under `src/external/` — the packaged `rust-analyzer.exe` and `rust_analyzer.pdb`, and the checked-in `src/external/vs.17.11` Visual Studio host assemblies. They are acquired, not generated: replace one only from its official published asset with the hash verified before extraction, never by editing it in place. The Rust nightly bootstrap manifest under `%LOCALAPPDATA%\ravsq\` is likewise written only by `Initialize-RustNightly.ps1` and never hand-edited or repaired.
- **App run/restart & liveness mechanism:** Not a long-running service — this is a Visual Studio 2022 VSIX extension. To run/debug, build then start the `RustAnalyzer` project (F5), which launches the VS experimental instance (`devenv.exe /rootsuffix Exp`, `DeployExtension=True`). No liveness signal; validate via unit/integration tests and manual smoke in the Exp instance.

### Commands

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
