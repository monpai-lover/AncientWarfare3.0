# Grand Strategy Army Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a restart-selected, mutually exclusive CK3-style `Grand Strategy Army` mode with numeric levy armies, strategic movement, persistent abstract battles and sieges, commander events, reusable temporary occupation, map projections, reports, persistence, multiplayer authority, AI, and performance safeguards while preserving native and existing RTS behavior.

**Architecture:** Keep native `Army` and the existing `Army RTS` runtime untouched behind an explicit mode gate. Add a pure-data grand-strategy authority layer under `Code/core/grandstrategy`, with kingdom ledgers, armies, battles, sieges, stable IDs, staged transactions, and snapshot persistence. Connect it to WorldBox through narrow adapters for wars, pathfinding, actors, temporary occupation, UI, presentation, multiplayer, and Harmony suppression patches; presentation objects are disposable projections and never own simulation state.

**Tech Stack:** C# 11 / .NET Framework 4.8, Unity 2021 APIs exposed by WorldBox, HarmonyLib, NeoModLoader, existing AW3 SQLite archive and save/load pipeline, existing `AWPathFinder`, PowerShell source-guard tests, isolated .NET 9 rule/integration test projects.

---

## File Map

Create the following focused modules. Every production file is included by `Code\\**\\*.cs` in `AncientWarfare3.csproj`.

- `Code/core/grandstrategy/GrandStrategyArmyMode.cs`: mode enum and restart/load policy.
- `Code/core/grandstrategy/GrandStrategyIds.cs`: stable ID allocation and validation.
- `Code/core/grandstrategy/GrandStrategyModels.cs`: kingdom ledger, army, commander assignment, route, battle, siege, report records.
- `Code/core/grandstrategy/GrandStrategyLedgerRules.cs`: conservation, raising, reinforcement, recovery, disband rules.
- `Code/core/grandstrategy/GrandStrategyLedgerService.cs`: per-world ledger index and transactions.
- `Code/core/grandstrategy/GrandStrategyArmyRules.cs`: composition, split/merge, task and order legality.
- `Code/core/grandstrategy/GrandStrategyArmyService.cs`: army index, raising, organization, split/merge, disband.
- `Code/core/grandstrategy/GrandStrategyCommanderRules.cs`: positions, eligibility, succession and risk outcomes.
- `Code/core/grandstrategy/GrandStrategyCommanderService.cs`: actor assignment/synchronization.
- `Code/core/grandstrategy/GrandStrategyTroopRules.cs`: five troop types, technology, training, equipment and frontage.
- `Code/core/grandstrategy/GrandStrategyPathRules.cs`: strategic cost, transport, retreat and route validation.
- `Code/core/grandstrategy/GrandStrategyPathService.cs`: one path request per numeric army and interpolation state.
- `Code/core/grandstrategy/GrandStrategyBattleRules.cs`: deterministic rolls, phases, modifiers, casualties and pursuit.
- `Code/core/grandstrategy/GrandStrategyBattleModels.cs`: active battle rounds and immutable report DTOs.
- `Code/core/grandstrategy/GrandStrategyBattleService.cs`: engagement, monthly rounds, reinforcements, retreat and completion transactions.
- `Code/core/grandstrategy/GrandStrategySiegeRules.cs`: siege power, defense, monthly progress and assault risk.
- `Code/core/grandstrategy/GrandStrategySiegeService.cs`: active siege lifecycle and occupation bridge.
- `Code/core/grandstrategy/GrandStrategyRuntime.cs`: mode-gated world tick, clear/rebuild, and service orchestration.
- `Code/core/grandstrategy/GrandStrategyPersistence.cs`: versioned JSON/SQLite snapshot read/write and idempotent recovery.
- `Code/core/grandstrategy/GrandStrategyMultiplayerAdapter.cs`: host authority, command validation and snapshot projection.
- `Code/core/presentation/GrandStrategyArmyProjectionService.cs`: pooled banner, dummy, arrow and route projections.
- `Code/core/presentation/GrandStrategyArmyProjectionRules.cs`: zoom/visibility and pool sizing rules.
- `Code/api/commands/GrandStrategyArmyCommandModels.cs`: client command DTOs.
- `Code/api/commands/GrandStrategyArmyCommandService.cs`: movement, split, merge, retreat, siege and assignment commands.
- `Code/ui/windows/GrandStrategyArmyWindow.cs`: army detail window.
- `Code/ui/windows/GrandStrategyBattleWindow.cs`: live battle and read-only report window.
- `Code/ui/windows/GrandStrategySiegeWindow.cs`: live siege window.
- `Code/ui/GrandStrategyArmyInputController.cs`: banner/endpoint selection and drag destination input.
- `Code/patch/AW_GrandStrategyArmyModePatch.cs`: mode-gated native army/actor/occupation interception.
- `Code/patch/AW_GrandStrategyArmyWorldLifecyclePatch.cs`: runtime clear/load/new-world hooks.
- `Tests/GrandStrategyArmyRulesTests.ps1`: source-guard for mode boundaries and tests.
- `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs` and `.csproj`: pure rules and transaction executable.
- `Tests/GrandStrategyArmyIntegrationSourceGuard.ps1`: Harmony and compatibility boundary guard.
- `Tests/GrandStrategyArmyPersistenceSourceGuard.ps1`: save/load and idempotency guard.

