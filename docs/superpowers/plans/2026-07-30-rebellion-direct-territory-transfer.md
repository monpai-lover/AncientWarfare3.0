# Rebellion Direct Territory Transfer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every active war whose authoritative `WarTypeAsset.rebellion` flag is true transfer captured cities immediately to the actual capturing kingdom in both directions, while disabling ordinary peace settlements for that war.

**Architecture:** A pure policy class owns fail-closed decisions and the stable settlement rejection reason. A bounded resolver inspects only the proposed capturer's active wars and branches the Harmony capture patch before vassal redirection and frozen occupation; the matched war ID travels through Harmony state for scoped cleanup. The settlement world rejects these wars authoritatively, while UI and automatic settlement services add secondary guards.

**Tech Stack:** C#/.NET 9 rule tests, Harmony, WorldBox runtime APIs, SQLite-backed AW3 war score and peace services, PowerShell source guards.

**Workspace Note:** Execute in the current shared `master` worktree. A clean worktree would omit required untracked runtime sources, so every commit must stage only the files named by its task.

---

### Task 1: Add the Pure Direct-Transfer Policy

**Files:**
- Create: `Code/core/lineage/RebellionDirectTerritoryTransferRules.cs`
- Create: `Tests/RebellionDirectTerritoryTransferRulesSlice/RebellionDirectTerritoryTransferRulesSlice.csproj`
- Create: `Tests/RebellionDirectTerritoryTransferRulesSlice/Program.cs`

- [ ] **Step 1: Write the failing policy tests**

Create an isolated test executable covering rebel capture, old-regime recapture, normal-war fallback, same-side rejection, invalid facts, and ordinary-settlement blocking:

```csharp
using AncientWarfare3.core.lineage;

internal static class RebellionDirectTerritoryTransferRulesTests
{
    public static void Run()
    {
        True(RebellionDirectTerritoryTransferRules.ShouldTransfer(
            true, true, true, false, true, true, true),
            "an exact active rebellion transfers directly");
        False(RebellionDirectTerritoryTransferRules.ShouldTransfer(
            true, true, true, false, true, true, false),
            "an ordinary war keeps frozen occupation");
        False(RebellionDirectTerritoryTransferRules.ShouldTransfer(
            true, true, true, false, true, false, true),
            "same-side participants cannot authorize transfer");
        False(RebellionDirectTerritoryTransferRules.ShouldTransfer(
            true, true, true, true, true, true, true),
            "an owner cannot capture its own city");
        True(RebellionDirectTerritoryTransferRules.BlocksOrdinarySettlement(
            true, true, true),
            "an active authoritative rebellion blocks ordinary peace");
        False(RebellionDirectTerritoryTransferRules.BlocksOrdinarySettlement(
            true, true, false),
            "an ordinary active war remains negotiable");
        Equal("rebellion_uses_direct_territory_transfer",
            RebellionDirectTerritoryTransferRules.SettlementBlockedReason,
            "the rejection reason is stable");
    }
}
```

The project links only `RebellionDirectTerritoryTransferRules.cs` and `WarPeaceProtectionRules.cs`, so it does not stage the shared untracked rules-test project.

- [ ] **Step 2: Run the slice and verify RED**

```powershell
dotnet run --project Tests/RebellionDirectTerritoryTransferRulesSlice/RebellionDirectTerritoryTransferRulesSlice.csproj --no-restore
```

Expected: build fails because `RebellionDirectTerritoryTransferRules` does not exist.

