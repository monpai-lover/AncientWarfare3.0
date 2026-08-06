# RTS War Doctrines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Standard, Last Stand, and deterministic remote numerical-card duel doctrines while preventing every offensive mission from targeting a friendly city.

**Architecture:** Centralize doctrine and mission-target decisions in pure rules reached by both Native and AW3/Large schedulers. Keep vanilla tactical combat outside RTS ownership after target-zone handoff. Run the host-authoritative abstract resolver between director grouping and route generation, using `Army.countUnits()` plus bounded commander buffs, one-to-many participant aggregation, and a persisted transfer-before-demobilization transaction.

**Tech Stack:** C# 11/net48, Harmony, Unity/WorldBox army APIs, NML config UI, System.Data.SQLite, .NET 9 rules and adversarial simulation, AW3 multiplayer snapshots.

---

## Hard Exclusions

Do not modify `ZhuluWarService.cs`, `ZhuluWarRules.cs`,
`ZhuluWarMigrationService.cs`, `ZhuluAgeDirectorService.cs`,
`ZhuluAgeRules.cs`, `ZhuluAgeStatePersistence.cs`,
`ZhuluAgeStateTableItem.cs`, `ZhuluWorldAgeContent.cs`,
`ZhuluWarRulesTests.cs.txt`, `ZhuluAgeRulesTests.cs.txt`, `MandateService.cs`,
or the Zhulu declaration/settlement branches in `AW_WarPatch.cs`. Doctrine code
must never call `setType("totalwar")`, change a war type, or add/remove war
participants.

### Task 1: Register The Doctrine Slice And Setting Rules

**Files:**
- Create: `Code/core/lineage/ArmyRtsWarDoctrineRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsWarDoctrineRulesTests.cs.txt`
- Modify: test `.csproj` and `Program.cs.txt`

- [ ] **Step 1: Add failing parser and withdrawal tests**

```csharp
Equal(ArmyRtsWarResolutionMode.Standard,
    ArmyRtsWarDoctrineRules.Normalize(-1), "unknown defaults safely");
Equal(false, ArmyRtsWarDoctrineRules.AllowAutomaticWithdrawal(
    ArmyRtsWarResolutionMode.LastStand,
    ArmyRtsWithdrawalOrigin.CasualtyThreshold), "last stand blocks casualty retreat");
Equal(true, ArmyRtsWarDoctrineRules.AllowWithdrawal(
    ArmyRtsWarResolutionMode.LastStand,
    ArmyRtsWithdrawalOrigin.PlayerCommand), "manual retreat remains available");
```

- [ ] **Step 2: Register `--rts-war-doctrines` and run it; verify compile failure**
- [ ] **Step 3: Implement exact enums and rules**

```csharp
public enum ArmyRtsWarResolutionMode { Standard = 0, LastStand = 1, AbstractDecisive = 2 }
public enum ArmyRtsWithdrawalOrigin
{
    CasualtyThreshold, Logistics, MinimumForce, RegroupStall, Watchdog,
    PlayerCommand
}
public static ArmyRtsWarResolutionMode Normalize(int value) =>
    value >= 0 && value <= 2 ? (ArmyRtsWarResolutionMode)value :
    ArmyRtsWarResolutionMode.Standard;
```

- [ ] **Step 4: Run the slice; expect `RTS war doctrine rules passed.`**
- [ ] **Step 5: Commit `feat: define RTS war doctrine rules`**

### Task 2: Add A Working Three-State NML Selector

**Files:**
- Modify: `default_config.json`
- Modify: `Code/core/performance/AWPerformanceSettings.cs`
- Create: `Code/core/lineage/ArmyRtsWarDoctrine.cs`
- Create: `Code/patch/AW_ModConfigSelectPatch.cs`
- Modify: `Code/ModClass.cs`
- Modify: `Locales/cz.json`, `Locales/ch.json`, `Locales/en.json`
- Create: `Tests/ArmyRtsWarDoctrineSettingSourceGuard.ps1`

NML's installed `ModConfigItem` supports `SELECT` as integer `IntVal` with an
`Action<int>` callback, but its `ModConfigureWindow.ModConfigListItem.Setup`
currently leaves the `SELECT` case empty. AW3 must fill the existing
`select_area`; using an invisible raw `SELECT` is not acceptable.

