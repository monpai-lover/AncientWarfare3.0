# Military Governorate Vassal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add real Xia military-governorate vassal kingdoms with frontier creation, mandatory joint warfare, suzerain-controlled diplomacy, dual-track succession, synchronized colors, existing-vassal-window management, and bounded AI work.

**Architecture:** Extend the existing vassal relation with an explicit subject kind and persist governorate-only state separately. Pure rules own eligibility, naming, permission, war leadership, succession, and budgets; runtime services reuse native kingdom creation, vassal topology, original war APIs, active-general indexes, RTS army commands, and event-driven color/city/death hooks.

**Tech Stack:** C#, Unity/WorldBox APIs, Harmony patches, SQLite reflection tables, CSV localization, .NET rules tests, PowerShell source guards

---

### Task 1: Define subject-kind and governorate rules

**Files:**
- Create: `Code/core/lineage/MilitaryGovernorateRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing tests for all pure invariants**

Cover these exact APIs:

```csharp
True(MilitaryGovernorateRules.CanCreate(
    isXiaSystem: true, cityCount: 7, maxCities: 6),
    "Xia realm over direct limit may create a governorate");
False(MilitaryGovernorateRules.CanCreate(true, 6, 6),
    "realm at limit is not over limit");
False(MilitaryGovernorateRules.CanCreate(false, 9, 6),
    "non-Xia realm cannot create a governorate");
True(MilitaryGovernorateRules.IsEligibleSeat(
    owned: true, capital: false, specialAdministration: false,
    bordersOutsideRootNetwork: true), "external frontier is eligible");
False(MilitaryGovernorateRules.IsEligibleSeat(
    true, true, false, true), "capital is excluded");
Equal("天平军", MilitaryGovernorateRules.CommandName("天平", "军"),
    "regional command suffix is stable");
True(MilitaryGovernorateRules.MustJoinSuzerainWar(
    VassalSubjectKind.MilitaryGovernorate),
    "military governorate obligation is absolute");
False(MilitaryGovernorateRules.CanConductStateDiplomacy(
    VassalSubjectKind.MilitaryGovernorate),
    "military governorate has no state diplomacy");
Equal(1, MilitaryGovernorateRules.AnnualCreationLimit,
    "AI creates at most one governorate per realm-year");
```

- [ ] **Step 2: Link the new production and test files, then verify RED**

Add explicit `<Compile Include>` entries and call `MilitaryGovernorateRulesTests.Run()` from `Program.cs.txt`.

Run the rules project and expect compile failure because production rules do not exist.

- [ ] **Step 3: Implement minimal enums and pure rules**

Create:

```csharp
public enum VassalSubjectKind
{
    Ordinary = 0,
    MilitaryGovernorate = 1
}

public static class MilitaryGovernorateRules
{
    public const int AnnualCreationLimit = 1;
    public const int CityScanBudget = 16;
    public const int GeneralScanBudget = 32;

    public static bool CanCreate(bool isXiaSystem, int cityCount,
        int maxCities) => isXiaSystem && cityCount > maxCities;

    public static bool IsEligibleSeat(bool owned, bool capital,
        bool specialAdministration, bool bordersOutsideRootNetwork) =>
        owned && !capital && !specialAdministration &&
        bordersOutsideRootNetwork;

    public static string CommandName(string region, string suffix) =>
        KingdomNameplateSuffixRules.ProjectName(region, suffix, true);

    public static bool MustJoinSuzerainWar(VassalSubjectKind kind) =>
        kind == VassalSubjectKind.MilitaryGovernorate;

    public static bool CanConductStateDiplomacy(VassalSubjectKind kind) =>
        kind != VassalSubjectKind.MilitaryGovernorate;
}
```

- [ ] **Step 4: Verify GREEN and commit**

Run the rules project, expect `Rule tests passed.`, then commit the four files as `test: define military governorate rules`.

### Task 2: Persist authoritative governorate state

**Files:**
- Modify: `Code/core/db/VassalRelationTableItem.cs`
- Create: `Code/core/db/MilitaryGovernorateStateTableItem.cs`
- Modify: `Code/core/db/LineageArchiveIndexRules.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Create: `Code/core/lineage/MilitaryGovernorateStore.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernoratePersistenceSourceGuard.ps1`

- [ ] **Step 1: Write a failing schema/source guard**

Require `subject_kind`, the new `[TableDef("MilitaryGovernorateState")]`, indexes by subject/suzerain/active state, and runtime keys:

```powershell
foreach ($token in @(
  'public int subject_kind = 0;',
  '[TableDef("MilitaryGovernorateState")]',
  'MILITARY_GOVERNORATE_SUBJECT_KIND',
  'MILITARY_GOVERNORATE_STATE_ID'
)) {
  if (-not $allSource.Contains($token)) { throw "Missing $token" }
}
```

Run and verify failure on the first missing token.

- [ ] **Step 2: Add migration-safe fields and state table**

Add `subject_kind` with default ordinary semantics. Define the state row with:

```csharp
[TableDef("MilitaryGovernorateState")]
public class MilitaryGovernorateStateTableItem :
    AbstractTableItem<MilitaryGovernorateStateTableItem>
{
    [TableItemDef(pIsPrimary: true)] public long state_id;
    public long relation_id = -1;
    public long subject_kingdom_id = -1;
    public long suzerain_kingdom_id = -1;
    public long seat_city_id = -1;
    public long governor_actor_id = -1;
    public long successor_actor_id = -1;
    public long expeditionary_army_id = -1; // unused compatibility field
    public string command_name = "";
    public int created_year = -1;
    public int succession_state = 0;
    public int active = 1;
    public double end_time = -1;
    public string end_reason = "";
}
```

- [ ] **Step 3: Implement store reads/writes and projections**

`MilitaryGovernorateStore` must expose:

```csharp
TryCreate(..., out long stateId)
TryGetActive(Kingdom subject, out MilitaryGovernorateSnapshot snapshot)
GetDirectActive(Kingdom suzerain, int limit)
SetSuccessor(long stateId, long actorId)
SetExpeditionaryArmy(long stateId, long armyId) // legacy compatibility only
End(long stateId, string reason)
RestoreProjection(Kingdom subject)
```

Use this runtime read model:

```csharp
public sealed class MilitaryGovernorateSnapshot
{
    public long StateId = -1;
    public long RelationId = -1;
    public long SubjectKingdomId = -1;
    public long SuzerainKingdomId = -1;
    public long SeatCityId = -1;
    public long GovernorActorId = -1;
    public long SuccessorActorId = -1;
    public long ExpeditionaryArmyId = -1; // unused compatibility projection
    public string CommandName = "";
    public int CreatedYear = -1;
    public int SuccessionState;
}
```

Use parameterized SQLite calls and table indexes; never enumerate all kingdoms for a hot read.

- [ ] **Step 4: Run guard/rules and commit**

Expect schema guard and rules suite to pass. Commit as `feat: persist military governorate state`.

### Task 3: Extend vassal relation APIs

**Files:**
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Code/core/lineage/VassalContractTierRules.cs`
- Modify: `Code/core/lineage/VassalRelationRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateRulesTests.cs.txt`

- [ ] **Step 1: Add failing relation tests**

Assert military terms are autonomy-preserving, zero-tribute, and 100 military obligation, and ordinary relation behavior remains unchanged.

- [ ] **Step 2: Add an explicit creation route**

Add:

```csharp
public static bool SetMilitaryGovernorate(Kingdom pSubject,
    Kingdom pSuzerain, string pReason)