- [ ] **Step 3: Implement the minimal pure policy**

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class RebellionDirectTerritoryTransferRules
    {
        public const string SettlementBlockedReason =
            "rebellion_uses_direct_territory_transfer";

        public static bool ShouldTransfer(bool pCityValid,
            bool pOwnerValid, bool pCapturerValid, bool pSameKingdom,
            bool pActiveWar, bool pOpposingSides,
            bool pAuthoritativeRebellion)
        {
            return pCityValid && pOwnerValid && pCapturerValid &&
                   !pSameKingdom && pActiveWar && pOpposingSides &&
                   pAuthoritativeRebellion;
        }

        public static bool BlocksOrdinarySettlement(bool pWarValid,
            bool pActiveWar, bool pAuthoritativeRebellion)
        {
            return pWarValid && pActiveWar && pAuthoritativeRebellion;
        }
    }
}
```

- [ ] **Step 4: Run the slice and verify GREEN**

Run the Step 2 command. Expected: `AW3 rebellion direct-transfer rules passed.`

- [ ] **Step 5: Commit the policy slice**

```powershell
git add -- Code/core/lineage/RebellionDirectTerritoryTransferRules.cs Tests/RebellionDirectTerritoryTransferRulesSlice/RebellionDirectTerritoryTransferRulesSlice.csproj Tests/RebellionDirectTerritoryTransferRulesSlice/Program.cs
git commit -m "test: define direct rebellion transfer policy"
```

### Task 2: Branch the City-Capture Pipeline

**Files:**
- Create: `Code/core/lineage/RebellionDirectTerritoryTransferService.cs`
- Create: `Code/core/lineage/WarScoreRebellionDirectTransferBridge.cs`
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Create: `Tests/RebellionDirectTerritoryTransferSourceGuard.ps1`

- [ ] **Step 1: Write the failing source guard**

Require these invariants, plus ordering checks proving the direct branch precedes recipient redirection and occupation freezing:

```powershell
Require $service 'foreach (War war in pCapturer.getWars())'
Require $service 'war.getAsset()?.rebellion == true'
Require $service 'war.isInWarWith(pOwner, pCapturer)'
Require $patch 'RebellionDirectTerritoryTransferService.TryResolve('
Require $patch 'RebellionDirectCaptureState'
Require $patch 'WarScoreService.ClearDirectRebellionTransferState('
Require $bridge 'runtime.ClearCityControl(pWarId, pCityId'
Forbid $service 'foreach (War war in World.world.wars)'
Forbid $service '.endWar('
Forbid $patch 'static RebellionDirectCaptureState'
```

The guard also isolates `JoinCapturedCity_Prefix` and requires it to return before `VassalCaptureService.ResolveCaptureRecipient` when the exact rebellion resolver succeeds.

- [ ] **Step 2: Run the guard and verify RED**

```powershell
& Tests/RebellionDirectTerritoryTransferSourceGuard.ps1
```

Expected: failure reporting the missing runtime resolver.

- [ ] **Step 3: Implement the bounded resolver**

```csharp
public static bool TryResolve(City pCity, Kingdom pCapturer,
    out War pWar)
{
    pWar = null;
    Kingdom owner = pCity?.kingdom;
    if (pCity?.data == null || owner?.data == null ||
        pCapturer?.data == null || owner == pCapturer) return false;
    try
    {
        foreach (War war in pCapturer.getWars())
        {
            bool active = war?.data != null && !war.hasEnded();
            bool opponents = active &&
                war.isInWarWith(owner, pCapturer);
            bool rebellion = active &&
                war.getAsset()?.rebellion == true;
            if (!RebellionDirectTerritoryTransferRules.ShouldTransfer(
                    true, true, true, false, active, opponents,
                    rebellion)) continue;
            pWar = war;
            return true;
        }
    }
    catch { }
    return false;
}
```

Do not inspect government IDs, traits, claims, historical war rows, or `World.world.wars`.

- [ ] **Step 4: Add scoped Harmony state and direct cleanup**

```csharp
internal readonly struct RebellionDirectCaptureState
{
    public RebellionDirectCaptureState(Kingdom pOldOwner,
        Kingdom pCapturer, long pWarId, bool pDirect)
    {
        OldOwner = pOldOwner;
        Capturer = pCapturer;
        WarId = pWarId;
        Direct = pDirect;
    }

    public Kingdom OldOwner { get; }
    public Kingdom Capturer { get; }
    public long WarId { get; }
    public bool Direct { get; }
}
```

In `FinishCapture_Prefix`, after the natural 100% check but before `ResolveCaptureRecipient`, resolve the exact rebellion war. On success, preserve `pNewKingdom`, populate the state, and return `true`. Keep the ordinary frozen-occupation branch unchanged.

In `JoinCapturedCity_Prefix`, return without redirecting when the exact resolver succeeds. In the postfix, preserve all existing callbacks and, only after the direct transfer committed to `state.Capturer`, call:

```csharp
WarScoreService.ClearDirectRebellionTransferState(
    state.WarId, __instance.id);