Modify only the following existing integration files, preserving unrelated user changes:

- `Code/core/performance/AWPerformanceSettings.cs`: add the grand-strategy switch and mutual exclusion with `SwitchArmyRts`.
- `default_config.json`: add `AW3_ENABLE_GRAND_STRATEGY_ARMY` disabled by default.
- `Locales/aw3_config.csv`: add Chinese/English labels and restart-required description.
- `Code/ModClass.cs`: initialize/patch grand-strategy runtime and log the selected mode.
- `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`: restore grand-strategy state after wars/actors and rebuild projections.
- `Code/patch/AW_SavePatch.cs`: publish grand-strategy snapshot on save and observe its load directory.
- `Code/core/pathfinding/ArmyRouteProvider.cs`: add a data-coordinate backend without changing native/RTS backends.
- `Code/core/lineage/WarScoreRuntimeBridge.cs` or the smallest existing occupation bridge file: call it only after an abstract siege completes.

Do not modify the existing `ArmyRts*` rules/services except for a mode predicate consumed by the new gate. Do not touch the currently dirty school, supporter, actor-safety, or test files.

## Task 1: Runtime Mode And Configuration

**Files:**
- Create: `Code/core/grandstrategy/GrandStrategyArmyMode.cs`
- Modify: `Code/core/performance/AWPerformanceSettings.cs`
- Modify: `default_config.json`
- Modify: `Locales/aw3_config.csv`
- Modify: `Code/ModClass.cs`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/GrandStrategyArmyRules.Isolated.Tests.csproj`
- Test: `Tests/GrandStrategyArmyRulesTests.ps1`

- [ ] **Step 1: Write failing mode tests.** Add an isolated executable that asserts the default mode is `Vanilla`, `SwitchArmyRts(true)` resolves `ArmyRts`, `SwitchGrandStrategyArmy(true)` resolves `GrandStrategy`, enabling one disables the other, and mode changes are marked restart-required.

```csharp
Equal(GrandStrategyArmyMode.Vanilla,
    GrandStrategyArmyModeRules.Resolve(false, false));
Equal(GrandStrategyArmyMode.ArmyRts,
    GrandStrategyArmyModeRules.Resolve(true, false));
Equal(GrandStrategyArmyMode.GrandStrategy,
    GrandStrategyArmyModeRules.Resolve(false, true));
True(GrandStrategyArmyModeRules.RequiresRestart(
    GrandStrategyArmyMode.ArmyRts, GrandStrategyArmyMode.GrandStrategy));