- [ ] **Step 1: Write a source guard** requiring one config item with ID
  `AW3_ARMY_RTS_WAR_RESOLUTION_MODE`, type `SELECT`, `IntVal=0`, callback
  `AWPerformanceSettings:SetArmyRtsWarResolutionMode`, all three option labels,
  and a Harmony patch that activates `select_area` for `SELECT`.
- [ ] **Step 2: Run the guard and verify RED**
- [ ] **Step 3: Add the config item**

```json
{
  "Id": "AW3_ARMY_RTS_WAR_RESOLUTION_MODE",
  "Type": "SELECT",
  "IntVal": 0,
  "Callback": "AWPerformanceSettings:SetArmyRtsWarResolutionMode"
}
```

Implement `SetArmyRtsWarResolutionMode(int)`. `ArmyRtsWarDoctrine` captures the
normalized value once on first runtime access and exposes `Current`. Patch
NML's nested `Setup(ModConfigItem)` by reflection; when the item is
this ID, activate `select_area`, add fixed-width previous/next icon buttons and
a non-resizing value label, cycle 0..2, and call `SetValue(index, true)`. Use
localization keys `<ID> Option 0/1/2` for Standard, Last Stand, and Abstract
Decisive. The normal NML Apply operation invokes the integer callback.

- [ ] **Step 4: Run the source guard and net48 build; verify PASS**
- [ ] **Step 5: Commit `feat: add RTS war doctrine selector`**

### Task 3: Define The Mission Target Matrix

**Files:**
- Create: `Code/core/lineage/ArmyRtsMissionTargetRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsMissionTargetRulesTests.cs.txt`
- Modify: test `.csproj` and doctrine slice registration

- [ ] **Step 1: Add a complete matrix test**

```csharp
Equal(true, Validate(Attack, OpenAttack, targetFriendly: false));
Equal(false, Validate(Attack, OpenAttack, targetFriendly: true));
Equal(true, Validate(Defend, OpenDefense, targetFriendly: true));
Equal(false, Validate(Defend, OpenDefense, targetFriendly: false));
Equal(true, Validate(Retreat, SafeFriendly, targetFriendly: true));
Equal(true, Validate(FrontHold, ControlledFront, targetFriendly: true));
```

Also cover missing war/city/kingdom, closed objective, wrong war participant,
and formerly hostile city now friendly.

- [ ] **Step 2: Run the doctrine slice and verify RED**
- [ ] **Step 3: Implement `Validate(ArmyRtsMissionTargetFacts)` returning a
  reasoned `ArmyRtsMissionTargetDecision`**. Do not collapse mission kinds into
  one broad `target kingdom is in war` predicate.
- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `feat: validate RTS targets by mission kind`**

### Task 4: Invalidate Bad Missions Atomically At Every Boundary

**Files:**
- Create: `Code/core/lineage/ArmyRtsMissionIntegrityService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/ArmyMissionPersistence.cs`
- Modify: `Code/core/lineage/KingdomWarDirectorService.cs`
- Modify: `Code/api/commands/ArmyRtsCommandService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsMissionIntegrityRulesTests.cs.txt`
- Create: `Tests/ArmyRtsMissionIntegritySourceGuard.ps1`

- [ ] **Step 1: Add tests and a guard** for director proposal/commit, player
  city command, replenishment restoration, save restoration, runtime recheck,
  and abstract preflight. Assert that one invalidation clears mission,
  persistence, route/path, transport, formation, objective/index, vanilla
  target, and `PreviousOffensiveMission`, then queues the director once.
- [ ] **Step 2: Run tests/guard and verify RED**
- [ ] **Step 3: Implement the single boundary**

```csharp
internal static bool ValidateOrInvalidate(Army army, ArmyRtsMission mission,
    ArmyRtsMissionIngress ingress)
{
    ArmyRtsMissionTargetDecision decision = BuildAndValidate(army, mission);
    if (decision.Valid) return true;
    InvalidateAndReplan(army, mission, ingress, decision.Reason);
    return false;
}
```

Replace controller `IsMissionValid` and persistence `IsTargetInWar` acceptance
with this shared result. `InvalidateAndReplan` is idempotent and never converts
an attack into defense.

- [ ] **Step 4: Run `--rts-war-doctrines`, `--rts-command-slice`, and
  `--rts-replenishment-arrival-slice`; verify PASS**
