# Restoration Rebellion Redirect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redirect an eligible vanilla uprising into AW3's existing restoration campaign so Ji Yi restores kingdom ID 1 as Zhou instead of founding a new random kingdom named North Guo.

**Architecture:** Add a small pure routing contract, expose ordered dormant claims for a rebel actor, and extend `AutonomousRestorationService` with an exact-seed entry that reuses its existing transaction, identity restoration, mobilization, rollback, history, and war flow. The two vanilla rebellion prefixes call one redirect service and suppress vanilla only after restoration identity creation has committed, leaving all ineligible ordinary rebellions unchanged.

**Tech Stack:** C# 11, .NET Framework 4.8 mod assembly, Harmony prefixes/postfixes, System.Data.SQLite, standalone .NET 9 slice tests, WorldBox publicized game assemblies.

---

## File Map

- Create `Code/core/lineage/RestorationRebellionRedirectRules.cs`: pure eligibility and outcome rules shared by tests and Harmony integration.
- Create `Code/core/lineage/RestorationRebellionRedirectService.cs`: event-level actor/city claim lookup and redirect orchestration.
- Modify `Code/core/lineage/RoyalRestorationRules.cs`: distinguish a rebellion-triggered campaign from autonomous scheduling so a real uprising is not blocked by the AI strength threshold.
- Modify `Code/core/lineage/RoyalClaimService.cs`: return all dormant claims for one actor in deterministic priority order.
- Modify `Code/core/lineage/AutonomousRestorationService.cs`: accept an exact rebellion city, revalidate it as a persisted core/capital, and report whether identity creation committed.
- Modify `Code/patch/AW_ChroniclePatch.cs`: redirect `startRebellion` and `useInspire` before vanilla creates a random kingdom and suppress generic rebellion history after a redirect.
- Create `Tests/RestorationRebellionRedirectSlice/RestorationRebellionRedirectSlice.csproj`: focused test harness project.
- Create `Tests/RestorationRebellionRedirectSlice/Program.cs`: regression tests for routing, strength bypass, and fallback/consume behavior.

### Task 1: Specify the pure redirect contract

**Files:**
- Create: `Tests/RestorationRebellionRedirectSlice/RestorationRebellionRedirectSlice.csproj`
- Create: `Tests/RestorationRebellionRedirectSlice/Program.cs`
- Create after RED: `Code/core/lineage/RestorationRebellionRedirectRules.cs`
- Modify after RED: `Code/core/lineage/RoyalRestorationRules.cs`

- [ ] **Step 1: Write the failing slice project**

Create the project with production files linked directly:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\..\Code\core\lineage\RestorationRebellionRedirectRules.cs" Link="Production\RestorationRebellionRedirectRules.cs" />
    <Compile Include="..\..\Code\core\lineage\RoyalRestorationRules.cs" Link="Production\RoyalRestorationRules.cs" />
  </ItemGroup>
</Project>
```

Create `Program.cs` with these assertions:

```csharp
using AncientWarfare3.core.lineage;

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, got {actual}");
}

static void True(bool value, string message) => Equal(true, value, message);

True(RestorationRebellionRedirectRules.ShouldInspect(
        restorationCreationActive: false, actorValid: true, cityValid: true),
    "a valid ordinary uprising may inspect restoration claims");
Equal(false, RestorationRebellionRedirectRules.ShouldInspect(
        restorationCreationActive: true, actorValid: true, cityValid: true),
    "identity restoration cannot recursively redirect itself");
True(RestorationRebellionRedirectRules.IsMatchingClaimCity(
        originalKingdomDead: true, isOriginalCapital: true,
        isPersistedCore: false),
    "the original capital is a valid restoration seed identity");
True(RestorationRebellionRedirectRules.IsMatchingClaimCity(
        originalKingdomDead: true, isOriginalCapital: false,
        isPersistedCore: true),
    "a persisted core is a valid restoration seed identity");
Equal(false, RestorationRebellionRedirectRules.IsMatchingClaimCity(
        originalKingdomDead: false, isOriginalCapital: true,
        isPersistedCore: true),
    "a live original kingdom cannot be restored again");
Equal(false, RestorationRebellionRedirectRules.IsPeacefulHostCity(
        ownerIsClaimantHost: true, rebellionTriggered: true),
    "an actively rebelling host city is not a peaceful host city");
