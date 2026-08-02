# Runtime, MapMode, and No-Force War Collapse Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with review checkpoints.

**Goal:** Reduce Actor and hierarchical MapMode runtime overhead while making late wars with no military potential end deterministically, including full-territory surrender for Zhulu/total wars.

**Architecture:** Keep all simulation and settlement writes on the WorldBox main thread. Actor diagnostics gain a zero-cost disabled path and bounded detail sampling. The hierarchical MapMode uses event-driven dirty generations, per-city geometry caches, and a bounded fallback cursor; minimap filtering is applied once per mode transition. A pure no-force rules layer feeds the existing annual settlement service, with a separate authoritative total-war transfer path.

**Tech Stack:** C# source mod, Harmony patches, Unity/WorldBox runtime APIs, existing AW3 rules test executable (`dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`).

---

## Task 1: Add failing rules tests for all three workstreams

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorDiagnosticSamplingRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalMapModeInvalidationRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarNoForceSurrenderRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing tests**

Add tests that call the not-yet-existing pure rules APIs:

```csharp
Equal(false, ActorDiagnosticSamplingRules.ShouldCollectDetail(
    diagnosticsEnabled: false, benchmarkEnabled: false,
    sampleOrdinal: 0, perFrameBudget: 8),
    "disabled Actor diagnostics do no work");
Equal(true, ActorDiagnosticSamplingRules.ShouldCollectDetail(
    diagnosticsEnabled: true, benchmarkEnabled: false,
    sampleOrdinal: 0, perFrameBudget: 8),
    "enabled diagnostics admit the first bounded sample");
Equal(false, ActorDiagnosticSamplingRules.ShouldCollectDetail(
    diagnosticsEnabled: true, benchmarkEnabled: false,
    sampleOrdinal: 8, perFrameBudget: 8),
    "Actor detail sampling has a hard frame budget");
Equal(true, HierarchicalVassalMapModeInvalidationRules.IsFallbackDue(
    elapsedSeconds: 2.0, fallbackIntervalSeconds: 2.0),
    "MapMode fallback becomes due at the accepted two-second bound");
Equal(false, HierarchicalVassalMapModeInvalidationRules.IsFallbackDue(
    elapsedSeconds: 1.99, fallbackIntervalSeconds: 2.0),
    "MapMode fallback does not run early");
Equal(true, WarNoForceSurrenderRules.IsNoForce(
    activeFieldSoldiers: 0, reserveSoldiers: 0, recruitableSoldiers: 0,
    minimumOperationalArmyCount: 0),
    "a side with no military potential is exhausted");
Equal(false, WarNoForceSurrenderRules.IsNoForce(
    activeFieldSoldiers: 0, reserveSoldiers: 4, recruitableSoldiers: 0,
    minimumOperationalArmyCount: 0),
    "reserve soldiers prevent false surrender");
Equal(true, WarNoForceSurrenderRules.ShouldSurrender(
    warYears: 3, sideNoForce: true, enemyHasForce: true,
    protectedTotalWar: false, bothSidesNoForce: false),
    "late ordinary war with no force surrenders");
Equal(true, WarNoForceSurrenderRules.ShouldSurrender(
    warYears: 3, sideNoForce: true, enemyHasForce: true,
    protectedTotalWar: true, bothSidesNoForce: false),
    "late total war with no force also collapses");
Equal(false, WarNoForceSurrenderRules.ShouldSurrender(
    warYears: 3, sideNoForce: true, enemyHasForce: false,
    protectedTotalWar: true, bothSidesNoForce: true),
    "a two-sided empty war waits for a deterministic winner");
```

Register each test file in the `.csproj` and call each `Run()` from `Program.cs.txt`.

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: compile failures naming the three missing rules classes. Do not add production code before observing this failure.

- [ ] **Step 3: Commit the red tests**

```powershell
git add Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define runtime and no-force performance contracts"
```

## Task 2: Implement pure sampling, invalidation, and no-force rules

**Files:**
- Create: `Code/core/policy/ActorDiagnosticSamplingRules.cs`
- Create: `Code/core/policy/HierarchicalVassalMapModeInvalidationRules.cs`
- Create: `Code/core/lineage/WarNoForceSurrenderRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Add minimal rules implementations**

Use allocation-free scalar rules with explicit boundaries:

```csharp
namespace AncientWarfare3.core.policy
{
    internal static class ActorDiagnosticSamplingRules
    {
        public const int MaximumSamplesPerFrame = 64;

