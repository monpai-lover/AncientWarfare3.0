# Lineage Rename and Feudatory Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make clan renaming target the actual displayed branch, add surname renaming for the centered person and patrilineal descendants, and enforce that active feudatory princes cannot be slaves.

**Architecture:** Keep graph selection and eligibility as pure tested rules, while SQLite/live Actor mutation remains in focused lineage services. Both rename operations travel through the existing authoritative multiplayer command router. Feudatory identity cleanup is centralized in `SlaveService`, invoked after the active feudatory cache is published and guarded again at the single enslavement eligibility boundary.

**Tech Stack:** C# 10 rules test harness, .NET Framework 4.8 WorldBox mod sources, SQLite, Unity UI, AW3 authoritative command API.

---

### Task 1: Patrilineal surname selection rules

**Files:**
- Create: `Code/core/lineage/VisibleSurnameRenameRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/VisibleSurnameRenameRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing pure-rule test**

Define `SurnameRelationNode(long actorId, bool male, long fatherId)` fixtures and assert that `CollectPatrilinealRenameIds(1, nodes)` returns the root, sons, daughters, sons' children and no daughter's children, siblings or ancestors. Include duplicate IDs and a corrupt father cycle.

```csharp
Sequence(new long[] { 1, 2, 3, 4, 5 },
    VisibleSurnameRenameRules.CollectPatrilinealRenameIds(1, new[]
    {
        new SurnameRelationNode(1, true, 9),
        new SurnameRelationNode(2, true, 1),
        new SurnameRelationNode(3, false, 1),
        new SurnameRelationNode(4, true, 2),
        new SurnameRelationNode(5, false, 2),
        new SurnameRelationNode(6, true, 3),
        new SurnameRelationNode(9, true, -1)
    }), "rename follows only male-line parents");
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --visible-surname-rename
```

Expected: compile failure because `VisibleSurnameRenameRules` does not exist.

- [ ] **Step 3: Implement the minimal graph rule**

Create a normalized-name helper and a bounded breadth-first traversal. Index nodes by `FatherId`, add the root first, enqueue children only when the current node is male, and use a `HashSet<long>` to terminate corrupt cycles.

```csharp
public static IReadOnlyList<long> CollectPatrilinealRenameIds(
    long rootActorId, IEnumerable<SurnameRelationNode> nodes)
```

- [ ] **Step 4: Run focused and full rule tests**

Expected: `Visible surname rename rules passed.` followed by `Rule tests passed.`

- [ ] **Step 5: Commit the rules slice**

```powershell
git add Code/core/lineage/VisibleSurnameRenameRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: define patrilineal surname rename scope"
```

### Task 2: Correct clan branch targeting and include the founder

**Files:**
- Modify: `Code/core/lineage/VisibleClanRenameRules.cs`
- Modify: `Code/core/lineage/VisibleClanRenameService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/VisibleClanRenameRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing tests for current branch identity and founder inclusion**

Assert that the current snapshot Shi ID wins over navigation fallback and that archive/live/founder IDs are deduplicated with the founder retained.

```csharp
Equal(42L, VisibleClanRenameRules.ResolveTargetShiId(42L, 1L));
Sequence(new long[] { 7, 8, 9 }, VisibleClanRenameRules.MergeMemberIds(
    founderActorId: 7, archiveIds: new long[] { 8, 7 },
    liveIds: new long[] { 9, 8 }));
```

- [ ] **Step 2: Run the focused test and verify RED**

Expected: missing `ResolveTargetShiId` and `MergeMemberIds` members.

- [ ] **Step 3: Implement rules and service query**

Read `FOUNDER_ACTOR_ID` from `ShiBranch` for the requested `shi_id`, merge it with archive and live members, and then update the branch, every live Actor, and every archive row. Keep `HistoricalContentRevision.AdvanceAfterSuccessfulSynchronousWrite` around successful DB mutations.

- [ ] **Step 4: Verify the focused rules and production compile**

Run the focused rule test and the net48 build command from Task 6. Expected: both exit 0.

- [ ] **Step 5: Commit the clan fix**

```powershell
git add Code/core/lineage/VisibleClanRenameRules.cs Code/core/lineage/VisibleClanRenameService.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "fix: rename the displayed clan branch including founder"
```

### Task 3: Persist surname changes for live and archived descendants

**Files:**
- Create: `Code/core/lineage/VisibleSurnameRenameService.cs`
- Create: `Code/core/lineage/VisibleSurnameRenameSqlRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/VisibleSurnameRenameSqlRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add a failing descendant-query contract test**

Assert the query selects both archive parent columns and `FamilyEdge`, is bounded, and does not mutate outside the selected ID list. The graph-scope behavior itself remains covered by Task 1.

```csharp
Contains("PARENT_ID_1", VisibleSurnameRenameSqlRules.DescendantRelationQuery);
Contains("PARENT_ID_2", VisibleSurnameRenameSqlRules.DescendantRelationQuery);
Contains("FamilyEdge", VisibleSurnameRenameSqlRules.DescendantRelationQuery);
Contains("LIMIT @limit", VisibleSurnameRenameSqlRules.DescendantRelationQuery);
```

- [ ] **Step 2: Run and verify RED**

Expected: missing `VisibleSurnameRenameSqlRules`.

- [ ] **Step 3: Implement bounded selection and mutation**

Load the descendant relation slice through a bounded recursive SQLite CTE over `FamilyEdge` plus the indexed `PARENT_ID_1/PARENT_ID_2` fallback, project it into `SurnameRelationNode`, and pass it through Task 1's pure rule. Merge unarchived live actors, then for each selected ID:

```csharp
live.data.set(LineageKeys.FAMILY_NAME, familyName);
live.data.set(LineageKeys.CHINESE_FAMILY_NAME, familyName);
LineageService.ApplyDisplayName(live);
LineageService.ArchiveActor(live, pAlive: live.isAlive());
```

Update dead/archive-only rows directly and rebuild `DISPLAY_NAME` through `LineageDisplayNameRules.Build`.

- [ ] **Step 4: Verify surname persistence tests**

Expected: target, sons, daughters and sons' descendants changed; daughter's descendants unchanged.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/VisibleSurnameRenameService.cs Code/core/lineage/VisibleSurnameRenameSqlRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: rename a patrilineal surname branch"
```