True(RestorationRebellionRedirectRules.IsPeacefulHostCity(
        ownerIsClaimantHost: true, rebellionTriggered: false),
    "scheduled restoration retains the peaceful host exclusion");

Equal(RestorationRebellionStartOutcome.Started,
    RestorationRebellionRedirectRules.ResolveOutcome(
        started: true, identityCreationCommitted: true),
    "a completed start suppresses vanilla");
Equal(RestorationRebellionStartOutcome.ConsumedAfterCommit,
    RestorationRebellionRedirectRules.ResolveOutcome(
        started: false, identityCreationCommitted: true),
    "post-commit failure relies on rollback and suppresses vanilla");
Equal(RestorationRebellionStartOutcome.NotStarted,
    RestorationRebellionRedirectRules.ResolveOutcome(
        started: false, identityCreationCommitted: false),
    "pre-commit failure falls back to vanilla");
True(RestorationRebellionRedirectRules.ShouldSuppressVanilla(
        RestorationRebellionStartOutcome.Started),
    "successful restoration consumes the uprising event");
True(RestorationRebellionRedirectRules.ShouldSuppressVanilla(
        RestorationRebellionStartOutcome.ConsumedAfterCommit),
    "post-commit rollback state consumes the uprising event");
Equal(false, RestorationRebellionRedirectRules.ShouldSuppressVanilla(
        RestorationRebellionStartOutcome.NotStarted),
    "ineligible restoration leaves vanilla rebellion intact");

True(RoyalRestorationRules.CanStartAutonomousCampaign(
        mandateExists: false, chaosPhase: true,
        playerRequested: false, claimStrength: 70,
        claimantValid: true, oldKingdomDead: true,
        hasEligibleSeed: true, cooldownReady: true,
        rebellionTriggered: true),
    "a rebellion already launched by game AI bypasses the autonomous 85 strength threshold");
Equal(false, RoyalRestorationRules.CanStartAutonomousCampaign(
        mandateExists: false, chaosPhase: true,
        playerRequested: false, claimStrength: 70,
        claimantValid: true, oldKingdomDead: true,
        hasEligibleSeed: true, cooldownReady: true,
        rebellionTriggered: false),
    "the annual autonomous scheduler still requires 85 strength");

Console.WriteLine("RestorationRebellionRedirectSlice PASS");
```

- [ ] **Step 2: Run RED and confirm the missing contract is the failure**

Run:

```powershell
dotnet run --project Tests/RestorationRebellionRedirectSlice/RestorationRebellionRedirectSlice.csproj
```

Expected: compilation fails because `RestorationRebellionRedirectRules`, `RestorationRebellionStartOutcome`, and the `rebellionTriggered` argument do not exist.

- [ ] **Step 3: Add the minimal pure contract**

Create `RestorationRebellionRedirectRules.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    public enum RestorationRebellionStartOutcome
    {
        NotStarted = 0,
        Started = 1,
        ConsumedAfterCommit = 2
    }

    public static class RestorationRebellionRedirectRules
    {
        public static bool ShouldInspect(bool restorationCreationActive,
            bool actorValid, bool cityValid)
        {
            return !restorationCreationActive && actorValid && cityValid;
        }

        public static bool IsMatchingClaimCity(bool originalKingdomDead,
            bool isOriginalCapital, bool isPersistedCore)
        {
            return originalKingdomDead &&
                   (isOriginalCapital || isPersistedCore);
        }

        public static bool IsPeacefulHostCity(bool ownerIsClaimantHost,
            bool rebellionTriggered)
        {
            return ownerIsClaimantHost && !rebellionTriggered;
        }

        public static RestorationRebellionStartOutcome ResolveOutcome(
            bool started, bool identityCreationCommitted)
        {
            if (started) return RestorationRebellionStartOutcome.Started;
            return identityCreationCommitted
                ? RestorationRebellionStartOutcome.ConsumedAfterCommit
                : RestorationRebellionStartOutcome.NotStarted;
        }

        public static bool ShouldSuppressVanilla(
            RestorationRebellionStartOutcome outcome)
        {
            return outcome != RestorationRebellionStartOutcome.NotStarted;
        }
    }
}
```

Add an optional final parameter to `RoyalRestorationRules.CanStartAutonomousCampaign`:

```csharp
public static bool CanStartAutonomousCampaign(bool mandateExists,
    bool chaosPhase, bool playerRequested, int claimStrength,
    bool claimantValid, bool oldKingdomDead, bool hasEligibleSeed,
    bool cooldownReady, bool rebellionTriggered = false)
{
    return !mandateExists && chaosPhase && claimantValid && oldKingdomDead &&
           hasEligibleSeed && cooldownReady &&
           (playerRequested || rebellionTriggered ||
            claimStrength >= AiMinimumClaimStrength);
}
```

- [ ] **Step 4: Run GREEN and the existing restoration guard slice**

Run:

```powershell
dotnet run --project Tests/RestorationRebellionRedirectSlice/RestorationRebellionRedirectSlice.csproj
dotnet run --project Tests/RoyalRestorationGuardSlice/RoyalRestorationGuardSlice.csproj
```

Expected: both print `PASS`.

- [ ] **Step 5: Commit the pure contract**

```powershell
git add Code/core/lineage/RestorationRebellionRedirectRules.cs Code/core/lineage/RoyalRestorationRules.cs Tests/RestorationRebellionRedirectSlice
git commit -m "test: specify restoration rebellion redirect"
```

### Task 2: Add deterministic claim lookup and exact-city restoration

**Files:**
- Modify: `Code/core/lineage/RoyalClaimService.cs`
- Modify: `Code/core/lineage/AutonomousRestorationService.cs`
- Modify: `Tests/RestorationRebellionRedirectSlice/Program.cs`

- [ ] **Step 1: Add a failing regression for claim priority**

Append to `Program.cs`:

```csharp
Equal(-1, RestorationRebellionRedirectRules.CompareClaimPriority(
        leftStrength: 90, leftClaimId: 12,
        rightStrength: 70, rightClaimId: 3),
    "stronger dormant claims sort first");
