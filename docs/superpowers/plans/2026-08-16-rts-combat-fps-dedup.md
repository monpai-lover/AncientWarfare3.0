# RTS Combat FPS Deduplication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove provably duplicated RTS combat work without changing combat results, task timing, target ordering, pathfinding, logging, or military scheduling.

**Architecture:** Keep every optimization local to one actor or controller invocation. Source-guard regressions establish the intended call shape before production edits; already-proven facts are then reused only inside the current call.

**Tech Stack:** C# 7.3/.NET Framework 4.8 mod code, .NET 9 rules harness, PowerShell, Git.

---

## File Map

- Modify `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt`: add failing structural regressions.
- Modify `Code/ai/behaviours/actor/BehArmyRtsCaptainCombat.cs`: remove immediate revalidation of the same member target.
- Modify `Code/core/lineage/ArmyRtsControllerService.cs`: hoist member invariants and short-circuit a redundant roster scan.

### Task 1: Add Failing Equivalence Guards

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt:424-1194`

- [ ] **Step 1: Add an exact occurrence helper beside `Assert`**

```csharp
private static int CountOccurrences(string source, string value)
{
    int count = 0;
    int index = 0;
    while ((index = source.IndexOf(value, index,
               StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }
    return count;
}
```

- [ ] **Step 2: Guard the member target path**

Load `BehArmyRtsCaptainCombat.cs`, isolate `BehArmyRtsMemberCombat`, and add:

```csharp
Assert(CountOccurrences(memberCombatBody,
           "IsValidMemberCombatTarget(") == 1 &&
       memberCombatBody.Contains("FindMemberCombatTarget(",
           StringComparison.Ordinal) &&
       !memberCombatBody.Contains("attack_target",
           StringComparison.Ordinal),
    "member combat validates each selected target once without changing target sources");
```

- [ ] **Step 3: Guard candidate-invariant hoisting**

Isolate `FindMemberCombatTarget` from `ArmyRtsControllerService.cs` and add:

```csharp
Assert(memberSearchBody.Contains(
           "IsValidOwnedMemberCombatTarget(", StringComparison.Ordinal) &&
       !memberSearchBody.Contains(
           "IsValidMemberCombatTarget(pActor, candidate)",
           StringComparison.Ordinal),
    "member target search hoists actor ownership outside candidate validation");
```

- [ ] **Step 4: Guard the captain-P0 roster scan**

Isolate `TryPrepareMilitaryP0Actor` up to the following
`private static bool IsMissionTargetEnemy` method, find
`if (combatTarget == null)` and `CountFieldCombatEngagement(army`, and assert
the guard occurs first:

```csharp
Assert(noCaptainTargetGuard >= 0 && engagementScan > noCaptainTargetGuard,
    "captain P0 scans the roster only when a missing captain target can permit combat exit");
```

- [ ] **Step 5: Run the rules harness and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: FAIL on the new member validation guard. This proves the test sees
the current duplicate call shape.

- [ ] **Step 6: Commit the failing tests**

```powershell
git add Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt
git commit -m "test: guard RTS combat deduplication"
```

### Task 2: Remove Per-Actor Duplicate Validation

**Files:**
- Modify: `Code/ai/behaviours/actor/BehArmyRtsCaptainCombat.cs:107-134`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs:1852-1932`

- [ ] **Step 1: Validate each behavior target once**

Use this retained-target-first flow:

```csharp
Actor target = pActor.beh_actor_target?.a;
if (!ArmyRtsControllerService.IsValidMemberCombatTarget(pActor, target))
{
    target = ArmyRtsControllerService.FindMemberCombatTarget(pActor);
    if (target == null)
    {
        pActor.beh_actor_target = null;
        pActor.makeWait(0.15f);
        return BehResult.RepeatStep;
    }
}
pActor.beh_actor_target = target;
return BehResult.Continue;
```

`FindMemberCombatTarget` already returns only a validated actor or `null`, so
the retained-target-first and nearest-fallback results remain unchanged.

- [ ] **Step 2: Split actor context from owned-member candidate checks**

Add a private `HasValidMemberCombatActorContext(Actor)` that checks actor life,
tile presence, and `HasMemberCombatMission`. Add a private
`IsValidOwnedMemberCombatTarget(Actor, Actor)` that checks only target life,
target tile, same island, hostility, and calls `ShouldRetainMemberTarget` with
`combatOwned: true`. Make the public `IsValidMemberCombatTarget` call these two
helpers in order.

- [ ] **Step 3: Reuse actor context across the fallback loop**

Make `FindMemberCombatTarget` call
`HasValidMemberCombatActorContext(pActor)` once, then validate each candidate
with `IsValidOwnedMemberCombatTarget(pActor, candidate)`. Preserve this code
unchanged:

```csharp
Finder.getUnitsFromChunk(pActor.current_tile, 2, 10)
```

Also preserve squared-distance comparison and first-nearest tie behavior.

- [ ] **Step 4: Run the rules harness**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: member guards pass; captain-P0 roster-scan guard still fails.

- [ ] **Step 5: Commit**

```powershell
git add Code/ai/behaviours/actor/BehArmyRtsCaptainCombat.cs Code/core/lineage/ArmyRtsControllerService.cs
git commit -m "perf: deduplicate RTS member target validation"
```

### Task 3: Short-Circuit Captain-P0 Roster Counting

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs:2623-2648`

- [ ] **Step 1: Guard the existing count and abort block**

Keep captain target resolution unchanged. Wrap
`CountFieldCombatEngagement` and `ShouldAbortFieldCombatFromP0` in
`if (combatTarget == null)`, passing `pCaptainHasCombatTarget: false` to the
predicate. A non-null valid target already made the predicate false, so this
does not alter control flow.

- [ ] **Step 2: Run the rules harness and verify GREEN**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected final line: `Rule tests passed.`

- [ ] **Step 3: Commit**

```powershell
git add Code/core/lineage/ArmyRtsControllerService.cs
git commit -m "perf: skip redundant RTS captain engagement scans"
```

### Task 4: Full Verification

**Files:**
- Verify only.

- [ ] **Step 1: Run continuity simulation**

```powershell
dotnet run --project Tests/ArmyRtsAdversarialSimulation/ArmyRtsAdversarialSimulation.csproj -c Release -- --scenario continuity --seed 17
```

Expected: `PASS continuity seed=17 scenarios=10 large_armies=80 route_workers=0`.

- [ ] **Step 2: Run foundation simulation**

```powershell
dotnet run --project Tests/ArmyRtsAdversarialSimulation/ArmyRtsAdversarialSimulation.csproj -c Release
```

Expected: `PASS foundation seed=17 trace=64`.

- [ ] **Step 3: Build the release DLL**

```powershell
dotnet build AncientWarfare3.csproj -c Release
```

Expected: `0 Warning(s)`, `0 Error(s)`, and
`bin/Release/net48/AncientWarfare3.dll`.

- [ ] **Step 4: Check scope and invariants**

```powershell
git diff HEAD~3 --check
git diff HEAD~3 -- Code/ai/behaviours/actor/BehArmyRtsCaptainCombat.cs Code/core/lineage/ArmyRtsControllerService.cs Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt
```

Confirm no changes to search radius, target ordering, engagement thresholds,
task registration, path calls, diagnostics, or P0 batch sizes.
