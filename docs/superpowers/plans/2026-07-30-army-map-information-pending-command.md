# Army Map Information Pending Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep every valid selected-kingdom army flag informative while its RTS projection or mission is pending.

**Architecture:** Preserve `ArmyStrategicIndexService` as the bounded source of army identities. Move pending-state selection into pure `ArmyMapInformationRules`, admit live armies before a mission exists, and let `ArmyMapInformationService` choose either the existing full mission text or a localized replenishing/awaiting-orders fallback.

**Tech Stack:** C#, Harmony/WorldBox runtime APIs, .NET 9 rules test executable, PowerShell source guards, CSV localization.

---

### Task 1: Specify Pending Army Information

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyMapInformationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/ArmyMapInformationMinimapSourceGuardTests.ps1`

- [ ] **Step 1: Write the failing rules tests**

Add tests that require `ArmyMapInformationRules.ResolvePendingState` to retain an existing projection, resolve an under-strength missionless army to `ArmyRtsState.Replenish`, and resolve an operational missionless army to `ArmyRtsState.Idle`. Add tests for `PendingOperationLocalizationKey` returning `aw_army_rts_state_replenish` or `aw_army_rts_state_awaiting_orders`.

- [ ] **Step 2: Add a focused test command**

Register the test file in the rules-test project and add `--army-map-information-slice` to `Program.cs.txt` so the new tests can run without the full suite.

- [ ] **Step 3: Strengthen the source guard**

Require `ArmyMapInformationService` to call `ResolvePendingState` and `PendingOperationLocalizationKey`. Reject the old combined guard that returns false solely because `TryGetProjection` or `TryGetMission` failed. Require `Locales/aw3_army_rts.csv` to contain `aw_army_rts_state_awaiting_orders`.

- [ ] **Step 4: Run RED verification**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --army-map-information-slice
```

Expected: FAIL because the pending-state rule methods do not exist.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests/ArmyMapInformationMinimapSourceGuardTests.ps1
```

Expected: FAIL because the service still filters missionless armies and the localization key is absent.

### Task 2: Render Missionless Armies

**Files:**
- Modify: `Code/core/lineage/ArmyMapInformationRules.cs`
- Modify: `Code/core/presentation/ArmyMapInformationService.cs`
- Modify: `Locales/aw3_army_rts.csv`

- [ ] **Step 1: Implement pending-state rules**

Add:

```csharp
public static ArmyRtsState ResolvePendingState(bool hasProjection,
    ArmyRtsState projectionState, int memberCount,
    int minimumOperationalForce)
```

Return the existing projection state when present; otherwise return `Replenish` below the operational-force threshold and `Idle` at or above it. Add `PendingOperationLocalizationKey` so `Replenish` uses the existing replenishment key and every other missionless state uses `aw_army_rts_state_awaiting_orders`.

- [ ] **Step 2: Admit armies before mission publication**

In `ProcessSelectionBatch`, resolve the army first, read projection and mission opportunistically, derive the fallback state through `ResolvePendingState`, and submit a visualization candidate with `ArmyRtsRole.Reserve` when no mission exists. Keep the existing fixed candidate and per-frame limits.

- [ ] **Step 3: Compose fallback flag text**

In `TryComposeText`, validate only army/captain identity before formatting. Use the current detailed operation path when projection and mission both exist. Otherwise format the same army name, member count, and commander with the localized pending operation. For a replenishing fallback, calculate shortage against `ArmyLogisticsRules.MinimumOperationalForce` so a one-person army visibly reports the immediate operational shortage.

- [ ] **Step 4: Add localization**

Add:

```csv
aw_army_rts_state_awaiting_orders,等待军令,Awaiting orders,等待軍令
```

- [ ] **Step 5: Run GREEN verification**

Run the focused rules test and source guard from Task 1. Expected: PASS.

### Task 3: Regression, Merge, And Deploy

**Files:**
- Verify: `AncientWarfare3.csproj`
- Deploy: `Code/core/lineage/ArmyMapInformationRules.cs`
- Deploy: `Code/core/presentation/ArmyMapInformationService.cs`
- Deploy: `Locales/aw3_army_rts.csv`

- [ ] **Step 1: Run relevant regression suites**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --army-map-information-slice
powershell -ExecutionPolicy Bypass -File Tests/ArmyMapInformationMinimapSourceGuardTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-command-slice
dotnet build AncientWarfare3.csproj -c Debug -t:Rebuild --no-incremental
```

Expected: all tests pass and the mod build succeeds with zero errors.

- [ ] **Step 2: Commit the isolated implementation**

Commit only the display tests, production files, and localization with message `fix: show armies awaiting RTS orders`.

- [ ] **Step 3: Integrate into master**

Merge the isolated branch into `master` without rewriting unrelated history, then rerun the focused test and `git diff --check`.

- [ ] **Step 4: Deploy touched runtime files**

Copy the three production/runtime files to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0` at the same relative paths. Start WorldBox and verify the loaded source timestamp in `Player.log`.

- [ ] **Step 5: Perform in-game acceptance checks**

Select a kingdom at war and verify that attacker, defender, and newly created army flags all show army name, live count, commander, and either a current mission, `补员`, or `等待军令`. Confirm no duplicate native flags and no new exceptions in `Player.log`.