Equal(-1, RestorationRebellionRedirectRules.CompareClaimPriority(
        leftStrength: 70, leftClaimId: 3,
        rightStrength: 70, rightClaimId: 12),
    "equal-strength claims use the oldest claim id");
```

- [ ] **Step 2: Run RED**

Run the redirect slice. Expected: compilation fails because `CompareClaimPriority` does not exist.

- [ ] **Step 3: Implement deterministic priority and actor claim query**

Add to the rules:

```csharp
public static int CompareClaimPriority(int leftStrength, long leftClaimId,
    int rightStrength, long rightClaimId)
{
    int strength = rightStrength.CompareTo(leftStrength);
    return strength != 0 ? strength : leftClaimId.CompareTo(rightClaimId);
}
```

Add to `RoyalClaimService`:

```csharp
internal static List<ClaimRow> GetDormantClaimsForActor(
    long pActorId, int pLimit)
{
    var result = new List<ClaimRow>();
    if (!Ready || pActorId < 0 || pLimit <= 0) return result;
    try
    {
        using var cmd = new SQLiteCommand(DB);
        cmd.CommandText = FullClaimSelect() +
            " WHERE CLAIMANT_ACTOR_ID=@a AND ACTIVE=1 " +
            "AND RESTORATION_STATE='dormant' " +
            "AND IFNULL(RESTORE_MODE,'')='' " +
            "ORDER BY CLAIM_STRENGTH DESC, CLAIM_ID ASC LIMIT @lim";
        cmd.Parameters.AddWithValue("@a", pActorId);
        cmd.Parameters.AddWithValue("@lim", pLimit);
        using var reader = (SQLiteDataReader)cmd.ExecuteReader();
        while (reader.Read()) result.Add(ReadFullClaimRow(reader));
    }
    catch (Exception e)
    {
        ModClass.LogWarning(
            "Restoration rebellion claim read failed: " + e.Message);
    }
    return result;
}
```

- [ ] **Step 4: Extend the existing restoration core without duplicating it**

Change the private core signature to:

```csharp
private static bool TryStartSelfRestorationCore(long pClaimId,
    bool pPlayerRequested, bool pRebellionTriggered,
    City pRequiredSeed, out bool pIdentityCreationCommitted,
    out string pError)
