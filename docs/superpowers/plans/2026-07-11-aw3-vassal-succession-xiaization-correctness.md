# AW3 Vassal, Succession, And Xiaization Correctness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Require a direct border for every new vassal relation, make direct sons and the full paternal tree succeed reliably, and rename fully Xiaized foreign kingdoms exactly once.

**Architecture:** Put deterministic decisions in small pure rule classes exercised from a temporary `F:\tmp` harness, then connect those rules to the existing yearly/event-driven services. `VassalService` remains the only relation gate, `HeirService` remains the only mutable succession owner, and `XiaNamingRepair` remains the only kingdom-name generator.

**Tech Stack:** C# 11, .NET Framework 4.8 mod assembly, Harmony, WorldBox kingdom/city/actor APIs, SQLite-backed lineage state, temporary .NET console rule tests.

**Execution constraint:** Work directly on `master` as requested. Never stage the user's deleted `Tests/` or `Verification/` trees, and never stage court/icon work while executing this plan.

---

## File Map

- Create `Code/core/lineage/KingdomAdjacency.cs`: one runtime definition of direct kingdom borders.
- Modify `Code/core/lineage/VassalRelationRules.cs`: pure adjacency gate.
- Modify `Code/core/lineage/VassalService.cs`: feed direct adjacency into every relation creation/reparent operation.
- Modify `Code/core/lineage/VassalAIService.cs`: remove the remote-suzerain score path and reuse the shared adjacency helper.
- Create `Code/core/lineage/HeirDirectSonRules.cs`: pure eldest-son and cache-reconciliation decisions.
- Modify `Code/core/lineage/HeirService.cs`: direct-son-first search, once-per-year cache reconciliation, and full paternal fallback.
- Modify `Code/core/lineage/LineageKeys.cs`: heir reconciliation and Xia naming markers.
- Modify `Code/core/lineage/LineageService.cs`: stop committing succession before final baby sex.
- Modify `Code/patch/AW_BabyNamePatch.cs`: refresh succession after `BabyMaker.makeBaby` completes.
- Modify `Code/patch/AW_KingdomPolicyPatch.cs`: invoke cheap yearly heir reconciliation.
- Create `Code/core/lineage/XiaizedKingdomNamingRules.cs`: pure one-time naming rules.
- Modify `Code/core/lineage/XiaizationService.cs`: request naming on the maximum-level transition.
- Modify `Code/content/XiaNamingRepair.cs`: recognize maximum Xiaization and maintain the applied marker.
- Create only temporarily: `F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj` and `Program.cs`.

### Task 1: Centralize Direct-Border Vassal Eligibility

**Files:**
- Create: `Code/core/lineage/KingdomAdjacency.cs`
- Modify: `Code/core/lineage/VassalRelationRules.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Code/core/lineage/VassalAIService.cs`
- Test: `F:\tmp\AW3CorrectnessRuleTests\Program.cs`

- [ ] **Step 1: Create the temporary focused test project**

Create `F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="F:\WorldBox New Mod\AncientWarfare3.0\Code\core\lineage\VassalRelationRules.cs" Link="VassalRelationRules.cs" />
  </ItemGroup>
</Project>
```

Create `Program.cs` with an assertion that names the new parameter:

```csharp
using AncientWarfare3.core.lineage;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

Check(!VassalRelationRules.CanSetVassal(true, false, false, true, false,
    pDirectlyAdjacent: false, out string remoteReason) && remoteReason == "not_adjacent",
    "remote vassalization must be rejected");
Check(VassalRelationRules.CanSetVassal(true, false, false, true, false,
    pDirectlyAdjacent: true, out _), "bordering vassalization must remain valid");
Console.WriteLine("AW3 correctness rule tests passed");
```

- [ ] **Step 2: Run the test and verify the new contract fails to compile**

Run: `dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj`

Expected: compilation fails because `CanSetVassal` has no `pDirectlyAdjacent` parameter.

- [ ] **Step 3: Add the pure adjacency gate**

Change the signature and insert the adjacency check after cycle/title validation:

```csharp
public static bool CanSetVassal(bool pBasicValid, bool pVassalIsRebel, bool pSuzerainIsRebel,
    bool pSuzerainTitleAboveVassal, bool pCycleDetected, bool pDirectlyAdjacent,
    out string pReason)
{
    // Preserve existing invalid/rebel/title/cycle checks in their current order.
    if (!pDirectlyAdjacent)
    {
        pReason = "not_adjacent";
        return false;
    }
    pReason = "";
    return true;
}
```

- [ ] **Step 4: Run the pure test and verify it passes**

