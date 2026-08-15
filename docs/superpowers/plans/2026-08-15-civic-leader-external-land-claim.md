# Civic Leader External Land Claim Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make kings and city leaders walk into a legal unoccupied zone adjacent to their city before any land-claim animation can begin.

**Architecture:** Preserve the native `claim_land` task and native `BehGoToTileTarget`, but intercept its unique `BehActorCheckZoneTarget` selector for civic leaders. A shared runtime service applies one external-zone predicate during selection, pre-animation arrival, and final claim validation; an idempotently inserted behavior enforces the pre-animation gate.

**Tech Stack:** C#, Harmony patches, WorldBox actor behavior tasks, .NET 9 focused rules executable, MSBuild, PowerShell deployment.

---

## File Map

- Modify `Code/core/policy/XiaExpansionDecisionRules.cs`: pure civic-leader, zone-validity, arrival, and task-installation decisions.
- Create `Code/core/policy/CivicLeaderLandClaimService.cs`: runtime adapter from `Actor`, `City`, `TileZone`, and `WorldTile` to the pure rules.
- Create `Code/ai/behaviours/actor/BehCivicLeaderClaimArrival.cs`: pre-animation behavior gate.
- Modify `Code/patch/AW_XiaExpansionPatch.cs`: early selector interception and final fail-closed claim validation; remove the ineffective late redirect.
- Modify `Code/content/XiaExpansionDecisionContent.cs`: validate the native task shape and insert the arrival guard exactly once.
- Modify `Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt`: focused behavioral and source-wiring regression tests.

### Task 1: Pure Claim Invariants And Task-Shape Rules

**Files:**
- Modify: `Code/core/policy/XiaExpansionDecisionRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt`

- [ ] **Step 1: Write failing tests for the civic role and external-zone invariants**

Add these assertions near the existing claim-land weight tests:

```csharp
Equal(true, XiaExpansionDecisionRules.IsCivicLeader(
        actorIsKing: true, actorIsCityLeader: false),
    "a king uses the external civic claim route");
Equal(true, XiaExpansionDecisionRules.IsCivicLeader(
        actorIsKing: false, actorIsCityLeader: true),
    "a city leader uses the external civic claim route");
Equal(false, XiaExpansionDecisionRules.IsCivicLeader(
        actorIsKing: false, actorIsCityLeader: false),
    "an ordinary resident keeps the native claim route");

Equal(true, XiaExpansionDecisionRules.IsExternalClaimZoneValid(
        zoneExists: true, centerTileExists: true, zoneHasCity: false,
        touchesOwnCity: true, sameIsland: true, nativeClaimAllowed: true),
    "an empty adjacent same-island zone accepted by vanilla is legal");
Equal(false, XiaExpansionDecisionRules.IsExternalClaimZoneValid(
        zoneExists: true, centerTileExists: true, zoneHasCity: true,
        touchesOwnCity: true, sameIsland: true, nativeClaimAllowed: true),
    "an occupied zone cannot be selected for a civic claim");
Equal(false, XiaExpansionDecisionRules.IsExternalClaimZoneValid(
        zoneExists: true, centerTileExists: true, zoneHasCity: false,
        touchesOwnCity: false, sameIsland: true, nativeClaimAllowed: true),
    "a detached empty zone cannot be selected");
Equal(false, XiaExpansionDecisionRules.IsExternalClaimZoneValid(
        zoneExists: true, centerTileExists: true, zoneHasCity: false,
        touchesOwnCity: true, sameIsland: false, nativeClaimAllowed: true),
    "an adjacent zone on another island cannot be selected");
Equal(false, XiaExpansionDecisionRules.IsExternalClaimZoneValid(
        zoneExists: true, centerTileExists: true, zoneHasCity: false,
        touchesOwnCity: true, sameIsland: true, nativeClaimAllowed: false),
    "the native claim veto remains authoritative");

Equal(true, XiaExpansionDecisionRules.CanBeginExternalClaimAnimation(
        currentZoneMatchesSelectedTarget: true,
        externalZoneStillValid: true),
    "arrival in the selected legal external zone permits animation");
Equal(false, XiaExpansionDecisionRules.CanBeginExternalClaimAnimation(
        currentZoneMatchesSelectedTarget: false,
        externalZoneStillValid: true),
    "standing in another zone cannot begin the claim animation");
Equal(false, XiaExpansionDecisionRules.CanBeginExternalClaimAnimation(
        currentZoneMatchesSelectedTarget: true,
        externalZoneStillValid: false),
    "a stale selected zone cannot begin the claim animation");
```