```

Pass `VassalSubjectKind.MilitaryGovernorate` into `SetVassalInternal`, write `SUBJECT_KIND`, force terms `(autonomy: 50, tribute: 0, military: 100)`, and project the kind to kingdom data. Do not encode the kind in `relation_type` or `POLICY_CLASS_STATE`.

- [ ] **Step 3: Restore kind in every relation read path**

Extend `ActiveRelationDetails` and all active-relation SQL selects to read `SUBJECT_KIND`. Ensure old rows default to ordinary.

- [ ] **Step 4: Verify and commit**

Run relation/rules tests and commit as `feat: add military vassal relation kind`.

### Task 4: Build transactional frontier creation

**Files:**
- Create: `Code/core/lineage/MilitaryGovernorateCreationRules.cs`
- Create: `Code/core/lineage/MilitaryGovernorateCreationService.cs`
- Modify: `Code/core/lineage/GeneralService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateRulesTests.cs.txt`
- Test: `Tests/MilitaryGovernorateCreationSourceGuard.ps1`

- [ ] **Step 1: Add RED tests for seat/general scoring and rollback stages**

Define deterministic score APIs and a transaction-stage enum. Assert external frontier priority, merit/loyalty/ambition scoring, scan limits, and rollback requirements for each committed stage.

- [ ] **Step 2: Add bounded candidate APIs**

Use `CentralizationBorderDeploymentService` frontier semantics and `GeneralService.GetActiveGeneralsForReadModel(..., pLimit: 32)`. Do not call unbounded `GetActiveGenerals` from AI or UI refresh.

- [ ] **Step 3: Implement `TryCreate` using native methods**

The service performs:

```csharp
makeNewCivKingdom(general)
seat.setKingdom(subject)
general.joinCity(seat)
subject.setCapital(seat)
VassalService.SetMilitaryGovernorate(subject, suzerain, "military_governorate")
MilitaryGovernorateStore.TryCreate(...)
```

Retire the old general career only after the city and relation are valid. Track stages and rollback city, actor, relation, state, and new kingdom in reverse order on failure.

- [ ] **Step 4: Write chronicles and verify**

Record creation in kingdom, city, and actor chronicles. Run focused tests/source guard and commit as `feat: create frontier military governorates`.

### Task 5: Add bounded player and AI entry points

**Files:**
- Create: `Code/core/lineage/MilitaryGovernorateAiService.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Code/ui/AW_LineageTab.cs`
- Test: `Tests/MilitaryGovernorateCreationSourceGuard.ps1`

- [ ] **Step 1: Add failing budget/source assertions**

Require `AnnualCreationLimit = 1`, `CityScanBudget = 16`, `GeneralScanBudget = 32`, a persisted last-evaluation year, and no `World.world.units` scan.

- [ ] **Step 2: Implement annual AI work**

On the existing bounded kingdom-year scheduler, evaluate Xia eligibility and persistent over-limit state. Process at most one governorate creation per kingdom-year and one bounded candidate batch per invocation.

- [ ] **Step 3: Register player city power**

Add a kingdom-side action that selects a city using the original city map selection flow, validates it through the same creation rules, and opens the general candidate window. Do not create a separate unbounded selector.

- [ ] **Step 4: Verify and commit**

Run guard/rules and commit as `feat: expose military governorate creation`.

### Task 6: Synchronize primary and secondary colors by event

**Files:**
- Create: `Code/core/lineage/MilitaryGovernorateColorService.cs`
- Modify: `Code/patch/AW_KingdomColorPatch.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateRulesTests.cs.txt`
- Test: `Tests/MilitaryGovernorateColorSourceGuard.ps1`

- [ ] **Step 1: Add RED rules and source guard**

Assert only direct active military governorates synchronize and ordinary vassals/tributaries do not. Require the original color-change hook to call `MilitaryGovernorateColorService.OnSuzerainColorChanged` and forbid annual/update polling.

- [ ] **Step 2: Reuse native color APIs**

Implement `CopyFromSuzerain(subject, suzerain)` using the same primary/secondary color operation used by the original kingdom color UI. Creation calls it once. The postfix for the original player-facing color commit calls:

```csharp
MilitaryGovernorateColorService.OnSuzerainColorChanged(__instance);
```

Read direct children through the vassal index/store with a fixed upper bound; do not scan all kingdoms.

- [ ] **Step 3: Add independence recoloring**

On relation end reason `independence_success`, call the original random visual/color generator once.

- [ ] **Step 4: Verify and commit**

Run color guard/rules and commit as `feat: synchronize military governorate colors`.

### Task 7: Redirect diplomacy and defensive war leadership

**Files:**
- Modify: `Code/core/lineage/VassalWarPermissionRules.cs`
- Modify: `Code/core/lineage/WarDecisionService.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Code/core/lineage/DiplomacyProposalRules.cs`
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateWarRulesTests.cs.txt`