Run: `dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj`

Expected: `AW3 correctness rule tests passed`.

- [ ] **Step 5: Add the shared runtime border helper**

Create `KingdomAdjacency.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    internal static class KingdomAdjacency
    {
        public static bool AreDirectNeighbors(Kingdom pA, Kingdom pB)
        {
            if (pA?.data == null || pB?.data == null || pA == pB || pA.isRekt() || pB.isRekt())
                return false;
            try
            {
                foreach (City city in pA.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    foreach (Kingdom neighbor in city.neighbours_kingdoms)
                        if (neighbor == pB) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
```

- [ ] **Step 6: Make `VassalService.CanSetVassal` the mandatory border gate**

Compute adjacency once and pass it to the pure rule:

```csharp
bool directlyAdjacent = KingdomAdjacency.AreDirectNeighbors(pVassal, pSuzerain);
return VassalRelationRules.CanSetVassal(
    basicValid,
    MandateRebelService.IsRebelKingdom(pVassal),
    MandateRebelService.IsRebelKingdom(pSuzerain),
    titleAbove,
    cycleDetected,
    directlyAdjacent,
    out _);
```

Keep `SetVassal` calling `CanSetVassal` before ending an existing relation. This ensures a rejected remote reparent cannot first orphan the vassal.

- [ ] **Step 7: Remove the AI-only remote candidate path**

In `FindBestSuzerain`, require `KingdomAdjacency.AreDirectNeighbors(pKingdom, other)` before power/opinion scoring, remove `distanceScore`, and replace the private `AreNeighbors` implementation/callers with the shared helper:

```csharp
if (!KingdomAdjacency.AreDirectNeighbors(pKingdom, other)) continue;
if (!VassalService.CanSetVassal(pKingdom, other)) continue;
float score = power + opinion * 2f;
```

- [ ] **Step 8: Build and commit the adjacency slice**

Run: `dotnet build AncientWarfare3.csproj`

Expected: build succeeds with zero errors.

Run:

```powershell
git add -- Code/core/lineage/KingdomAdjacency.cs Code/core/lineage/VassalRelationRules.cs Code/core/lineage/VassalService.cs Code/core/lineage/VassalAIService.cs
git commit -m "fix: require borders for new vassals"
```

### Task 2: Make the Eldest Eligible Direct Son Unconditionally First

**Files:**
- Create: `Code/core/lineage/HeirDirectSonRules.cs`
- Modify: `Code/core/lineage/HeirService.cs`
- Confirm: `Code/core/lineage/SuccessionTransitionRules.cs`
- Test: `F:\tmp\AW3CorrectnessRuleTests\Program.cs`

- [ ] **Step 1: Add failing direct-son ordering tests**

Add the new file to the temporary csproj and replace `Program.cs` with:

```csharp
using AncientWarfare3.core.lineage;

static void Equal(long expected, long actual, string message)
{
    if (expected != actual) throw new Exception($"{message}: expected {expected}, got {actual}");
}

var sons = new[]
{
    new HeirDirectSonCandidate(20, eligible: true, birthTime: 200, isAdult: true),
    new HeirDirectSonCandidate(10, eligible: true, birthTime: 100, isAdult: false),
    new HeirDirectSonCandidate(5, eligible: false, birthTime: 50, isAdult: true)
};
Equal(10, HeirDirectSonRules.SelectEldestEligibleId(sons),
    "underage elder son must outrank adult younger son");
if (!HeirDirectSonRules.NeedsRefresh(cachedHeirId: 20, cachedEligible: true,
        eldestEligibleDirectSonId: 10))
    throw new Exception("a cached younger or collateral heir must be refreshed");
Console.WriteLine("direct-son rules passed");
```

- [ ] **Step 2: Run and verify the missing-type failure**

Run: `dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj`

Expected: compilation fails because `HeirDirectSonRules` is not defined.

- [ ] **Step 3: Implement the pure direct-son rules**

Create `HeirDirectSonRules.cs` with a stable actor-ID tie-break and no adulthood preference:

```csharp
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct HeirDirectSonCandidate
    {
        public readonly long ActorId;
        public readonly bool Eligible;
        public readonly double BirthTime;
        public readonly bool IsAdult;

        public HeirDirectSonCandidate(long actorId, bool eligible, double birthTime, bool isAdult)
        {
            ActorId = actorId;
            Eligible = eligible;
            BirthTime = birthTime;
            IsAdult = isAdult;
        }
    }

    public static class HeirDirectSonRules
    {
        public static long SelectEldestEligibleId(IEnumerable<HeirDirectSonCandidate> pCandidates)
        {
            long bestId = -1;
            double bestBirth = double.MaxValue;
            if (pCandidates == null) return bestId;
            foreach (HeirDirectSonCandidate candidate in pCandidates)
            {
                if (!candidate.Eligible) continue;
                if (candidate.BirthTime > bestBirth) continue;
                if (candidate.BirthTime == bestBirth && bestId >= 0 && candidate.ActorId >= bestId) continue;
                bestId = candidate.ActorId;
                bestBirth = candidate.BirthTime;
            }
            return bestId;
        }

        public static bool NeedsRefresh(long cachedHeirId, bool cachedEligible,
            long eldestEligibleDirectSonId)
        {
            if (!cachedEligible) return true;
            return eldestEligibleDirectSonId >= 0 && cachedHeirId != eldestEligibleDirectSonId;
        }
    }
}
```

- [ ] **Step 4: Run and verify the direct-son tests pass**

Run: `dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj`

Expected: `direct-son rules passed`.

- [ ] **Step 5: Move the direct-son pass before genealogy**

At the start of `HeirService.FindHeir`, after resolving `king` and `kingId`, select the eldest direct son and return immediately:

```csharp
Actor directSon = PickEldestLivingSon(king);
if (directSon?.data != null)
    return new HeirSelection(directSon,
        directSon.isAdult() ? SuccessionMode.DIRECT : SuccessionMode.UNDERAGE_DIRECT);
```

Remove the old `if (best == null) PickEldestLivingSon(...)` fallback. The genealogy loop now runs only when no eligible direct son exists.

- [ ] **Step 6: Use one eligibility definition for direct and collateral candidates**

Build `HeirDirectSonCandidate` values from `king.getChildren(false)` using `IsHeirBaseEligible`. Do not check city leader, general, captain, fief, or central-office status. Preserve only male/alive/not-mad/not-slave/not-king checks. Confirm `SuccessionTransitionRules.IsOfficialRoleEligible` still returns only `!pIsKing`.

- [ ] **Step 7: Add the crown-prince-branch regression to the harness**

Link `HeirGenerationRules.cs` and assert that ineligible dead descendants are skipped and the living brother is selected:

```csharp
var paternal = new[]
{
    new HeirCandidateRank(101, false, true, 1, 10, true),
    new HeirCandidateRank(102, false, true, 2, 20, true),
    new HeirCandidateRank(200, true, false, 0, 30, true),
    new HeirCandidateRank(300, true, false, 1, 40, true)
};
Equal(200, HeirGenerationRules.SelectBestCandidateId(paternal),
    "living brother must beat nephew/collateral after the dead crown-prince branch");
```

Run the harness and expect all assertions to pass.

- [ ] **Step 8: Build and commit the direct-son slice**

Run: `dotnet build AncientWarfare3.csproj`

Run:

```powershell
git add -- Code/core/lineage/HeirDirectSonRules.cs Code/core/lineage/HeirService.cs
git commit -m "fix: prioritize the eldest eligible son"
```

### Task 3: Reconcile the Heir Cache at Safe Event Boundaries

**Files:**
- Modify: `Code/core/lineage/HeirDirectSonRules.cs`
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/patch/AW_BabyNamePatch.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`
- Test: `F:\tmp\AW3CorrectnessRuleTests\Program.cs`

- [ ] **Step 1: Add failing once-per-year reconciliation tests**

Add assertions:

```csharp
if (!HeirDirectSonRules.ShouldReconcile(currentYear: 50, lastYear: 49, successionPending: false))
    throw new Exception("new world year must reconcile");
if (HeirDirectSonRules.ShouldReconcile(50, 50, false))
    throw new Exception("same world year must stay O(1)");
if (HeirDirectSonRules.ShouldReconcile(50, 49, true))
    throw new Exception("delayed accession window must preserve prepared cache");
```

Run the harness and expect a missing-method compilation failure.

- [ ] **Step 2: Implement the pure yearly gate**

Add:

```csharp
public static bool ShouldReconcile(int currentYear, int lastYear, bool successionPending)
{
    return !successionPending && currentYear != lastYear;
}
```

Run the harness and expect all assertions to pass.

- [ ] **Step 3: Add the reconciliation marker**

Add to `LineageKeys`:

```csharp
public const string KINGDOM_HEIR_LAST_RECONCILE_YEAR = "aw_heir_last_reconcile_year";
```

- [ ] **Step 4: Add cheap runtime reconciliation**

Add `HeirService.ReconcileHeir(Kingdom, bool pForce)` and `OnKingdomYear`:

```csharp
public static void OnKingdomYear(Kingdom pKingdom) => ReconcileHeir(pKingdom, pForce: false);

