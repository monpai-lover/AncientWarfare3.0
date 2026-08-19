# Mass Uprising Culture Clusters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add culture-and-connectivity based mass-uprising rebel clusters with vanilla-visible local-corruption loyalty, capital-adjacent protection, rebel civil war and final unification.

**Architecture:** Keep `BanditGreatUprisingService` as the annual trigger and add a bounded `MassUprisingClusterService` beside it. Pure cluster and phase rules live in `MassUprisingClusterRules`; durable cluster state is stored in `KingdomData` through compact encoded IDs. Existing rebel creation, war admission and elimination services remain the authority for actual kingdoms and wars.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox publicized API, existing AW3 `KingdomData` and rules test harness.

---

### Task 1: Add failing pure rules for thresholds and grouping

**Files:**
- Create: `Code/core/lineage/MassUprisingClusterRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MassUprisingClusterRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write tests first**

Cover:

```csharp
Equal(true, MassUprisingClusterRules.IsCandidate(-1, false));
Equal(false, MassUprisingClusterRules.IsCandidate(0, false));
Equal(true, MassUprisingClusterRules.IsCore(-51));
Equal(false, MassUprisingClusterRules.IsCore(-50));
Equal(true, MassUprisingClusterRules.IsCapitalProtected(true));
Equal(false, MassUprisingClusterRules.IsCapitalProtected(false));
```

Use an in-memory city graph with culture IDs and assert that same-culture adjacent cities form one cluster, different cultures split, and a disconnected same-culture city forms a second cluster. Assert deterministic ordering by city ID and that a cluster without a core city is not founder-eligible.

- [ ] **Step 2: Run targeted test and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --mass-uprising-clusters
```

Expected: compile failure because `MassUprisingClusterRules` is not defined.

- [ ] **Step 3: Implement minimal pure rules**

Define `CandidateThreshold = 0`, `CoreThreshold = -50`, a `MassUprisingCityFact` record, deterministic `BuildClusters`, `ClusterKey`, and phase transition helpers. Keep the graph input as `IReadOnlyList`/delegate data so tests do not require WorldBox types.

- [ ] **Step 4: Run targeted test and full rules**

Expected: targeted output `Mass uprising cluster rules passed.` and full output `Rule tests passed.`

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/MassUprisingClusterRules.cs Tests/AncientWarfare3.Rules.Tests/MassUprisingClusterRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: define mass uprising cluster rules"
```

### Task 2: Register the local-corruption vanilla loyalty asset

**Files:**
- Modify: `Code/content/WarLoyaltyContent.cs`
- Modify: `Locales/en.json`
- Modify: `Locales/ch.json`
- Modify: `Locales/cz.json`
- Create: `Tests/AncientWarfare3.Rules.Tests/LocalCorruptionLoyaltySourceGuardTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing source guards**

Require `aw_local_corruption`, `CorruptionService.ReadCity`, `AssetManager.loyalty_library.add`, and a negative score return. Require all three locale keys and the negative label key.

- [ ] **Step 2: Verify RED**

Run `--local-corruption-loyalty`; expected failure for the missing asset ID and locale keys.

- [ ] **Step 3: Add the asset**

Extend `WarLoyaltyContent.Init` with:

```csharp
AddIfMissing(new LoyaltyAsset
{
    id = LOCAL_CORRUPTION_LOYALTY_ID,
    translation_key = "aw_loyalty_local_corruption",
    translation_key_negative = "aw_loyalty_local_corruption_negative",
    calc = CalculateLocalCorruptionPenalty
});
```

Return `-Math.Max(0, Math.Min(CorruptionRules.MaxScore, CorruptionService.ReadCity(pCity).Score))` and return zero for invalid cities.

- [ ] **Step 4: Verify**

Run the targeted guard, parse all locale JSON as UTF-8, then run the full rules harness.

- [ ] **Step 5: Commit**

```powershell
git add Code/content/WarLoyaltyContent.cs Locales/en.json Locales/ch.json Locales/cz.json Tests/AncientWarfare3.Rules.Tests/LocalCorruptionLoyaltySourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: expose local corruption as vanilla loyalty modifier"
```

### Task 3: Add bounded cluster state and runtime service

**Files:**
- Create: `Code/core/lineage/MassUprisingClusterService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/policy/KingdomAnnualWorkService.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MassUprisingClusterSourceGuardTests.cs.txt`
- Modify: rules project/program.

- [ ] **Step 1: Write source guards**