        public static bool ShouldCollectDetail(bool diagnosticsEnabled,
            bool benchmarkEnabled, int sampleOrdinal, int perFrameBudget)
        {
            if (!diagnosticsEnabled && !benchmarkEnabled) return false;
            int budget = Math.Max(0, Math.Min(MaximumSamplesPerFrame,
                perFrameBudget));
            return sampleOrdinal >= 0 && sampleOrdinal < budget;
        }
    }
}
```

`HierarchicalVassalMapModeInvalidationRules.IsFallbackDue` compares elapsed seconds with a positive interval. `WarNoForceSurrenderRules` must require war years `>= 3`, positive enemy force, and reject the both-empty case; the `protectedTotalWar` argument is informational for the caller and must not suppress the trigger.

- [ ] **Step 2: Run the focused rules tests**

Run the full rules executable and expect all new assertions plus existing tests to pass.

- [ ] **Step 3: Commit the green pure rules**

```powershell
git add Code/core/policy/ActorDiagnosticSamplingRules.cs Code/core/policy/HierarchicalVassalMapModeInvalidationRules.cs Code/core/lineage/WarNoForceSurrenderRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: add bounded runtime and no-force war rules"
```

## Task 3: Remove per-Actor diagnostic overhead

**Files:**
- Modify: `Code/patch/AW_ActorAiBenchmarkPatch.cs`
- Modify: `Code/patch/AW_ActorRacePerformancePatch.cs`
- Modify: `Code/patch/AW_ActorBatchBenchmarkPatch.cs`
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `Code/core/policy/ActorDiagnosticSamplingRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/ActorDiagnosticSamplingRulesTests.cs.txt`

- [ ] **Step 1: Add a frame sample counter test seam**

Add `RuntimePerformanceDiagnostic.BeginFrame()` reset state and an internal `TryConsumeActorDetailSample()` that increments a counter only when sampling or `Bench.bench_enabled` is active. The counter must stop at `MaximumSamplesPerFrame`.

- [ ] **Step 2: Make the Actor AI prefix short-circuit**

Change `AW_ActorAiBenchmarkPatch.UpdateAi_Prefix` so the first branch checks the diagnostic/benchmark gate and writes an empty state. It must not access `__instance.ai.task` until `TryConsumeActorDetailSample()` returns true. Only sampled calls classify task IDs and call `RecentFeatureBenchmark.Begin()`.

```csharp
if (!RuntimePerformanceDiagnostic.ShouldCollectActorDetail())
{
    __state = default;
    return;
}
if (!RuntimePerformanceDiagnostic.TryConsumeActorDetailSample())
{
    __state = default;
    return;
}
```

Postfixes must return immediately for the default state; normal Actor behavior is untouched.

- [ ] **Step 3: Gate race and sprite diagnostic scopes**

Keep `BeginActorRaceScope` and `BeginActorBatch` disabled when `_sampling` is false. For enabled sampling, use the same bounded actor detail budget for `updateAge` and `calculateMainSprite`; do not add task lookup to these patches.

- [ ] **Step 4: Verify and commit**

Run the rules executable, then inspect the source to ensure disabled paths contain no task lookup before the gate. Commit:

```powershell
git add Code/patch/AW_ActorAiBenchmarkPatch.cs Code/patch/AW_ActorRacePerformancePatch.cs Code/patch/AW_ActorBatchBenchmarkPatch.cs Code/core/policy/RuntimePerformanceDiagnostic.cs Code/core/policy/ActorDiagnosticSamplingRules.cs
git commit -m "perf: bound Actor diagnostic instrumentation"
```

## Task 4: Add MapMode dirty generations and bounded fallback cursor

**Files:**
- Create: `Code/core/policy/HierarchicalVassalMapModeChangeTracker.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs`
- Create: `Code/patch/AW_HierarchicalVassalMapLifecyclePatch.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalMapModeInvalidationRulesTests.cs.txt`

- [ ] **Step 1: Add the tracker API**

The tracker exposes `MarkAll()`, `MarkKingdom(long)`, `MarkCity(long)`, `MarkHierarchy()`, `HasDirtyWork`, `AdvanceFallback(IReadOnlyList<Kingdom>, int)`, and `Reset()`. It owns a `long Generation`, dirty ID sets, a cursor, and the last fallback timestamp. `AdvanceFallback` processes at most the supplied count and returns whether the visible generation changed.

- [ ] **Step 2: Replace 15-frame full revision polling**

In `HierarchicalVassalMapModeService.RefreshIfWorldChanged`, remove `ComputeWorldRevision()` from the per-15-frame path. While active, call the tracker fallback with a bounded slice; call `InvalidateSnapshotCaches()` only when the tracker reports a change. Keep `DirtyMap()` as the authoritative full invalidation for load/reset/mode transitions.

- [ ] **Step 3: Wire known lifecycle changes**

Add Harmony postfixes for `City.setKingdom`, `City.joinAnotherKingdom`, `City.addZone`, `City.destroyCity`, `Kingdom.newCivKingdom`, `Kingdom.updateColor`, `Kingdom.setKing`, and `Kingdom.isReadyForRemoval` where the target method returns normally. Each hook must only call `MarkCity`, `MarkKingdom`, or `MarkHierarchy`; it must not build snapshots or read tile arrays.

- [ ] **Step 4: Keep label processing cache-driven**

`HierarchicalVassalMapModeLabelLayer.ProcessFrame` calls `RefreshIfWorldChanged()` and rebuilds only when the label dirty flag is set. `Reset()` clears the tracker. No code in this path may call `ComputeWorldRevision` or scan all world tiles.

- [ ] **Step 5: Run rules and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git add Code/core/policy/HierarchicalVassalMapModeChangeTracker.cs Code/core/policy/HierarchicalVassalMapModeService.cs Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs Code/patch/AW_HierarchicalVassalMapLifecyclePatch.cs
git commit -m "perf: replace hierarchical map polling with dirty generations"
```

