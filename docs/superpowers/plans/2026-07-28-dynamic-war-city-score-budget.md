# Dynamic War City Score Budget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fixed 45-point occupied-city pool with a stable 60/75/85/100 budget selected by war type.

**Architecture:** `WarScoreRules` owns the pure mapping, normalization, per-city value, and clamping rules. `WarScoreRuntimeBridge` resolves the live war type and registers its immutable budget with `WarScoreService`; the service keeps only a bounded in-memory budget per active war and applies it consistently to capture, recapture, goal updates, and restore-time revaluation. No schema or negotiation-price changes are introduced.

**Tech Stack:** C# 10, .NET Framework 4.8 game mod, .NET 9 isolated rules tests, PowerShell source guards, SQLite-backed existing war-score persistence.

---

### Task 1: Pure War-Type Budget Rules

**Files:**
- Modify: `Code/core/lineage/WarScoreRules.cs:43-93`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarScoreBudgetRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt:118-145,6640-6648`

- [ ] **Step 1: Write the failing mapping and proportional-score tests**

Add `WarScoreBudgetRulesTests.Run()` assertions for:

```csharp
Equal(60, WarScoreRules.CityScoreBudgetForWarType("tributary_war"));
Equal(60, WarScoreRules.CityScoreBudgetForWarType("vassal_war"));
Equal(75, WarScoreRules.CityScoreBudgetForWarType("reclaim"));
Equal(75, WarScoreRules.CityScoreBudgetForWarType("restoration_war"));
Equal(85, WarScoreRules.CityScoreBudgetForWarType("aw_normal_war"));
Equal(85, WarScoreRules.CityScoreBudgetForWarType("unknown_mod_war"));
Equal(100, WarScoreRules.CityScoreBudgetForWarType("tianming"));
Equal(100, WarScoreRules.CityScoreBudgetForWarType("tianmingrebel"));
Equal(60, WarScoreRules.NormalizeCityScoreBudget(1));
Equal(100, WarScoreRules.NormalizeCityScoreBudget(999));

var threeOfFive = new WarScoreCityFacts(1, 0f, 0, 0, 0,
    false, false, false, 5);
Equal(20, WarScoreRules.CityControlValue(threeOfFive, 100));
Equal(12, WarScoreRules.CityControlValue(threeOfFive, 60));
Equal(60, WarScoreRules.ClampCityScore(90, 60));
Equal(100, WarScoreRules.ClampCityScore(120, 100));
```

Link `WarScoreRules.cs` and `WarParticipantCityBaselineRules.cs` into the rules project, add the new test file, add `--war-score-budget-slice`, and call the test from the full suite.

- [ ] **Step 2: Run the slice and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --war-score-budget-slice
```

Expected: compile failure because `CityScoreBudgetForWarType`, `NormalizeCityScoreBudget`, and the budget overloads do not exist.

- [ ] **Step 3: Implement the pure rules**

Add constants and mapping:

```csharp
public const int MinimumCityScoreBudget = 60;
public const int LimitedTerritorialCityScoreBudget = 75;
public const int DefaultCityScoreBudget = 85;
public const int MaximumCityScoreBudget = 100;

public static int CityScoreBudgetForWarType(string pWarType)
{
    return pWarType switch
    {
        "tributary_war" or "vassal_war" => 60,
        "reclaim" or "restoration_war" => 75,
        "tianming" or "tianmingrebel" => 100,
        _ => 85
    };
}

public static int NormalizeCityScoreBudget(int pBudget)
{
    return Math.Max(MinimumCityScoreBudget,
        Math.Min(MaximumCityScoreBudget, pBudget));
}
```

Change `CityControlValue`, `CityControlContribution`, and `ClampCityScore` to accept a budget and normalize it before computing the proportional share or clamp. Keep compatibility overloads that use `DefaultCityScoreBudget` only for callers not yet migrated in this task.

- [ ] **Step 4: Run the slice and full rules suite**

Run the slice command above, then:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: both exit 0 and print their pass messages.

- [ ] **Step 5: Commit the pure rule boundary**

```powershell
git add Code/core/lineage/WarScoreRules.cs Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/WarScoreBudgetRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: map war types to city score budgets"
```

### Task 2: Apply One Budget Throughout A War

**Files:**
- Modify: `Code/core/lineage/WarScoreService.cs:84-158,162-270,443-454,575-628`
- Modify: `Code/core/lineage/WarScoreRuntimeBridge.cs:36-49,204-235,440-470,785-813`
- Create: `Tests/WarScoreBudgetSourceGuard.ps1`

- [ ] **Step 1: Write the failing source guard**

The guard must require these production relationships:

```powershell
$rules = Get-Content -Raw Code/core/lineage/WarScoreRules.cs
$service = Get-Content -Raw Code/core/lineage/WarScoreService.cs
$bridge = Get-Content -Raw Code/core/lineage/WarScoreRuntimeBridge.cs

foreach ($needle in @(
    '_cityScoreBudgets',
    'WarScoreRules.CityControlValue(pFacts, cityScoreBudget)',
    'WarScoreRules.ClampCityScore(city, CityScoreBudgetForWar(')) {
    if (-not $service.Contains($needle)) { throw "missing: $needle" }
}
foreach ($needle in @(
    'WarScoreRules.CityScoreBudgetForWarType(',
    'war.asset?.id',
    'runtime.StartWar(war.data.id')) {
    if (-not $bridge.Contains($needle)) { throw "missing: $needle" }
}
```