Require annual invocation after `BanditGreatUprisingService`, authoritative and replica guards, bounded per-year cursor/budget, `City.neighbours_cities`, `city.getLoyalty`, `CorruptionService.ReadCity` through the vanilla loyalty asset only, additive kingdom keys, and runtime rebuild/clear registration.

- [ ] **Step 2: Implement state codec**

Add keys for cluster index, cluster key, origin ID, culture ID, phase, target IDs, core IDs, completion year and processed year. Encode ID lists as sorted comma-separated positive IDs with a fixed maximum length; reject malformed or oversized values.

- [ ] **Step 3: Implement candidate scan and connected components**

For one origin/year, read live cities, exclude capital and direct capital neighbors, read `getLoyalty()`, require `<0`, resolve exact `city.culture.id`, and call the pure rules BFS. Only components with a `<-50` core are eligible.

- [ ] **Step 4: Implement bounded creation**

Process one cluster per authority cycle. Select the lowest-loyalty core city as the seed and call `PeasantRebelBanditStrongholdService.TryCreateDirect`. On success, write the cluster metadata to the new rebel kingdom and attach the stable cluster key. On failure, keep the cluster pending for the next year without retry loops.

- [ ] **Step 5: Implement load/reset recovery**

Rebuild runtime indexes from kingdom data after restore and clear them on world reset. Destroyed origin/rebel/city IDs transition to `failed` and are removed from the active queue.

- [ ] **Step 6: Verify and commit**

Run targeted source guards, full rules, and main build.

```powershell
git add Code/core/lineage/MassUprisingClusterService.cs Code/core/lineage/LineageKeys.cs Code/core/policy/KingdomAnnualWorkService.cs Code/core/multiplayer/AW3RuntimeRestorePipeline.cs Tests/AncientWarfare3.Rules.Tests/MassUprisingClusterSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: create bounded culture uprising clusters"
```

### Task 4: Integrate target acquisition and civil-war phases

**Files:**
- Modify: `Code/core/lineage/MassUprisingClusterService.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs`
- Modify: `Code/patch/AW_WarPatch.cs` or the existing rebel war admission boundary.
- Create: `Tests/AncientWarfare3.Rules.Tests/MassUprisingCivilWarRulesTests.cs.txt`
- Modify: rules project/program.

- [ ] **Step 1: Write failing phase tests**

Cover incomplete targets staying in `cluster_uprising`, all targets owned by the rebel transitioning to `cluster_complete`, all origin clusters complete transitioning to `civil_war`, defeated rebel settlement transferring cities and final survivor transitioning to `unification`.

- [ ] **Step 2: Implement target gating**

Make `MassUprisingClusterService` expose `CanAcquireClusterTarget` and have the existing rebel acquisition boundary reject cities outside the rebel's stored target set while phase is `cluster_uprising`.

- [ ] **Step 3: Implement completion detection**

At most one cluster completion check per authority cycle, using current city ownership and no world-wide actor scan. When all target IDs are owned, mark complete and release that rebel from cluster-only targeting.

- [ ] **Step 4: Implement civil-war admission**

When every active cluster for an origin is complete, mark all surviving rebels `civil_war` and allow only those same-origin rebel pairs to bypass the normal rebel war restriction. Do not start wars in the same loop as cluster creation.

- [ ] **Step 5: Implement final unification**

After existing elimination/territory settlement leaves one rebel for an origin, set its phase to `unification` and use the existing origin-suppression/unification war path against the original kingdom, including the capital and protected ring.

- [ ] **Step 6: Verify and commit**

Run targeted phase tests, full rules and main build; commit:

```powershell
git add Code/core/lineage/MassUprisingClusterService.cs Code/core/lineage/PeasantRebelRouteService.cs Code/patch/AW_WarPatch.cs Tests/AncientWarfare3.Rules.Tests/MassUprisingCivilWarRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: resolve mass uprising through rebel civil war"
```

### Task 5: Regression, review and merge

- [ ] **Step 1: Run targeted tests**

Run `--mass-uprising-clusters`, `--local-corruption-loyalty` and the civil-war selector.

- [ ] **Step 2: Run full rules harness**

Expected: `Rule tests passed.`

- [ ] **Step 3: Build**

Run `dotnet build AncientWarfare3.csproj`; expected `0 warnings / 0 errors`.

- [ ] **Step 4: Parse locales and check diff**

Parse `Locales/en.json`, `Locales/ch.json`, `Locales/cz.json` as UTF-8 JSON and run `git diff --check`.

- [ ] **Step 5: Review branch and merge**

Review the diff against `master`, merge the feature branch into `master` with a merge commit, rerun full verification on merged `master`, then push `master` through the configured proxy.