- [ ] **Step 5: Commit `fix: invalidate friendly RTS attack targets`**

### Task 5: Release Target-Zone Combat To Vanilla

**Files:**
- Modify: `Code/core/lineage/ArmyRtsWarLifecycleRules.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/ArmyRtsTaskOwnershipRules.cs`
- Modify: `Code/content/ArmyRtsContent.cs`
- Modify: `Code/patch/AW_ArmySafetyPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsWarLifecycleRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt`

- [ ] **Step 1: Add failing tests** proving valid target-zone entry releases
  without nearby-hostile discovery, pass-through territory does not release,
  transient absence of enemies does not immediately reacquire, invalid target
  blocks release, and handed-off actors allow vanilla decisions.
- [ ] **Step 2: Run `--rts-wartime-lifecycle-slice`; verify RED**
- [ ] **Step 3: Change `ShouldReleaseToVanilla` to require valid target zone,
  not hostile proximity. Keep `VanillaCombat` until target/local-combat
  completion or Standard casualty withdrawal. Make all Harmony decision gates
  call `ArmyRtsControllerService.OwnsLiveActor(actor)`.
- [ ] **Step 4: Run lifecycle, actor-runtime, and army-rts slices; verify PASS**
- [ ] **Step 5: Commit `fix: hand target-zone combat to vanilla AI`**

### Task 6: Gate Every Automatic Retreat In Last Stand

**Files:**
- Modify: `Code/core/lineage/ArmyRtsRules.cs`
- Modify: `Code/core/lineage/ArmyRtsWarLifecycleRules.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/ArmyRetreatService.cs`
- Modify: `Code/core/lineage/ArmyLogisticsRules.cs`
- Modify: `Code/core/lineage/ArmyStallWatchdogService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsLastStandRulesTests.cs.txt`

- [ ] **Step 1: Add one test per origin**: casualty, logistics,
  minimum-force, regroup-stall, and watchdog are blocked in Last Stand; all
  remain enabled in Standard; player command remains enabled in every mode.
- [ ] **Step 2: Run doctrine slice and verify RED**
- [ ] **Step 3: Pass an explicit `ArmyRtsWithdrawalOrigin` through every call
  to `AssignArmyRetreat`, `AssignRetreatMission`, and watchdog fallback. Gate
  the call with `AllowWithdrawal`; do not infer origin from posture.
- [ ] **Step 4: Search and verify coverage**

```powershell
rg -n "AssignRetreatMission|AssignArmyRetreat|ScheduleArmyRetreat|ArmyRtsState\.Retreat" Code/core/lineage
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-war-doctrines
```

- [ ] **Step 5: Commit `feat: disable automatic withdrawal in last stand`**

### Task 7: Persist Standard Combat Baselines And Prior Missions

**Files:**
- Modify: `Code/core/lineage/ArmyRtsWarLifecycleRules.cs`
- Modify: `Code/core/lineage/ArmyRtsWarLifecycleService.cs`
- Modify: `Code/core/lineage/ArmyMissionPersistence.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsWarLifecyclePersistenceSqlTests.cs.txt`

- [ ] **Step 1: Add rule and SQL round-trip tests** for baseline, phase, and
  serialized prior offensive mission. Verify withdrawal at and below 20 percent,
  no withdrawal above it, replenishment continuation at 99 percent, resume only
  at 100 percent, repeated baseline capture is write-once, war end clears
  records, and restored missions pass Task 4 validation.
- [ ] **Step 2: Run lifecycle slice and verify RED**
- [ ] **Step 3: Make `ShouldWithdraw` use integer `living * 100 <= baseline *
  20` and `ShouldResume` require `living >= baseline`. Add persisted fields for
  war/army/baseline/phase/prior mission IDs and proposal/posture values. Persist
  before changing runtime phase and restore only through `ValidateOrInvalidate`.
- [ ] **Step 4: Re-run lifecycle and replenishment slices; verify PASS**
- [ ] **Step 5: Commit `feat: persist RTS wartime lifecycle intent`**

### Task 8: Define Deterministic Abstract Battle Rules

**Files:**
- Create: `Code/core/lineage/ArmyAbstractBattleModels.cs`
- Create: `Code/core/lineage/ArmyAbstractBattleRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyAbstractBattleRulesTests.cs.txt`
- Modify: test `.csproj` and doctrine slice