- [ ] **Step 2: Write failing tests for guard placement and idempotence**

Add these assertions after the arrival assertions:

```csharp
Equal(2, XiaExpansionDecisionRules.ClaimLandGuardInsertionIndex(
        new[]
        {
            "BehActorCheckZoneTarget",
            "BehGoToTileTarget",
            "BehActorReverseFlip"
        }),
    "the arrival guard is inserted immediately after native movement");
Equal(XiaExpansionDecisionRules.ClaimLandGuardAlreadyInstalled,
    XiaExpansionDecisionRules.ClaimLandGuardInsertionIndex(
        new[]
        {
            "BehActorCheckZoneTarget",
            "BehGoToTileTarget",
            "BehCivicLeaderClaimArrival"
        }),
    "an existing guard suppresses duplicate installation");
Equal(XiaExpansionDecisionRules.ClaimLandTaskIncompatible,
    XiaExpansionDecisionRules.ClaimLandGuardInsertionIndex(
        new[] { "BehFindRandomTile", "BehGoToTileTarget" }),
    "an unexpected native task shape is left unchanged");
```

- [ ] **Step 3: Run the focused slice and verify the tests fail**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --xia-monkey-slice
```

Expected: compilation fails because `IsCivicLeader`, `IsExternalClaimZoneValid`, `CanBeginExternalClaimAnimation`, and `ClaimLandGuardInsertionIndex` do not exist.

- [ ] **Step 4: Implement the minimal pure rules**

Add `using System.Collections.Generic;` above the namespace and add these members inside `XiaExpansionDecisionRules`:

```csharp
internal const int ClaimLandTaskIncompatible = -1;
internal const int ClaimLandGuardAlreadyInstalled = -2;
private const string ClaimSelectorType = "BehActorCheckZoneTarget";
private const string ClaimMovementType = "BehGoToTileTarget";
private const string ClaimArrivalGuardType =
    "BehCivicLeaderClaimArrival";

public static bool IsCivicLeader(bool actorIsKing,
    bool actorIsCityLeader)
{
    return actorIsKing || actorIsCityLeader;
}

public static bool IsExternalClaimZoneValid(bool zoneExists,
    bool centerTileExists, bool zoneHasCity, bool touchesOwnCity,
    bool sameIsland, bool nativeClaimAllowed)
{
    return zoneExists && centerTileExists && !zoneHasCity &&
           touchesOwnCity && sameIsland && nativeClaimAllowed;
}

public static bool CanBeginExternalClaimAnimation(
    bool currentZoneMatchesSelectedTarget,
    bool externalZoneStillValid)
{
    return currentZoneMatchesSelectedTarget && externalZoneStillValid;
}

