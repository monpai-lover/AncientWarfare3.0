# AW3 Elite Standing Army, Guard Priority, And Xia Fertility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace random full-capacity peacetime recruitment with a bounded 30-percent elite core, prevent new royal guards from consuming that core, and make Xia long-term offspring capacity exactly twice the human baseline.

**Architecture:** Pure force rules calculate core size, readiness, and deterministic combat score. A city-scoped service owns bounded recruitment, reduction, and replacement; a thread-static recruitment scope distinguishes AW3-controlled ordinary and special enlistment from original random recruitment. Royal-guard maintenance reads national readiness before candidate work, while existing guards remain stable.

**Tech Stack:** C# 10, Harmony, WorldBox actor/city APIs, existing AW3 rule-test console and PowerShell source guards.

---

### Task 1: Add force-model rule tests

**Files:**
- Create: `Code/core/lineage/StandingArmyRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Link `StandingArmyRules.cs` in the test project and add failing assertions**

```csharp
Eq(0, StandingArmyRules.PeacetimeCore(0), "zero establishment has no standing core");
Eq(1, StandingArmyRules.PeacetimeCore(1), "positive establishment keeps one regular");
Eq(3, StandingArmyRules.PeacetimeCore(10), "standing core rounds 30 percent upward");
Eq(4, StandingArmyRules.PeacetimeCore(11), "standing core uses ceiling");
Eq(true, StandingArmyRules.IsKingdomReady(new[] { 1, 3 }, new[] { 1, 9 }),
    "surplus in a filled city is clamped to its own requirement");
Eq(false, StandingArmyRules.IsKingdomReady(new[] { 1, 3 }, new[] { 2, 2 }),
    "surplus cannot hide another city's shortage");
Eq(false, StandingArmyRules.IsKingdomReady(Array.Empty<int>(), Array.Empty<int>()),
    "a realm with no positive core cannot form guards");
Eq(35f, StandingArmyRules.MilitaryScore(10f, 5f, 50f, 2f, 8f),
    "military score follows the approved weights");
```

- [ ] **Step 2: Run the rule tests and verify RED**

Run: `dotnet run --project '.\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj' -c Release`

Expected: compilation fails because `StandingArmyRules` does not exist.

- [ ] **Step 3: Implement the pure rules**

```csharp
public static int PeacetimeCore(int pWarriorSlots) =>
    pWarriorSlots <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(pWarriorSlots * 0.30d));

public static float MilitaryScore(float pDamage, float pWarfare, float pHealth, float pArmor, float pSpeed) =>
    pDamage + pWarfare * 2f + pHealth * 0.1f + pArmor * 2f + pSpeed * 0.25f;
```

`IsKingdomReady` must return false for zero positive requirements and otherwise compare every city independently after clamping filled strength to its requirement.

- [ ] **Step 4: Run the rule tests and verify GREEN**

Expected: all rule tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/StandingArmyRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define elite standing army rules"
```

### Task 2: Introduce scoped enlistment and bounded elite maintenance

