# City Army Reinforcement Capacity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cap ordinary RTS armies by their recruitment city's shared 35 percent population budget and immediately fill only approved shortages.

**Architecture:** A pure allocation rule calculates city capacity and deterministically divides it among anchored ordinary armies. A runtime service projects those approved targets into the RTS controller and levy system; existing soldiers are never deleted when population drops.

**Tech Stack:** C# 11, WorldBox/NeoModLoader APIs, AW3 rules test console project.

---

### Task 1: Add Pure City Allocation Rules

**Files:**
- Create: `Code/core/lineage/CityArmyReinforcementRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CityArmyReinforcementRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing allocation tests**

```csharp
Equal(35, CityArmyReinforcementRules.CityCapacity(100, 80));
Equal(20, CityArmyReinforcementRules.CityCapacity(100, 20));
var allocations = CityArmyReinforcementRules.Allocate(50, new[] {
    new CityArmyReinforcementRequest(9, 10, 40, CityArmyPriority.Reserve),
    new CityArmyReinforcementRequest(2, 10, 40, CityArmyPriority.Frontline),
    new CityArmyReinforcementRequest(1, 10, 40, CityArmyPriority.Frontline)
});
Equal(40, allocations[1].ApprovedTarget);
Equal(10, allocations[2].ApprovedTarget);
Equal(10, allocations[9].ApprovedTarget);
Equal(12, CityArmyReinforcementRules.Shortage(28, 40));
```

Add the test file to the project and call `Run()` from `Program.cs.txt`.

- [ ] **Step 2: Run the rules test and confirm the new type is absent**

Run: `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --nologo`

Expected: compilation failure naming `CityArmyReinforcementRules`.

- [ ] **Step 3: Implement the minimal rules API**

```csharp
public enum CityArmyPriority { Recapture = 0, Frontline = 1, War = 2, Reserve = 3 }
public static int CityCapacity(int population, int effectiveWarriorSlots) =>
    Math.Min(Math.Max(0, effectiveWarriorSlots), Math.Max(0, population) * 35 / 100);
public static int Shortage(int living, int approvedTarget) =>
    Math.Max(0, approvedTarget - Math.Max(0, living));
```

Define `CityArmyReinforcementRequest` and result entries with army id,
living roster, desired target, priority, and approved target. `Allocate` sorts
by priority then army id, reserves living strength first, and allocates only
remaining capacity. Its result never lowers a living roster or grants capacity
above the shared city cap.

- [ ] **Step 4: Run the same test and confirm the new assertions pass**

Expected: city-cap, priority, stable-tie, and shortage assertions pass. The
known unrelated school-portrait baseline failure may occur later in the suite;
record it without changing that code.

- [ ] **Step 5: Commit the rules and tests**

```powershell
git add Code/core/lineage/CityArmyReinforcementRules.cs Tests/AncientWarfare3.Rules.Tests/CityArmyReinforcementRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: cap armies by city recruitment capacity"
```

### Task 2: Project Approved Targets Into RTS And Levy Work

**Files:**
- Create: `Code/core/lineage/CityArmyReinforcementService.cs`
- Modify: `Code/core/lineage/StandingArmyService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Code/core/lineage/ArmyMapInformationRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CityArmyReinforcementRulesTests.cs.txt`

- [ ] **Step 1: Write failing target-projection guards**

Add source assertions that `StandingArmyService.TargetStrength` delegates to
`CityArmyReinforcementService`, and map information computes `待补` from an
approved target. Add a rule test showing a falling city population blocks new
reinforcement but returns the current living count as its target.

- [ ] **Step 2: Run the test and confirm the guards fail**

Run: `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --nologo`

Expected: missing runtime-service delegation and new guard failures.

- [ ] **Step 3: Implement one shared live-city projection**

Implement `CityArmyReinforcementService.ApprovedTarget(Army, Kingdom)`. It
must snapshot `World.world.armies`, retain only live ordinary armies anchored
to the same valid city, calculate live population and effective warrior slots,
then call `CityArmyReinforcementRules.Allocate`. Classify missions as
recapture, frontline/defense, war, reserve; special armies retain existing
rules. Invalidate cached projections on authority-cycle completion, reset,
load, army removal, anchor change, and mission refresh.

Replace ordinary `StandingArmyService.TargetStrength` slot-only results with
this service. In `ArmyRtsControllerService.ResolveMissionTargetStrength`, do
not allow persisted mission targets to raise an ordinary target above its
approved value. Feed the same value to `ArmyMapInformationRules`.

- [ ] **Step 4: Bind levy demand to the approved shortage and complete it**

In `TemporaryLevyService`, replace directed raw demand with
`CityArmyReinforcementRules.Shortage(living, approvedTarget)`. Before each
enlistment batch, re-read city population, ordinary roster, and allocation.
Continue authority-cycle enlistment until the approved shortage is zero or no
candidate remains. Clear pending demand, retry state, and `Replenish` once it
is zero; rejected or stale allocation creates no actor. Keep arrival tracking
only for formation placement and use its existing immediate teleport/complete
path for approved members.

- [ ] **Step 5: Run verification and commit**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --nologo
dotnet build AncientWarfare3.csproj -c Release --nologo
git diff --check
git add Code/core/lineage/CityArmyReinforcementService.cs Code/core/lineage/StandingArmyService.cs Code/core/lineage/ArmyRtsControllerService.cs Code/core/lineage/TemporaryLevyService.cs Code/core/lineage/ArmyMapInformationRules.cs Tests
git commit -m "fix: replenish RTS armies within city capacity"
```

Expected: build succeeds and all new tests pass; only the recorded unrelated
school-portrait baseline failure may remain in the full rules run.
