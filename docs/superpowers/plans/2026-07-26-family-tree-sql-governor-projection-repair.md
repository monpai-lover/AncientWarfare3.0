# Family Tree SQL And Governor Projection Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore family-tree reads on the bundled SQLite 3.9.2 and prevent transient city/actor kingdom ordering from producing false governor projection failures.

**Architecture:** Keep the two-command detached lineage read but normalize every persisted parent source into one non-recursive edge CTE, so each recursive CTE has one recursive SELECT. Route city-leader projection through a pure classifier and a bounded, coalesced authority-cycle repair service that resolves only the recorded actor and city IDs.

**Tech Stack:** C# 11, .NET Framework 4.8, System.Data.SQLite 1.0.99 / SQLite 3.9.2, Harmony, PowerShell source guards, .NET 9 rule slices.

---

### Task 1: SQLite 3.9.2 Family Closure

**Files:**
- Modify: `Code/core/lineage/LineageBulkQuery.cs:625`
- Test: `Tests/LineageBulkQuery.Integration.Tests/Program.cs`

- [ ] **Step 1: Run the real-SQLite regression test and verify RED**

Run:

```powershell
dotnet run --project Tests/LineageBulkQuery.Integration.Tests/LineageBulkQuery.Integration.Tests.csproj -c Release
```

Expected: FAIL with `circular reference: relatives` from the bundled SQLite 3.9.2.

- [ ] **Step 2: Replace compound recursive branches with a unified edge source**

In the first command of `LineageBulkQuery.Load`, construct this shape while retaining the existing parameters, limits, result ordering, and command count:

```csharp
"WITH RECURSIVE all_edges(CHILD_ID,PARENT_ID,PARENT_SLOT," +
"CREATED_TIME) AS (" +
"SELECT CHILD_ID,PARENT_ID,PARENT_SLOT,CREATED_TIME " +
"FROM FamilyEdge WHERE CHILD_ID>=0 AND PARENT_ID>=0 " +
"UNION SELECT ID,PARENT_ID_1,1,0 FROM ActorArchive " +
"WHERE ID>=0 AND PARENT_ID_1>=0 " +
"UNION SELECT ID,PARENT_ID_2,2,0 FROM ActorArchive " +
"WHERE ID>=0 AND PARENT_ID_2>=0)," +
"ancestors(ID) AS (" +
"SELECT @root UNION SELECT edge.PARENT_ID FROM all_edges edge " +
"JOIN ancestors child ON edge.CHILD_ID=child.ID " +
"WHERE edge.PARENT_ID>=0 LIMIT @relativeLimit)," +
"relatives(ID) AS (" +
"SELECT ID FROM ancestors WHERE ID>=0 " +
"UNION SELECT edge.CHILD_ID FROM all_edges edge " +
"JOIN relatives parent ON edge.PARENT_ID=parent.ID " +
"WHERE edge.CHILD_ID>=0 LIMIT @relativeLimit) "
```

Select edge rows from `all_edges` joined to `relatives` on both endpoints. Preserve the existing `@edgeLimit` ordering and do not add SQL commands or unbounded scans.

- [ ] **Step 3: Run the integration test and verify GREEN**

Run the command from Step 1.

Expected: PASS, including two-command count, 512-node bound, ancestor priority, archive-only relations, and string budget assertions.

### Task 2: Governor Projection Timing Rules

**Files:**
- Create: `Code/core/court/CityGovernorProjectionTimingRules.cs`
- Create: `Tests/CityGovernorProjectionTimingSlice/CityGovernorProjectionTimingSlice.csproj`
- Create: `Tests/CityGovernorProjectionTimingSlice/Program.cs`

- [ ] **Step 1: Write the failing rule slice**

The test program must assert:

```csharp
Equal(CityGovernorProjectionDecision.ApplyNow,
    CityGovernorProjectionTimingRules.Decide(true, true, true, true,
        true, true, true, false), "stable assignment applies now");
Equal(CityGovernorProjectionDecision.Defer,
    CityGovernorProjectionTimingRules.Decide(true, true, true, true,
        true, true, false, false), "kingdom mismatch defers");
Equal(CityGovernorProjectionDecision.Ignore,
    CityGovernorProjectionTimingRules.Decide(true, true, true, false,
        true, true, false, false), "obsolete leader is ignored");
Equal(CityGovernorProjectionDecision.Ignore,
    CityGovernorProjectionTimingRules.Decide(true, true, true, true,
        true, true, true, true), "royal asylum is not appointed");
True(CityGovernorProjectionTimingRules.ShouldRetry(1));
False(CityGovernorProjectionTimingRules.ShouldRetry(3));
Equal("city_governor_projection:14:2",
    CityGovernorProjectionTimingRules.CoalescingKey(14, 2));
```

Link the production rules file from the test project.

- [ ] **Step 2: Run the slice and verify RED**