```

- [ ] **Step 2: Run the isolated project and verify it fails** with missing type/member errors.

Run: `dotnet run --project Tests/GrandStrategyArmyRules.Isolated.Tests/GrandStrategyArmyRules.Isolated.Tests.csproj`

Expected: FAIL because `GrandStrategyArmyMode` and `GrandStrategyArmyModeRules` do not exist.

- [ ] **Step 3: Implement the mode gate and settings callbacks.** Add `GrandStrategyArmyMode` values `Vanilla`, `ArmyRts`, `GrandStrategy`; resolve from the two switches; make both callbacks clear the other switch; expose `EnableGrandStrategyArmy`, `CurrentArmyMode`, and a restart-required marker. Keep existing `EnableArmyRts` semantics intact for native/RTS callers.

```csharp
public static GrandStrategyArmyMode CurrentArmyMode =>
    GrandStrategyArmyModeRules.Resolve(
        EnableArmyRts, EnableGrandStrategyArmy);

public static void SwitchGrandStrategyArmy(bool value)
{
    _configGrandStrategyArmyEnabled = value;
    if (value) _configArmyRtsEnabled = false;
}
```

- [ ] **Step 4: Add config and localization entries.** Use ID `AW3_ENABLE_GRAND_STRATEGY_ARMY`, `Type: SWITCH`, `BoolVal: false`, callback `AWPerformanceSettings:SwitchGrandStrategyArmy`; description must state that it is mutually exclusive with RTS and takes effect after restart/world load.

- [ ] **Step 5: Add startup logging and source guards.** `ModClass.OnModLoad` logs `vanilla`, `army_rts`, or `grand_strategy`; the guard verifies the new setting, callback, exclusion, config entry, and all three locale columns.

- [ ] **Step 6: Run tests and build.**

Run: `dotnet run --project Tests/GrandStrategyArmyRules.Isolated.Tests/GrandStrategyArmyRules.Isolated.Tests.csproj` and `dotnet build AncientWarfare3.csproj`

Expected: isolated mode tests pass; production build succeeds with no changes to existing RTS behavior.

- [ ] **Step 7: Commit.**

```text
git add Code/core/grandstrategy/GrandStrategyArmyMode.cs Code/core/performance/AWPerformanceSettings.cs default_config.json Locales/aw3_config.csv Code/ModClass.cs Tests/GrandStrategyArmyRules.Isolated.Tests Tests/GrandStrategyArmyRulesTests.ps1
git commit -m "feat: add mutually exclusive grand strategy army mode"
```

## Task 2: Stable IDs, Models, And Kingdom Manpower Ledger

**Files:**
- Create: `Code/core/grandstrategy/GrandStrategyIds.cs`
- Create: `Code/core/grandstrategy/GrandStrategyModels.cs`
- Create: `Code/core/grandstrategy/GrandStrategyLedgerRules.cs`
- Create: `Code/core/grandstrategy/GrandStrategyLedgerService.cs`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`

- [ ] **Step 1: Write failing conservation tests.** Cover available/raised/wounded/dispersed/dead conservation, raising only from available manpower, casualty classification, wounded/dispersed recovery, and exactly-once disband return.

```csharp
var ledger = new GrandStrategyKingdomLedger(kingdomId: 7, total: 1000);
True(GrandStrategyLedgerRules.TryRaise(ledger, 600, out _), "raise");
Equal(400, ledger.AvailableManpower);
GrandStrategyLedgerRules.ApplyCasualties(ledger,
    permanentDeaths: 20, wounded: 30, dispersed: 50, prisoners: 10);
Equal(990, ledger.AccountedManpower);
```

- [ ] **Step 2: Run tests and verify failure.**

Run: `dotnet run --project Tests/GrandStrategyArmyRules.Isolated.Tests/GrandStrategyArmyRules.Isolated.Tests.csproj`

Expected: FAIL because ledger types and rules are absent.

- [ ] **Step 3: Implement immutable-safe models and ID allocator.** Use `long` IDs scoped by world generation and separate army/battle/siege/report namespaces; reject negative IDs and duplicate allocation. Model five troop counts, technology, training, equipment generation, position tile, route, task, commander assignments, and ledger pools.

- [ ] **Step 4: Implement ledger rules/service.** Every mutation returns a transaction result with before/after totals and an idempotency key. `TryRaise`, `ApplyCasualties`, `RecoverWounded`, `RecoverDispersed`, `ReturnSurvivors`, and `DisbandOnce` must conserve totals except recruitment growth and permanent deaths.

- [ ] **Step 5: Run isolated tests and build.**

Expected: conservation, duplicate-disband, and recovery tests pass; `dotnet build AncientWarfare3.csproj` succeeds.