### Task 4: Add authoritative RenameSurname command and family-tree UI

**Files:**
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalogModels.cs`
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalog.cs`
- Modify: `Code/core/multiplayer/commands/AW3AuthoritativeCommandRouter.cs`
- Modify: `Code/core/multiplayer/commands/AW3RecordsCommandHandler.cs`
- Modify: `Code/ui/windows/FamilyTreeWindow.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AW3MultiplayerCatalogRulesTests.cs.txt`

- [ ] **Step 1: Write failing command catalog tests**

Add `RenameSurname` without renumbering existing serialized enum values, add its request factory example, and require the records handler route. Assert `ActorId` carries the centered family-tree target.

- [ ] **Step 2: Run catalog test and verify RED**

Expected: missing enum/factory/router case.

- [ ] **Step 3: Implement command contract and handler**

Append `RenameSurname = 27`; route it to `AW3RecordsCommandHandler`; validate a live or archived actor target and call `VisibleSurnameRenameService.RenamePatrilinealBranch(request.ActorId, request.Text)`. Remove `RulerOwnsShi` from RenameClan; retain country authorization in the existing facade/router layer.

- [ ] **Step 4: Implement explicit UI targets**

In BigTree mode, resolve rename-clan target from `_bulkSnapshot`/`_readSpec.ShiId`, never `_backShiId`. In Family mode expose a separate `改姓` side button and input panel whose request is:

```csharp
AW3CommandRequest.RenameSurname(
    ResolveRenameCountryIdForActor(_centerActorId), _centerActorId, raw)
```

Disable input while pending and rebuild the current snapshot after acceptance using the existing command-change callback.

- [ ] **Step 5: Run command tests and compile**

Expected: catalog coverage count matches all enum values and production compile exits 0.

- [ ] **Step 6: Commit**

```powershell
git add Code/api/multiplayer Code/core/multiplayer Code/ui/windows/FamilyTreeWindow.cs Tests
git commit -m "feat: expose authoritative surname renaming"
```

### Task 5: Enforce active feudatory non-slave identity

**Files:**
- Create: `Code/core/lineage/FeudatoryIdentityRules.cs`
- Modify: `Code/core/lineage/FeudatoryService.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/FeudatoryIdentityRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing invariant tests**

Assert active princes cannot be enslaved regardless of important-capture override, while former princes remain eligible under the ordinary conditions.

```csharp
False(FeudatoryIdentityRules.CanEnslave(
    ordinaryEligible: true, activePrince: true));
True(FeudatoryIdentityRules.CanEnslave(
    ordinaryEligible: true, activePrince: false));
```

- [ ] **Step 2: Run focused test and verify RED**

Expected: `FeudatoryIdentityRules` missing.

- [ ] **Step 3: Add the unified cleanup path**

Expose an idempotent `SlaveService.ReleaseForFeudatoryAppointment(Actor)` that deactivates slave indexes, clears slave trait/data/task ownership, marks the actor noble, archives it, and refreshes graphics without restoring slavery later.

- [ ] **Step 4: Wire appointment, load repair and enslavement gate**

Call cleanup after `PublishAdded(snapshot)` so `IsActivePrince` is already true. During `LoadActiveCache`, publish the complete cache first, then repair each live prince. Add `if (FeudatoryService.IsActivePrince(pActor)) return false;` at the start of `CanBeEnslaved`, before important-capture exceptions.

- [ ] **Step 5: Run focused/full tests and production compile**

Expected: all tests pass; no warnings/errors.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/lineage/FeudatoryIdentityRules.cs Code/core/lineage/FeudatoryService.cs Code/core/lineage/SlaveService.cs Tests
git commit -m "fix: keep active feudatory princes out of slavery"
```

### Task 6: Final verification and source-only handoff

**Files:**
- Verify all modified files

- [ ] **Step 1: Run the complete rule suite**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: `Rule tests passed.`

- [ ] **Step 2: Run a fresh production compile**

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore `
  -p:FrameworkPathOverride="$env:USERPROFILE\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8"
```

Expected: `0 warnings`, `0 errors`.

- [ ] **Step 3: Check diff hygiene and scope**

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; pre-existing school/Zhulu work remains preserved and is not reverted.

- [ ] **Step 4: Review runtime scenarios before deployment**

Confirm from code paths: non-root clan branch uses its own Shi ID; founder is included; family center is the surname root; active prince cleanup follows cache publication; every enslavement path reaches `CanBeEnslaved`.

- [ ] **Step 5: Do not deploy a DLL**

If deployment is requested later, copy changed source/resources only into the WorldBox Mods folder.
