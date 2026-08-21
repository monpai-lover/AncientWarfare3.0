# Xia Building Resources and Academy Capacity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish the new Xia building textures from the runtime resource path and make the Xia academy provide four housing slots while retaining vanilla library behavior.

**Architecture:** Keep all runtime building sprites under `GameResources/buildings/civ_main/Xia/`, matching `XiaArchitecture.MAIN_PATH`. Extend the existing `SchoolAcademyBuildingContent.Init` clone configuration with the vanilla `housing_slots` field; leave `book_slots` copied from `library_Xia` so the vanilla city book cache continues to work.

**Tech Stack:** C#, Unity/WorldBox asset APIs, NeoModLoader, PowerShell, Git.

---

### Task 1: Record and verify resource cleanup scope

**Files:**
- Verify: `GameResources/buildings/barracks_Xia/`
- Verify: `GameResources/buildings/docks_Xia/`
- Verify: `GameResources/buildings/hall_Xia_0/`
- Verify: `GameResources/buildings/hall_Xia_1/`
- Verify: `GameResources/buildings/hall_Xia_2/`
- Verify: corresponding directories under `GameResources/buildings/civ_main/Xia/`

- [ ] **Step 1: Compare hashes before deletion**

Run a SHA-256 comparison for every same-named file in each legacy and nested directory. Continue only when every candidate slated for deletion has an identical nested counterpart.

- [ ] **Step 2: Remove only verified legacy duplicates**

Delete the matching files from the five legacy root directories. Keep nested files and keep unrelated legacy assets.

- [ ] **Step 3: Verify resource references**

Confirm `XiaArchitecture.cs` and `SchoolAcademyBuildingContent.cs` use `buildings/civ_main/Xia/`; run `rg -n "buildings/.*Xia|MAIN_PATH|MainPath" Code/content` and ensure no runtime code points to the legacy root.

### Task 2: Add academy housing capacity

**Files:**
- Modify: `Code/content/schools/SchoolAcademyBuildingContent.cs:30-33`

- [ ] **Step 1: Add the vanilla housing field assignment**

Immediately after copying `book_slots`, assign:

```csharp
academy.housing_slots = 4;
```

Do not replace or alter the existing `academy.book_slots = source.book_slots;` assignment.

- [ ] **Step 2: Add focused rule coverage**

Add or extend a source-level test fixture under `Tests/AncientWarfare3.Rules.Tests` that asserts the academy content contains both `academy.housing_slots = 4` and `academy.book_slots = source.book_slots`, preventing future removal of either capability.

- [ ] **Step 3: Run focused tests**

Run the repository's existing test command for `AncientWarfare3.Rules.Tests`; expected result is PASS with no localization or RTS test changes.

### Task 3: Build and deploy

**Files:**
- Build output: project-defined compiled mod assembly
- Deploy target: `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`

- [ ] **Step 1: Build the mod**

Use the repository's existing build script/solution command and stop on compiler errors.

- [ ] **Step 2: Deploy source/resources**

Copy the compiled assembly and the retained `GameResources/buildings/civ_main/Xia` resources to the target mod directory without overwriting unrelated user files.

- [ ] **Step 3: Verify deployment**

Check that `academy_Xia` resources and the five updated building directories exist below `civ_main/Xia`, and that no required legacy-root path is referenced.

### Task 4: Commit and push the scoped change

**Files:**
- Modified: `Code/content/schools/SchoolAcademyBuildingContent.cs`
- Added/updated: nested Xia building assets
- Deleted: only verified duplicate legacy asset files
- Added: tests and design/plan documents if tracked by the repository

- [ ] **Step 1: Review the staged diff**

Use `git diff --stat` and `git diff --name-status` to ensure unrelated existing worktree changes are not staged.

- [ ] **Step 2: Commit**

Commit with message `feat: add academy housing capacity and publish Xia building assets`.

- [ ] **Step 3: Push and verify**

Push `master`, then confirm `git status --short --branch` reports the branch synchronized with `origin/master` except for pre-existing unrelated worktree edits.