- [ ] **Step 6: Commit.**

```text
git add Code/core/grandstrategy/GrandStrategyIds.cs Code/core/grandstrategy/GrandStrategyModels.cs Code/core/grandstrategy/GrandStrategyLedgerRules.cs Code/core/grandstrategy/GrandStrategyLedgerService.cs Tests/GrandStrategyArmyRules.Isolated.Tests
git commit -m "feat: add grand strategy ledgers and stable records"
```

## Task 3: Troops, Raising, Organization, Split, Merge, And Disband

**Files:**
- Create: `Code/core/grandstrategy/GrandStrategyTroopRules.cs`
- Create: `Code/core/grandstrategy/GrandStrategyArmyRules.cs`
- Create: `Code/core/grandstrategy/GrandStrategyArmyService.cs`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`

- [ ] **Step 1: Write failing tests.** Verify infantry, spearmen, archers, cavalry, and engineers; technology unlocks and training ceilings; automatic multi-army organization by supply/front/general limits; co-location requirement for merge; split conservation; no standing numeric army in peace; and war-end disband.

```csharp
True(GrandStrategyTroopRules.IsUnlocked(
    GrandStrategyTroopType.Engineers, technology: 3), "engineers unlocked");
False(GrandStrategyArmyRules.CanMerge(first, second, sameTile: false),
    "different tiles cannot merge");
Equal(first.TotalStrength, split.TotalStrength + remainder.TotalStrength);
```

- [ ] **Step 2: Run tests and verify failure.**

Expected: missing troop, army, and organization implementation.

- [ ] **Step 3: Implement troop and quality rules.** Add deterministic composition proportions from kingdom technology and military organization; record equipment generation on raising; dilute only on reinforcement; compute organization, supply capacity, and frontage.

- [ ] **Step 4: Implement army service.** `RaiseForWar` consumes the ledger, chooses rally anchors from capital/war goal/border/safety/supply inputs, and creates multiple `GrandStrategyArmy` records. Add `Split`, `Merge`, `AssignTroops`, `DisbandForWarEnd`, and index rebuild APIs.

- [ ] **Step 5: Run tests/build and commit.**

Expected: all organization and conservation tests pass; production build succeeds.

## Task 4: Commander Roles, Succession, And Actor Synchronization

**Files:**
- Create: `Code/core/grandstrategy/GrandStrategyCommanderRules.cs`
- Create: `Code/core/grandstrategy/GrandStrategyCommanderService.cs`
- Modify: `Code/core/grandstrategy/GrandStrategyModels.cs`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`

- [ ] **Step 1: Write failing role tests.** Cover commander, vanguard, left/right wing, rear guard, siege officer; multiple commanders; no-commander movement; automatic replacement after death, capture, severe injury, or invalid actor; and risk outcome ordering.

```csharp
var next = GrandStrategyCommanderRules.SelectSuccessor(assignments,
    unavailableCommanderId: 12);
Equal(24L, next.ActorId);
Equal(GrandStrategyCommanderOutcome.Captured,
    GrandStrategyCommanderRules.ResolveRisk(roll: 9, routed: true,
        prowess: 2, lossesPercent: .7));
```

- [ ] **Step 2: Run tests and verify failure.**

- [ ] **Step 3: Implement role and deterministic risk rules.** Do not call actor combat APIs. Risk checks consume battle seed/round and actor snapshot facts only; outcomes are safe, wounded, severely wounded, captured, killed.

- [ ] **Step 4: Implement synchronization service.** Assign/unassign live warrior actors, keep them following the data army position, exclude royal guards and civil authority actors, and repair assignments after load. Invalid actors are removed without deleting the numeric army.

- [ ] **Step 5: Run tests/build and commit.**

## Task 5: Army-Level Pathfinding, Retreat, And Sea Transport