public static int ClaimLandGuardInsertionIndex(
    IReadOnlyList<string> actionTypeNames)
{
    if (actionTypeNames == null)
        return ClaimLandTaskIncompatible;
    for (int i = 0; i < actionTypeNames.Count; i++)
        if (actionTypeNames[i] == ClaimArrivalGuardType)
            return ClaimLandGuardAlreadyInstalled;
    if (actionTypeNames.Count < 2 ||
        actionTypeNames[0] != ClaimSelectorType ||
        actionTypeNames[1] != ClaimMovementType)
        return ClaimLandTaskIncompatible;
    return 2;
}
```

- [ ] **Step 5: Run the focused slice and verify it passes**

Run the same `dotnet run` command.

Expected: `Xia expansion and civ monkey naming rules passed.`

- [ ] **Step 6: Commit the pure rules**

```powershell
git add Code/core/policy/XiaExpansionDecisionRules.cs Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt
git commit -m "test: define civic leader claim invariants"
```

### Task 2: Early External Target Selection

**Files:**
- Create: `Code/core/policy/CivicLeaderLandClaimService.cs`
- Modify: `Code/patch/AW_XiaExpansionPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt`

- [ ] **Step 1: Add failing source-wiring tests**

Add these checks while leaving the existing late-redirect assertion in place
until Task 4:

```csharp
Equal(true, expansionPatch.Contains(
        "nameof(BehActorCheckZoneTarget.execute)"),
    "claim target selection is intercepted before native movement");
Equal(true, expansionPatch.Contains(
        "CivicLeaderLandClaimService.TrySetExternalTarget(pActor)"),
    "the selector delegates to the shared civic claim service");

string civicClaimServicePath = Path.Combine(
    Directory.GetCurrentDirectory(), "Code", "core", "policy",
    "CivicLeaderLandClaimService.cs");
Equal(true, File.Exists(civicClaimServicePath),
    "civic claim selection and validation share one runtime service");
string civicClaimService = File.Exists(civicClaimServicePath)
    ? File.ReadAllText(civicClaimServicePath)
    : string.Empty;
Equal(true, civicClaimService.Contains("city.border_zones"),
    "civic claim targets originate from the current city border");
Equal(true, civicClaimService.Contains(
        "XiaExpansionDecisionRules.IsExternalClaimZoneValid("),
    "runtime zone checks delegate to the pure external-zone invariant");
```

- [ ] **Step 2: Run the focused slice and verify it fails**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --xia-monkey-slice
```

Expected: failure reporting that the early selector patch and runtime service are absent.

- [ ] **Step 3: Create the shared runtime service**

Create `CivicLeaderLandClaimService.cs` with this implementation:

```csharp
namespace AncientWarfare3.core.policy
{
    internal static class CivicLeaderLandClaimService
    {
        internal static bool IsCivicLeader(Actor pActor)
        {
            City city = pActor?.city;
            if (pActor?.data == null || city?.data == null) return false;
            bool isKing = false;
            try { isKing = pActor.isKing(); }
            catch { }
            return XiaExpansionDecisionRules.IsCivicLeader(
                isKing, city.leader == pActor);
        }

        internal static bool TrySetExternalTarget(Actor pActor)
        {
            City city = pActor?.city;
            if (!IsCivicLeader(pActor) || city?.border_zones == null)
                return false;
            foreach (TileZone border in city.border_zones)
            {
                TileZone[] neighbours = border?.neighbours_all;
                if (neighbours == null) continue;
                for (int i = 0; i < neighbours.Length; i++)
                {
                    TileZone candidate = neighbours[i];
                    if (!IsValidExternalZone(pActor, candidate)) continue;
                    pActor.beh_tile_target = candidate.centerTile;
                    return true;
                }
            }
            return false;
        }

        internal static bool IsValidArrival(Actor pActor)
        {
            TileZone currentZone = pActor?.current_tile?.zone;
            TileZone selectedZone = pActor?.beh_tile_target?.zone;
            bool zoneStillValid = IsValidExternalZone(pActor, currentZone);
            return XiaExpansionDecisionRules.CanBeginExternalClaimAnimation(
                ReferenceEquals(currentZone, selectedZone), zoneStillValid);
        }

        internal static bool IsValidExternalZone(Actor pActor,
            TileZone pZone)
        {
            City city = pActor?.city;
            WorldTile cityTile = city?.getTile();
            bool exists = pZone != null;
            bool hasCenter = pZone?.centerTile != null;
            bool hasCity = exists && pZone.hasCity();
            bool touchesOwnCity = TouchesCityBoundary(city, pZone);
            bool sameIsland = hasCenter && cityTile != null &&
                              pZone.centerTile.isSameIsland(cityTile);
            bool nativeAllowed = exists && cityTile != null &&
                                 city.isZoneToClaimStillGood(
                                     pActor, pZone, cityTile);
            return XiaExpansionDecisionRules.IsExternalClaimZoneValid(
                exists, hasCenter, hasCity, touchesOwnCity, sameIsland,
                nativeAllowed);
        }

        private static bool TouchesCityBoundary(City pCity,
            TileZone pZone)
        {
            if (pCity?.data == null || pZone?.neighbours_all == null)
                return false;
            TileZone[] neighbours = pZone.neighbours_all;
            for (int i = 0; i < neighbours.Length; i++)
                if (neighbours[i]?.city == pCity) return true;
            return false;
        }
    }
}
```

