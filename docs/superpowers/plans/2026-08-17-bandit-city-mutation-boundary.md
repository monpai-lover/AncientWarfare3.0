# Bandit City Mutation Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent bandit stronghold cleanup from changing the live city set during `CityManager.update`.

**Architecture:** Track the active city-manager update scope with a small main-thread counter. Logical stronghold fall remains in `PeasantRebelBanditStrongholdService`, while physical city removal moves into a coalesced disposal service drained from the existing authority boundary.

**Tech Stack:** C#, Harmony patches, AncientWarfare3 rules harness, PowerShell source guards, .NET Release build.

---

### Task 1: Add A Testable City Update Scope

**Files:**
- Create: `Code/core/performance/CityManagerMutationScope.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CityManagerMutationScopeTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing scope test**

Add tests that require inactive initial state, nested `Enter`/`Exit`, underflow-safe exit, and `Reset`:

```csharp
CityManagerMutationScope.Reset();
False(CityManagerMutationScope.IsCityUpdateActive, "scope starts inactive");
CityManagerMutationScope.EnterCityUpdate();
CityManagerMutationScope.EnterCityUpdate();
True(CityManagerMutationScope.IsCityUpdateActive, "nested update remains active");
CityManagerMutationScope.ExitCityUpdate();
True(CityManagerMutationScope.IsCityUpdateActive, "one nested update remains");
CityManagerMutationScope.ExitCityUpdate();
False(CityManagerMutationScope.IsCityUpdateActive, "scope closes at zero");
```

Add a `--bandit-city-mutation` test entry and include both the test and production file in the test project.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false -- --bandit-city-mutation
```

Expected: compilation fails because `CityManagerMutationScope` does not exist.

- [ ] **Step 3: Implement the minimal scope**

Create a static main-thread scope with:

```csharp
internal static bool IsCityUpdateActive => _cityUpdateDepth > 0;
internal static void EnterCityUpdate() { _cityUpdateDepth++; }
internal static void ExitCityUpdate()
{
    if (_cityUpdateDepth > 0) _cityUpdateDepth--;
}
internal static void Reset() { _cityUpdateDepth = 0; }
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2. Expected: `Bandit city mutation rules passed.`

### Task 2: Guard CityManager.update With A Finalizer

**Files:**
- Create: `Code/patch/AW_CityManagerMutationBoundaryPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityManagerMutationScopeTests.cs.txt`

- [ ] **Step 1: Add failing source assertions**

Require a Harmony prefix on `CityManager.update` that enters the scope and a Harmony finalizer that exits it and returns the original exception.

- [ ] **Step 2: Run the focused test and verify RED**

Expected: failure stating that the city update boundary patch is missing.

- [ ] **Step 3: Implement the patch**

Use:

```csharp
[HarmonyPrefix]
[HarmonyPatch(typeof(CityManager), nameof(CityManager.update))]
private static void UpdatePrefix() =>
    CityManagerMutationScope.EnterCityUpdate();

[HarmonyFinalizer]
[HarmonyPatch(typeof(CityManager), nameof(CityManager.update))]
private static Exception UpdateFinalizer(Exception __exception)
{
    CityManagerMutationScope.ExitCityUpdate();
    return __exception;
}
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Expected: all scope and source assertions pass.

### Task 3: Move Stronghold City Removal To The Authority Boundary

**Files:**
- Create: `Code/core/lineage/BanditStrongholdCityDisposalService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityManagerMutationScopeTests.cs.txt`

- [ ] **Step 1: Add failing source assertions**

Require:

```text
BanditStrongholdCityDisposalService.Schedule(...)
BanditStrongholdCityDisposalService.ProcessAuthorityCycle()
```

and reject direct `World.world.cities.removeObject` calls from
`PeasantRebelBanditStrongholdService`.

- [ ] **Step 2: Run the focused test and verify RED**

Expected: failure because the stronghold service still removes cities directly.

- [ ] **Step 3: Implement the disposal queue**

Maintain a coalesced `Dictionary<long, long>` of city ID to expected bandit kingdom ID. `Schedule` records the request. `ProcessAuthorityCycle` returns immediately while `CityManagerMutationScope.IsCityUpdateActive`; otherwise it snapshots pending IDs, re-resolves each city, validates the expected owner when supplied, and calls `World.world.cities.removeObject` once.

Replace the three direct removal paths in inherited cleanup, `CompleteFall`, and rollback with `Schedule`. Wire `ProcessAuthorityCycle` at the start of `DrainDeferredAuthorityWork`, before draining generic deferred actions. Reset the queue during world clear through the existing runtime reset path.

- [ ] **Step 4: Run the focused test and verify GREEN**

Expected: scope, patch, authority drain, and no-direct-removal assertions pass.

### Task 4: Verify The Bandit Fix

**Files:**
- No additional files.

- [ ] **Step 1: Run bandit-focused tests**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false -- --bandit-city-mutation
```

- [ ] **Step 2: Run the full rules harness**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false
```

- [ ] **Step 3: Run a Release build**

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore -p:UseSharedCompilation=false
```

- [ ] **Step 4: Commit only the bandit boundary files**

```powershell
git add Code/core/performance/CityManagerMutationScope.cs Code/patch/AW_CityManagerMutationBoundaryPatch.cs Code/core/lineage/BanditStrongholdCityDisposalService.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Code/core/performance/AWAuthorityCycleService.cs Tests/AncientWarfare3.Rules.Tests/CityManagerMutationScopeTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "fix: defer bandit stronghold city disposal"
```