**Files:**
- Create: `Code/core/grandstrategy/GrandStrategyPathRules.cs`
- Create: `Code/core/grandstrategy/GrandStrategyPathService.cs`
- Modify: `Code/core/pathfinding/ArmyRouteProvider.cs`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`
- Test: `Tests/GrandStrategyArmyIntegrationSourceGuard.ps1`

- [ ] **Step 1: Write failing path-rule tests.** Verify terrain/road/forest/mountain/river/hostile-territory/supply costs, one request per army, coast embarkation, fleet projection, landing, unreachable destinations, forced retreat to nearest safe territory, and command lock during retreat/battle.

```csharp
True(GrandStrategyPathRules.IsLowerCost(mountainCost: 12,
    forestCost: 8, roadBonus: 4), "road-adjusted route comparison");
Equal(GrandStrategyMovementState.Fleet,
    GrandStrategyPathRules.NextMovementState(
        GrandStrategyMovementState.Land, reachedCoast: true,
        validLanding: false));
```

- [ ] **Step 2: Run tests and verify failure.**

- [ ] **Step 3: Implement data-coordinate route backend.** Extend `ArmyRouteProvider` with a `GrandStrategyArmyRouteProvider` that submits `AWPathRequest` from stored tile IDs and movement profile; never calls `getCaptain`, `goTo`, or writes native army paths. Preserve existing backend selection for Vanilla/RTS.

- [ ] **Step 4: Implement interpolation and transport.** Store route cursor, estimated arrival, supply cost, movement state, and fleet projection; submit exactly one request per army; sync assigned generals and presentation coordinates; reject ordinary orders in forced retreat.

- [ ] **Step 5: Run source guards, isolated tests, and build.**

Expected: no grand-strategy provider references `getCaptain`/`goTo`; existing RTS provider tests remain unchanged.

- [ ] **Step 6: Commit.**

## Task 6: Runtime Orchestration, War Raising, And Native Compatibility Gate

**Files:**
- Create: `Code/core/grandstrategy/GrandStrategyRuntime.cs`
- Create: `Code/patch/AW_GrandStrategyArmyModePatch.cs`
- Create: `Code/patch/AW_GrandStrategyArmyWorldLifecyclePatch.cs`
- Modify: `Code/ModClass.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Test: `Tests/GrandStrategyArmyIntegrationSourceGuard.ps1`

- [ ] **Step 1: Write failing source guards.** Assert that grand-strategy mode suppresses covered native army creation, wartime actor attack/AI, native strategic army decisions, and `City.updateCapture`; assert royal guards, animals, disasters, god powers, small real-actor conflicts, and unrelated damage remain native.

- [ ] **Step 2: Run the guard and verify failure.**

- [ ] **Step 3: Implement explicit classification and Harmony gate.** `GrandStrategyArmyModePatch` must resolve `Native`, `GrandStrategy`, or `RoyalGuardHybrid` before actions. Covered wars use data armies and no native soldier combat; royal-guard encounters hand a special force to the abstract battle layer.

- [ ] **Step 4: Implement runtime tick and lifecycle.** Tick ledger recovery, war discovery/raising, path service, battle service, siege service, reports, AI hook, and bounded presentation queue only when `CurrentArmyMode == GrandStrategy`. Clear all state on new world and rebuild on load.

- [ ] **Step 5: Add restore stages.** Restore ledgers before armies, armies before commanders, commanders before battles/sieges, then rebuild indexes and projections; recover ended wars exactly once.

- [ ] **Step 6: Run guards/build and commit.**

## Task 7: Player Commands, Dragging, And Army Window

**Files:**
- Create: `Code/api/commands/GrandStrategyArmyCommandModels.cs`
- Create: `Code/api/commands/GrandStrategyArmyCommandService.cs`
- Create: `Code/ui/GrandStrategyArmyInputController.cs`
- Create: `Code/ui/windows/GrandStrategyArmyWindow.cs`
- Create: `Code/core/presentation/GrandStrategyArmyProjectionRules.cs`
- Create: `Code/core/presentation/GrandStrategyArmyProjectionService.cs`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`
- Test: `Tests/GrandStrategyArmyIntegrationSourceGuard.ps1`

- [ ] **Step 1: Write failing command tests.** Cover authorized selected-kingdom commands, movement/rally/pursuit/siege/follow/merge/split/retreat/disband, invalid commands during battle/retreat, drag preview legality and route recalculation, and rejection of another kingdom’s army.

```csharp
var preview = GrandStrategyArmyCommandRules.PreviewMove(army, target,
    routeCost: 42, supplyCost: 8, reachable: true);