## Task 5: Cache city geometry and eliminate per-frame QuantumSprite clearing

**Files:**
- Create: `Code/core/policy/HierarchicalVassalMapModeCityCache.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalMapModeInvalidationRulesTests.cs.txt`

- [ ] **Step 1: Add city cache storage**

Store per-city visible zones, land tile positions, area, centroid, and geometry metrics keyed by `City.id`. `Rebuild(city)` reads the city's zones once; `Remove(cityId)` drops stale entries. Do not use a worker thread for WorldBox objects.

- [ ] **Step 2: Make snapshot aggregation consume cached cities**

Update `AddKingdomTerritory` and city-label collection to use the cache. A dirty city rebuilds once; unaffected cities contribute existing arrays/lists. Preserve the current visible-land predicate and water-color behavior.

- [ ] **Step 3: Increase city label readability**

Update `HierarchicalVassalMapModeGeometry.CalculateCityLabelSize` so its base
scale is five times the current `0.065` value, while retaining area-derived
scaling and a bounded maximum. Add assertions that a fixed city area produces
a five-times larger base result and that the maximum still clamps large cities.

- [ ] **Step 4: Add transition-only QuantumSprite suppression**

Replace `HideNonEssentialMinimapAssets`'s per-frame `countActive()/clearFull()` loop with a transition state:

```csharp
private static readonly Dictionary<QuantumSpriteAsset, bool> SavedMapFlags =
    new Dictionary<QuantumSpriteAsset, bool>();
private static bool _mapAssetsSuppressed;

private static void SyncMapAssets(bool active)
{
    if (_mapAssetsSuppressed == active) return;
    if (active) SaveAndSuppressNonEssentialAssets();
    else RestoreSavedMapFlags();
    _mapAssetsSuppressed = active;
}
```

Invoke `SyncMapAssets` in the `QuantumSpriteManager.update` prefix. Clear each suppressed group only during the active transition. Preserve `ShouldKeepMinimapQuantumAsset` assets and restore exact original `render_map` values on exit/reset.

- [ ] **Step 5: Verify and commit**

Run rules tests and source guards. Confirm the patch no longer calls `clearFull()` from a per-frame postfix. Commit:

```powershell
git add Code/core/policy/HierarchicalVassalMapModeCityCache.cs Code/core/policy/HierarchicalVassalMapModeService.cs Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "perf: cache map geometry and filter minimap assets at transitions"
```

## Task 6: Add no-force side aggregation and failing integration guards

**Files:**
- Create: `Code/core/lineage/WarNoForceMilitaryService.cs`
- Modify: `Code/core/lineage/WarMilitaryFactsService.cs`
- Modify: `Code/core/lineage/WartimeMilitaryPotentialService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarNoForceSurrenderRulesTests.cs.txt`

- [ ] **Step 1: Add side-force aggregation**

Implement `WarNoForceMilitaryService.BuildSideFacts(War, bool attackers)` using the existing participant enumerators and `WartimeMilitaryPotentialService`. Sum active operational soldiers, reserve soldiers, and force-recruitable population with saturation. Ignore stale/destroyed participants and return a `BothSidesNoForce` flag.

- [ ] **Step 2: Preserve reserve/potential semantics**