```

The existing public entry passes `false, null, out _, out pError`. Add this event entry:

```csharp
internal static RestorationRebellionStartOutcome
    TryStartSelfRestorationFromRebellion(long pClaimId,
        Actor pClaimant, City pRequiredSeed, out string pError)
{
    pError = "";
    if (pClaimant?.data == null || pRequiredSeed?.data == null ||
        pClaimant.data.id < 0 || pClaimId < 0)
        return RestorationRebellionStartOutcome.NotStarted;
    bool started = TryStartSelfRestorationCore(
        pClaimId, pPlayerRequested: false,
        pRebellionTriggered: true, pRequiredSeed,
        out bool committed, out pError);
    return RestorationRebellionRedirectRules.ResolveOutcome(
        started, committed);
}
```

Inside the core:

- initialize `pIdentityCreationCommitted = false` before validation;
- use `pPlayerRequested || pRebellionTriggered` for the one-year retry cooldown;
- pass `pRebellionTriggered` into `CanStartAutonomousCampaign`;
- avoid scheduler attempt markers when `pPlayerRequested || pRebellionTriggered`;
- read all persisted cores when an exact seed is supplied;
- reject the exact seed before campaign insertion unless it is the original capital or appears in those persisted cores;
- call `FindSeedSelection` with a singleton list containing only `pRequiredSeed.id`;
- extend `FindSeedSelection` and `RevalidateSeedSelection` with a `pRebellionTriggered` argument, using `RestorationRebellionRedirectRules.IsPeacefulHostCity(owner == claimant.kingdom, pRebellionTriggered)` so only the active uprising may use its current host city;
- revalidate reference equality with `seed == pRequiredSeed` before `BeginSelfCampaign`;
- set `pIdentityCreationCommitted = true` immediately after `RestoreFromCity` returns the live original kingdom;
- leave it true if mobilization fails and rollback begins, so Harmony never creates a second random kingdom over partial restoration state.

Use the existing core list and seed functions rather than duplicating validation:

```csharp
List<long> allCoreIds = pRequiredSeed == null
    ? null
    : ReadOldCoreIds(claim,
        RestorationCampaignRules.MaxPersistedCoreIds);
bool exactSeedMatches = pRequiredSeed == null ||
    RestorationRebellionRedirectRules.IsMatchingClaimCity(
        oldKingdomDead,
        pRequiredSeed.id == claim.originalCapitalCityId,
        allCoreIds.Contains(pRequiredSeed.id));
if (!exactSeedMatches)
{
    pError = "restoration_rebellion_city_not_core";
    return false;
}

List<long> seedCandidateIds = pRequiredSeed == null
    ? ReadOldCoreIds(claim, RoyalRestorationRules.MaxCoreCandidates)
    : new List<long> { pRequiredSeed.id };
SeedSelection seedSelection = FindSeedSelection(
    claimant, seedCandidateIds, claim.originalCapitalCityId,
    pRebellionTriggered);
if (pRequiredSeed != null && seedSelection?.City != pRequiredSeed)
{
    pError = "restoration_rebellion_seed_invalid";
    return false;
}
```

Update both seed helpers without changing their scheduled-restoration defaults:

```csharp
private static SeedSelection FindSeedSelection(Actor pClaimant,
    List<long> pCoreIds, long pOriginalCapitalCityId,
    bool pRebellionTriggered = false)
{
    // Keep the existing loop and scoring. Replace only peacefulHostCity with:
    bool peacefulHostCity =
        RestorationRebellionRedirectRules.IsPeacefulHostCity(
            ownerValid && owner == peacefulHost,
            pRebellionTriggered);
    // Pass the same flag to revalidation at the existing call site.
}

