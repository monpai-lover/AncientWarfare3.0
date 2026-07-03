# AW3 General Fief Rebellion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the AW3 version of AW2's intended general, fief, military-power, rebellion, restoration, and vassal integration loop.

**Architecture:** Use the existing AW3 lineage/history architecture: SQLite `[TableDef]` tables for durable state, small services under `Code/core/lineage`, Harmony patches only at game event boundaries, and existing `HistoryWriter`/`ChronicleEvents` for biography, kingdom history, and city chronicle output. The system should not scan all actors every year; it should only inspect kings, city leaders, army captains, active generals, and claimants.

**Tech Stack:** C# net48, NeoModLoader, Harmony, WorldBox API, AW3 SQLite archive layer, existing AW3 history/UI services.

---

## Scope

This plan starts after the current vassal baseline:

- `VassalRelation` persistence exists.
- Manual create/remove vassal powers exist.
- `vassal_war` and `independence_war` exist.
- Vassal AI can voluntarily submit, start vassal wars, absorb weak vassals, and rebel for independence.
- Vassal mapmode renders kingdoms by root suzerain color.

This plan does not implement the Mandate of Heaven system. Tianming-only emperor logic, era reforms, and imperial restoration titles remain out of scope.

## File Structure

- Create `Code/core/db/GeneralStateTableItem.cs`: durable general state.
- Create `Code/core/db/FiefGrantTableItem.cs`: durable fief grant records.
- Create `Code/core/lineage/GeneralService.cs`: candidate selection, merit, loyalty, ambition, fief grant, rebellion-risk calculation.
- Create `Code/core/lineage/FiefService.cs`: fief city eligibility, grant/revoke, city ownership checks, fief history.
- Create `Code/core/lineage/GeneralRebellionService.cs`: yearly risk check and rebellion action selection.
- Create `Code/patch/AW_GeneralPatch.cs`: minimal Harmony entry points for yearly kingdom checks, war end merit, city transfer merit, and army captain refresh.
- Modify `Code/content/DiplomacyContent.cs`: register `general_rebellion_war` and `fief_independence_war`.
- Modify `Code/core/lineage/ChronicleKeys.cs`: add general/fief event keys.
- Modify `Code/core/lineage/ChronicleEvents.cs`: add person/kingdom/city history wrappers.
- Modify `Code/ui/windows/HistoryListWindow.cs`: show general/fief role labels in biography event rows.
- Modify `Locales/war.csv`: add war type localization.
- Create `Locales/aw3_generals_fiefs.csv`: UI and history text keys for general/fief system.

## Task 1: Persistent Tables

**Files:**
- Create: `Code/core/db/GeneralStateTableItem.cs`
- Create: `Code/core/db/FiefGrantTableItem.cs`

- [ ] **Step 1: Add `GeneralStateTableItem`**

Create a `[TableDef("GeneralState")]` class with these fields:

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("GeneralState")]
    public class GeneralStateTableItem : AbstractTableItem<GeneralStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long actor_id;
        public string actor_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public long home_city_id = -1;
        public string home_city_name = "";
        public long fief_city_id = -1;
        public string fief_city_name = "";
        public long personal_army_id = -1;
        public int merit_score = 0;
        public int loyalty_score = 50;
        public int ambition_score = 20;
        public int troop_power_snapshot = 0;
        public double appointed_time = -1;
        public double granted_time = -1;
        public double last_reward_time = -1;
        public double last_risk_check_time = -1;
        public int active = 1;
        public int rebelled = 0;
        public string end_reason = "";
    }
}
```

- [ ] **Step 2: Add `FiefGrantTableItem`**

Create a `[TableDef("FiefGrant")]` class:

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("FiefGrant")]
    public class FiefGrantTableItem : AbstractTableItem<FiefGrantTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long grant_id;
        public long general_actor_id = -1;
        public string general_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public long king_actor_id = -1;
        public string king_name = "";
        public long city_id = -1;
        public string city_name = "";
        public string city_color = "";
        public string grant_reason = "";
        public double start_time = -1;
        public double end_time = -1;
        public int revoked = 0;
        public string revoke_reason = "";
    }
}
```