Also reject remaining production calls to parameterless `ClampCityScore(city)` and calls that compute city contributions without the registered budget.

- [ ] **Step 2: Run the guard and verify RED**

Run:

```powershell
& Tests/WarScoreBudgetSourceGuard.ps1
```

Expected: exit 1 with the first missing budget-flow relationship.

- [ ] **Step 3: Add bounded per-war budget state to the service**

Add:

```csharp
private readonly Dictionary<long, int> _cityScoreBudgets = new();

private int CityScoreBudgetForWar(long pWarId)
{
    return _cityScoreBudgets.TryGetValue(pWarId, out int budget)
        ? WarScoreRules.NormalizeCityScoreBudget(budget)
        : WarScoreRules.DefaultCityScoreBudget;
}
```

Extend `StartWar` with `int pCityScoreBudget`, normalize and register it even when the active snapshot already exists, and initialize loaded snapshots to the default until the runtime bridge supplies the live war type. Remove the dictionary entry in `EndWar` and every active-war cleanup path.

In `RecordCityControlChanged`, read `cityScoreBudget` once and pass it to `CityControlValue` and `CityControlContribution`. In `CalculateRawTotals` and `CalculateRawTotalsWithout`, clamp with `CityScoreBudgetForWar(pSnapshot.WarId)` so goal updates and recaptures cannot fall back to the old fixed pool.

- [ ] **Step 4: Resolve and register the budget from the runtime bridge**

In `WarScoreRuntimeBridge.StartWar` resolve:

```csharp
string warType = war.asset?.id ?? "";
int cityScoreBudget = WarScoreRules.CityScoreBudgetForWarType(warType);
return runtime.StartWar(war.data.id, attacker.id,
    defender?.id ?? -1L, CurrentWorldTime(), cityScoreBudget);
```

Keep capture and restore-time `RevalueCityControl` going through `StartWar(pWar)` before writing controls. This guarantees old saves register the current war's budget before contributions are revalued. Recapture uses the service's already registered budget for the same active war.

- [ ] **Step 5: Run source guard, rules, and war-peace integration**

Run:

```powershell
& Tests/WarScoreBudgetSourceGuard.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --war-score-budget-slice
& Tests/WarPeaceIntegrationTests.ps1
```

Expected: all commands exit 0.

- [ ] **Step 6: Commit the runtime flow**

```powershell
git add Code/core/lineage/WarScoreService.cs Code/core/lineage/WarScoreRuntimeBridge.cs Tests/WarScoreBudgetSourceGuard.ps1
git commit -m "fix: scale occupied city score by war type"
```

### Task 3: Build, Deploy, And Autosave Verification

**Files:**
- Deploy: `Code/core/lineage/WarScoreRules.cs`
- Deploy: `Code/core/lineage/WarScoreService.cs`
- Deploy: `Code/core/lineage/WarScoreRuntimeBridge.cs`
- Deploy: `Tests/WarScoreBudgetSourceGuard.ps1` is not copied to the game installation.

- [ ] **Step 1: Run focused and broad verification**

```powershell
& Tests/WarScoreBudgetSourceGuard.ps1
& Tests/WarPeaceIntegrationTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
git diff --check -- Code/core/lineage/WarScoreRules.cs Code/core/lineage/WarScoreService.cs Code/core/lineage/WarScoreRuntimeBridge.cs Tests/WarScoreBudgetSourceGuard.ps1
```

Expected: all exit 0. If the shared source build still fails only in `ArmyRtsControllerService.cs`, record that separately and do not edit the RTS file.

- [ ] **Step 2: Deploy only the three production files**

Stop the running WorldBox process, copy the three production files into `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0` at the same relative paths, and verify source hashes match. Do not copy tests, `bin`, `obj`, or unrelated dirty files.

- [ ] **Step 3: Build the installation copy**

```powershell
dotnet build D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0\AncientWarfare3.csproj -c Debug --no-restore
dotnet build D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0\AncientWarfare3.csproj -c Release --no-restore
```

Expected: both builds report 0 warnings and 0 errors.

- [ ] **Step 4: Verify the autosave**

Use only the `autosaves/1785226969` save. For an active five-city opponent, verify the persisted `WarScoreControl` contributions and `WarScoreSnapshot.CITY_SCORE` match the war-type budget: three ordinary-war cities should contribute roughly three-fifths of 85 before modifiers, not remain constrained by the former 45-point pool. Verify recapturing one city removes exactly its persisted contribution and that full hostile occupation still reaches decisive score 100.

- [ ] **Step 5: Inspect logs and database integrity**

Confirm `PRAGMA integrity_check = ok` and no new `WarScore`, SQLite, diplomacy, or mod compilation errors appear in `Player.log`.
