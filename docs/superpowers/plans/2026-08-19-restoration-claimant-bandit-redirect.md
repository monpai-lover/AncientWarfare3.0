# Restoration Claimant Bandit Redirect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redirect a prospective ordinary bandit ruler with a dormant royal claim into an external-seed restoration campaign under the old kingdom identity.

**Architecture:** Extend the existing restoration rebellion entry with an explicit `ExternalBandit` seed mode. Keep the external base in `SEED_CITY_ID` while `CORE_CITY_IDS` remains the historical core set, so no new persistence schema is required. Put one bounded claim gate before ordinary bandit finalization, bypass it for an already-qualified Guiyi route, and reuse existing restoration identity, campaign, mobilization, rollback, war, and protection services.

**Tech Stack:** C#/.NET, Harmony-integrated WorldBox runtime, System.Data.SQLite, isolated .NET rule tests, PowerShell source guards.

---

### Task 1: External-Seed Rules And Isolated Tests

**Files:**
- Modify: `Code/core/lineage/RestorationRebellionRedirectRules.cs`
- Create: `Tests/RestorationBanditRedirect.Isolated.Tests/RestorationBanditRedirect.Isolated.Tests.csproj`
- Create: `Tests/RestorationBanditRedirect.Isolated.Tests/Program.cs`

- [ ] **Step 1: Write the failing isolated rule tests**

Create a test executable that links only `RestorationRebellionRedirectRules.cs` and verifies:

```csharp
using AncientWarfare3.core.lineage;

Equal(true, RestorationRebellionRedirectRules.CanUseRequiredSeed(
    RestorationRebellionSeedMode.ExternalBandit, true, false, false));
Equal(false, RestorationRebellionRedirectRules.CanUseRequiredSeed(
    RestorationRebellionSeedMode.Core, true, false, false));
Equal(true, RestorationRebellionRedirectRules.CanUseRequiredSeed(
    RestorationRebellionSeedMode.Core, true, true, false));
Equal(false, RestorationRebellionRedirectRules.ShouldCountSeedAsCore(
    RestorationRebellionSeedMode.ExternalBandit, true));
Equal(true, RestorationRebellionRedirectRules.ShouldCountSeedAsCore(
    RestorationRebellionSeedMode.Core, true));
Equal(false, RestorationRebellionRedirectRules.ShouldInspectBanditFounder(
    false, true, true));
Equal(-1, RestorationRebellionRedirectRules.CompareCoreTargets(
    25, 9, 36, 2));
Equal(-1, RestorationRebellionRedirectRules.CompareCoreTargets(
    25, 2, 25, 9));

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}");
}
```

Use this project definition:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Code/core/lineage/RestorationRebellionRedirectRules.cs"
             Link="Production/RestorationRebellionRedirectRules.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Run the test and verify it fails**

Run `dotnet run --project Tests\RestorationBanditRedirect.Isolated.Tests\RestorationBanditRedirect.Isolated.Tests.csproj`.

Expected: compilation fails because the external-seed types and methods do not exist.

- [ ] **Step 3: Add the minimal pure rules**

```csharp
public enum RestorationRebellionSeedMode
{
    Core = 0,
    ExternalBandit = 1
}

public static bool CanUseRequiredSeed(RestorationRebellionSeedMode mode,
    bool originalKingdomDead, bool isOriginalCapital,
    bool isPersistedCore)
{
    return originalKingdomDead &&
           (mode == RestorationRebellionSeedMode.ExternalBandit ||
            isOriginalCapital || isPersistedCore);
}

public static bool ShouldCountSeedAsCore(
    RestorationRebellionSeedMode mode, bool isPersistedCore)
{
    return mode == RestorationRebellionSeedMode.Core && isPersistedCore;
}

public static bool ShouldInspectBanditFounder(bool allowRedirect,
    bool actorValid, bool cityValid)
{
    return allowRedirect && actorValid && cityValid;
}

public static int CompareCoreTargets(int leftDistanceSquared,
    long leftCityId, int rightDistanceSquared, long rightCityId)
{
    int distance = leftDistanceSquared.CompareTo(rightDistanceSquared);
    return distance != 0 ? distance : leftCityId.CompareTo(rightCityId);
}
```

Keep `IsMatchingClaimCity` as a compatibility wrapper over `Core` mode.

- [ ] **Step 4: Run the isolated tests**

Expected output: `Restoration bandit redirect rules passed.`

- [ ] **Step 5: Commit the rules slice**

```powershell
git add -- Code/core/lineage/RestorationRebellionRedirectRules.cs Tests/RestorationBanditRedirect.Isolated.Tests
git commit -m "test: define external restoration seed rules"
```