- [ ] **Step 1: Add failing pure tests** for `0/0`, one-side-zero, exact
  1.25 threshold, weighted boundary, stable seed/replay, and primary-attacker
  count/tie ordering.
- [ ] **Step 2: Run doctrine slice and verify RED**
- [ ] **Step 3: Implement integer-safe resolution**

```csharp
if (attack == 0 && defense == 0) return NoBattle;
if (defense == 0) return AttackVictory;
if (attack == 0) return DefenseVictory;
if ((long)Math.Max(attack, defense) * 100L >=
    (long)Math.Min(attack, defense) * 125L) return StrongerSide;
long roll = StableHash(operationIdentity) % (attack + defense);
return roll < attack ? AttackVictory : DefenseVictory;
```

Use the existing stable hash/seed utility selected by repository search; reject
`UnityEngine.Random` and `System.Random` in a source guard.

- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `feat: define deterministic count battle rules`**

### Task 9: Reserve Battle Participants Once

**Files:**
- Create: `Code/core/lineage/ArmyAbstractBattleReservationService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyAbstractBattleReservationRulesTests.cs.txt`
- Modify: `Code/core/lineage/KingdomWarDirectorService.cs`

- [ ] **Step 1: Add tests** for grouping by `(warId,targetCityId)`, stable
  target/army order, one army per battlefield, actor-ID deduplication, canonical
  army plus indexed garrison/special defenders, and invalid-target rejection.
- [ ] **Step 2: Run doctrine slice and verify RED**
- [ ] **Step 3: Publish immutable complete-grouping candidate facts from the
  director. Build defenders from `StandingArmyService`,
  `WartimeGarrisonService`, `GarrisonSortieService`, strategic indexes, and city
  threat facts; never scan the world.
- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `feat: reserve abstract battle participants`**

### Task 10: Persist The Abstract Battle Transaction

**Files:**
- Create: `Code/core/db/ArmyAbstractBattleTableItem.cs`
- Create: `Code/core/db/ArmyAbstractBattleParticipantTableItem.cs`
- Create: `Code/core/lineage/ArmyAbstractBattlePersistence.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyAbstractBattlePersistenceSqlTests.cs.txt`

- [ ] **Step 1: Add SQLite tests** for Prepared/Transferred/Demobilizing/
  Complete transitions, sorted participant snapshots, sequence uniqueness,
  rollback injection, replay, and persisted primary owning city.
- [ ] **Step 2: Run doctrine slice and verify RED**
- [ ] **Step 3: Implement compare-and-set phase transitions in transactions**.
  The operation key is `warId:targetCityId:sequence`; Prepared stores the seed
  identity, result, receiver, transferred-city ID, participant revision/hash,
  and all army/actor IDs before any runtime mutation.
- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `feat: persist abstract battle transactions`**

### Task 11: Transfer Territory Before Demobilization

**Files:**
- Create: `Code/core/lineage/ArmyAbstractBattleService.cs`
- Create: `Code/core/lineage/ArmyAbstractBattleDemobilizationService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyAbstractBattleDemobilizationRulesTests.cs.txt`

- [ ] **Step 1: Add tests** proving attack victory transfers target city,
  defense victory transfers the persisted primary attacker's owning city,
  failed transfer changes no soldier, only battlefield losers are processed,
  real soldiers become civilians, protected civil authorities retain offices,
  synthetic actors are deleted, and winners remain intact.
- [ ] **Step 2: Run doctrine slice and verify RED**
- [ ] **Step 3: Implement phase execution**: call `City.setKingdom(receiver)`,
  verify the postcondition, persist `Transferred`, then process a bounded loser
  cursor using existing demobilization, synthetic-removal, deployment, guard,
  general, garrison, and RTS cleanup services. Persist cursor and phase before
  yielding.
- [ ] **Step 4: Run doctrine and army-membership slices; verify PASS**
- [ ] **Step 5: Commit `feat: resolve abstract battles transactionally`**

### Task 12: Schedule And Restore Abstract Battles

**Files:**
- Modify: `Code/core/performance/ArmyRtsSchedulingService.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Code/core/multiplayer/AW3WorldLoadCoordinator.cs`
- Modify: `Code/core/db/LineageArchiveManager.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyAbstractBattleSchedulingRulesTests.cs.txt`