- [ ] **Step 4: Patch the native selector only for civic leaders**

Add this prefix to `AW_XiaExpansionPatch`:

```csharp
[HarmonyPrefix]
[HarmonyPatch(typeof(BehActorCheckZoneTarget),
    nameof(BehActorCheckZoneTarget.execute))]
private static bool CivicLeaderClaimTarget_Prefix(Actor pActor,
    ref BehResult __result)
{
    if (!CivicLeaderLandClaimService.IsCivicLeader(pActor)) return true;
    __result = CivicLeaderLandClaimService.TrySetExternalTarget(pActor)
        ? BehResult.Continue
        : BehResult.Stop;
    return false;
}
```

The original source has only one `BehActorCheckZoneTarget` use, at index zero of `claim_land`, so this prefix does not affect unrelated tasks. Returning `true` for ordinary residents preserves the native selector exactly.

- [ ] **Step 5: Run the focused slice and build the mod**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --xia-monkey-slice
dotnet build AncientWarfare3.csproj -c Release
```

Expected: the focused slice prints its pass message and the mod build completes with zero errors.

- [ ] **Step 6: Commit early selection**

```powershell
git add Code/core/policy/CivicLeaderLandClaimService.cs Code/patch/AW_XiaExpansionPatch.cs Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt
git commit -m "fix: select external civic claim targets early"
```

### Task 3: Pre-Animation Arrival Guard

**Files:**
- Create: `Code/ai/behaviours/actor/BehCivicLeaderClaimArrival.cs`
- Modify: `Code/content/XiaExpansionDecisionContent.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt`

- [ ] **Step 1: Add failing behavior and installer source tests**

Append these checks after the existing `expansionDecisionContent` assertions:

```csharp
string arrivalBehaviorPath = Path.Combine(
    Directory.GetCurrentDirectory(), "Code", "ai", "behaviours",
    "actor", "BehCivicLeaderClaimArrival.cs");
Equal(true, File.Exists(arrivalBehaviorPath),
    "claim_land has a dedicated pre-animation arrival behavior");
string arrivalBehavior = File.Exists(arrivalBehaviorPath)
    ? File.ReadAllText(arrivalBehaviorPath)
    : string.Empty;
Equal(true, arrivalBehavior.Contains(
        "CivicLeaderLandClaimService.IsValidArrival(pActor)"),
    "the arrival behavior reuses the shared runtime validation");
Equal(true, expansionDecisionContent.Contains(
        "ClaimLandGuardInsertionIndex("),
    "claim_land installation validates the native task shape");
Equal(true, expansionDecisionContent.Contains(
        "task.list.Insert(insertionIndex, guard)"),
    "the arrival guard is inserted at the validated pre-animation index");