### Task 2: External-Bandit Restoration Launch

**Files:**
- Modify: `Code/core/lineage/AutonomousRestorationService.cs`
- Modify: `Code/core/lineage/RestorationRebellionRedirectService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`

- [ ] **Step 1: Add the explicit external entry**

Refactor the existing rebellion entry through a required-seed helper and add:

```csharp
internal static RestorationRebellionStartOutcome
    TryStartSelfRestorationFromExternalBandit(long claimId,
        Actor claimant, City externalSeed, out Kingdom restored,
        out string error)
{
    return TryStartSelfRestorationFromRequiredSeed(claimId, claimant,
        externalSeed, RestorationRebellionSeedMode.ExternalBandit,
        out restored, out error);
}
```

The existing `TryStartSelfRestorationFromRebellion` passes `Core`; ordinary autonomous starts keep their current no-required-seed path.

- [ ] **Step 2: Make seed validation mode-aware**

Read historical core IDs before validating the required seed. Use `CanUseRequiredSeed` instead of weakening `IsMatchingClaimCity`. Thread a `seedAllowed` boolean through `FindSeedSelection`, `RevalidateSeedSelection`, and post-creation seed validation so an external seed is valid without being mislabeled as an old core.

- [ ] **Step 3: Preserve the historical core set**

Use this split before `BeginSelfCampaign`:

```csharp
bool seedIsCore = allCoreIds.Contains(seed.data.id);
allCoreIds = FilterLivingCoreIds(allCoreIds);
if (allCoreIds.Count == 0)
{
    pError = "restoration_no_living_core";
    return false;
}
if (seedMode == RestorationRebellionSeedMode.ExternalBandit)
    SortCoreIdsByDistance(allCoreIds, seed);
else if (!allCoreIds.Contains(seed.data.id))
    allCoreIds.Add(seed.data.id);
int controlledCoreCount =
    RestorationRebellionRedirectRules.ShouldCountSeedAsCore(
        seedMode, seedIsCore) ? 1 : 0;
```

Pass `controlledCoreCount` to `BeginSelfCampaign`. Never append an external seed to `CORE_CITY_IDS`; it remains persisted as `SEED_CITY_ID`.

- [ ] **Step 4: Select the nearest old core deterministically**

Sort at most `RestorationCampaignRules.MaxPersistedCoreIds` live core IDs by squared tile distance from the external seed, then city ID, using `CompareCoreTargets`. Existing `TryStartNextCoreWar` will consume this order.

- [ ] **Step 5: Add the bounded best-claim redirect**

Add `TryRedirectBanditFounder` to `RestorationRebellionRedirectService`. It must call `RoyalClaimService.FindBestDormantClaimIdForActor` exactly once, invoke `TryStartSelfRestorationFromExternalBandit`, return the restored kingdom, and preserve `NotStarted`, `Started`, and `ConsumedAfterCommit`. `NotStarted` permits ordinary bandit fallback; both committed outcomes suppress it.

- [ ] **Step 6: Persist and retry post-commit initialization**

Add `LineageKeys.RESTORATION_INITIALIZATION_PENDING`. For external-bandit
launches only, once `RestoreFromCity` succeeds, a transient initial-cohort or
first-war initialization failure sets this flag and returns
`ConsumedAfterCommit`; it does not call `RollbackProvisionalRestoration` and
never resumes bandit creation. `MaintainCampaign` checks the flag before
normal campaign advancement, re-resolves the persisted `seedCityId`, boundedly
collects and revalidates at most `MaxSeedResidentsInspected` supporters,
retries `TryStartWithInitialCohort`, and clears the flag once initialization
succeeds. An absent/dead seed, wrong restored owner, or dead claimant uses the
existing atomic rollback/failure path rather than remaining pending forever.
Repeated yearly calls must not duplicate armies, wars, history, or protection.

- [ ] **Step 7: Run tests and compile**

```powershell
dotnet run --project Tests\RestorationBanditRedirect.Isolated.Tests\RestorationBanditRedirect.Isolated.Tests.csproj
dotnet build AncientWarfare3.csproj
```

Expected: isolated tests pass and the production build has 0 errors.

- [ ] **Step 8: Commit the launch slice**

```powershell
git add -- Code/core/lineage/AutonomousRestorationService.cs Code/core/lineage/RestorationRebellionRedirectService.cs Code/core/lineage/RestorationRebellionRedirectRules.cs Code/core/lineage/LineageKeys.cs
git commit -m "feat: launch restoration from external bandit bases"
```

### Task 3: Ordinary Bandit Entry Integration And Guiyi Exemption

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs`
- Modify: `Code/core/lineage/MandateRebelService.cs`
- Modify: `Code/core/lineage/PeasantRebelGuiyiService.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Create: `Tests/RestorationBanditRedirectSourceGuard.ps1`