- [ ] **Step 1: Add ordering tests/source assertions** requiring:

```text
Coalition war work -> KingdomWarDirector -> ArmyAbstractBattleService
-> ArmyRouteProvider -> ArmyRtsController
```

Test both Native and AW3/Large paths and incomplete phase recovery.
- [ ] **Step 2: Run doctrine slice and verify RED**
- [ ] **Step 3: Insert one shared `ProcessFrame` after director work and before
  route work. Run it only for `AbstractDecisive` and authority sessions. Restore
  persisted jobs after DB/index readiness and before normal RTS work.
- [ ] **Step 4: Re-run scheduler and doctrine slices; verify PASS**
- [ ] **Step 5: Commit `feat: schedule abstract battles before RTS routes`**

### Task 13: Replicate Outcomes Without Client Rerolls

**Files:**
- Modify: `Code/api/multiplayer/AW3MultiplayerStrategicStateModels.cs`
- Modify: `Code/api/multiplayer/AW3MultiplayerStrategicStateFacade.cs`
- Modify: `Code/core/multiplayer/AW3MultiplayerStrategicStateCoordinator.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyAbstractBattleMultiplayerRulesTests.cs.txt`

- [ ] **Step 1: Add tests** for sorted/deduplicated outcome projection,
  host-only prepare/roll/commit, replica idempotence, reconnect replay, and no
  local seed evaluation on clients.
- [ ] **Step 2: Run multiplayer strategic-state slice and verify RED**
- [ ] **Step 3: Add `AbstractBattleSnapshot` fields for operation key, war,
  target, sequence, phase, winner, receiver, transferred city, revision, and
  participant hash. Guard authority work with `AW3MultiplayerReplicaScope` and
  let clients only apply persisted phase/outcome projections.
- [ ] **Step 4: Run multiplayer and doctrine slices; verify PASS**
- [ ] **Step 5: Commit `feat: replicate abstract battle outcomes`**

### Task 14: Diagnostics, Simulation, And Final Guards

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Create: `Code/core/lineage/ArmyRtsDoctrineDiagnostics.cs`
- Modify: `Tests/ArmyRtsAdversarialSimulation/Program.cs`
- Modify: `Tests/ArmyRtsAdversarialSimulation/SimulationModels.cs`
- Modify: `Tests/ArmyRtsAdversarialSimulation/SimulationEngine.cs`
- Modify: `Tests/ArmyRtsAdversarialSimulation/ScenarioFactory.cs`
- Modify: `Tests/ArmyRtsAdversarialSimulation/ProgressOracle.cs`
- Modify: `Tests/ArmyRtsAdversarialSimulation/ArmyRtsAdversarialSimulation.csproj`
- Create: `Tests/ArmyRtsDoctrineBoundarySourceGuard.ps1`
- Modify: `Tests/ArmyRtsMissionIntegritySourceGuard.ps1`

- [ ] **Step 1: Extend simulation arguments** with
  `--doctrine standard|last-stand|abstract-decisive` and
  `--scheduler native|aw3`. Add ordinary, Zhulu, rebellion, and restoration
  scenarios and assert war type/participants are unchanged.
- [ ] **Step 2: Add bounded diagnostics** for doctrine, ingress/reason,
  mission IDs, handoff/reacquisition, count/seed identity, transaction phase,
  cursor, and retry. Extend guards to reject random APIs, world scans, forbidden
  Zhulu files, `totalwar`, and participant mutation.
- [ ] **Step 3: Run focused verification**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-war-doctrines
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-war-lifecycle
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --army-rts
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --preparation-recruitment-completion-slice
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\ArmyRtsWarDoctrineSettingSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\ArmyRtsMissionIntegritySourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\ArmyRtsDoctrineBoundarySourceGuard.ps1
```

- [ ] **Step 4: Run the full matrix**

```powershell
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj -c Release -- --all --first-seed 0 --seeds 32 --ticks 10000
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release --no-restore
git diff --check
```

Expected: every command exits `0`, the simulation covers all doctrine/scheduler
pairs, preparation recruitment still filters every heir, king, city leader, and
official while filling the army cap, and the final diff contains none of the
forbidden Zhulu files.

- [ ] **Step 5: Commit `test: verify RTS war doctrine boundaries`**