**Files:**
- Create: `Code/core/lineage/MilitaryRecruitmentScope.cs`
- Create: `Code/core/lineage/StandingArmyService.cs`
- Create: `Code/patch/AW_StandingArmyPatch.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/patch/AW_RetirementPatch.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing source guards**

Require a Harmony prefix on private `City.tryToMakeWarrior`, require a thread-static recruitment scope, require a 64-candidate scan and two-change limits, and reject calls to `StandingArmyService` from `Actor.updateAge`.

- [ ] **Step 2: Run source guards and verify RED**

Run: `& '.\Tests\SourceGuardTests.ps1'`

Expected: the new standing-army guards fail.

- [ ] **Step 3: Add the recruitment scope**

`MilitaryRecruitmentScope` exposes nested disposable scopes for `StandingCore`, `TemporaryLevy`, `SlaveVanguard`, and `ExistingSpecialArmy`. It stores the active kind in `[ThreadStatic]` fields, supports nesting by restoring the previous value on dispose, and provides `AllowsOriginalRecruitment` only for an explicit AW3 scope.

- [ ] **Step 4: Block original random peacetime recruitment**

Patch `City.tryToMakeWarrior(Actor)` with a prefix. Allow it unchanged during an explicit AW3 recruitment scope or an active defensive mobilization; otherwise return false once the original city army reaches `StandingArmyRules.PeacetimeCore(city.status.warrior_slots)`. Do not patch `Actor.setProfession` globally.

- [ ] **Step 5: Implement bounded city maintenance**

`StandingArmyService.MaintainCity(City)` must:

- count only living members of `city.getArmy()` that are neither temporary levies nor AW3 special-role soldiers;
- scan at most 64 residents from `aw_standing_army_scan_cursor`;
- reuse `city.checkCanMakeWarrior(actor)` plus existing guard, slave, asylum, court, heir, leader, historical-master, and retired-veteran exclusions;
- rank by `MilitaryScore`, then lower actor ID;
- appoint or reduce at most two actors per pass;
- perform at most one stronger-candidate replacement per pass;
- demote surplus to civilians without retirement/veteran history.

Call it from the existing staggered `CityBehCheckArmy` maintenance before guard maintenance. Preserve the existing five-year city stagger.

- [ ] **Step 6: Run source guards and builds**

Run source guards, rule tests, and Debug build. Expected: all pass with zero warnings and errors.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/lineage Code/patch Tests
git commit -m "feat: maintain bounded elite standing armies"
```

### Task 3: Gate royal-guard formation and reinforcement

**Files:**
- Create: `Code/core/lineage/KingdomMilitaryReadinessService.cs`
- Modify: `Code/core/lineage/RoyalGuardMaintenanceRules.cs`
- Modify: `Code/core/lineage/RoyalGuardService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing tests for the guard gate**

Assert that no existing guard plus an unready core blocks candidate collection, an existing guard plus an unready core permits identity repair but blocks reinforcement, and any notice/war blocks formation and reinforcement.

- [ ] **Step 2: Run tests and verify RED**

- [ ] **Step 3: Implement national readiness without actor scans**

Iterate living cities, derive each core, and count only direct living members of that city's original army. Do not count levies, guards, slave vanguards, border armies, or fief armies. Return false when no city has a positive core.

- [ ] **Step 4: Put the gate before candidate collection**

In `RoyalGuardService.EnsureKingdomGuard`, preserve republic/extinction/dismissal/hard-limit precedence. Repair an existing guard army regardless of later core losses, but skip candidate gathering, creation, and reinforcement unless the realm is at peace, has no mobilization, and every city core is ready.

- [ ] **Step 5: Run tests and commit**

```powershell
git add Code/core/lineage Tests
git commit -m "fix: prioritize regular armies before royal guards"
```

### Task 4: Correct Xia long-term fertility

**Files:**
- Modify: `Code/content/XiaRace.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add a failing test/source guard for the actual inherited totals**

The test documents human `offspring=5`, Xia delta `5`, final Xia `offspring=10`, inherited human `birth_rate=3`, Xia delta `4`, and final Xia `birth_rate=7`.

- [ ] **Step 2: Run tests and verify RED**

- [ ] **Step 3: Change only the Xia offspring delta**

Change `("offspring", 1f)` to `("offspring", 5f)`. Keep `("birth_rate", 4f)` unchanged and rewrite the adjacent comments so they describe inherited totals rather than treating the Xia delta as the whole stat.

- [ ] **Step 4: Run the full verification matrix and commit**

```powershell
& '.\Tests\SourceGuardTests.ps1'
dotnet run --project '.\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj' -c Release
dotnet build '.\AncientWarfare3.csproj' -c Debug --no-restore
dotnet build '.\AncientWarfare3.csproj' -c Release --no-restore
git add Code/content/XiaRace.cs Tests
git commit -m "balance: double Xia offspring capacity"
```
