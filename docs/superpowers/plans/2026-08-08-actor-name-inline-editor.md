# Actor Name Inline Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a normal full actor name at rest and expand the same control into split structured-name fields only during editing.

**Architecture:** Add a pure display/edit state transition rule beside the existing manual-name rules. Refactor the Unity patch to restore the original layout for display, enter split layout from the original field's pointer click, and commit only after both editor fields lose focus.

**Tech Stack:** C# 11, Unity UGUI `InputField`, Harmony, NUnit-free AW3 rules executable, PowerShell source guards.

---

### Task 1: Editor State Rule

**Files:**
- Modify: `Code/core/naming/ActorManualNameRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorManualNameRulesTests.cs.txt`

- [ ] Add failing tests proving display enters editing on name selection, field-to-field focus remains editing, and complete focus loss returns to display.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --actor-manual-name-rules` and verify the missing state rule fails compilation.
- [ ] Add `ActorManualNameEditorState`, `ActorManualNameEditorEvent`, and `ActorManualNameEditorRules.Resolve` with only the two required states.
- [ ] Re-run the focused rules command and verify it prints `Actor manual name rules passed.`

### Task 2: Original-Position Expansion

**Files:**
- Modify: `Code/patch/naming/AW_ActorManualNamePatch.cs`
- Create: `Tests/ActorManualNameEditorSourceGuard.ps1`

- [ ] Add a failing source guard requiring an idle full-name presentation, pointer-triggered split editing, deferred focus checks, and one commit boundary.
- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/ActorManualNameEditorSourceGuard.ps1` and verify it fails on the current always-expanded patch.
- [ ] Refactor the patch so `ShowDisplay` restores `OriginalSize` and `OriginalPosition`, hides the second field and labels, and displays `actor.getName()`.
- [ ] Add a UGUI pointer handler to call `EnterEditing`, populate the two structured fields, and preserve editing while either field remains focused.
- [ ] On complete focus loss or window disable, call `TryCommit` once; collapse on success and retain editing/focus on validation failure.
- [ ] Re-run the source guard and focused rules test.

### Task 3: Verification And Source Deployment

**Files:**
- Deploy: `Code/core/naming/ActorManualNameRules.cs`
- Deploy: `Code/patch/naming/AW_ActorManualNamePatch.cs`

- [ ] Run `dotnet run --project Tests/LocalizedNamePersistence.Isolated.Tests/LocalizedNamePersistence.Isolated.Tests.csproj -- --all` and verify `PASS --all`.
- [ ] Run `dotnet build AncientWarfare3.csproj --no-restore` and verify zero errors.
- [ ] Copy only the two changed production `.cs` files to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`.
- [ ] Verify `Assemblies` still exists and SHA-256 hashes match for both deployed files.