```

The partial war-score bridge removes the pending row for that city, reads only `(warId, cityId)`, clears its matching goal control, and calls `runtime.ClearCityControl(warId, cityId, CurrentWorldTime())`. It must never clear another war's row.

- [ ] **Step 5: Run capture regressions**

```powershell
dotnet run --project Tests/RebellionDirectTerritoryTransferRulesSlice/RebellionDirectTerritoryTransferRulesSlice.csproj --no-restore
& Tests/RebellionDirectTerritoryTransferSourceGuard.ps1
& Tests/CityOccupationFailClosedSourceGuard.ps1
& Tests/OccupationVanillaProgressSourceGuard.ps1
& Tests/VassalOccupationAttributionSourceGuard.ps1
```

Expected: all pass; normal/vassal capture behavior stays green.

- [ ] **Step 6: Commit the capture branch**

```powershell
git add -- Code/core/lineage/RebellionDirectTerritoryTransferService.cs Code/core/lineage/WarScoreRebellionDirectTransferBridge.cs Code/patch/AW_CityOccupationAccelerationPatch.cs Tests/RebellionDirectTerritoryTransferSourceGuard.ps1
git commit -m "feat: transfer rebellion captures directly"
```

### Task 3: Block Ordinary Peace for Rebellion Wars

**Files:**
- Modify: `Code/core/lineage/WarPeaceProtectionRules.cs`
- Modify: `Code/core/lineage/WarPeaceSettlementRuntime.cs`
- Modify: `Code/core/lineage/WarPeaceSettlementService.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Code/core/lineage/WarScoreDecisiveSettlementService.cs`
- Modify: `Code/core/lineage/WarGoalSettlementRuntimeService.cs`
- Modify: `Code/core/lineage/WarExhaustionSettlementRuntimeService.cs`
- Modify: `Code/ui/windows/WarPeaceNegotiationController.cs`
- Modify: `Code/ui/windows/DiplomacyConversationWindow.cs`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Tests/RebellionDirectTerritoryTransferRulesSlice/Program.cs`
- Modify: `Tests/RebellionDirectTerritoryTransferSourceGuard.ps1`

- [ ] **Step 1: Extend the failing policy test**

```csharp
True(WarPeaceProtectionRules.IsProtected("future_revolt", false,
        false, pAuthoritativeRebellion: true),
    "future rebellion assets automatically bypass ordinary peace");
False(WarPeaceProtectionRules.IsProtected("future_revolt", false,
        false, pAuthoritativeRebellion: false),
    "an unknown ordinary war remains negotiable");
```

The isolated slice project already contains this exact production link:

```xml
<Compile Include="..\..\Code\core\lineage\WarPeaceProtectionRules.cs"
         Link="Production\WarPeaceProtectionRules.cs" />
```

- [ ] **Step 2: Extend the failing source guard**

```powershell
Require $peaceRuntime 'RebellionDirectTerritoryTransferService.BlocksOrdinarySettlement(war)'
Require $peaceRuntime 'RebellionDirectTerritoryTransferRules.SettlementBlockedReason'
Require $controller 'RebellionDirectTerritoryTransferService.BlocksOrdinarySettlement(war)'
Require $diplomacy 'pAuthoritativeRebellion: authoritativeRebellion'
Require $decisive 'RebellionDirectTerritoryTransferService.BlocksOrdinarySettlement(pWar)'
Require $goals 'RebellionDirectTerritoryTransferService.BlocksOrdinarySettlement(pWar)'
Require $exhaustion 'RebellionDirectTerritoryTransferService.BlocksOrdinarySettlement(pWar)'
```

Require the service to cancel an old pending/accepted proposal when validation returns `SettlementBlockedReason`, before the proposal can enter `Executing`.

- [ ] **Step 3: Run the policy and guard and verify RED**

```powershell
dotnet run --project Tests/RebellionDirectTerritoryTransferRulesSlice/RebellionDirectTerritoryTransferRulesSlice.csproj --no-restore
& Tests/RebellionDirectTerritoryTransferSourceGuard.ps1
```

Expected: failures for missing authoritative peace gates.

- [ ] **Step 4: Implement the shared runtime gate**

```csharp
public static bool BlocksOrdinarySettlement(War pWar)
{
    bool valid = pWar?.data != null;
    bool active;
    bool rebellion;
    try
    {
        active = valid && !pWar.hasEnded();
        rebellion = active && pWar.getAsset()?.rebellion == true;
    }
    catch { return false; }
    return RebellionDirectTerritoryTransferRules.
        BlocksOrdinarySettlement(valid, active, rebellion);
}
```

Call this in `WarPeaceSettlementWorld.TryContext` immediately after active-war validation and return `SettlementBlockedReason`. Update `WarPeaceProtectionRules.IsProtected` with a fourth optional boolean and pass the authoritative asset flag from `DiplomacyProposalService.IsProtectedWar`. Add early returns to the three automatic settlement queue services and `WarPeaceNegotiationController.Open/TryGetNegotiationContext`.

When an old `Pending` or `Accepted` proposal validates with `SettlementBlockedReason`, cancel it with the same reason and return a failed execution result. Do not cancel `Executing`, `TermsApplied`, or `Executed` proposals because they may already contain irreversible effects and must follow existing idempotent recovery.

- [ ] **Step 5: Add localized feedback**

Map `rebellion_uses_direct_territory_transfer` in `ProposalFailure` to `aw_diplomacy_failure_rebellion_direct_transfer` and register:

```text
简体中文：叛乱战争直接按实际占领转移城池，无需普通和谈
English: Rebellion territory transfers by direct capture and cannot use ordinary peace talks
繁體中文：叛亂戰爭直接按實際佔領轉移城池，無需普通和談
```