```

- [ ] **Step 2: Run the focused slice and verify it fails**

Run the focused `dotnet run` command from Task 1.

Expected: failure because the behavior file and installer wiring do not exist.

- [ ] **Step 3: Create the arrival behavior**

Create `BehCivicLeaderClaimArrival.cs`:

```csharp
using AncientWarfare3.core.policy;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehCivicLeaderClaimArrival :
        BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!CivicLeaderLandClaimService.IsCivicLeader(pActor))
                return BehResult.Continue;
            return CivicLeaderLandClaimService.IsValidArrival(pActor)
                ? BehResult.Continue
                : BehResult.Stop;
        }
    }
}
```

- [ ] **Step 4: Add idempotent task installation**

Add these imports to `XiaExpansionDecisionContent.cs`:

```csharp
using System.Collections.Generic;
using AncientWarfare3.ai.behaviours.actor;
using ai.behaviours;
```

Call `ConfigureClaimLandTask();` from `Init()` after `ConfigureClaimLandDecision();`, then add:

```csharp
private static void ConfigureClaimLandTask()
{
    BehaviourTaskActor task = AssetManager.tasks_actor.get(
        ClaimLandDecisionId);
    if (task?.list == null)
    {
        ModClass.LogWarning(
            "[Xia expansion] Missing actor task: " +
            ClaimLandDecisionId);
        return;
    }

    var actionTypeNames = new List<string>(task.list.Count);
    for (int i = 0; i < task.list.Count; i++)
        actionTypeNames.Add(task.list[i]?.GetType().Name ?? string.Empty);

    int insertionIndex = XiaExpansionDecisionRules
        .ClaimLandGuardInsertionIndex(actionTypeNames);
    if (insertionIndex ==
        XiaExpansionDecisionRules.ClaimLandGuardAlreadyInstalled)
        return;
    if (insertionIndex ==
        XiaExpansionDecisionRules.ClaimLandTaskIncompatible)
    {
        ModClass.LogWarning(
            "[Xia expansion] Incompatible claim_land task shape; " +
            "arrival guard not installed.");
        return;
    }

    var guard = new BehCivicLeaderClaimArrival();
    guard.id = guard.GetType().ToString();
    guard.id = guard.id.Replace("ai.behaviours.", string.Empty);
    guard.create();
    task.list.Insert(insertionIndex, guard);
}
```

This reproduces the initialization performed by native `BehaviourTaskBase.addBeh` before inserting into the middle of the public list.

- [ ] **Step 5: Run focused tests and build**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --xia-monkey-slice
dotnet build AncientWarfare3.csproj -c Release
```

Expected: focused tests pass and the build reports zero errors.

- [ ] **Step 6: Commit the pre-animation guard**

```powershell
git add Code/ai/behaviours/actor/BehCivicLeaderClaimArrival.cs Code/content/XiaExpansionDecisionContent.cs Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt
git commit -m "fix: guard civic claim animations on arrival"
```

### Task 4: Final Validation And Late-Redirect Removal

**Files:**
- Modify: `Code/patch/AW_XiaExpansionPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt`

- [ ] **Step 1: Add failing final-enforcement regression tests**

Replace the obsolete positive late-redirect assertion and add:

```csharp
Equal(false, expansionPatch.Contains("TrySetKingClaimBorderTarget"),
    "final claim execution never tries to restart completed movement");
Equal(true, expansionPatch.Contains(
        "CivicLeaderLandClaimService.IsValidArrival(pActor)"),
    "the final claim step rejects stale civic leader arrivals");
Equal(true, expansionPatch.Contains(
        "if (!LineageService.IsXiaKingdom(city.kingdom)) return true;"),
    "valid non-Xia civic claims return to native claim execution");
```

- [ ] **Step 2: Run the focused slice and verify it fails**

Run the focused `dotnet run` command.

Expected: failure because `TrySetKingClaimBorderTarget` is still present.

- [ ] **Step 3: Replace the late redirect with final fail-closed validation**

In `ClaimZoneWithinTechCap_Prefix`, remove the `actorIsKing` probe and the complete block that changes `beh_tile_target`. Immediately after the shared city-growth check, insert:

```csharp
if (CivicLeaderLandClaimService.IsCivicLeader(pActor) &&
    !CivicLeaderLandClaimService.IsValidArrival(pActor))
{
    __result = BehResult.Stop;
    return false;
}
```

Delete `IsKingAtClaimBorder`, `TrySetKingClaimBorderTarget`, and the patch-local `TouchesCityBoundary`. Keep `IsVanillaNeighbourClaimable`, the Xia zone-cap logic, bounded expansionist neighbor claiming, and loot behavior unchanged.

- [ ] **Step 4: Run the focused test, full rules suite, and production build**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --xia-monkey-slice
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release
```

Expected:

- Focused slice: `Xia expansion and civ monkey naming rules passed.`
- Full suite: `Rule tests passed.`
- Build: zero errors.

- [ ] **Step 5: Inspect the final diff for scope and ordering**

```powershell
git diff --check
git diff -- Code/core/policy/XiaExpansionDecisionRules.cs Code/core/policy/CivicLeaderLandClaimService.cs Code/ai/behaviours/actor/BehCivicLeaderClaimArrival.cs Code/content/XiaExpansionDecisionContent.cs Code/patch/AW_XiaExpansionPatch.cs Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt
```

Expected: no whitespace errors; the task order is selector, native movement, arrival guard, unchanged animation sequence, final claim.

- [ ] **Step 6: Commit final enforcement**

```powershell
git add Code/patch/AW_XiaExpansionPatch.cs Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt
git commit -m "fix: reject stale civic land claims"
```

### Task 5: Deploy And Runtime Verification

**Files:**
- Verify: `Code/core/policy/CivicLeaderLandClaimService.cs`
- Verify: `Code/ai/behaviours/actor/BehCivicLeaderClaimArrival.cs`
- Verify: `Code/content/XiaExpansionDecisionContent.cs`
- Verify: `Code/patch/AW_XiaExpansionPatch.cs`

- [ ] **Step 1: Deploy the source tree with a timestamped backup**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1
```

Expected: a backup path under `.aw3-deploy-backups` followed by `DEPLOY-DONE`.

- [ ] **Step 2: Verify deployed source hashes**

```powershell
$destination = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
$files = @(
    'Code\core\policy\CivicLeaderLandClaimService.cs',
    'Code\ai\behaviours\actor\BehCivicLeaderClaimArrival.cs',
    'Code\content\XiaExpansionDecisionContent.cs',
    'Code\patch\AW_XiaExpansionPatch.cs'
)
foreach ($file in $files) {
    $sourceHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $deployedHash = (Get-FileHash -LiteralPath (Join-Path $destination $file) -Algorithm SHA256).Hash
    if ($sourceHash -ne $deployedHash) { throw "Deploy mismatch: $file" }
}
```

Expected: command exits successfully with no mismatch exception.

- [ ] **Step 3: Verify game behavior with one king and one city leader**

In a running save where each actor can select `claim_land`, confirm these cases:

1. The actor selects a visibly unoccupied zone directly touching its own city border.
2. The actor walks there with the native movement task before raising the flag.
3. The actor does not animate while still standing inside its city.
4. Removing or occupying the target during travel cancels the task without a flag animation or zone mutation.
5. An ordinary resident continues to claim land with vanilla behavior.
6. Reinitializing or reloading does not add a second arrival guard or produce repeated movement/animation stages.

- [ ] **Step 4: Check the runtime log for installation or claim exceptions**

```powershell
$log = 'C:\Users\24908\AppData\LocalLow\mkarpenko\WorldBox\Player.log'
Select-String -LiteralPath $log -Pattern '\[Xia expansion\]|BehCivicLeaderClaimArrival|BehActorCheckZoneTarget|Exception' | Select-Object -Last 120
```

Expected: no `Incompatible claim_land task shape` warning and no exception from the selector, arrival guard, or final claim patch.
