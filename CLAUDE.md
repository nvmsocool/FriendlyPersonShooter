# FriendlyPersonShooter

Co-op PvE FPS in Unity 6000.6.7f1, built with friends. Design: `Docs/GameDesignDocument.md` (pillars: every bullet helps someone, strangers are good news, hard honest aim, no PvP). Playtest notes: `Docs/Playtest.md`.

## Layout
- `Assets/Scripts/Core`: engine-free game logic (combat, expedition, progression). Tested outside Unity.
- `Assets/Scripts/Arena`, `Assets/Scripts/Net`: Unity/Netcode gameplay, server systems, bots.
- `Assets/Scripts/ZoneArt`: zone visuals.
- `Assets/Editor`: build entry points (`PlaytestBuild`, `NetTestBuild`).
- Scenes: `Menu`, `Expedition`, `Arena` (playtest build); `NetTest` (net test build).
- `Tests/Core.Tests`: NUnit tests compiling `Assets/Scripts/Core` directly.

## Commands
- Tests: `powershell -ExecutionPolicy Bypass -File Tools/ci/test.ps1` (seconds).
- Build: `powershell -ExecutionPolicy Bypass -File Tools/ci/build.ps1`, which writes `Builds/Playtest/` and a zip. Fails if the Editor has this same folder open.
- When an Editor is open, prefer driving it with the `unity` CLI (`unity command ...`) over hand-editing scene/prefab YAML.

## Conventions
- Put new rules and numbers in `Core` (plain C#, no UnityEngine) with tests; keep Unity code thin.
- Unity's C# is version 9: no file-scoped namespaces, no `required`, no raw string literals.
- One MonoBehaviour per file, named after the class.
- Keep `.meta` files with their assets; never delete or regenerate GUIDs.
- `Docs/GameDesignDocument.md` is the only design doc. Update it in the same change when design rules or prototype status change. Don't create a copy elsewhere (Claude Docs, Google Docs, artifacts).

## Headless build quirks
- The first build right after a recompile can silently skip; `build.ps1` checks the output DLL timestamp.
- Builds may modify `ProjectSettings/ProjectSettings.asset` and leave `Assets/netcode-build-assets-temp`; `build.ps1` reverts both.

## Issue workflow (GitHub)
Friends open issues; `.github/workflows/claude-issue.yml` runs Claude on the owner's PC.
New issue or `@claude ...` → Claude posts a plan (no code changes). `@claude go` → Claude implements on branch `claude/issue-<n>`, the workflow tests, builds, opens a PR to `main`, and posts a pre-release zip on the issue. Only users in the repo variable `ALLOWED_USERS` can trigger it. Prompts live in `.github/claude/`.