- [ ] **Step 6: Run peace regressions**

```powershell
dotnet run --project Tests/RebellionDirectTerritoryTransferRulesSlice/RebellionDirectTerritoryTransferRulesSlice.csproj --no-restore
& Tests/RebellionDirectTerritoryTransferSourceGuard.ps1
dotnet run --project Tests/WarPeaceSettlementServiceTests.csproj --no-restore
& Tests/WarGoalAutomaticSettlementSourceGuard.ps1
& Tests/WarExhaustionSettlementSourceGuard.ps1
& Tests/WarScoreDecisiveSettlementSourceGuard.ps1
& Tests/WarPeaceProposalSubmissionFailureSourceGuard.ps1
```

Expected: all pass; ordinary wars still prepare and execute settlements.

- [ ] **Step 7: Commit the peace block**

```powershell
git add -- Code/core/lineage/WarPeaceProtectionRules.cs Code/core/lineage/WarPeaceSettlementRuntime.cs Code/core/lineage/WarPeaceSettlementService.cs Code/core/lineage/DiplomacyProposalService.cs Code/core/lineage/WarScoreDecisiveSettlementService.cs Code/core/lineage/WarGoalSettlementRuntimeService.cs Code/core/lineage/WarExhaustionSettlementRuntimeService.cs Code/ui/windows/WarPeaceNegotiationController.cs Code/ui/windows/DiplomacyConversationWindow.cs Code/core/lineage/HistoryLocalizationRules.cs Tests/RebellionDirectTerritoryTransferRulesSlice/Program.cs Tests/RebellionDirectTerritoryTransferSourceGuard.ps1
git commit -m "fix: keep rebellion wars out of ordinary peace"
```

### Task 4: Verify Coalition Peace and Build Source

**Files:**
- Verify only; edit only if a failing test identifies a regression.

- [ ] **Step 1: Run the six coalition/negotiation suites**

```powershell
dotnet run --project Tests/WarPeaceSettlementServiceTests.csproj --no-restore
& Tests/WarPeaceCandidateEligibilitySourceGuard.ps1
& Tests/WarPeaceCandidateOrderingSourceGuard.ps1
& Tests/WarPeaceNegotiationPresentationTests.ps1
& Tests/WarPeaceNegotiationWindowTests.ps1
& Tests/WarPeaceIntegrationTests.ps1
```

Expected: all pass. Allied controller and same-side war leader remain equal-cost recipient alternatives, while an unrelated ally without frozen control remains invalid.

- [ ] **Step 2: Run direct-transfer and occupation regressions**

```powershell
dotnet run --project Tests/RebellionDirectTerritoryTransferRulesSlice/RebellionDirectTerritoryTransferRulesSlice.csproj --no-restore
& Tests/RebellionDirectTerritoryTransferSourceGuard.ps1
& Tests/CityOccupationFailClosedSourceGuard.ps1
& Tests/OccupationVanillaProgressSourceGuard.ps1
& Tests/VassalOccupationAttributionSourceGuard.ps1
& Tests/WarGoalAutomaticSettlementSourceGuard.ps1
& Tests/WarExhaustionSettlementSourceGuard.ps1
& Tests/WarScoreDecisiveSettlementSourceGuard.ps1
```

Expected: all pass.

- [ ] **Step 3: Build source**

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore
```

Expected: zero errors. Record shared-workspace warnings rather than claiming zero warnings if any remain.

### Task 5: Deploy Scoped Files and Verify Installed Build

**Files:**
- Copy only Task 1-3 files plus the already-verified coalition peace files to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0/` using identical relative paths.

- [ ] **Step 1: Hash source and installed files before copying**

Use `Get-FileHash -Algorithm SHA256` for every scoped file. Do not delete or overwrite unrelated RTS/scheduler files.

- [ ] **Step 2: Copy scoped files and compare hashes**

Create missing parent directories, use `Copy-Item -LiteralPath`, then require every installed SHA-256 hash to equal its source hash.

- [ ] **Step 3: Build the installed project**

Run from the installed mod directory:

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore
```

Expected: zero errors.

- [ ] **Step 4: Perform focused game acceptance checks**

1. Rebel captures old-regime city: actual rebel capturer owns it immediately.
2. Old regime recaptures rebel city: actual old-regime capturer owns it immediately.
3. Neither city becomes frozen occupation or an ordinary peace cession.
4. Rebellion negotiation is unavailable with localized direct-transfer feedback.
5. A simultaneous normal war still freezes at 100% and opens negotiation.

After these checks, continue the outstanding ordinary-peace bug pass; never use rebellion behavior to mask coalition recipient, allied cession, or proposal-submission failures.