public static Actor ReconcileHeir(Kingdom pKingdom, bool pForce)
{
    if (pKingdom?.data == null || RepublicGovernmentService.IsRepublic(pKingdom))
        return PeekRegisteredHeir(pKingdom);
    bool pending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
    int year = Date.getCurrentYear();
    pKingdom.data.get(LineageKeys.KINGDOM_HEIR_LAST_RECONCILE_YEAR, out int lastYear, -1);
    if (!pForce && !HeirDirectSonRules.ShouldReconcile(year, lastYear, pending))
        return PeekRegisteredHeir(pKingdom);
    if (pending) return PeekRegisteredHeir(pKingdom);
    pKingdom.data.set(LineageKeys.KINGDOM_HEIR_LAST_RECONCILE_YEAR, year);

    Actor cached = PeekRegisteredHeir(pKingdom);
    Actor eldest = PickEldestLivingSon(pKingdom.king);
    if (!pForce && !HeirDirectSonRules.NeedsRefresh(
            cached?.data?.id ?? -1L, cached?.data != null, eldest?.data?.id ?? -1L))
        return cached;
    return RefreshHeirAndReturn(pKingdom);
}
```

Change `GetHeir` to call this once per year before returning the cache. Repeated high-frequency reads in the same year remain O(1).

- [ ] **Step 5: Move birth refresh after final sex assignment**

Remove `HeirService.RefreshForNewRoyalChild(...)` from `LineageService.OnActorBornWithParents`; leave parent registration, naming, and archiving intact.

Extend the existing `BabyMaker.makeBaby` postfix signature and add the refresh after naming/archive:

```csharp
public static void MakeBaby_Postfix(Actor pParent1, Actor pParent2, Actor __result)
{
    if (__result?.data == null) return;
    if (!LineageService.IsXia(__result) && !LineageService.UsesAwLineageSystem(__result)) return;
    LineageService.ApplyDisplayName(__result);
    LineageService.ArchiveActor(__result, pAlive: true);
    HeirService.RefreshForNewRoyalChild(__result, pParent1, pParent2);
}
```

- [ ] **Step 6: Wire the yearly check and preserve existing force-refresh boundaries**

In `AW_KingdomPolicyPatch`, invoke `HeirService.OnKingdomYear(__instance)` once in the existing yearly postfix. Keep `PrepareSuccessionBeforeKingDeath` in `AW_ActorDeathPatch`/`AW_MandateSuccessionPatch` and `RefreshHeir` after successful `Kingdom.setKing` unchanged; those are already the required death/accession boundaries.

- [ ] **Step 7: Build, run rules, and commit**

Run:

```powershell
dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj
dotnet build AncientWarfare3.csproj
```

Expected: rule harness prints success; build has zero errors.

Run:

```powershell
git add -- Code/core/lineage/HeirDirectSonRules.cs Code/core/lineage/HeirService.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/LineageService.cs Code/patch/AW_BabyNamePatch.cs Code/patch/AW_KingdomPolicyPatch.cs
git commit -m "fix: reconcile succession after final birth state"
```

### Task 4: Rename Fully Xiaized Foreign Kingdoms Exactly Once

**Files:**
- Create: `Code/core/lineage/XiaizedKingdomNamingRules.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/XiaizationService.cs`
- Modify: `Code/content/XiaNamingRepair.cs`
- Test: `F:\tmp\AW3CorrectnessRuleTests\Program.cs`

- [ ] **Step 1: Add failing one-time naming tests**

Link the new rule file in the temporary csproj and add:

```csharp
if (!XiaizedKingdomNamingRules.ShouldApply(originalXia: false, xiaizationLevel: 5,
        maximumLevel: 5, markerApplied: false))
    throw new Exception("newly fully Xiaized foreign kingdom must rename");
if (XiaizedKingdomNamingRules.ShouldApply(false, 5, 5, true))
    throw new Exception("marked kingdom must preserve later manual names");
if (XiaizedKingdomNamingRules.ShouldApply(true, 5, 5, false))
    throw new Exception("original Xia naming must stay on its existing path");
```

Run the harness and expect a missing-type compilation failure.

- [ ] **Step 2: Implement the pure rule**

Create:

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class XiaizedKingdomNamingRules
    {
        public static bool ShouldApply(bool originalXia, int xiaizationLevel,
            int maximumLevel, bool markerApplied)
        {
            return !originalXia && !markerApplied && xiaizationLevel >= maximumLevel;
        }
    }
}
```