- [ ] **Step 1: Add RED war/diplomacy tests**

Cover:

```csharp
CanExternalRealmTargetGovernorate == true
ResolveMainDefender(governorate, rootSuzerain) == rootSuzerain
MustJoinSuzerainWar == true
CanConductStateDiplomacy == false
PeaceController == rootSuzerain
```

Also assert ordinary subjects remain blocked as direct war targets.

- [ ] **Step 2: Redirect declaration before native `startWar`**

When an external target is a military governorate, preserve original target kingdom/city IDs in the war goal, replace the native defender with `GetRootSuzerain(target)`, and start the war through the existing `WarDecisionService` transaction. Do not start a second war.

- [ ] **Step 3: Make obligation deterministic**

In `JoinObligatedNetwork`, set `accepted = true` for military-governorate relations before the probability codec; retain the existing decision codec for ordinary subjects.

- [ ] **Step 4: Block all state diplomacy**

Centralize the subject-kind check in diplomacy proposal availability and runtime commit. Permit only explicit suzerain administrative actions and independence war.

- [ ] **Step 5: Preserve peace/territory ownership semantics**

Peace UI and settlement resolve the root suzerain as controller while cession terms use the stored original governorate and target city. Trigger state repair after each city transfer.

- [ ] **Step 6: Verify and commit**

Run war tests and the full rules suite, then commit as `feat: enforce military governorate war obligations`.

### Task 8: Implement dual-track succession and independence

**Files:**
- Create: `Code/core/lineage/MilitaryGovernorateSuccessionRules.cs`
- Create: `Code/core/lineage/MilitaryGovernorateSuccessionService.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Code/core/lineage/GeneralService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateSuccessionRulesTests.cs.txt`

- [ ] **Step 1: Add RED succession tests**

Cover designated successor priority, grace period, stable-suzerain waiting, weak-center military election, military merit/prowess/support/local-service ordering, deterministic ties, dispatched parent general migration, and invalid/dead candidates.

- [ ] **Step 2: Implement designation and death-event orchestration**

The suzerain may designate a general from either realm. Store only the actor ID until succession commits. Ruler death queues a coalesced bounded succession operation; it does not scan synchronously in the death patch.

- [ ] **Step 3: Commit succession through native kingdom APIs**

Move a dispatched parent general only at commit, close previous career state, assign native king identity, clear old heir projections, and write actor/kingdom chronicles.

- [ ] **Step 4: Handle independence outcomes**

On success end relation/state, restore normal titles/diplomacy, and generate independent colors. On failure expose replacement of general and successor to the suzerain.

- [ ] **Step 5: Verify and commit**

Run succession/full rules tests and commit as `feat: add governorate succession and independence`.

### Task 9: Project command names, ruler titles, and map identity

**Files:**
- Modify: `Code/core/lineage/KingdomNameplateSuffixRules.cs`
- Modify: `Code/core/lineage/HeirTitleSelectionRules.cs`
- Modify: `Code/core/lineage/RulerAppellationRules.cs`
- Modify: `Code/core/lineage/RulerAppellationService.cs`
- Modify: `Code/core/policy/VassalMapModeService.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/ui/components/VassalNameplateSuzerainFlag.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateRulesTests.cs.txt`

- [ ] **Step 1: Add RED presentation tests**