Run:

```powershell
dotnet run --project Tests/CityGovernorProjectionTimingSlice/CityGovernorProjectionTimingSlice.csproj -c Release
```

Expected: FAIL because `CityGovernorProjectionTimingRules` is not yet defined.

- [ ] **Step 3: Implement the minimal pure rules**

Define `Ignore`, `ApplyNow`, and `Defer`. Apply immediately only when the new assignment, actor, city, current-leader relation, both kingdom references, same-kingdom relation, and non-asylum status are valid. Defer only a still-current leader whose city kingdom is valid but actor kingdom is absent or different. Limit deferred attempts to three and key work by both IDs.

- [ ] **Step 4: Run the slice and verify GREEN**

Run the command from Step 2.

Expected: `City governor projection timing rules passed.`

### Task 3: Event-Driven Projection Repair

**Files:**
- Create: `Code/core/court/CityGovernorProjectionRepairService.cs`
- Modify: `Code/patch/AW_PromotionPatch.cs:94`
- Create: `Tests/CityGovernorProjectionTimingSourceGuard.ps1`

- [ ] **Step 1: Write the failing source guard**

Require the patch to call `CityGovernorProjectionRepairService.OnLeaderAssigned`, require the service to use `DeferredRuntimeWorkService.EnqueueCoalesced`, resolve exactly one actor and city with `World.world?.units?.get` and `World.world?.cities?.get`, and reject `World.world.units.ToList`, kingdom-unit enumeration, and per-frame Harmony targets.

- [ ] **Step 2: Run the guard and verify RED**

Run:

```powershell
pwsh -NoProfile -File Tests/CityGovernorProjectionTimingSourceGuard.ps1
```

Expected: FAIL because the service and patch routing do not exist.

- [ ] **Step 3: Implement bounded repair**

`OnLeaderAssigned` classifies the live assignment. `ApplyNow` calls `CourtService.TryAssignCityGovernor(actor, city.kingdom, city)`. `Defer` enqueues a runtime item keyed by actor and city. The callback resolves both IDs, reclassifies, applies once when stable, reschedules only while `ShouldRetry(nextAttempt)` is true, and otherwise logs actor, city, actor-kingdom, and city-kingdom IDs. Obsolete assignments return silently.

Replace the direct projection and generic warning in `AW_CityLeaderCareerPatch.SetLeader_Postfix` with the service call. Preserve the existing multiplayer replica gate and previous-leader cleanup.

- [ ] **Step 4: Run the guard and rule slice and verify GREEN**

Run:

```powershell
pwsh -NoProfile -File Tests/CityGovernorProjectionTimingSourceGuard.ps1
dotnet run --project Tests/CityGovernorProjectionTimingSlice/CityGovernorProjectionTimingSlice.csproj -c Release
```

Expected: both pass.

### Task 4: Regression, Build, And Runtime Handoff

**Files:**
- Verify: `Code/core/lineage/LineageBulkQuery.cs`
- Verify: `Code/core/court/CityGovernorProjectionTimingRules.cs`
- Verify: `Code/core/court/CityGovernorProjectionRepairService.cs`
- Verify: `Code/patch/AW_PromotionPatch.cs`

- [ ] **Step 1: Run focused regression tests**

```powershell
dotnet run --project Tests/LineageBulkQuery.Integration.Tests/LineageBulkQuery.Integration.Tests.csproj -c Release
dotnet run --project Tests/CityGovernorProjectionTimingSlice/CityGovernorProjectionTimingSlice.csproj -c Release
pwsh -NoProfile -File Tests/CityGovernorProjectionTimingSourceGuard.ps1
pwsh -NoProfile -File Tests/FamilyTreeWorldResetSourceGuardTests.ps1
pwsh -NoProfile -File Tests/FamilyTreeRitualDisplayRulesTests.ps1
```

Expected: all commands pass.

- [ ] **Step 2: Build both configurations**

```powershell
dotnet build AncientWarfare3.csproj -c Debug
dotnet build AncientWarfare3.csproj -c Release
```

Expected: both builds complete with zero errors.

- [ ] **Step 3: Check the scoped diff**

```powershell
git diff --check -- Code/core/lineage/LineageBulkQuery.cs Code/core/court/CityGovernorProjectionTimingRules.cs Code/core/court/CityGovernorProjectionRepairService.cs Code/patch/AW_PromotionPatch.cs Tests/CityGovernorProjectionTimingSlice Tests/CityGovernorProjectionTimingSourceGuard.ps1
```

Expected: no whitespace errors.

- [ ] **Step 4: Prepare incremental live verification**

Do not copy the entire dirty worktree. Deploy only the four production files after confirming no overlapping user edits appeared. In game, open a normal family tree and appoint or generate a city leader. The new `Player.log` must contain neither `circular reference: relatives` nor a false `City governor career projection failed` warning.