True(preview.Accepted && preview.SupplyCost == 8, "legal drag preview");
False(GrandStrategyArmyCommandRules.CanCommand(army,
    selectedKingdomId: 2), "foreign army rejected");
```

- [ ] **Step 2: Run tests and verify failure.**

- [ ] **Step 3: Implement command DTOs/service.** Commands carry stable army ID, world generation, client sequence, target tile, and expected revision. Host validates ownership/revision and applies through army service; duplicate sequence is idempotently ignored.

- [ ] **Step 4: Implement input and projections.** Reuse native banner style/assets where available; pooled far/medium/near projections show banner, route, direction arrow, arrival, flag, dummy animation, and real generals. Dragging the banner or route endpoint previews and submits a new full optimal route.

- [ ] **Step 5: Implement army window.** Display composition, quality, morale, organization, supply, training, technology, equipment, generals/roles, task, route, arrival; expose legal split, merge, retreat, disband, assignment, and assault controls using icon buttons/tooltips consistent with existing AW3 windows.

- [ ] **Step 6: Run tests/build and commit.**

## Task 8: Persistent Round-Based Field Battles

**Files:**
- Create: `Code/core/grandstrategy/GrandStrategyBattleModels.cs`
- Create: `Code/core/grandstrategy/GrandStrategyBattleRules.cs`
- Create: `Code/core/grandstrategy/GrandStrategyBattleService.cs`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`

- [ ] **Step 1: Write failing battle tests.** Cover engagement stop, attacker/defender terrain and crossing, frontage and reserves, five troop matchups, technology/training/equipment/morale/organization/supply/commander/weather modifiers, deterministic 0-10 rolls, 3-12 round even battles, reinforcements next round, rout, voluntary withdrawal, pursuit, casualty classes, and immutable report creation.

```csharp
Equal(7, GrandStrategyBattleRules.Roll(worldSeed: 3, warId: 4,
    battleId: 5, round: 2));
var result = GrandStrategyBattleRules.ResolveRound(facts);
True(result.FrontlineCommitted <= facts.Frontage, "frontage respected");
Equal(result.AttackerLosses + result.AttackerRemaining,
    facts.AttackerEngaged, "attacker conservation");
```

- [ ] **Step 2: Run tests and verify failure.**

- [ ] **Step 3: Implement deterministic battle rules.** Seed a stable 64-bit hash of world seed, war ID, battle ID, and round. Calculate frontage-limited effective strength, roll, modifiers, casualties, morale/organization, commander risk, and phase transition without Unity state.

- [ ] **Step 4: Implement service and transactions.** Detect engagement range between numeric armies, freeze movement, add arrivals as next-round reinforcements, apply one monthly round, stage ledger/casualty/commander/war-score changes, and commit under an idempotency key `(battleId, round)`.

- [ ] **Step 5: Implement pursuit and retreat.** Lock a retreat route, convert dispersed troops according to cavalry/technology, and complete only after pursuit; no native actor attack or death is issued.

- [ ] **Step 6: Run tests/build and commit.**

## Task 9: Abstract Sieges And Temporary Occupation Bridge

