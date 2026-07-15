# AW3 Vassal War And Alliance Contract Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Isolate an independence rebel from every war shared with its suzerain and prevent vassal alliance plots from receiving a null alliance object.

**Architecture:** Persist an independence-war suspension marker on the rebel, remove shared-side war memberships at declaration, and make yearly vassal pulls consult the marker. Preserve the vanilla `AllianceManager.newAlliance` return contract and reject vassal plots at `DiplomacyHelpers.getAllianceTarget`, where vanilla already handles a null target safely.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox war/alliance APIs, .NET 9 rule executable, PowerShell source guards.

---

### Task 1: Add Red Independence And Alliance Rules

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Code/core/lineage/VassalWarSupportRules.cs`
- Modify: `Code/core/lineage/VassalWarPermissionRules.cs`

- [ ] Link both production rule files from the rule-test project.
- [ ] Add failing assertions:

```csharp
True(VassalWarSupportRules.ShouldLeaveForIndependence(false, true, true, true),
    "independence declaration leaves every shared suzerain war");
Equal(false, VassalWarSupportRules.ShouldLeaveForIndependence(true, true, true, false),
    "the independence war itself is preserved");
Equal(false, VassalWarSupportRules.ShouldLeaveForIndependence(false, true, false, false),
    "an unrelated rebel war is preserved");
True(VassalWarSupportRules.HasActiveIndependenceSuspension(true, true, true),
    "active opposition suspends military service");
Equal(false, VassalWarSupportRules.HasActiveIndependenceSuspension(true, false, true),
    "an ended war makes the marker stale");
Equal(false, VassalWarSupportRules.ShouldPullIntoSuzerainWar(
        true, false, false, false, independenceSuspended: true),
    "yearly service cannot rejoin an active rebel");
Equal(false, VassalWarPermissionRules.CanUseAlliancePlot(true, false),
    "a vassal cannot initiate the alliance plot");
Equal(false, VassalWarPermissionRules.CanUseAlliancePlot(false, true),
    "a vassal cannot be an alliance plot target");
```

- [ ] Run `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore`; require failure because these methods do not exist.
- [ ] Implement these pure rules while retaining the four current pull guards:

```csharp
public static bool ShouldLeaveForIndependence(bool isIndependenceWar,
    bool rebelInWar, bool suzerainInWar, bool sameSide)
    => !isIndependenceWar && rebelInWar && suzerainInWar && sameSide;

public static bool HasActiveIndependenceSuspension(bool markerMatches,
    bool warActive, bool rebelOpposesSuzerain)
    => markerMatches && warActive && rebelOpposesSuzerain;

public static bool CanUseAlliancePlot(bool initiatorIsVassal, bool targetIsVassal)
    => !initiatorIsVassal && !targetIsVassal;
```

- [ ] Add optional `bool independenceSuspended = false` to `ShouldPullIntoSuzerainWar` and reject it first.
- [ ] Re-run the focused tests and require `Rule tests passed.`.
- [ ] Commit the rule slice as `test: define independence war isolation rules`.

### Task 2: Suspend Military Obligations During Independence

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add persisted keys `aw_vassal_independence_war_id` and `aw_vassal_independence_suzerain_id`.
- [ ] Add source guards requiring `BeginIndependenceSuspension`, `EndIndependenceSuspension`, and a yearly `HasActiveIndependenceSuspension` check; run guards and require failure.
- [ ] Change independence-war start ordering to:

```csharp
BeginIndependenceSuspension(pWar, attacker, defender);
LeaveSuzerainWarsForIndependence(pWar, attacker, defender);
JoinLoyalVassalsToDefenders(pWar, defender, attacker);
return;
```

- [ ] Implement cleanup from `pRebel.getWars().ToList()`. Skip the independence war and call `LeaveWarPeacefully` only when the rebel and old suzerain occupy the same side of another active war.
- [ ] Resolve the recorded war with `World.world.wars.get(warId)`. Treat the suspension as active only when the war is live and the rebel opposes the recorded suzerain; clear stale keys otherwise.
- [ ] Pass suspension state to `ShouldPullIntoSuzerainWar` before `JoinSide`.
- [ ] On every independence-war result, clear matching suspension state before victory settlement. Success keeps existing release/reparent behavior; defeat or peace keeps the vassal relation.
- [ ] Run rule tests and source guards; require both to pass.
- [ ] Commit as `fix: suspend vassal service during independence wars`.

### Task 3: Preserve The Alliance Constructor Contract

**Files:**
- Modify: `Code/patch/AW_VassalDiplomacyPatch.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add guards forbidding `NewAlliance_Prefix` and requiring a `DiplomacyHelpers.getAllianceTarget` postfix; run guards and require failure.
- [ ] Delete the prefix that returns a null `Alliance`.
- [ ] Filter the safely nullable plot target:

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getAllianceTarget))]
public static void GetAllianceTarget_Postfix(Kingdom pKingdomStarter,
    ref Kingdom __result)
{
    if (!VassalWarPermissionRules.CanUseAlliancePlot(
            ShouldBlockAlliance(pKingdomStarter),
            ShouldBlockAlliance(__result)))
        __result = null;
}
```

- [ ] Make `ShouldBlockAlliance(null)` return false. Keep the `forceAlliance` and `Alliance.join` guards unchanged.
- [ ] Run rule tests and source guards; require both to pass.
- [ ] Commit as `fix: reject vassal alliance plots before construction`.

### Task 4: Full Verification And Deployment

**Files:**
- Verify: `Code/core/lineage/VassalService.cs`
- Verify: `Code/patch/AW_VassalDiplomacyPatch.cs`
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] Run rule tests and require `Rule tests passed.`.
- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1` and require `Source guard tests passed.`.
- [ ] Build Debug and Release with `--no-restore`; require zero warnings and zero errors.
- [ ] Run `git diff --check` and inspect `git status --short`.
- [ ] Deploy tracked files while preserving `.runtime/aw3_lineage_archive.db`, then compare assembly hashes.
- [ ] In a fresh world, verify a rebel immediately leaves all shared suzerain wars, remains out after the yearly check, and resumes obligations only after defeat or peace.
- [ ] Force a vassal alliance plot check and verify the latest `Player.log` contains neither `PlotsLibrary+<>c.<addBasic>b__18_15` nor its null reference.