- [ ] **Step 3: Build**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet build
```

Expected: `0 warnings, 0 errors`.

## Task 2: General Service Skeleton

**Files:**
- Create: `Code/core/lineage/GeneralService.cs`

- [ ] **Step 1: Add public event API**

Create `GeneralService` with these public methods:

```csharp
internal static class GeneralService
{
    public static void OnKingdomYear(Kingdom pKingdom) { }
    public static void OnWarEnded(War pWar, WarWinner pWinner) { }
    public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom) { }
    public static bool IsGeneral(Actor pActor) { return false; }
    public static int GetMerit(Actor pActor) { return 0; }
}
```

- [ ] **Step 2: Define candidate filters**

Implement private filters:

- actor is alive and adult.
- actor is not a slave.
- actor is not current kingdom heir.
- actor is not king.
- actor is not royal guard member.
- actor is army captain, city leader, high-merit noble, or royal adult male non-heir.

Use existing helpers where available:

- `SlaveService.IsSlave(actor)`
- `HeirService.IsHeir(actor)`
- `RoyalGuardService.IsRoyalGuard(actor)`
- `ChronicleGate.IsNobleActor(actor)`

- [ ] **Step 3: Add max general count**

Rules:

- 1 general below 3 cities.
- 2 generals at 3-5 cities.
- 3 generals at 6+ cities.

Do not appoint more than this limit.

## Task 3: Yearly Hook

**Files:**
- Create: `Code/patch/AW_GeneralPatch.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs` only if a separate patch conflicts.

- [ ] **Step 1: Hook yearly kingdom update**

Add a postfix for `Kingdom.updateAge` or extend the existing `AW_KingdomPolicyPatch.UpdateAge_Postfix`:

```csharp
GeneralService.OnKingdomYear(__instance);
```

- [ ] **Step 2: Throttle checks**

Inside `GeneralService.OnKingdomYear`, store a kingdom data key such as `aw_general_last_check_year`.

Rules:

- Candidate refresh every 3 years.
- Rebellion risk check every 5 years.
- Fief grant check every 8 years.

- [ ] **Step 3: Build**

Run `dotnet build` and expect `0 warnings, 0 errors`.

## Task 4: Military Merit

**Files:**
- Modify: `Code/core/lineage/GeneralService.cs`
- Modify: `Code/patch/AW_WarPatch.cs` or call from `AW_GeneralPatch.cs`

- [ ] **Step 1: Award war result merit**

On `WarManager.endWar`:

- winning main attacker captain: +10
- winning main defender captain: +10
- losing main captain: +3
- vassal war win: +14
- independence war win: +14
- reclaim/restoration war win: +16

Use `War.getMainAttacker()`, `War.getMainDefender()`, `Kingdom.getCapital()` and `City.getArmy()` where available. If captain lookup fails, skip silently.

- [ ] **Step 2: Award city transfer merit**

On real city transfer:

- new kingdom capital/city army captain: +6
- old kingdom defending city leader if still alive: +2

Do not record transfer merit during load (`pFromLoad`).

- [ ] **Step 3: Record important merit**

When a living general reaches 30, 60, 100 merit:

- person biography: "某某以军功闻名"
- kingdom history only for 60+ merit.
- city chronicle if the general has a fief or home city.

## Task 5: Appoint Generals

**Files:**
- Modify: `Code/core/lineage/GeneralService.cs`
- Modify: `Code/core/lineage/ChronicleKeys.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`

- [ ] **Step 1: Candidate score**

Score:

- merit score * 2
- actor is army captain: +30
- actor is city leader: +20
- noble actor: +15
- adult royal non-heir: +10
- slave: disqualify
- heir: disqualify
- king: disqualify
- royal guard: disqualify

- [ ] **Step 2: Appoint**

If score >= 45 and kingdom is below general cap:

- insert/update `GeneralState`.
- set active = 1.
- initial loyalty = 55.
- initial ambition = 20 + merit / 5.

- [ ] **Step 3: History**

Add:

- person biography: `general_appointed`
- kingdom history: `general_appointed`

Wording:

- "某某以军功受任为大将"

## Task 6: Grant Fiefs

**Files:**
- Create: `Code/core/lineage/FiefService.cs`
- Modify: `Code/core/lineage/GeneralService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`

- [ ] **Step 1: City eligibility**

A city can be granted if:

- same kingdom as general.
- not capital.
- not ruined.
- not already an active fief.
- not current heir's city.
- city was conquered recently, is border-facing, or is general's army/home city.

- [ ] **Step 2: Grant trigger**

Every 8 years per kingdom:

- find active generals with merit >= 45 and no fief.
- choose best eligible city.
- insert `FiefGrant`.
- update `GeneralState.fief_city_id/name`.
- loyalty +15.
- ambition +5.

- [ ] **Step 3: History**

Record:

- person biography: "某某受封于某城"
- kingdom history: "封某某于某城"
- city chronicle: "某城成为某某封地"

## Task 7: Rebellion Risk

**Files:**
- Create: `Code/core/lineage/GeneralRebellionService.cs`
- Modify: `Code/core/lineage/GeneralService.cs`

- [ ] **Step 1: Calculate troop power**

For each active general:

- if actor has army, count units in that army.
- if actor is city leader, add 30% of city warriors.
- if actor has fief, add 30% of fief city warriors.

Store `troop_power_snapshot`.

- [ ] **Step 2: Risk score**

Risk starts at:

- ambition - loyalty.

Add:

- controls over 35% of kingdom soldiers: +25
- fief population over 25% of kingdom population: +15
- king is child or very old: +15
- kingdom lost recent war: +10
- general merit >= 80 and no reward in 20 years: +20
- different shi/clan from king: +10
- vassal kingdom under weak suzerain: +10

Subtract:

- same paternal lineage as king: -15
- recently rewarded: -15
- king has high diplomacy/stewardship if available: -10
- kingdom is winning and stable: -10
- royal guard exists and king is protected: -10

- [ ] **Step 3: Risk history**

If risk >= 65, record once per 15 years:

- person biography: "某某拥兵自重，朝野侧目"
- kingdom history: "某某于某城拥兵自重"

Do not rebel immediately on first high-risk record.

## Task 8: Rebellion Actions

**Files:**
- Modify: `Code/content/DiplomacyContent.cs`
- Modify: `Code/core/lineage/GeneralRebellionService.cs`
- Modify: `Code/core/lineage/VassalService.cs`

- [ ] **Step 1: Register war types**

In `DiplomacyContent.Init()` add:

- `general_rebellion_war`
- `fief_independence_war`

Both can use `war_conquest` template and custom localized type keys.

- [ ] **Step 2: Choose rebellion type**

When risk >= 85 and cooldown passes:

- if general has fallen-kingdom royal claim: start `restoration_war`.
- if general has fief and nearby strong kingdom is friendly: fief becomes independent and then may become vassal of the strong kingdom.
- if general has fief but no patron: start `fief_independence_war`.
- if general is in capital and troop power is high: palace coup attempt, changing king if success.
- otherwise record unrest but do not fire war.

- [ ] **Step 3: War result**

On rebel win:

- new kingdom keeps tech/policy inheritance using existing `KingdomPolicyInheritanceService`.
- victorious rebel may become vassal if a patron supported the rebellion.
- history records in person, old kingdom, new kingdom, and fief city.

On rebel loss:

- general is removed from active general state.
- fief is revoked.
- person biography records execution, pardon, or exile.

## Task 9: Vassal Integration

**Files:**
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Code/core/lineage/VassalAIService.cs`
- Modify: `Code/core/lineage/GeneralRebellionService.cs`