**Files:**
- Create: `Code/core/grandstrategy/GrandStrategySiegeRules.cs`
- Create: `Code/core/grandstrategy/GrandStrategySiegeService.cs`
- Create: `Code/ui/windows/GrandStrategySiegeWindow.cs`
- Modify: smallest existing occupation bridge (`Code/core/lineage/WarScoreRuntimeBridge.cs` or `WarTerritoryService.cs`)
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`
- Test: `Tests/GrandStrategyArmyIntegrationSourceGuard.ps1`

- [ ] **Step 1: Write failing siege tests.** Cover blocking field armies, defense from city level/buildings/population/guards/terrain/policy/technology/resistance, siege power from engineers/equipment/officer/manpower/supply/technology, monthly steady siege, assault speed/risk, relief battle pause, failed besieger termination, and occupation idempotency.

- [ ] **Step 2: Run tests and verify failure.**

- [ ] **Step 3: Implement siege rules/service.** A siege is a stable data record with one monthly round, progress, defense, losses, assault flag, and report history. Never call native `City.updateCapture` or create a second ownership model.

- [ ] **Step 4: Bridge completed siege to existing temporary occupation and war score.** Use one transaction key for occupation and score; legal ownership remains unchanged until existing war settlement services decide it.

- [ ] **Step 5: Implement siege window and relief interaction.** Show defense, engineers, supply, progress, modifiers, losses, and steady/assault controls; relief stops siege rounds and enters battle flow.

- [ ] **Step 6: Run tests/build and commit.**

## Task 10: Battle/Siege UI, Reports, And History Access

**Files:**
- Create: `Code/ui/windows/GrandStrategyBattleWindow.cs`
- Modify: `Code/ui/windows/HistoryListWindow.cs` or its existing report adapter
- Modify: `Code/core/presentation/GrandStrategyArmyProjectionService.cs`
- Test: `Tests/GrandStrategyArmyIntegrationSourceGuard.ps1`

- [ ] **Step 1: Write failing UI source guards.** Require battle button between opposing banners, siege button/progress by city, live data fields, read-only completed report state, and no UI code that mutates simulation records directly.

- [ ] **Step 2: Run the guard and verify failure.**

- [ ] **Step 3: Implement live battle and report windows.** Show both sides, frontline/reserves, every round’s roll/modifiers/losses/morale/organization/reinforcements/commander events; completed reports are immutable and reachable from war/history views.

- [ ] **Step 4: Run source guards/build and commit.**

## Task 11: Persistence, Save Recovery, And Multiplayer

**Files:**
- Create: `Code/core/grandstrategy/GrandStrategyPersistence.cs`
- Create: `Code/core/grandstrategy/GrandStrategyMultiplayerAdapter.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Test: `Tests/GrandStrategyArmyPersistenceSourceGuard.ps1`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`

- [ ] **Step 1: Write failing persistence/transaction tests.** Cover versioned snapshots for ledgers/armies/routes/commanders/battles/sieges/reports, stable IDs, interrupted round, duplicate round, duplicate occupation/score, war-end reconciliation, recovery pools, and missing/old snapshot fallback.

```csharp
var first = GrandStrategyTransaction.ApplyRound(snapshot, round);
var second = GrandStrategyTransaction.ApplyRound(first, round);
Equal(first.LedgerChecksum, second.LedgerChecksum,
    "duplicate round is idempotent");