Run the harness and expect success.

- [ ] **Step 3: Add the persistent applied marker**

Add:

```csharp
public const string XIA_FULL_NAME_APPLIED = "aw_xia_full_name_applied";
```

- [ ] **Step 4: Add a dedicated naming-repair entry point**

In `XiaNamingRepair`, add a method that checks original identity, level, and marker, forces one existing-generator rename, and sets the marker only after success:

Add `using AncientWarfare3.core.lineage;` at the top of `XiaNamingRepair.cs`, then add:

```csharp
internal static bool TryApplyFullyXiaizedKingdomName(Kingdom pKingdom)
{
    if (pKingdom?.data == null || pKingdom.isRekt()) return false;
    pKingdom.data.get(LineageKeys.XIA_FULL_NAME_APPLIED, out bool applied, false);
    bool originalXia = pKingdom.data.original_actor_asset == XiaRace.ID ||
                       pKingdom.getActorAsset()?.id == XiaRace.ID;
    if (!XiaizedKingdomNamingRules.ShouldApply(originalXia,
            XiaizationService.GetLevel(pKingdom), XiaizationService.LevelXiaizedDynasty, applied))
        return false;
    if (!TryRenameKingdom(pKingdom, pKingdom.king, pForce: true)) return false;
    pKingdom.data.set(LineageKeys.XIA_FULL_NAME_APPLIED, true);
    return true;
}
```

Extend `IsXiaKingdom` with maximum Xiaization so normal repair recognizes the kingdom after transition.

- [ ] **Step 5: Trigger naming on transition and low-frequency repair**

After `TrySetLevel` persists the new level, call `XiaNamingRepair.TryApplyFullyXiaizedKingdomName(pKingdom)` when `pLevel >= LevelXiaizedDynasty`.

In `EnsureWorldNames`, call the dedicated method before ordinary `TryRenameKingdom` for each kingdom. A level-five kingdom lacking the marker is repaired once; a marked kingdom is not force-renamed again.

- [ ] **Step 6: Run rules, build, and commit**

Run:

```powershell
dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj
dotnet build AncientWarfare3.csproj
```

Run:

```powershell
git add -- Code/core/lineage/XiaizedKingdomNamingRules.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/XiaizationService.cs Code/content/XiaNamingRepair.cs
git commit -m "fix: name fully Xiaized kingdoms once"
```

### Task 5: Correctness Regression And Configuration Verification

**Files:**
- Verify only; production edits occur only if a failing check exposes a defect covered by this plan.

- [ ] **Step 1: Run the complete temporary rule harness**

Run: `dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj`

Expected: every adjacency, direct-son, collateral, yearly-cache, and Xia naming assertion passes.

- [ ] **Step 2: Build normal and no-Chinese-symbol configurations**

Run:

```powershell
dotnet build AncientWarfare3.csproj
dotnet build AncientWarfare3.csproj -p:DefineConstants="DEBUG;TRACE"
```

Expected: both builds complete with zero errors. The second build proves the Xia naming call does not introduce an unconditional Chinese Name dependency.

- [ ] **Step 3: Inspect the Harmony birth ordering**

Run:

```powershell
rg -n "applyParentsMeta|data.sex" "F:\WorldBox New Mod\AssetRipper_export_20260628_163320\ExportedProject\Assets\Scripts\Assembly-CSharp\BabyMaker.cs"
rg -n "MakeBaby_Postfix|RefreshForNewRoyalChild" Code/patch/AW_BabyNamePatch.cs Code/core/lineage/LineageService.cs
```

Expected: original final sex assignment precedes the AW3 postfix refresh, and the earlier parent-meta path no longer refreshes succession.

- [ ] **Step 4: Perform targeted in-game smoke scenarios**

Verify these observable cases:

1. A manual/AI/war-settlement remote vassal attempt fails; a bordering attempt succeeds.
2. Removing the border after establishment does not dissolve the relation.
3. An underage elder son remains crown prince over an adult younger son.
4. A general, city leader, captain, fief holder, or central officer can be crown prince.
5. After the crown prince and his sons die, the old king's next living son becomes crown prince and the kingdom remains monarchical.
6. A fully Xiaized foreign kingdom receives one Xia-style name; a later manual rename persists.

- [ ] **Step 5: Audit repository state**

Run: `git status --short`

Expected: only the user's known court icons, `XiaTraits.cs`, and intentional test deletions remain outside commits. Do not stage or remove `F:\tmp\AW3CorrectnessRuleTests`.
