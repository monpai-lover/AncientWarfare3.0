# Actor Manual Name Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the vanilla actor name field with a culture-aware two-field editor whose player-authored identity survives restore and lineage projection.

**Architecture:** `ActorManualNameRules` is pure field-order and composition logic. `ActorManualRenameService` is the only live-world write boundary. A UnitWindow patch only presents the two inputs and submits structured requests. Existing name migration and lineage projection receive explicit authored-name guards.

**Tech Stack:** C#, Harmony, UnityEngine.UI, WorldBox actor data, AW3 SQLite lineage archive, existing rules-test console projects.

---

### Task 1: Add pure manual-name rules

**Files:**
- Create: `Code/core/naming/ActorManualNameRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorManualNameRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing tests**

```csharp
ActorManualNameDraft xia = ActorManualNameRules.CreateDraft(
    ActorManualNameMode.Xia, "Ji", "Fa");
Equal("JiFa", xia.DisplayName, "Xia family precedes given name");

ActorManualNameDraft nonXia = ActorManualNameRules.CreateDraft(
    ActorManualNameMode.NonXia, "Louis", "de Lyon");
Equal("Louis de Lyon", nonXia.DisplayName,
    "non-Xia given name precedes family name");

Equal(false, ActorManualNameRules.CreateDraft(
    ActorManualNameMode.Xia, "Ji", " ").IsValid,
    "given name is required");
```

- [ ] **Step 2: Verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --actor-manual-name-rules`

Expected: compile failure because the rules types do not exist.

- [ ] **Step 3: Implement minimum rules**

Create `ActorManualNameMode` and immutable `ActorManualNameDraft`. `CreateDraft(mode, first, second)` trims whitespace, maps Xia as family/shi then given, maps non-Xia as given then family, rejects empty given names, and joins non-Xia components with exactly one separator.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/naming/ActorManualNameRules.cs Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/ActorManualNameRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add manual actor name rules"
```

### Task 2: Protect manual names during projection and restore

**Files:**
- Modify: `Code/core/naming/AWLocalizedNameService.cs:290-300`
- Modify: `Code/core/naming/AWLocalizedNameMigrationService.cs:153-207`
- Modify: `Code/patch/naming/AW_ActorLocalizedNamePatch.cs:24-46`
- Modify: `Code/core/lineage/LineageService.cs:2149-2270`
- Modify: `Tests/LocalizedNamePersistence.Isolated.Tests/IntegrationSourceTests.cs`

- [ ] **Step 1: Write failing source-contract tests**

```csharp
AssertEx.True(compact.Contains("if(pData.custom_name)"),
    "projection must preserve a player-authored identity");
AssertEx.True(migrationCompact.Contains("ActorManualName"),
    "restore must reconcile a manual actor name before stale projection");
AssertEx.True(lineageCompact.Contains("HasProtectedAuthoredName(pActor)"),
    "lineage recomposition must respect player-authored names");
```

- [ ] **Step 2: Verify RED**

Run: `dotnet run --project Tests/LocalizedNamePersistence.Isolated.Tests`

Expected: FAIL on a new authored-name assertion.

- [ ] **Step 3: Implement protection**

When a custom actor has a non-empty `data.name`, preserve it in `getName` and `ProjectStored`. During migration, replace stale identity slots with the saved manual display before database merge, persist both localized slots, and enqueue the repaired Unit identity. After the historical-figure branch, `LineageService.ApplyDisplayName` exits for `HasProtectedAuthoredName(pActor)`.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/naming/AWLocalizedNameService.cs Code/core/naming/AWLocalizedNameMigrationService.cs Code/patch/naming/AW_ActorLocalizedNamePatch.cs Code/core/lineage/LineageService.cs Tests/LocalizedNamePersistence.Isolated.Tests/IntegrationSourceTests.cs
git commit -m "fix: preserve manual actor names across restore"
```

### Task 3: Commit structured names and sync patrilineal descendants

**Files:**
- Create: `Code/core/naming/ActorManualRenameService.cs`
- Create: `Code/core/naming/ActorManualRenameRules.cs`
- Modify: `Code/core/lineage/VisibleSurnameRenameRules.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorManualRenameRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing branch-plan tests**

```csharp
ActorManualBranchPlan plan = ActorManualRenameRules.PlanBranchChange(
    10L, "Ji", "Jiang", new long[] { 10L, 11L, 12L });
Sequence(new long[] { 10L, 11L, 12L }, plan.ActorIds,
    "surname change targets root and patrilineal descendants");