```

- [ ] **Step 2: Run tests and verify failure.**

- [ ] **Step 3: Implement snapshot writer/reader.** Use the existing save directory and archive conventions; write a schema version, world generation, stable IDs, checksums, and committed transaction keys. Reject mismatched world generations and initialize a clean state only when no snapshot exists.

- [ ] **Step 4: Hook save/load.** `AW_SavePatch` flushes grand-strategy transactions before native save, writes after native save, observes load path, and restores in the staged pipeline after war data but before projections. New worlds clear all runtime stores.

- [ ] **Step 5: Implement multiplayer adapter.** Host owns all mutations; clients send validated commands and receive snapshots/committed round results. Deterministic rolls are verification data only; clients never commit outcomes.

- [ ] **Step 6: Run persistence/source guards, isolated tests, and build.**

- [ ] **Step 7: Commit.**

## Task 12: Strategic AI, Royal Guard Boundary, And Performance Hardening

**Files:**
- Modify: `Code/core/grandstrategy/GrandStrategyRuntime.cs`
- Modify: `Code/core/grandstrategy/GrandStrategyBattleService.cs`
- Modify: `Code/core/grandstrategy/GrandStrategyArmyService.cs`
- Modify: `Code/patch/AW_GrandStrategyArmyModePatch.cs`
- Modify: `Code/core/presentation/GrandStrategyArmyProjectionService.cs`
- Test: `Tests/GrandStrategyArmyRules.Isolated.Tests/Program.cs`
- Test: `Tests/GrandStrategyArmyIntegrationSourceGuard.ps1`

- [ ] **Step 1: Write failing AI/boundary/performance tests.** Verify non-player kingdoms choose legal rally/merge/split/siege/retreat actions, royal guards remain native until hybrid engagement, native RTS mode has zero grand-strategy mutations, one path request per army, pooled projection bounds, and frame-budgeted monthly processing.

- [ ] **Step 2: Run tests and verify failure.**

- [ ] **Step 3: Implement bounded strategic AI.** Score fronts by enemy strength, war goal, supply, relief risk, and siege demand; schedule only a bounded number of decisions per frame; use same command service as players.

- [ ] **Step 4: Implement royal-guard hybrid adapter.** Detect royal guards without taking ownership of their actors; suppress their native attack only while contributing a special numeric guard force to a grand-strategy battle; restore native behavior after disengagement.

- [ ] **Step 5: Implement performance safeguards.** Maintain one route request per army, bounded route queue, pooled dummies/arrows/banners, dirty projection updates, and frame-budgeted battle/siege rounds; expose diagnostics counters.

- [ ] **Step 6: Run all isolated tests, source guards, and production build.**

Expected: all new tests pass, all existing tests still pass, and `dotnet build AncientWarfare3.csproj` succeeds.

- [ ] **Step 7: Commit.**

## Task 13: Deployment And Game Acceptance

**Files:**
- Modify only generated build/deploy outputs as needed; do not edit unrelated dirty files.
- Test: `Tests/GrandStrategyArmyRulesTests.ps1`
- Test: `Tests/GrandStrategyArmyIntegrationSourceGuard.ps1`
- Test: `Tests/GrandStrategyArmyPersistenceSourceGuard.ps1`

- [ ] **Step 1: Run the complete automated suite.**

Run:

```powershell
dotnet build AncientWarfare3.csproj
dotnet run --project Tests/GrandStrategyArmyRules.Isolated.Tests/GrandStrategyArmyRules.Isolated.Tests.csproj
powershell -ExecutionPolicy Bypass -File Tests/GrandStrategyArmyRulesTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/GrandStrategyArmyIntegrationSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/GrandStrategyArmyPersistenceSourceGuard.ps1
```

Expected: every command exits 0; no existing school/supporter/actor-safety changes are reverted.

- [ ] **Step 2: Deploy source and built mod.** Run `powershell -ExecutionPolicy Bypass -File deploy-local.ps1`; verify `DEPLOY-DONE`, a timestamped backup under the destination, and matching production directories.

- [ ] **Step 3: Start WorldBox visibly and perform acceptance path.** Use the configured `D:\SteamLibrary\steamapps\common\worldbox\worldbox.exe`; create/load a world, enable only Grand Strategy Army, restart, declare a war, verify temporary levy raising, drag a banner, cross land/sea, reinforce, open battle details, retreat, siege, complete temporary occupation, make peace, disband, save, load, and confirm the report and manpower checksum remain unchanged. Verify RTS mode separately retains its existing behavior.

- [ ] **Step 4: Capture logs and final status.** Record mode selection, route count, round transaction IDs, occupation idempotency, save/load checksum, and any remaining acceptance gaps before release.

## Self-Review Checklist

- [ ] **Spec coverage:** Tasks 1-3 cover mode, authority, manpower, troops, training, technology, equipment, raising, organization and disband. Tasks 4-5 cover generals, royal guard boundary, movement, terrain, sea travel, retreat and commands. Tasks 6-9 cover native compatibility, battles, casualties, pursuit, sieges and temporary occupation. Tasks 7/10 cover map projection, dragging, army/battle/siege windows and reports. Task 11 covers persistence, transactions and multiplayer. Task 12 covers AI and performance. Task 13 covers the complete acceptance path and RTS isolation.
- [ ] **Placeholder scan:** No task uses `TBD`, `TODO`, “implement later”, or “write tests for the above”; each code-changing task identifies concrete files, behavior, commands, and expected outputs.
- [ ] **Type consistency:** The mode type is `GrandStrategyArmyMode`; stable IDs are `long`; army/battle/siege/report services use the names established in the file map; command DTOs carry army ID, world generation, client sequence, target tile, and expected revision consistently.
- [ ] **Workspace safety:** Existing dirty files and `.claude/worktrees/rts-army-overhaul` are outside the change list and must remain untouched. Implementation starts in a dedicated worktree or after explicit user authorization.