private static bool RevalidateSeedSelection(SeedSelection pSelection,
    Actor pClaimant, bool pRebellionTriggered = false)
{
    bool peacefulHostCity =
        RestorationRebellionRedirectRules.IsPeacefulHostCity(
            pSelection?.Owner == pClaimant?.kingdom,
            pRebellionTriggered);
    if (peacefulHostCity) return false;
    // Preserve every existing identity, owner, occupation, population,
    // supporter, and defender check.
}
```

- [ ] **Step 5: Run focused tests and compile the full mod**

Run:

```powershell
dotnet run --project Tests/RestorationRebellionRedirectSlice/RestorationRebellionRedirectSlice.csproj
dotnet run --project Tests/RoyalRestorationGuardSlice/RoyalRestorationGuardSlice.csproj
dotnet build AncientWarfare3.csproj -c Debug --no-restore
```

Expected: both slices print `PASS`; the mod builds with zero errors.

- [ ] **Step 6: Commit exact-city restoration**

```powershell
git add Code/core/lineage/RestorationRebellionRedirectRules.cs Code/core/lineage/RoyalRestorationRules.cs Code/core/lineage/RoyalClaimService.cs Code/core/lineage/AutonomousRestorationService.cs Tests/RestorationRebellionRedirectSlice
git commit -m "feat: start restoration from rebellion city"
```

### Task 3: Redirect both vanilla rebellion entry points

**Files:**
- Create: `Code/core/lineage/RestorationRebellionRedirectService.cs`
- Modify: `Code/patch/AW_ChroniclePatch.cs`

- [ ] **Step 1: Add the shared event orchestrator**

Create:

```csharp
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class RestorationRebellionRedirectService
    {
        private const int MaxClaimsInspected = 8;

        public static RestorationRebellionStartOutcome TryRedirect(
            Actor pActor, City pCity, out string pError)
        {
            pError = "";
            if (!RestorationRebellionRedirectRules.ShouldInspect(
                    KingdomIdentityContinuityService.IsCreatingRestoration,
                    pActor?.data != null && !pActor.isRekt(),
                    pCity?.data != null && !pCity.isRekt()))
                return RestorationRebellionStartOutcome.NotStarted;
            try
            {
                List<RoyalClaimService.ClaimRow> claims =
                    RoyalClaimService.GetDormantClaimsForActor(
                        pActor.data.id, MaxClaimsInspected);
                foreach (RoyalClaimService.ClaimRow claim in claims)
                {
                    RestorationRebellionStartOutcome outcome =
                        AutonomousRestorationService
                            .TryStartSelfRestorationFromRebellion(
                                claim.claimId, pActor, pCity, out pError);
                    if (RestorationRebellionRedirectRules
                        .ShouldSuppressVanilla(outcome))
                        return outcome;
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Restoration rebellion redirect failed: " + e.Message);
                pError = "restoration_rebellion_redirect_error";
            }
            return RestorationRebellionStartOutcome.NotStarted;
        }
    }
}
```

- [ ] **Step 2: Replace the rebellion history state with redirect-aware state**

In `AW_ChroniclePatch`, add:

```csharp
private readonly struct RebellionPatchState
{
    public RebellionPatchState(Kingdom pOriginalKingdom,
        bool pRestorationRedirected)
    {
        OriginalKingdom = pOriginalKingdom;
        RestorationRedirected = pRestorationRedirected;
    }

    public Kingdom OriginalKingdom { get; }
    public bool RestorationRedirected { get; }
}
```

Replace the `startRebellion` prefix with a priority-first boolean prefix:

```csharp
[HarmonyPrefix]
[HarmonyPriority(Priority.First)]
[HarmonyPatch(typeof(DiplomacyHelpersRebellion),
    nameof(DiplomacyHelpersRebellion.startRebellion))]
public static bool VanillaRebellion_Prefix(Actor pActor,
    out RebellionPatchState __state)
{
    Kingdom original = pActor?.kingdom;
    if (AW3MultiplayerReplicaScope.IsApplying)
    {
        __state = new RebellionPatchState(original, false);
        return true;
    }
    RestorationRebellionStartOutcome outcome =
        RestorationRebellionRedirectService.TryRedirect(
            pActor, pActor?.city, out _);
    bool redirected = RestorationRebellionRedirectRules
        .ShouldSuppressVanilla(outcome);
    __state = new RebellionPatchState(original, redirected);
    return !redirected;
}
```

The postfix reads `RebellionPatchState`, returns immediately when redirected, and otherwise records the existing generic history against `OriginalKingdom`.

- [ ] **Step 3: Apply the same behavior to inspired rebellions**

Replace the `City.useInspire` prefix with the same priority-first shape, but pass `__instance` as the exact city. Its postfix also suppresses generic rebellion history when `RestorationRedirected` is true.

```csharp
RestorationRebellionStartOutcome outcome =
    RestorationRebellionRedirectService.TryRedirect(
        pActor, __instance, out _);