Equal(true, plan.RequiresBranchFork, "changed branch identity forks");
Equal(false, ActorManualRenameRules.PlanBranchChange(
    10L, "Jiang", "Jiang", new long[] { 10L, 11L }).RequiresBranchFork,
    "resubmitting the surname is idempotent");
```

- [ ] **Step 2: Verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --actor-manual-rename-rules`

Expected: compile failure because `ActorManualRenameRules` and `ActorManualBranchPlan` do not exist.

- [ ] **Step 3: Implement the service**

Reuse the bounded patrilineal traversal in `VisibleSurnameRenameRules`; never scan all actors. For a changed lineage surname/shi, fork a child branch rooted at the edited actor, assign it to root plus selected descendants, and preserve every descendant's own given name. For an untraced actor, update only those actors' family fields.

For every changed actor write `custom_name`, `data.name`, `display_name`, native and Chinese localized slots, given/family components, archive state, and a Unit persistence enqueue. Refresh vanilla ruler/founder references after all actor writes succeed.

- [ ] **Step 4: Verify GREEN**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --actor-manual-rename-rules
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: both PASS.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/naming/ActorManualRenameService.cs Code/core/naming/ActorManualRenameRules.cs Code/core/naming/ActorManualNameRules.cs Code/core/lineage/VisibleSurnameRenameRules.cs Code/core/lineage/LineageService.cs Tests/AncientWarfare3.Rules.Tests/ActorManualRenameRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: commit manual actor names by branch"
```

### Task 4: Replace the UnitWindow name input

**Files:**
- Create: `Code/patch/naming/AW_ActorManualNamePatch.cs`
- Modify: `Locales/Chinese.csv`
- Modify: `Locales/English.csv`
- Modify: `Tests/LocalizedNamePersistence.Isolated.Tests/IntegrationSourceTests.cs`

- [ ] **Step 1: Write failing UI source-contract tests**

```csharp
AssertEx.True(source.Contains("AW_ActorManualNameSecondInput"),
    "patch creates one second input");
AssertEx.True(source.Contains("ActorManualRenameService"),
    "patch submits through the manual rename service");
AssertEx.True(source.Contains("ActorManualNameMode.Xia"),
    "patch supports Xia ordering");
AssertEx.True(source.Contains("ActorManualNameMode.NonXia"),
    "patch supports non-Xia ordering");
```

- [ ] **Step 2: Verify RED**

Run: `dotnet run --project Tests/LocalizedNamePersistence.Isolated.Tests`

Expected: FAIL because the patch source does not exist.

- [ ] **Step 3: Implement the UI patch**

Patch `UnitWindow.loadNameInput` after vanilla initialization. Reuse `name_input` as the first field and clone it once as `AW_ActorManualNameSecondInput`; split the original row width equally with a fixed gap. Clear vanilla listeners, repopulate both inputs from structured fields, and bind both end-edit events to one service commit.

Xia order is `surname/shi`, `given`; non-Xia is `given`, `surname`. Add `aw_actor_name_given` and `aw_actor_name_family_or_shi` to English and Chinese locale CSVs. Cache the selected actor id and submitted draft so window refresh cannot create a branch or duplicate listeners.

- [ ] **Step 4: Verify GREEN**

```powershell
dotnet run --project Tests/LocalizedNamePersistence.Isolated.Tests
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: both PASS.

- [ ] **Step 5: Commit**

```powershell
git add Code/patch/naming/AW_ActorManualNamePatch.cs Locales/Chinese.csv Locales/English.csv Tests/LocalizedNamePersistence.Isolated.Tests/IntegrationSourceTests.cs
git commit -m "feat: add split actor name editor"
```

### Task 5: Source-only deployment and final verification

**Files:**
- Modify: matching changed files under `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run final checks**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --actor-manual-name-rules
dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --actor-manual-rename-rules
dotnet run --project Tests/LocalizedNamePersistence.Isolated.Tests
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: all PASS.

- [ ] **Step 2: Deploy source only**

Copy only changed C# and locale files into the game mod source folder. Preserve `Assemblies`; do not copy a DLL.

- [ ] **Step 3: Verify deployment hashes**

Run `Get-FileHash` for each changed workspace/deployment pair. Expected: every SHA-256 pair matches.

- [ ] **Step 4: In-game acceptance pass**

For a Xia lineage and a non-Xia lineage: edit only the given name, edit surname/shi, inspect a child and sibling, save, reload, change language, and trigger promotion. Expected: the root and patrilineal descendants retain the submitted identity; sibling and ancestor retain the original branch; no duplicate inputs appear.