Do not treat an Army with `countUnits()==0` as exhausted when reserve or recruitable totals are positive. Keep the existing `WarMilitaryFactsService` cache and include side-force facts in its once-per-world-day result.

- [ ] **Step 3: Add source guards**

Assert that no-force evaluation runs after wartime recruitment, uses all participants on the side, and does not use only `War.countAttackersWarriors()` or `War.countDefendersWarriors()` as its sole potential source.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git add Code/core/lineage/WarNoForceMilitaryService.cs Code/core/lineage/WarMilitaryFactsService.cs Code/core/lineage/WartimeMilitaryPotentialService.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: aggregate military potential per war side"
```

## Task 7: Integrate ordinary and total-war surrender

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalAiRules.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Code/core/lineage/WarPeaceSettlementRuntime.cs`
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarNoForceSurrenderRulesTests.cs.txt`

- [ ] **Step 1: Add no-force decision priority**

At the annual war settlement assessment, build side facts after mobilization. Call `WarNoForceSurrenderRules.ShouldSurrender`; when true, create a surrender candidate with higher priority than ordinary peace/enforce-demands candidates. Do not bypass `IsProtectedWar`, proposal validation, or replica gates for ordinary wars.

- [ ] **Step 2: Route total-war no-force to full surrender**

For a `WarTypeAsset.total_war` asset, including Zhulu, bypass ordinary proposal creation only after the no-force rule is true. Zhulu must enqueue its existing `ZhuluWarSettlementService` full-territory transaction. Other total-war assets use one idempotent `WarTotalWarSurrenderService.Apply(war, defeatedSide, winnerSide)` main-thread transaction that:

```csharp
if (!WarNoForceSurrenderRules.ShouldSurrender(...)) return false;
WarTerritoryService.TransferAllEligibleDefeatedTerritory(
    pWar, pDefeatedSide, pWinnerSide);
WarScoreService.EndWarAsSurrender(pWar, pDefeatedSide, pWinnerSide);
```

The transaction must close Army missions, occupation locks, participant state, and war persistence once. If no territory remains, call existing extinction cleanup. Do not route rebellion, independence, mandate, or restoration protected wars through this full-annex path.

- [ ] **Step 3: Protect both-empty wars**

When both sides have zero potential, do not choose a surrender unless the existing war-score winner/tie-breaker identifies a surviving side. Add a regression test for a total war with both sides empty.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git add Code/core/lineage/DiplomacyProposalAiRules.cs Code/core/lineage/DiplomacyProposalService.cs Code/core/lineage/WarPeaceSettlementRuntime.cs Code/core/lineage/WarTerritoryService.cs Code/patch/AW_WarPatch.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: end no-force wars and annex total-war surrender"
```

## Task 8: Build, source-only deploy, and runtime verification

**Files:**
- Modify only files proven necessary by build diagnostics.
- Deploy source files to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`.

- [ ] **Step 1: Run all rules and source-guard tests**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
Get-ChildItem Tests -Filter '*.ps1' -Recurse | ForEach-Object { powershell -ExecutionPolicy Bypass -File $_.FullName }
```

Expected: every existing rule/source guard passes; no test disables the existing RTS behavior.

- [ ] **Step 2: Build the source mod validation target**

Run the repository's existing build command after locating it with `rg -n "dotnet build|Build" README.md docs Code *.ps1`. Expected: zero C# compile errors and no new warnings in touched files.

- [ ] **Step 3: Deploy source only**

Copy the changed `Code` files and required test-independent content into the existing Mod source directory. Do not copy any DLL or overwrite unrelated user files. Verify the deployed paths with `Get-ChildItem`.

- [ ] **Step 4: Run the large-map MapMode scenario**

Use the existing 640x640 save. Measure with diagnostics off and MapMode active for at least 120 seconds: no 15-frame full revision scan, no repeated nonessential QuantumSprite `clearFull`, and no visible simulation pause. Toggle out/in and confirm original minimap assets restore.

- [ ] **Step 5: Run the war collapse scenarios**

Create one ordinary war and one Zhulu/total war. Exhaust one side's active troops, reserve, and recruitable population after year three while leaving the opponent armed. Confirm ordinary war uses the existing surrender settlement and total war transfers all eligible defeated territory. Confirm a two-sided empty war remains unresolved until the existing score tie-breaker is available.

- [ ] **Step 6: Record verification and final diff**

Run `git diff --check`, `git status --short`, and a final performance comparison against the pre-change save. Report measured frame time/FPS, whether MapMode remained responsive, and the exact source files deployed.