```

- [ ] **Step 4: Verify no lower-level kingdom creation was intercepted**

Run:

```powershell
rg -n "TryRedirect|TryStartSelfRestorationFromRebellion" Code
```

Expected: callers are limited to the new redirect service and the two rebellion prefixes. `AW_KingdomPolicyPatch.MakeOwnKingdom_Prefix` remains unchanged.

- [ ] **Step 5: Run focused tests and Debug/Release builds**

```powershell
dotnet run --project Tests/RestorationRebellionRedirectSlice/RestorationRebellionRedirectSlice.csproj
dotnet run --project Tests/RoyalRestorationGuardSlice/RoyalRestorationGuardSlice.csproj
dotnet build AncientWarfare3.csproj -c Debug --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
git diff --check -- Code/core/lineage/RestorationRebellionRedirectRules.cs Code/core/lineage/RestorationRebellionRedirectService.cs Code/core/lineage/RoyalRestorationRules.cs Code/core/lineage/RoyalClaimService.cs Code/core/lineage/AutonomousRestorationService.cs Code/patch/AW_ChroniclePatch.cs Tests/RestorationRebellionRedirectSlice
```

Expected: both slices print `PASS`, both builds have zero errors, and diff check is empty.

- [ ] **Step 6: Commit Harmony integration**

```powershell
git add Code/core/lineage/RestorationRebellionRedirectService.cs Code/patch/AW_ChroniclePatch.cs
git commit -m "fix: restore claimed kingdom during rebellion"
```

### Task 4: Deploy and verify the reproduced autosave

**Files:**
- Deploy source: the six modified or created production files from Tasks 1-3
- Deploy target: matching paths under `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`
- Runtime evidence: `C:/Users/24908/AppData/LocalLow/mkarpenko/WorldBox/Player.log`
- Save evidence: `C:/Users/24908/AppData/LocalLow/mkarpenko/WorldBox/autosaves/1785142461/aw3_lineage_archive.db`

- [ ] **Step 1: Confirm only this task's files will be deployed**

Use `git diff --name-only 9c31a1c..HEAD` and verify the list contains only the redirect rules/service, restoration rules/service, claim service, chronicle patch, and focused test files. Do not delete or overwrite the installed `.runtime` directory or any save database.

- [ ] **Step 2: Copy only production sources to the installed mod**

Copy these matching relative paths:

```text
Code/core/lineage/RestorationRebellionRedirectRules.cs
Code/core/lineage/RestorationRebellionRedirectService.cs
Code/core/lineage/RoyalRestorationRules.cs
Code/core/lineage/RoyalClaimService.cs
Code/core/lineage/AutonomousRestorationService.cs
Code/patch/AW_ChroniclePatch.cs
```

- [ ] **Step 3: Compare deployed hashes**

For each copied file, run `Get-FileHash` on source and target. Expected: every source hash equals its target hash.

- [ ] **Step 4: Load the reproduced autosave and trigger Ji Yi's uprising**

Before triggering, confirm actor 1003 has claim 20 for extinct kingdom 1 and the current rebellion city is city 1. After triggering, verify in game and archive DB:

```sql
SELECT CLAIM_ID, ACTIVE, RESTORE_MODE, RESTORATION_STATE,
       RESTORED_KINGDOM_ID
FROM RoyalClaim WHERE CLAIM_ID=20;

SELECT CAMPAIGN_ID, CLAIM_ID, ORIGINAL_KINGDOM_ID,
       CLAIMANT_ACTOR_ID, SEED_CITY_ID, STATE
FROM RestorationCampaign WHERE CLAIM_ID=20
ORDER BY CAMPAIGN_ID DESC LIMIT 1;

SELECT KINGDOM_ID, KINGDOM_NAME, IS_ALIVE
FROM KingdomArchive WHERE KINGDOM_ID IN (1, 9)
ORDER BY KINGDOM_ID;
```

Expected:

- claim 20 is no longer dormant and points to restored kingdom 1;
- one restoration campaign exists for claimant 1003 and seed city 1;
- kingdom 1 is alive and named Zhou;
- no new North Guo identity is created by this uprising;
- `Player.log` contains no nested restoration, original-ID lease, duplicate kingdom, or rollback-pending error.

- [ ] **Step 5: Verify an ordinary rebellion still works**

Trigger or observe one rebel actor without a dormant restoration claim. Expected: vanilla creates a new kingdom normally and AW3 records the generic rebellion history once.