- [ ] **Step 1: Patron support API**

Add:

```csharp
public static bool TrySupportRebelAsVassal(Kingdom pPatron, Kingdom pRebelKingdom, Actor pGeneral)
```

Rules:

- patron is stronger than rebel.
- patron is not at war with rebel.
- patron is enemy or rival of rebel's old kingdom, or has positive opinion of claimant.
- result calls `SetVassal(rebelKingdom, patron, "supported_rebellion")`.

- [ ] **Step 2: Vassal AI awareness**

Vassal AI should consider:

- supporting restoration wars when claimant is useful.
- refusing to absorb a vassal that has active high-risk generals for 10 years.
- independence chance increased when a powerful general has a fief.

## Task 10: UI and Tooltip

**Files:**
- Modify: `Code/ui/windows/HistoryListWindow.cs`
- Modify: `Code/ui/windows/KingdomRosterWindow.cs` if current roster should show generals.
- Create or modify city tooltip patch if one exists for city chronicle/status.

- [ ] **Step 1: Biography role label**

Add role labels:

- `general`
- `fief_holder`
- `rebel_general`

- [ ] **Step 2: City tooltip**

If a city is active fief:

- show "封地：某某"
- show "军功/忠诚/野心"
- show "叛乱风险：低/中/高"

- [ ] **Step 3: Kingdom history**

Keep national history concise:

- appoint general
- grant fief
- fief rebellion starts/ends

Do not spam every yearly merit increment into kingdom history.

## Task 11: Localization

**Files:**
- Create: `Locales/aw3_generals_fiefs.csv`
- Modify: `Locales/war.csv`

- [ ] **Step 1: Add CSV keys**

Required keys:

- `aw_general`
- `aw_fief`
- `aw_fief_holder`
- `aw_general_merit`
- `aw_general_loyalty`
- `aw_general_ambition`
- `aw_general_risk_low`
- `aw_general_risk_mid`
- `aw_general_risk_high`
- `war_type_general_rebellion_war`
- `war_type_fief_independence_war`

- [ ] **Step 2: Build and inspect locale**

Run `dotnet build`.

In game, check no `???` or missing text appears in:

- war names.
- biography rows.
- city tooltip.
- kingdom history.

## Task 12: Verification

**Files:**
- No new files.

- [ ] **Step 1: Compile**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet build
```

Expected: `0 warnings, 0 errors`.

- [ ] **Step 2: Manual game test**

Test map with 3+ Xia kingdoms:

- create normal wars.
- verify army/city leaders gain merit.
- verify no heir/king/slave/royal guard becomes separatist general.
- verify at most 1/2/3 active generals per kingdom by city count.
- verify fief grant never uses capital or heir city.
- verify fief city tooltip appears.
- verify high-risk record appears before any rebellion.
- verify rebellion creates history in person, kingdom, and city histories.
- verify supported rebel can become vassal.
- verify vassal mapmode still uses root suzerain color after rebellion.

- [ ] **Step 3: Save/load**

Save and reload after:

- a general is appointed.
- a fief is granted.
- a rebellion starts.
- a vassal relation changes.

Expected:

- `GeneralState`, `FiefGrant`, and `VassalRelation` remain consistent.
- no duplicate active fief grants for one city.
- no active vassal relation points to a destroyed kingdom.