- [ ] **Step 1: Write a failing entry-order source guard**

The guard reads the six production files and asserts that `TryRedirectBanditFounder` occurs before `makeNewCivKingdom`, Guiyi calls the direct creator with `pAllowClaimRedirect: false`, route selection checks the redirect before `TryEnterBandit`, Mandate history uses `effectiveRebel`, and the divine power branches on `restorationRedirected`.

- [ ] **Step 2: Run the guard and verify it fails**

Run `powershell -ExecutionPolicy Bypass -File Tests\RestorationBanditRedirectSourceGuard.ps1`.

Expected: it reports the missing direct and route redirect wiring.

- [ ] **Step 3: Gate direct creation before all bandit writes**

Add this overload while preserving the existing wrapper:

```csharp
internal static bool TryCreateDirect(City mother,
    out Kingdom createdKingdom, out City createdBase,
    out string failureKey, out bool restorationRedirected,
    bool pAllowClaimRedirect = true)
```

After ruler selection and before `TryPlan`, `makeNewCivKingdom`, outlaw naming, route metadata, or stronghold state, call `TryRedirectBanditFounder`. For `Started` or `ConsumedAfterCommit`, mark the event consumed and never continue into bandit creation. For `NotStarted`, preserve the old path byte-for-byte apart from the wrapper.

- [ ] **Step 4: Keep Guiyi on its existing subtype path**

Change `PeasantRebelGuiyiService` to call the overload with `pAllowClaimRedirect: false`; its foreign-occupation restoration state remains unchanged.

- [ ] **Step 5: Handle provisional peasant-rebel bandit selection**

When `InitializeAndEnter` selects `Bandit`, call the same redirect before `TryEnterBandit` and return an `effectiveRebel` to `MandateRebelService.CreateRebelKingdom`. A successful redirect uses the restored kingdom for history and return values and writes no bandit/founding route metadata. Remove an empty provisional rebel shell only after the seed city and claimant are confirmed under the restored kingdom. `ConsumedAfterCommit` suppresses both bandit finalization and founding fallback.

- [ ] **Step 6: Correct the divine-power message**

Use the overload in `GodPowerLibrary`. A redirect displays `aw_restoration_started` when the restored kingdom exists and the generic restoration pending/failure message when initialization was consumed; it never reports a bandit stronghold success.

- [ ] **Step 7: Run guards, tests, and build**

```powershell
powershell -ExecutionPolicy Bypass -File Tests\RestorationBanditRedirectSourceGuard.ps1
dotnet run --project Tests\RestorationBanditRedirect.Isolated.Tests\RestorationBanditRedirect.Isolated.Tests.csproj
dotnet build AncientWarfare3.csproj
```

Expected: guard and isolated tests pass; build has 0 errors.

- [ ] **Step 8: Commit entry integration**

```powershell
git add -- Code/core/lineage/PeasantRebelBanditStrongholdService.cs Code/core/lineage/PeasantRebelRouteService.cs Code/core/lineage/MandateRebelService.cs Code/core/lineage/PeasantRebelGuiyiService.cs Code/content/GodPowerLibrary.cs Tests/RestorationBanditRedirectSourceGuard.ps1
git commit -m "feat: redirect claimant bandits into restoration"
```

### Task 4: Regression Verification

**Files:**
- Modify only feature-scoped files if a verification failure exposes a defect.

- [ ] **Step 1: Run focused restoration and bandit guards**

```powershell
dotnet run --project Tests\RestorationBanditRedirect.Isolated.Tests\RestorationBanditRedirect.Isolated.Tests.csproj
powershell -ExecutionPolicy Bypass -File Tests\RestorationBanditRedirectSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\BanditStrongholdTransactionSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\BanditStrongholdRouteSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\PeasantRebelRouteRuntimeSourceGuard.ps1
```

Expected: every command passes.

- [ ] **Step 2: Run production compilation and whitespace checks**

```powershell
dotnet build AncientWarfare3.csproj
git diff --check
```

Expected: build has 0 errors and `git diff --check` emits no errors.

- [ ] **Step 3: Review the final diff against the design**

Confirm that external bases are absent from `CORE_CITY_IDS`, their controlled core count starts at zero, claim lookup is actor-bounded, no-claim bandits keep the old path, Guiyi bypasses the gate, protection still comes from `RestoreFromCity`, and no per-frame or world-wide scan was added.

- [ ] **Step 4: Record the broad-suite limitation accurately**

Run the broad rules project only if its known `CourtOfficerTableItem` test-stub conflict has been fixed. Otherwise report that existing harness blocker without changing production code.