Assert active governorate projects `军`, `将军`, and `留后`; command names do not double-suffix; ordinary kingdoms and independent former governorates use existing title rules.

- [ ] **Step 2: Add relationship-aware overloads**

Do not replace generic title logic. Add explicit `pIsMilitaryGovernorate` inputs before existing mandate/rebel/republic resolution and return military labels only while the relation is active.

- [ ] **Step 3: Add map/nameplate marker**

Keep hierarchy and synchronized native colors. Add only a military-governorate icon/legend and command suffix; do not add a per-frame query. Feed the marker from cached subject-kind projection.

- [ ] **Step 4: Verify and commit**

Run presentation tests/source guards and commit as `feat: display military governorate identity`.

### Task 10: Integrate management into the existing vassal window

**Files:**
- Modify: `Code/ui/windows/MilitaryGovernorateWindow.cs`
- Modify: `Code/ui/windows/VassalRelationWindow.cs`
- Modify: `Code/ui/items/VassalRelationListItem.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Locales/others.csv`
- Modify: `Locales/aw3_diplomacy.csv`
- Modify: `Locales/aw3_ancestry_mapmode.csv`
- Test: `Tests/MilitaryGovernorateUiSourceGuard.ps1`

- [ ] **Step 1: Add RED UI source guard**

Require the existing `VassalRelationWindow`, bounded general read-model calls, live portrait reuse in the creation candidate window, localized labels, and no `World.world.units` scan.

- [ ] **Step 2: Build the two-stage workflow**

Keep the kingdom-side native city selection. A valid city opens the temporary creation candidate window and lists at most 32 generals with live portrait, merit, loyalty, ambition, and command. Confirmation calls only `MilitaryGovernorateCreationService.TryCreate`.

- [ ] **Step 3: Add management controls**

Extend military-governorate rows and context actions in the existing vassal-management window. Show suzerain, seat, general, successor, and military obligation. Commands: designate successor, replace officers after failed rebellion, and rename command through the existing kingdom rename flow. Do not create a separate management window.

- [ ] **Step 4: Localize and verify**

Add simplified Chinese, English, and traditional Chinese text for all labels, outcomes, failure reasons, `军`, `将军`, and `留后`. Run UI guard and commit as `feat: integrate military governorate vassal management`.

### Task 11: Integrate recovery, benchmarks, and final deployment

**Files:**
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Code/core/db/LineageArchiveIndexRules.cs`
- Create: `Tests/MilitaryGovernoratePerformanceSourceGuard.ps1`
- Modify: `Code/api/multiplayer/AW3MultiplayerStrategicStateModels.cs`
- Modify: `Code/core/multiplayer/AW3MultiplayerStrategicStateCoordinator.cs`

- [ ] **Step 1: Add RED recovery/performance guards**

Require indexed active-state restoration, coalesced repair, explicit budgets, event hooks, and absence of governorate loops in `Update`, actor-age loops, or all-unit scans.

- [ ] **Step 2: Restore persisted projections and stale state**

On world load, query active governorate rows by index and enqueue bounded repairs for missing kingdoms, seats, rulers, successors, or relations. Ignore the unused expeditionary-army compatibility column. End irrecoverable rows with explicit reasons.

- [ ] **Step 3: Run complete verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests\MilitaryGovernorateCreationSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\MilitaryGovernorateColorSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\MilitaryGovernorateUiSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\MilitaryGovernoratePerformanceSourceGuard.ps1
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore -p:TargetFrameworkVersion=v4.8.1
```

Expected: all focused guards and rules pass; main build has zero errors. Report any unrelated pre-existing warnings.

- [ ] **Step 4: Deploy source package and verify hashes**

Copy only changed `Code`, `Locales`, and required data files to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`. Do not copy DLL, `bin`, or `obj`. Compare SHA256 hashes for every deployed file.

- [ ] **Step 5: Commit final integration**

Commit recovery/guard changes as `feat: complete military governorate integration`, then verify `git status --short` preserves only unrelated user work.
