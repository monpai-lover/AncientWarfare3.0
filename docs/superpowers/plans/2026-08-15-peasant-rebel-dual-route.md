# Peasant Rebel Dual-Route Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Add AI-selected founding and bandit routes to Mandate peasant rebellions, including fixed original wooden walls, bandit diplomacy and territory restrictions, titles, persistence, and one-way founding conversion.

**Architecture:** Wrap MandateRebelService with a route coordinator and two route behaviors. Preserve the current founding flow; isolate bandit runtime and walls in focused services. Keep decisions in detached rules and enforce them at existing AW3 and original WorldBox mutation boundaries.

**Tech Stack:** C#/.NET, Harmony, WorldBox 0.51 APIs, Newtonsoft.Json, AW3 standalone rules tests, PowerShell guards, CSV localization.

---

## Original-Code Reuse Contract

Reference source, read-only:

    F:\WorldBox New Mod\AssetRipper_export_20260628_163320\ExportedProject\Assets\Scripts\Assembly-CSharp

Never edit that directory or copy its implementations. Reuse:

- City.makeOwnKingdom from City.cs:2250 for the realm split.
- Actor.generateName(MetaType.Kingdom, seed), used by Kingdom.cs:443, for a fresh cultural name root.
- WarManager.getWars and WarManager.endWar(..., WarWinner.Peace) from WarManager.cs for real peace and cleanup.
- City.recalculateNeighbourZones and City.border_zones from City.cs for the entry-time boundary.
- WorldTile.neighboursAll and zone_city for perimeter membership.
- TopTileLibrary.wall_wild from TopTileLibrary.cs:981 as the only wall asset.
- WorldTile.setTopTileType from WorldTile.cs:528 for placement and repair.
- City.joinAnotherKingdom from City.cs:2318 as the final ownership boundary.
- Normal KingdomManager extinction when the last city is lost.

AW3 adds route state, rules, permission gates, wall-coordinate persistence, history, and orchestration only.

## File Map

Create:

- Code/core/lineage/PeasantRebelRouteRules.cs
- Code/core/lineage/PeasantRebelRouteBehavior.cs
- Code/core/lineage/PeasantRebelRouteService.cs
- Code/core/lineage/PeasantRebelFoundingRoute.cs
- Code/core/lineage/PeasantRebelBanditRoute.cs
- Code/core/lineage/PeasantRebelBanditWallService.cs
- Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt
- Tests/PeasantRebelRouteRuntimeSourceGuard.ps1

Modify:

- Code/core/lineage/LineageKeys.cs
- Code/core/lineage/MandateRebelService.cs
- Code/core/lineage/WarDecisionService.cs
- Code/patch/AW_WarPatch.cs
- Code/patch/AW_CityOccupationAccelerationPatch.cs
- Code/patch/AW_ChroniclePatch.cs
- Code/core/lineage/WarPeaceSettlementRuntime.cs
- Code/core/lineage/RulerAppellationRules.cs
- Code/core/lineage/RulerAppellationService.cs
- Code/core/lineage/HeirTitleRules.cs
- Code/core/lineage/HeirTitleSelectionRules.cs
- Code/core/lineage/HistoryLocalizationRules.cs
- Code/core/multiplayer/AW3RuntimeRestorePipeline.cs
- locales/others.csv
- locales/aw3_mandate_extra.csv
- Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
- Tests/AncientWarfare3.Rules.Tests/NameSystemRulesTests.cs.txt
- Tests/AncientWarfare3.Rules.Tests/HistoryLocalizationRulesTests.cs.txt

## Task 1: Create The Isolated Feature Worktree

**Files:** None.

- [ ] **Step 1: Inspect existing state**

    git status --short
    git worktree list

Expected: known linked RTS state may appear. Do not touch it or the paused grand-strategy worktree.

- [ ] **Step 2: Create and enter the feature worktree**

    git worktree add '.worktrees/peasant-rebel-dual-route' -b 'feature/peasant-rebel-dual-route' master
    git -C '.worktrees/peasant-rebel-dual-route' status --short

Expected: clean worktree. Run later commands there.

## Task 2: Define Detached Route Rules Test-First

**Files:**

- Create: Code/core/lineage/PeasantRebelRouteRules.cs
- Create: Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt

- [ ] **Step 1: Register the test and production rule**

Add:

    <Compile Include="PeasantRebelRouteRulesTests.cs.txt" />
    <Compile Include="..\..\Code\core\lineage\PeasantRebelRouteRules.cs"
             Link="Production\PeasantRebelRouteRules.cs" />

Add a focused switch and full-suite call:

    if (args.Length == 1 && args[0] == "--peasant-rebel-routes")
    {
        PeasantRebelRouteRulesTests.Run();
        Console.WriteLine("Peasant rebel route rules passed.");
        return;
    }

- [ ] **Step 2: Write failing tests**

Create PeasantRebelRouteRulesTests.cs.txt with these assertions and the same local True, False, and Equal helpers used by adjacent tests:

    Equal(50, PeasantRebelRouteRules.FoundingChance(0, 0, 0, 0),
        "neutral facts produce fifty percent");
    Equal(10, PeasantRebelRouteRules.FoundingChance(-15, -15, -20, 0),
        "chance has a ten percent floor");
    Equal(90, PeasantRebelRouteRules.FoundingChance(15, 15, 20, 10),
        "chance has a ninety percent ceiling");
    Equal(PeasantRebelRouteIds.Founding,
        PeasantRebelRouteRules.SelectRoute(49, 50),
        "roll below chance chooses founding");
    Equal(PeasantRebelRouteIds.Bandit,
        PeasantRebelRouteRules.SelectRoute(50, 50),
        "roll at chance chooses bandit");
    False(PeasantRebelRouteRules.CanDeclareWar(true, false, false),
        "bandit cannot declare");
    False(PeasantRebelRouteRules.CanDeclareWar(false, true, false),
        "non-origin cannot attack bandit");
    True(PeasantRebelRouteRules.CanDeclareWar(false, true, true),
        "origin can suppress bandit");
    True(PeasantRebelRouteRules.ShouldBypassTruce(true, true),
        "origin suppression bypasses entry truce");
    False(PeasantRebelRouteRules.CanAcquireCity(true, 1, false),
        "bandit cannot acquire second city");
    True(PeasantRebelRouteRules.CanAcquireCity(false, 8, false),
        "founding route remains unrestricted");
    True(PeasantRebelRouteRules.ShouldRepairWalls(true, false),
        "peace permits repair");
    False(PeasantRebelRouteRules.ShouldRepairWalls(true, true),
        "suppression pauses repair");
    True(PeasantRebelRouteRules.CanEvaluateWeakOriginTransition(
            3, true, true, 0, 0),
        "eligible three-year hideout can convert");
    False(PeasantRebelRouteRules.CanEvaluateWeakOriginTransition(
            2, true, true, 0, 0),
        "two-year hideout cannot convert");
    Equal(90, PeasantRebelRouteRules.TransitionChance(
            true, 4, true, 15, 15),
        "transition chance clamps");
    Equal("赤眉贼", PeasantRebelRouteRules.ComposeName(
            "赤眉", PeasantRebelRouteIds.Bandit),
        "bandit suffix is stable");
    Equal("赤眉义军", PeasantRebelRouteRules.ComposeName(
            "赤眉", PeasantRebelRouteIds.Founding),
        "conversion keeps root");
    Equal(PeasantRebelRouteIds.Founding,
        PeasantRebelRouteRules.ResolvePersistedRoute("", true),
        "old rebel saves default to founding");

- [ ] **Step 3: Observe failure**

    $env:DOTNET_ROLL_FORWARD='Major'
    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --peasant-rebel-routes

Expected: compile failure because rule types do not exist.

- [ ] **Step 4: Implement the pure rule surface**

Create `PeasantRebelRouteRules.cs` with `using System;` and the existing
`AncientWarfare3.core.lineage` namespace:

    public static class PeasantRebelRouteIds
    {
        public const string Founding = "founding";
        public const string Bandit = "bandit";
    }

    public static int FoundingChance(int leader, int city,
        int origin, int turmoil)
    {
        return Math.Clamp(50 + Math.Clamp(leader, -15, 15) +
            Math.Clamp(city, -15, 15) + Math.Clamp(origin, -20, 20) +
            Math.Clamp(turmoil, 0, 10), 10, 90);
    }

    public static string SelectRoute(int roll, int chance)
    {
        return Math.Clamp(roll, 0, 99) < Math.Clamp(chance, 10, 90)
            ? PeasantRebelRouteIds.Founding
            : PeasantRebelRouteIds.Bandit;
    }

    public static bool CanDeclareWar(bool attackerBandit,
        bool defenderBandit, bool attackerIsOrigin)
    {
        if (attackerBandit) return false;
        return !defenderBandit || attackerIsOrigin;
    }

    public static bool CanAcquireCity(bool bandit, int currentCityCount,
        bool alreadyOwned)
    {
        return !bandit || alreadyOwned || currentCityCount == 0;
    }

    public static bool ShouldBypassTruce(bool defenderBandit,
        bool attackerIsOrigin)
    {
        return defenderBandit && attackerIsOrigin;
    }

    public static bool ShouldRepairWalls(bool bandit,
        bool suppressionActive)
    {
        return bandit && !suppressionActive;
    }

    public static bool CanEvaluateWeakOriginTransition(int banditAgeYears,
        bool originWeak, bool turmoil, int cityFactor, int leaderFactor)
    {
        return banditAgeYears >= 3 && originWeak && turmoil &&
               cityFactor >= 0 && leaderFactor >= 0;
    }

    public static int TransitionChance(bool quarterStrength,
        int hostileWarCount, bool capitalLost, int cityFactor,
        int leaderFactor)
    {
        int chance = 20 + (quarterStrength ? 20 : 0) +
            Math.Min(20, Math.Max(0, hostileWarCount - 1) * 10) +
            (capitalLost ? 15 : 0) +
            Math.Clamp(cityFactor, 0, 15) +
            Math.Clamp(leaderFactor, 0, 15);
        return Math.Clamp(chance, 20, 90);
    }

    public static string ComposeName(string root, string route)
    {
        return (root ?? "").Trim() +
            (route == PeasantRebelRouteIds.Bandit
                ? "\u8d3c" : "\u4e49\u519b");
    }

    public static string ResolvePersistedRoute(string storedRoute,
        bool currentPeasantRebel)
    {
        string route = (storedRoute ?? "").Trim();
        if (route == PeasantRebelRouteIds.Founding ||
            route == PeasantRebelRouteIds.Bandit) return route;
        return currentPeasantRebel ? PeasantRebelRouteIds.Founding : "";
    }

Keep this file detached from Unity and WorldBox types; later tasks extend this
same pure rule surface.

- [ ] **Step 5: Run and commit**

    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --peasant-rebel-routes
    git add Code/core/lineage/PeasantRebelRouteRules.cs Tests/AncientWarfare3.Rules.Tests
    git commit -m "test: define peasant rebel route rules"

## Task 3: Add Persisted Coordination And Behavior Boundaries

**Files:**

- Create: Code/core/lineage/PeasantRebelRouteBehavior.cs
- Create: Code/core/lineage/PeasantRebelRouteService.cs
- Create: Code/core/lineage/PeasantRebelFoundingRoute.cs
- Modify: Code/core/lineage/LineageKeys.cs
- Modify: Code/core/lineage/MandateRebelService.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt

- [ ] **Step 1: Add failing factor tests**

    Equal(-15, PeasantRebelRouteRules.LeaderFactor(
            0, 0, 0, false, true), "weak peaceful leader");
    Equal(15, PeasantRebelRouteRules.LeaderFactor(
            20, 20, 20, true, false), "strong ambitious leader");
    Equal(0, PeasantRebelRouteRules.CityFactor(100, 100),
        "median city is neutral");
    Equal(-15, PeasantRebelRouteRules.CityFactor(50, 100),
        "half median city");
    Equal(15, PeasantRebelRouteRules.CityFactor(150, 100),
        "one-and-a-half median city");

Implement:

    public static int LeaderFactor(int warfare, int stewardship,
        int diplomacy, bool ambitious, bool peaceful)
    {
        int average = Math.Clamp(
            (Math.Max(0, warfare) + Math.Max(0, stewardship) +
             Math.Max(0, diplomacy)) / 3, 0, 20);
        int personality = (ambitious ? 5 : 0) - (peaceful ? 5 : 0);
        return Math.Clamp(average - 10 + personality, -15, 15);
    }

    public static int CityFactor(int population, int originMedian)
    {
        if (originMedian <= 0) return 0;
        double ratio = Math.Max(0, population) / (double)originMedian;
        return Math.Clamp((int)Math.Round((ratio - 1d) * 30d), -15, 15);
    }

    public static int OriginStrengthFactor(int originStrength,
        int rebelStrength)
    {
        if (originStrength <= 0) return 20;
        double ratio = Math.Max(0, rebelStrength) /
                       (double)originStrength;
        if (ratio <= 0.25d) return -20;
        if (ratio >= 1d) return 20;
        return Math.Clamp((int)Math.Round(
            -20d + (ratio - 0.25d) / 0.75d * 40d), -20, 20);
    }

Run the focused test command from Task 2 and expect PASS.

- [ ] **Step 2: Add route persistence keys**

Add beside current `MANDATE_REBEL` keys:

    public const string MANDATE_REBEL_ROUTE =
        "aw_mandate_rebel_route";
    public const string MANDATE_REBEL_NAME_ROOT =
        "aw_mandate_rebel_name_root";
    public const string MANDATE_REBEL_FOUNDING_CITY_ID =
        "aw_mandate_rebel_founding_city_id";
    public const string MANDATE_REBEL_ROUTE_CREATED_YEAR =
        "aw_mandate_rebel_route_created_year";
    public const string MANDATE_REBEL_ROUTE_LAST_YEAR =
        "aw_mandate_rebel_route_last_year";
    public const string MANDATE_REBEL_ORIGIN_CITY_COUNT =
        "aw_mandate_rebel_origin_city_count";
    public const string MANDATE_REBEL_ORIGIN_STRENGTH =
        "aw_mandate_rebel_origin_strength";
    public const string MANDATE_REBEL_ORIGIN_CAPITAL_ID =
        "aw_mandate_rebel_origin_capital_id";
    public const string MANDATE_REBEL_ORIGIN_RULER_ID =
        "aw_mandate_rebel_origin_ruler_id";
    public const string MANDATE_REBEL_BANDIT_WALLS =
        "aw_mandate_rebel_bandit_walls";
    public const string MANDATE_REBEL_BANDIT_WALL_CURSOR =
        "aw_mandate_rebel_bandit_wall_cursor";

Every value is stored in `Kingdom.data`.

- [ ] **Step 3: Define the route contract**

    internal readonly struct PeasantRebelRouteEntryContext
    {
        public Kingdom Rebel { get; }
        public Kingdom Origin { get; }
        public City FoundingCity { get; }
        public Actor Founder { get; }
    }

    internal interface IPeasantRebelRouteBehavior
    {
        string Id { get; }
        bool Enter(PeasantRebelRouteEntryContext context);
        void OnKingdomYear(Kingdom kingdom);
        bool CanDeclareWar(Kingdom kingdom);
        bool CanReceiveDirectWar(Kingdom kingdom, Kingdom attacker);
        bool CanAcquireCity(Kingdom kingdom, City city);
        string ComposeStateName(string root);
        string RulerTitleKey { get; }
        string HeirTitleKey { get; }
        void Exit(Kingdom kingdom);
        void OnKingdomDestroying(Kingdom kingdom);
    }

Use a full context constructor assigning all four properties. Founding returns
`true` from all three permission methods, composes `<root>义军`, and exposes
empty route-title keys. Bandit returns `false` for declaration, permits only
the persisted origin as a direct attacker, delegates city checks to
`PeasantRebelRouteRules.CanAcquireCity`, composes `<root>贼`, and exposes
`aw_bandit_ruler_title` / `aw_bandit_heir_title`.

- [ ] **Step 4: Implement the coordinator surface**

Declare these exact coordinator signatures:

    internal static string GetRouteId(Kingdom kingdom)
    internal static bool IsBandit(Kingdom kingdom)
    internal static bool IsBanditOrEntering(Kingdom kingdom)
    internal static bool InitializeAndEnter(Kingdom rebel, Kingdom origin,
        City foundingCity, Actor founder)
    internal static void OnKingdomYear(Kingdom kingdom)
    internal static bool ConvertBanditToFounding(Kingdom kingdom,
        Kingdom origin)
    internal static bool CanAcquireCity(Kingdom recipient, City city)
    internal static bool CanStartWar(Kingdom attacker, Kingdom defender,
        out bool bypassTruce, out string reason)
    internal static void ClearRuntime()

Define `private static readonly Dictionary<long, string> RuntimeByKingdom` as a
rebuildable lookup only; `Kingdom.data` stays authoritative. Register the
founding behavior in this task. Task 4 registers the completed bandit behavior
before enabling live route selection.

Use a kingdom-specific prospective entry scope:

    [ThreadStatic] private static long? _enteringBanditKingdomId;

    private sealed class BanditEntryScope : IDisposable
    {
        private readonly long? _previous;
        internal BanditEntryScope(long kingdomId)
        {
            _previous = _enteringBanditKingdomId;
            _enteringBanditKingdomId = kingdomId;
        }
        public void Dispose()
        {
            _enteringBanditKingdomId = _previous;
        }
    }

Draw and persist a fresh name root through original code:

    long seed = rebel.getID() ^ (founder.getID() << 1) ^
                ((long)Date.getCurrentYear() << 32);
    string root = founder.generateName(MetaType.Kingdom, seed);
    if (string.IsNullOrWhiteSpace(root)) root = rebel.name ?? "";
    rebel.data.set(LineageKeys.MANDATE_REBEL_NAME_ROOT, root.Trim());

- [ ] **Step 5: Implement thin adapters**

Founding `Enter` calls `MandateRebelService.EnterFoundingRoute`; its annual
method calls `RunFoundingRouteYear`. Complete all non-entry contract members
with the founding values specified in Step 3. Do not wire creation dispatch in
this task; Task 4 adds the complete bandit implementation and then changes the
live creation path, so this intermediate commit remains buildable.

- [ ] **Step 6: Build and commit**

    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --peasant-rebel-routes
    dotnet build AncientWarfare3.csproj --no-restore
    git add Code/core/lineage Tests/AncientWarfare3.Rules.Tests
    git commit -m "feat: add peasant rebel route framework"

## Task 4: Dispatch Creation And Annual Work

**Files:**

- Modify: Code/core/lineage/MandateRebelService.cs
- Modify: Code/core/lineage/PeasantRebelRouteService.cs
- Create: Code/core/lineage/PeasantRebelBanditRoute.cs
- Create: Tests/PeasantRebelRouteRuntimeSourceGuard.ps1

- [ ] **Step 1: Write a failing creation-order source guard**

Create the guard with this reusable assertion helper:

    $ErrorActionPreference = 'Stop'
    function Require([string]$Text, [string]$Needle,
        [string]$Message) {
        if (-not $Text.Contains($Needle)) { throw $Message }
    }
    $mandate = Get-Content -Raw 'Code/core/lineage/MandateRebelService.cs'
    $route = Get-Content -Raw 'Code/core/lineage/PeasantRebelRouteService.cs'
    $bandit = Get-Content -Raw 'Code/core/lineage/PeasantRebelBanditRoute.cs'

Call `Require` for:

- CreateRebelKingdom calls PeasantRebelRouteService.InitializeAndEnter.
- Existing TryPullAlignedCities and StartRebelWar live behind EnterFoundingRoute.
- Route service calls generateName(MetaType.Kingdom.
- Bandit service calls World.world.wars.endWar with WarWinner.Peace.

Run:

    pwsh -File 'Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'

Expected: FAIL on the first missing integration string.

- [ ] **Step 2: Move existing founding behavior without rewriting it**

In CreateRebelKingdom:

    MarkRebelKingdom(rebel, pFounder, pOriginKingdom);
    if (!PeasantRebelRouteService.InitializeAndEnter(
            rebel, pOriginKingdom, pCity, pFounder))
        PeasantRebelRouteService.EnterFoundingFallback(
            rebel, pOriginKingdom, pCity);

Extract:

    internal static void EnterFoundingRoute(
        Kingdom rebel, Kingdom origin, City seed)
    {
        TryPullAlignedCities(rebel, origin, seed);
        StartRebelWar(origin, rebel);
    }

    internal static void RunFoundingRouteYear(Kingdom kingdom)
    {
        EnsureRebelGovernment(kingdom);
        MobilizeRebelForces(kingdom);
        TryClaimMandate(kingdom);
    }

`EnterFoundingFallback` persists `founding`, renames from the saved root, and
then invokes `EnterFoundingRoute`, so a failed prospective bandit transaction
cannot leave an unmarked hybrid. Keep current annual de-duplication, then call
`PeasantRebelRouteService.OnKingdomYear(pKingdom)`.

- [ ] **Step 3: Extract weighted facts without world scans**

    int leader = PeasantRebelRouteRules.LeaderFactor(
        SafeStat(founder, "warfare"), SafeStat(founder, "stewardship"),
        SafeStat(founder, "diplomacy"), founder.hasTrait("ambitious"),
        founder.hasTrait("peaceful") || founder.hasTrait("pacifist"));
    int city = PeasantRebelRouteRules.CityFactor(
        foundingCity.getPopulationPeople(), MedianOriginCityPopulation(origin));
    int originFactor = PeasantRebelRouteRules.OriginStrengthFactor(
        RealmStrength(origin), RealmStrength(rebel));
    int turmoil = Math.Min(10,
        Math.Max(0, CountActiveWars(origin) - 1) * 5 +
        (origin.capital?.data == null || !origin.hasKing() ? 5 : 0));

Implement detached-fact helpers with no `World.world.kingdoms` scan:

    private static int RealmStrength(Kingdom kingdom)
    {
        if (kingdom?.data == null) return 0;
        int population = 0;
        int cities = 0;
        foreach (City city in kingdom.getCities())
        {
            if (city?.data == null || city.isRekt()) continue;
            cities++;
            population += Math.Max(0, city.getPopulationPeople());
        }
        int warriors = 0;
        foreach (Actor unit in kingdom.getUnits())
            if (unit?.data != null && !unit.isRekt() && unit.isWarrior())
                warriors++;
        return population + warriors * 5 + cities * 50;
    }

    private static int CountActiveWars(Kingdom kingdom)
    {
        int count = 0;
        foreach (War war in kingdom.getWars())
            if (war?.data != null && !war.hasEnded()) count++;
        return count;
    }

Persist origin city count, strength, capital ID, ruler ID, founding city,
created year, last evaluation year, and generated root before selecting. Use
`Randy.randomInt(0, 100)` with `FoundingChance` and `SelectRoute` only on
simulation authority.

- [ ] **Step 4: Implement real bandit entry**

Inside the prospective-bandit scope:

    using var wars = new ListPool<War>(context.Rebel.getWars());
    foreach (War war in wars)
        if (war?.data != null && !war.hasEnded())
            World.world.wars.endWar(war, WarWinner.Peace);

Before ending wars, retain the founding city with original ownership APIs:

    foreach (City city in new List<City>(context.Rebel.getCities()))
    {
        if (city == context.FoundingCity) continue;
        city.joinAnotherKingdom(context.Origin,
            pCaptured: false, pRebellion: true);
    }

If any extra city remains, any active war remains, or the founding city is no
longer owned by the rebel, return `false` without committing `bandit`; the
caller enters the founding fallback. While the prospective scope is still
active, apply and verify the bandit name/title projection. Wall placement in
Task 6 is best-effort. Write the authoritative bandit route marker and cache it
only after identity, peace, and the one-city invariant are valid.

At the top of SettleRebelGovernment:

    if (PeasantRebelRouteService.IsBanditOrEntering(pKingdom)) return;

After peace and one-city checks, compose the stored root plus 贼, call original
`Kingdom.setName`, refresh through `KingdomRenameProjectionService`, and verify
the stored display name before the route marker is committed. Founding entry
composes the same root plus 义军. A failed prospective rename falls back to
founding and overwrites the prospective display through the same projection
boundary.

- [ ] **Step 5: Verify and commit**

    pwsh -File 'Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --peasant-rebel-routes
    dotnet build AncientWarfare3.csproj --no-restore
    git add Code Tests/PeasantRebelRouteRuntimeSourceGuard.ps1
    git commit -m "feat: select peasant rebel routes at uprising"

## Task 5: Enforce Bandit Diplomacy And The One-City Invariant

**Files:**

- Modify: Code/core/lineage/PeasantRebelRouteRules.cs
- Modify: Code/core/lineage/PeasantRebelRouteService.cs
- Modify: Code/core/lineage/WarDecisionService.cs
- Modify: Code/patch/AW_WarPatch.cs
- Modify: Code/patch/AW_CityOccupationAccelerationPatch.cs
- Modify: Code/core/lineage/WarPeaceSettlementRuntime.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt
- Modify: Tests/PeasantRebelRouteRuntimeSourceGuard.ps1

- [ ] **Step 1: Add failing permission tests**

Add these assertions to `PeasantRebelRouteRulesTests.Run`:

    False(PeasantRebelRouteRules.CanDeclareWar(
            attackerBandit: true, defenderBandit: false,
            attackerIsOrigin: false),
        "bandits cannot initiate wars");
    False(PeasantRebelRouteRules.CanDeclareWar(
            attackerBandit: false, defenderBandit: true,
            attackerIsOrigin: false),
        "unrelated kingdoms cannot directly attack bandits");
    True(PeasantRebelRouteRules.CanDeclareWar(
            attackerBandit: false, defenderBandit: true,
            attackerIsOrigin: true),
        "the recorded origin may suppress its bandits");
    True(PeasantRebelRouteRules.ShouldBypassTruce(
            defenderBandit: true, attackerIsOrigin: true),
        "suppression bypasses the entry peace blocker");
    False(PeasantRebelRouteRules.CanAcquireCity(
            bandit: true, currentCityCount: 1, alreadyOwned: false),
        "a second city is rejected");
    True(PeasantRebelRouteRules.CanAcquireCity(
            bandit: true, currentCityCount: 1, alreadyOwned: true),
        "an idempotent transfer of the founding city is permitted");

Run:

    $env:DOTNET_ROLL_FORWARD='Major'
    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --peasant-rebel-routes

Expected: FAIL until the complete route permission methods are present.

- [ ] **Step 2: Make the coordinator the only permission source**

Implement these methods in `PeasantRebelRouteService`:

    internal static bool CanAcquireCity(Kingdom pRecipient, City pCity)
    {
        if (!IsBanditOrEntering(pRecipient)) return true;
        bool alreadyOwned = pCity?.kingdom == pRecipient;
        int count;
        try { count = pRecipient?.countCities() ?? 0; }
        catch { count = pRecipient?.hasCities() == true ? 1 : 0; }
        return PeasantRebelRouteRules.CanAcquireCity(
            bandit: true, currentCityCount: count, alreadyOwned);
    }

    internal static bool CanStartWar(Kingdom pAttacker,
        Kingdom pDefender, out bool pBypassTruce, out string pReason)
    {
        pBypassTruce = false;
        pReason = "";
        bool attackerBandit = IsBanditOrEntering(pAttacker);
        bool defenderBandit = IsBanditOrEntering(pDefender);
        bool attackerIsOrigin = defenderBandit &&
            ReadOriginKingdomId(pDefender) == (pAttacker?.id ?? -1L);
        if (!PeasantRebelRouteRules.CanDeclareWar(attackerBandit,
                defenderBandit, attackerIsOrigin))
        {
            pReason = attackerBandit
                ? "bandit_cannot_declare_war"
                : "only_origin_can_suppress_bandit";
            return false;
        }
        pBypassTruce = PeasantRebelRouteRules.ShouldBypassTruce(
            defenderBandit, attackerIsOrigin);
        return true;
    }

    internal static bool IsOriginSuppressionPair(Kingdom pAttacker,
        Kingdom pDefender)
    {
        return IsBanditOrEntering(pDefender) &&
               ReadOriginKingdomId(pDefender) == (pAttacker?.id ?? -1L);
    }

`ReadOriginKingdomId` reads the existing
`MANDATE_REBEL_ORIGIN_KINGDOM_ID` value. Do not infer the origin from current
city ownership or diplomacy.

- [ ] **Step 3: Gate queued, AW3, and native war starts**

At the beginning of `CanStartCivilWar`, `CanQueueWarPair`, and private
`StartWar`, call `PeasantRebelRouteService.CanStartWar`. A suppression pair
skips active treaty, vassal, alliance, casus-belli, and no-CB checks, but still
keeps participant validity and the existing already-at-war check. Use this
shape in private `StartWar`:

    if (!PeasantRebelRouteService.CanStartWar(pAttacker, pDefender,
            out bool routeBypass, out pFailureReason)) return null;

    bool activeTreaty = DiplomacyProposalService.HasActiveWarBlocker(
        pAttacker, pDefender) || mainDefender != pDefender &&
        DiplomacyProposalService.HasActiveWarBlocker(
            pAttacker, mainDefender);
    if (!routeBypass && DiplomaticWarDeclarationLedgerRules
            .ShouldBlockWarWithActiveTreaty(activeTreaty,
                independenceWar, pTreatyExemptInternalWar))
    {
        pFailureReason = "active_war_blocker";
        return null;
    }

In `CanStartCivilWar` and `CanQueueWarDecision`, return `true` for
`IsOriginSuppressionPair` after participant/already-at-war checks and before
active-treaty or casus-belli checks. This ensures the origin AI can actually
choose suppression without manufacturing a normal claim.

Wrap the later non-engine policy checks with `!routeBypass`. In
`ShouldBlockWarStart`, route permission must run before
`IsAw3AllowedWarStart`; return `false` immediately for an origin-suppression
pair so the existing Harmony prefixes permit the original
`DiplomacyManager.startWar` and `WarManager.newWar` calls:

    if (!PeasantRebelRouteService.CanStartWar(pAttacker, pDefender,
            out _, out _)) return true;
    if (PeasantRebelRouteService.IsOriginSuppressionPair(
            pAttacker, pDefender)) return false;

Do not create a second `War` implementation. The successful path must continue
through original WorldBox war creation and the existing AW3 war lifecycle.

- [ ] **Step 4: Gate city ownership before original mutation**

Change `JoinCapturedCity_Prefix` from `void` to `bool`. The first lines are:

    if (!PeasantRebelRouteService.CanAcquireCity(
            pNewSetKingdom, __instance)) return false;
    if (!pCaptured) return true;

Keep the existing rebellion and vassal-recipient logic, returning `true` at
every former bare return and at the end. This Harmony prefix is the final,
authoritative gate for every call to original `City.joinAnotherKingdom`.

In `FinishCapture_Prefix`, reject the requested recipient before direct-rebel
resolution, then repeat the check after `VassalCaptureService.ResolveCaptureRecipient`
because that method may redirect ownership:

    if (!PeasantRebelRouteService.CanAcquireCity(
            pNewKingdom, __instance)) return false;

In `WarPeaceSettlementRuntime.TryCedeCity`, before preparing annexation state:

    if (!PeasantRebelRouteService.CanAcquireCity(recipient, city))
    {
        reason = "bandit_single_city";
        return false;
    }

This preserves original `City.joinAnotherKingdom` for permitted transfers and
blocks capture, settlement, vassal, inheritance, event, and other-mod calls
before ownership changes. Do not transfer and roll back.

- [ ] **Step 5: Add a malformed-save audit without deleting cities**

In the bandit annual method, count live owned cities. When the count is greater
than one, log once per kingdom-year and return before wall or transition work:

    if (SafeCountCities(pKingdom) > 1)
    {
        ModClass.LogWarning("Bandit realm " + pKingdom.id +
            " owns more than its founding city; acquisition remains locked.");
        return;
    }

The audit must not select an owner or call `joinAnotherKingdom`; an old save or
another mod may have made provenance ambiguous.

- [ ] **Step 6: Extend the source guard and commit**

Require the source guard to find all of these exact integration strings:

    PeasantRebelRouteService.CanStartWar
    PeasantRebelRouteService.IsOriginSuppressionPair
    PeasantRebelRouteService.CanAcquireCity(
    City.joinAnotherKingdom

Run:

    pwsh -File 'Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --peasant-rebel-routes
    dotnet build AncientWarfare3.csproj --no-restore

Expected: all commands exit 0.

    git add Code Tests
    git commit -m "feat: enforce bandit war and territory rules"

## Task 6: Build And Maintain A Fixed Vanilla Wooden Wall Ring

**Files:**

- Create: Code/core/lineage/PeasantRebelBanditWallService.cs
- Modify: Code/core/lineage/PeasantRebelBanditRoute.cs
- Modify: Code/core/lineage/PeasantRebelRouteRules.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt
- Modify: Tests/PeasantRebelRouteRuntimeSourceGuard.ps1

- [ ] **Step 1: Add failing detached wall-lifecycle tests**

Add:

    True(PeasantRebelRouteRules.ShouldRepairWalls(
            bandit: true, suppressionActive: false),
        "peaceful bandits repair");
    False(PeasantRebelRouteRules.ShouldRepairWalls(
            bandit: true, suppressionActive: true),
        "suppression pauses repair");
    False(PeasantRebelRouteRules.ShouldRepairWalls(
            bandit: false, suppressionActive: false),
        "converted rebels never repair old walls");
    Equal(3, PeasantRebelRouteRules.RepairCount(
            missing: 9, yearlyBudget: 3),
        "repair work is bounded");
    Equal(0, PeasantRebelRouteRules.RepairCount(
            missing: 0, yearlyBudget: 3),
        "complete rings require no writes");

Implement:

    public static int RepairCount(int missing, int yearlyBudget)
    {
        return Math.Min(Math.Max(0, missing), Math.Max(0, yearlyBudget));
    }

Run the focused test and expect PASS.

- [ ] **Step 2: Define the persisted coordinate codec**

In `PeasantRebelBanditWallService`, define a private JSON DTO and use
Newtonsoft.Json already referenced by the project:

    private sealed class WallPoint
    {
        public int x;
        public int y;
    }

    private const int REPAIR_BUDGET_PER_YEAR = 12;

    private static string Serialize(IReadOnlyList<WallPoint> pPoints)
    {
        return JsonConvert.SerializeObject(pPoints);
    }

    private static List<WallPoint> Deserialize(string pJson)
    {
        if (string.IsNullOrWhiteSpace(pJson)) return null;
        try { return JsonConvert.DeserializeObject<List<WallPoint>>(pJson); }
        catch { return null; }
    }

Malformed JSON returns `null`, which disables repair without changing zones or
route identity.

- [ ] **Step 3: Snapshot the entry-time union perimeter**

Implement `CaptureAndBuild(Kingdom pKingdom, City pCity)` as follows:

1. Call original `pCity.recalculateNeighbourZones()`.
2. Enumerate `pCity.border_zones`, their `tiles`, and each tile's original
   `neighboursAll`.
3. Keep an inside-city tile when at least one neighbor is outside `pCity`
   according to `tile.zone_city == pCity || tile.zone?.city == pCity`.
4. De-duplicate by `x + ":" + y`, sort by `x` then `y`, and persist every
   attempted eligible coordinate before placing walls.
5. For each eligible coordinate call only:

       tile.setTopTileType(TopTileLibrary.wall_wild);

Use the same terrain checks as `MandateBorderDefenseService.IsWallCandidate`:
ground, non-liquid, non-lava, non-block, non-wall, non-road, no building, and
no existing top tile. Do not call `ResolveBorderWallType`, register a custom
asset, or copy original `setTopTileType` internals.

- [ ] **Step 4: Repair only recorded coordinates**

Implement:

    internal static void RepairYear(Kingdom pKingdom,
        bool pSuppressionActive)
    {
        if (!PeasantRebelRouteRules.ShouldRepairWalls(
                PeasantRebelRouteService.IsBandit(pKingdom),
                pSuppressionActive)) return;
        string json = ReadString(pKingdom,
            LineageKeys.MANDATE_REBEL_BANDIT_WALLS);
        List<WallPoint> points = Deserialize(json);
        if (points == null || points.Count == 0) return;
        int cursor = ReadInt(pKingdom,
            LineageKeys.MANDATE_REBEL_BANDIT_WALL_CURSOR, 0);
        int repaired = 0;
        int inspected = 0;
        while (inspected < points.Count &&
               repaired < REPAIR_BUDGET_PER_YEAR)
        {
            WallPoint point = points[(cursor + inspected) % points.Count];
            WorldTile tile = World.world?.GetTile(point.x, point.y);
            inspected++;
            if (!CanRestoreAtRecordedPosition(tile)) continue;
            if (tile.top_type == TopTileLibrary.wall_wild) continue;
            try
            {
                tile.setTopTileType(TopTileLibrary.wall_wild);
                repaired++;
            }
            catch { }
        }
        pKingdom.data.set(LineageKeys.MANDATE_REBEL_BANDIT_WALL_CURSOR,
            (cursor + inspected) % points.Count);
    }

`CanRestoreAtRecordedPosition` repeats the terrain/building validity checks but
does not require the tile to remain in the founding city's current zone. This
is what keeps the original ring fixed when the cityzone grows or shrinks.

- [ ] **Step 5: Connect entry and peace-only annual repair**

After identity/peace/one-city validation and before the final bandit marker
commit, call:

    PeasantRebelBanditWallService.CaptureAndBuild(
        context.Rebel, context.FoundingCity);

In annual bandit work, detect suppression by enumerating original
`pKingdom.getWars()` and checking for an active war whose other main kingdom is
the persisted origin. Pass that Boolean to `RepairYear`. Conversion and
destruction do not delete wall tiles; they only stop invoking repair.

- [ ] **Step 6: Guard original-code reuse, verify, and commit**

The PowerShell guard must require:

    pCity.recalculateNeighbourZones()
    pCity.border_zones
    neighboursAll
    TopTileLibrary.wall_wild
    tile.setTopTileType(TopTileLibrary.wall_wild)
    World.world?.GetTile(point.x, point.y)

It must fail if `new TopTileType`, `AssetManager.top_tiles.add`, or any copied
implementation of `WorldTile.setTopTileType` appears in the new wall service.

Run:

    pwsh -File 'Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --peasant-rebel-routes
    dotnet build AncientWarfare3.csproj --no-restore

Expected: all commands exit 0.

    git add Code/core/lineage/PeasantRebelBanditWallService.cs Code/core/lineage/PeasantRebelBanditRoute.cs Code/core/lineage/PeasantRebelRouteRules.cs Tests
    git commit -m "feat: add fixed vanilla walls to bandit hideouts"

## Task 7: Implement The One-Way Bandit-To-Founding Transition

**Files:**

- Modify: Code/core/lineage/PeasantRebelRouteRules.cs
- Modify: Code/core/lineage/PeasantRebelRouteService.cs
- Modify: Code/core/lineage/PeasantRebelBanditRoute.cs
- Modify: Code/core/lineage/MandateRebelService.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt
- Modify: Tests/PeasantRebelRouteRuntimeSourceGuard.ps1

- [ ] **Step 1: Add failing eligibility and chance tests**

Add:

    False(PeasantRebelRouteRules.CanEvaluateWeakOriginTransition(
            banditAgeYears: 2, originWeak: true, turmoil: true,
            cityFactor: 0, leaderFactor: 0),
        "weak origin still requires three complete years");
    True(PeasantRebelRouteRules.CanEvaluateWeakOriginTransition(
            banditAgeYears: 3, originWeak: true, turmoil: true,
            cityFactor: 0, leaderFactor: 0),
        "three-year qualified hideout may convert");
    False(PeasantRebelRouteRules.CanEvaluateWeakOriginTransition(
            banditAgeYears: 3, originWeak: false, turmoil: true,
            cityFactor: 15, leaderFactor: 15),
        "leader quality cannot replace origin weakness");
    Equal(20, PeasantRebelRouteRules.TransitionChance(
            quarterStrength: false, hostileWarCount: 1,
            capitalLost: false, cityFactor: 0, leaderFactor: 0),
        "eligible transition begins at twenty percent");
    Equal(90, PeasantRebelRouteRules.TransitionChance(
            quarterStrength: true, hostileWarCount: 4,
            capitalLost: true, cityFactor: 15, leaderFactor: 15),
        "transition chance clamps at ninety percent");

Implement the exact formula from the spec: base 20, +20 at quarter strength,
+10 per hostile war beyond the first capped at +20, +15 for capital loss, and
positive city/leader factors capped at +15 each, with a final 20..90 clamp.

- [ ] **Step 2: Evaluate once per year from persisted snapshots**

In bandit annual work, compare current year to
`MANDATE_REBEL_ROUTE_LAST_YEAR`; set the key before evaluation. Resolve origin
with `World.world.kingdoms.get(originId)`. If it is missing, rekt, non-civil,
or has zero cities, call conversion immediately without a random roll.

For a live origin calculate:

    bool weak = currentCityCount * 2 <= originalCityCount ||
                currentStrength * 2 <= originalStrength;
    bool quarter = currentCityCount * 4 <= originalCityCount ||
                   currentStrength * 4 <= originalStrength;
    City originalCapital = World.world?.cities?.get(originalCapitalId);
    bool capitalLost = originalCapital?.kingdom != origin;
    int hostileWars = CountActiveWars(origin);
    bool turmoil = hostileWars >= 2 || capitalLost || !origin.hasKing();
    int age = currentYear - createdYear;

Recompute the current founding-city and current ruler factors with the same
methods used at creation. Call `Randy.randomInt(0, 100)` only after all floors
pass, and convert when the roll is less than `TransitionChance`.

- [ ] **Step 3: Make conversion atomic and one-way**

Implement `ConvertBanditToFounding` with this order:

    if (!IsBandit(pKingdom) ||
        AW3MultiplayerReplicaScope.IsReplicaSession) return false;
    pKingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE,
        PeasantRebelRouteIds.Founding);
    RenameForRoute(pKingdom, PeasantRebelRouteIds.Founding);
    RulerAppellationService.RefreshLivingProjection(pKingdom);
    KingdomRenameProjectionService.Refresh(pKingdom);
    PeasantRebelFoundingRoute.RecordTransition(pKingdom, pOrigin);
    if (pOrigin?.data != null && !pOrigin.isRekt())
        MandateRebelService.StartExistingRebelWar(pOrigin, pKingdom);
    return true;

Expose the current `StartRebelWar` body as internal
`StartExistingRebelWar`; do not reimplement original war creation. Do not
clear the name root, wall coordinates, entry year, or origin snapshots. There
is no code path that assigns `bandit` after creation.

- [ ] **Step 4: Re-enter current founding annual behavior**

On later years, route dispatch sends `founding` to
`MandateRebelService.RunFoundingRouteYear`. If the origin disappeared, do not
create a replacement opponent; run existing government/mobilization/Mandate
logic and let `SettleRebelGovernment` remain authoritative.

- [ ] **Step 5: Verify source invariants and commit**

Add source-guard assertions that:

- `ConvertBanditToFounding` contains only a write of `Founding`.
- `StartExistingRebelWar` is invoked instead of direct `new War` construction.
- wall coordinates are not cleared during conversion.
- `Randy.randomInt(0, 100)` appears after eligibility checks.

Run:

    pwsh -File 'Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --peasant-rebel-routes
    dotnet build AncientWarfare3.csproj --no-restore

Expected: all commands exit 0.

    git add Code/core/lineage Tests
    git commit -m "feat: convert qualified bandit rebels to founding route"

## Task 8: Project Names, Titles, Localization, And History Everywhere

**Files:**

- Modify: Code/core/lineage/PeasantRebelRouteRules.cs
- Modify: Code/core/lineage/PeasantRebelRouteService.cs
- Modify: Code/core/lineage/PeasantRebelFoundingRoute.cs
- Modify: Code/core/lineage/PeasantRebelBanditRoute.cs
- Modify: Code/core/lineage/RulerAppellationRules.cs
- Modify: Code/core/lineage/RulerAppellationService.cs
- Modify: Code/core/lineage/HeirTitleRules.cs
- Modify: Code/core/lineage/HeirTitleSelectionRules.cs
- Modify: Code/core/lineage/HistoryLocalizationRules.cs
- Modify: Code/patch/AW_WarPatch.cs
- Modify: locales/others.csv
- Modify: locales/aw3_mandate_extra.csv
- Modify: Tests/AncientWarfare3.Rules.Tests/NameSystemRulesTests.cs.txt
- Modify: Tests/AncientWarfare3.Rules.Tests/HistoryLocalizationRulesTests.cs.txt
- Modify: Tests/PeasantRebelRouteRuntimeSourceGuard.ps1

- [ ] **Step 1: Add failing pure name and title tests**

Add assertions using Unicode escapes so test source encoding cannot alter the
expected Chinese text:

    Equal("\u8d64\u7709\u8d3c",
        PeasantRebelRouteRules.ComposeName("\u8d64\u7709",
            PeasantRebelRouteIds.Bandit),
        "bandit name uses the persisted root");
    Equal("\u8d64\u7709\u4e49\u519b",
        PeasantRebelRouteRules.ComposeName("\u8d64\u7709",
            PeasantRebelRouteIds.Founding),
        "founding conversion changes only the suffix");
    Equal("aw_bandit_ruler_title",
        RulerAppellationRules.RouteRulerTitleKey(true),
        "bandit ruler uses the shared title key");
    Equal("aw_bandit_heir_title",
        HeirTitleSelectionRules.RouteHeirTitleKey(true),
        "bandit heir uses the shared title key");
    Equal("", RulerAppellationRules.RouteRulerTitleKey(false),
        "ordinary rulers retain existing logic");

Implement:

    public static string ComposeName(string root, string route)
    {
        string value = (root ?? "").Trim();
        return value + (route == PeasantRebelRouteIds.Bandit
            ? "\u8d3c" : "\u4e49\u519b");
    }

    public static string RouteRulerTitleKey(bool bandit)
    {
        return bandit ? "aw_bandit_ruler_title" : "";
    }

Add this detached method to `HeirTitleSelectionRules`:

    public static string RouteHeirTitleKey(bool bandit)
    {
        return bandit ? "aw_bandit_heir_title" : "";
    }

- [ ] **Step 2: Rename only through the shared projection boundary**

Implement route-service `RenameForRoute`:

    internal static void RenameForRoute(Kingdom pKingdom, string pRoute)
    {
        if (pKingdom?.data == null) return;
        pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
            out string root, "");
        string name = PeasantRebelRouteRules.ComposeName(root, pRoute);
        if (string.IsNullOrWhiteSpace(name)) return;
        pKingdom.setName(name, pTrack: false);
        KingdomRenameProjectionService.Refresh(pKingdom);
    }

Call it on founding entry, bandit entry, and conversion. Old saves without a
route migrate to founding without renaming or replaying entry effects. Do not
alter already formatted window text or actor display names.

- [ ] **Step 3: Route ruler and heir titles through existing read models**

At the top of `RulerAppellationService.GetFullLivingAppellation`, after invalid
kingdom validation and before government/rank branches:

    if (PeasantRebelRouteService.IsBandit(pKingdom))
        return AW_L10n.Text(
            RulerAppellationRules.RouteRulerTitleKey(true),
            "\u5927\u5f53\u5bb6");

At the top of kingdom-aware `HeirTitleRules.TitleKey`,
`DefaultTitleText`, and `BuildSocialTitle`, use:

    if (PeasantRebelRouteService.IsBandit(pKingdom))
        return HeirTitleSelectionRules.RouteHeirTitleKey(true);

for the key method, and:

    if (PeasantRebelRouteService.IsBandit(pKingdom))
        return AW_L10n.Text(
            HeirTitleSelectionRules.RouteHeirTitleKey(true),
            "\u5c11\u5f53\u5bb6");

for display methods. Preserve every existing military-governorate, republic,
imperial, Mandate, and succession branch for non-bandit kingdoms.

- [ ] **Step 4: Add stable localization keys**

Append route/title keys to `locales/others.csv` without duplicate rows:

    aw_bandit_route_name,落草为寇,Bandit Route,落草為寇
    aw_founding_route_name,建国义军,Founding Rebels,建國義軍
    aw_bandit_ruler_title,大当家,Chieftain,大當家
    aw_bandit_heir_title,少当家,Young Chieftain,少當家

Add history fragments to `aw3_mandate_extra.csv` and matching
`HistoryLocalizationRules.Entry` rows:

    aw_hist_rebel_route_founding,选择建国路线, chose the founding route,選擇建國路線
    aw_hist_rebel_route_bandit,落草为寇并筑寨, became bandits and fortified a hideout,落草為寇並築寨
    aw_hist_bandit_suppression_started,发兵剿匪, began a suppression campaign,發兵剿匪
    aw_hist_bandit_converted,改旗建号，转为建国义军, raised a state banner and became founding rebels,改旗建號，轉為建國義軍
    aw_hist_bandit_destroyed,山寨被攻破，贼国覆灭, lost its hideout and was destroyed,山寨被攻破，賊國覆滅

Add test assertions that `HistoryLocalizationRules.Text` returns the listed
`cz`, `en`, and `ch` values for every new key.

- [ ] **Step 5: Record the five distinct lifecycle events**

Use `HistoryWriter.RecordKingdom` with
`KingdomEvent.MANDATE_REBELLION` for route selection, conversion, and
destruction. On successful founding entry record `aw_hist_rebel_route_founding`;
on successful bandit entry record `aw_hist_rebel_route_bandit`.

In `AW_WarPatch.NewWar_Postfix`, after `__result` validation, call:

    PeasantRebelRouteService.OnWarStarted(__result);

`OnWarStarted` records `aw_hist_bandit_suppression_started` only when the main
attacker is the persisted origin and the main defender is currently bandit.
Conversion records `aw_hist_bandit_converted`. Destruction is recorded by the
Task 9 extinction hook only when the route was bandit and an active war against
the persisted origin existed at removal time.

- [ ] **Step 6: Verify projection coverage and commit**

Extend the source guard to require the new keys in both CSV/read-model paths
and to reject hard-coded title replacement in files under `Code/ui`.

Run:

    pwsh -File 'Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj'
    dotnet build AncientWarfare3.csproj --no-restore

Expected: all commands exit 0 and the full rules suite prints its normal
success summary.

    git add Code locales Tests
    git commit -m "feat: project bandit rebel identity across shared views"

## Task 9: Harden Save Migration, Replica Authority, And Extinction Cleanup

**Files:**

- Modify: Code/core/lineage/PeasantRebelRouteRules.cs
- Modify: Code/core/lineage/PeasantRebelRouteService.cs
- Modify: Code/core/lineage/PeasantRebelBanditRoute.cs
- Modify: Code/core/multiplayer/AW3RuntimeRestorePipeline.cs
- Modify: Code/patch/AW_ChroniclePatch.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt
- Modify: Tests/PeasantRebelRouteRuntimeSourceGuard.ps1

- [ ] **Step 1: Add failing migration and authority tests**

Add:

    Equal(PeasantRebelRouteIds.Founding,
        PeasantRebelRouteRules.ResolvePersistedRoute(
            storedRoute: "", currentPeasantRebel: true),
        "old peasant rebels migrate to founding");
    Equal("", PeasantRebelRouteRules.ResolvePersistedRoute(
            storedRoute: "", currentPeasantRebel: false),
        "ordinary kingdoms do not receive a route");
    Equal(PeasantRebelRouteIds.Bandit,
        PeasantRebelRouteRules.ResolvePersistedRoute(
            storedRoute: "bandit", currentPeasantRebel: true),
        "saved bandit route survives load");
    False(PeasantRebelRouteRules.CanMutateAuthority(
            replicaSession: true),
        "replicas cannot select, transfer, make peace, or repair");
    True(PeasantRebelRouteRules.CanMutateAuthority(
            replicaSession: false),
        "simulation authority may mutate route state");

Implement `ResolvePersistedRoute` so only exact `founding`/`bandit` values are
accepted, blank current rebels become `founding`, and non-rebels remain blank.
`CanMutateAuthority` returns `!replicaSession`.

- [ ] **Step 2: Rebuild route runtime from Kingdom.data**

Implement:

    internal static void RebuildRuntime()
    {
        ClearRuntime();
        if (World.world?.kingdoms == null) return;
        bool authority = PeasantRebelRouteRules.CanMutateAuthority(
            AW3MultiplayerReplicaScope.IsReplicaSession);
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            string stored = ReadRouteRaw(kingdom);
            string resolved = PeasantRebelRouteRules.ResolvePersistedRoute(
                stored, MandateRebelService.IsRebelKingdom(kingdom));
            if (authority && stored.Length == 0 &&
                resolved == PeasantRebelRouteIds.Founding)
                kingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE, resolved);
            if (resolved.Length > 0)
                RulerAppellationService.RefreshLivingProjection(kingdom);
        }
    }

Migration must not call either route's `Enter`, pull cities, start/end wars,
rename old realms, or place walls. Existing rebels therefore retain current
founding behavior without rerunning creation effects.

- [ ] **Step 3: Insert restore stages in projection-safe order**

In both stage lists in `AW3RuntimeRestorePipeline`, insert:

    new AW3RestoreStage("peasant_rebel_routes",
        PeasantRebelRouteService.RebuildRuntime),

after Mandate marker restoration and before
`RulerAppellationService.RebuildLivingCache`. In `ResetRuntimeCaches`, add:

    new AW3RestoreStage("peasant_rebel_routes",
        PeasantRebelRouteService.ClearRuntime),

This consumes persisted `Kingdom.data`; it does not create a parallel save
file or database table.

- [ ] **Step 4: Put authority guards before every mutation**

At the start of route selection/entry, war-ending, conversion, and wall
placement/repair methods use:

    if (!PeasantRebelRouteRules.CanMutateAuthority(
            AW3MultiplayerReplicaScope.IsReplicaSession)) return false;

Use a bare `return` for void methods. Read-only route checks and presentation
refresh remain available on replicas. The source guard must verify the
authority check occurs before `Randy.randomInt`, `endWar`,
`joinAnotherKingdom`, route `data.set`, and `setTopTileType` calls.

Also return `null` at the start of
`MandateRebelService.CreateRebelKingdom` on a replica, before original
`City.makeOwnKingdom`, so a rejected route initialization cannot fall through
to the founding fallback on a replica. Add
`using AncientWarfare3.api.multiplayer;` to each new runtime service that reads
`AW3MultiplayerReplicaScope`.

- [ ] **Step 5: Reuse normal kingdom extinction and clear survivor identity**

In `AW_ChroniclePatch.RemoveKingdom_Prefix`, immediately after local selection
cleanup and before the existing replica early return, call:

    PeasantRebelRouteService.OnKingdomDestroying(pKingdom,
        pAuthoritative: !AW3MultiplayerReplicaScope.IsApplying);

Implement the hook:

    internal static void OnKingdomDestroying(Kingdom pKingdom,
        bool pAuthoritative)
    {
        if (pKingdom?.data == null) return;
        bool bandit = IsBandit(pKingdom);
        if (pAuthoritative && bandit)
            PeasantRebelBanditRoute.RecordDestruction(pKingdom);
        if (pAuthoritative)
        {
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (unit?.data == null) continue;
                unit.data.set(LineageKeys.MANDATE_REBEL_LEADER, false);
                if (unit.hasTrait("rebel")) unit.removeTrait("rebel");
            }
        }
        RuntimeByKingdom.Remove(pKingdom.id);
        RulerAppellationService.RemoveKingdom(pKingdom.id);
    }

Do not call `KingdomManager.removeObject` from route code and do not remove or
rewrite recorded wall tiles. Original last-city loss and existing
`KingdomManager` extinction remain authoritative.

`RecordDestruction` first confirms an active war against the persisted origin;
otherwise normal non-suppression removal receives only the existing generic
kingdom-destruction history event.

- [ ] **Step 6: Handle missing founding city and malformed wall data safely**

During annual dispatch, if the stored founding city cannot be resolved or is
not owned by the bandit realm, skip wall repair and transition evaluation and
remove only its runtime cache entry. Malformed wall JSON already returns null
from Task 6. Neither condition may change current city zones, route ID, or
ownership.

- [ ] **Step 7: Run regression guards and commit**

Run:

    pwsh -File 'Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj'
    dotnet build AncientWarfare3.csproj --no-restore

Expected: source guard, full rules suite, and source build all exit 0.

    git add Code Tests
    git commit -m "fix: restore and clean up peasant rebel routes safely"

## Task 10: Verify, Deploy Source, And Run Gameplay Acceptance

**Files:**

- Modify only if a defect is found: files listed in Tasks 2-9
- Deployment target: D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0

- [ ] **Step 1: Run all automated verification from the feature worktree**

    $env:DOTNET_ROLL_FORWARD='Major'
    pwsh -File 'Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
    dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj'
    dotnet build AncientWarfare3.csproj --no-restore
    git diff --check master...HEAD

Expected: every command exits 0, the rules runner prints its normal success
summary, the project has zero build errors, and `git diff --check` is silent.

- [ ] **Step 2: Audit original-code reuse before deployment**

Run:

    rg -n "makeOwnKingdom|generateName\(MetaType.Kingdom|getWars\(|endWar\(|recalculateNeighbourZones|border_zones|neighboursAll|wall_wild|setTopTileType|joinAnotherKingdom" Code/core/lineage/PeasantRebel* Code/core/lineage/MandateRebelService.cs Code/patch/AW_CityOccupationAccelerationPatch.cs
    rg -n "new TopTileType|top_tiles.*add|new War\(" Code/core/lineage/PeasantRebel* Code/core/lineage/MandateRebelService.cs

Expected: the first command shows original API calls at the planned
boundaries. The second command has no matches. No file under the AssetRipper
export is modified.

- [ ] **Step 3: Deploy the feature worktree source with the existing script**

Close WorldBox before mirroring. From the feature worktree run:

    $source = (Resolve-Path '.').Path
    $target = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
    & "$source\deploy-local.ps1" -SourceRoot $source -DestinationRoot $target

Expected: the script first prints a timestamped backup path below
`.aw3-deploy-backups`, then `DEPLOY-DONE`. Passing `-SourceRoot` is mandatory;
the script's default points to the master worktree, not this feature worktree.

- [ ] **Step 4: Verify deployed-source parity and build the deployed copy**

    $source = (Resolve-Path '.').Path
    $target = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
    $sourceFiles = Get-ChildItem "$source\Code" -Recurse -File
    $mismatch = foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring((Join-Path $source 'Code').Length)
        $deployed = Join-Path (Join-Path $target 'Code') $relative
        if (-not (Test-Path -LiteralPath $deployed) -or
            (Get-FileHash -LiteralPath $file.FullName).Hash -ne
            (Get-FileHash -LiteralPath $deployed).Hash) { $relative }
    }
    if ($mismatch) { throw "Deployment mismatch: $($mismatch -join ', ')" }
    dotnet build "$target\AncientWarfare3.csproj" --no-restore

Expected: no mismatch exception and deployed build exits 0 with zero errors.

- [ ] **Step 5: Validate the founding-route regression in game**

Create or load a controlled world, trigger peasant uprisings until one selects
the founding route, and verify:

- Its root is freshly generated and the visible suffix is `义军`.
- Existing aligned-city pull, rebellion war, mobilization, expansion, Mandate
  claim, and government settlement still operate.
- Actor, kingdom, genealogy, tooltip, and history surfaces agree on the name
  and existing founding-rebel titles.

Expected: behavior matches the pre-feature founding path except for the newly
generated persisted root and route history entry.

- [ ] **Step 6: Validate bandit entry, walls, and restrictions in game**

Trigger a bandit result and verify:

- It retains exactly the founding city and all its entry wars end through the
  normal war-end UI/history lifecycle.
- The state is `<root>贼`, ruler is `大当家`, and heir is `少当家` on all
  existing windows.
- A fixed ring of original `wall_wild` tiles surrounds the entry-time cityzone.
- Cityzone growth works without moving or extending the saved wall ring.
- An unrelated kingdom cannot directly declare war and no transfer path can
  give the bandit a second city.

Expected: all checks pass without exceptions or window crashes.

- [ ] **Step 7: Validate suppression, repair, conversion, and extinction**

At peace, remove several recorded wall tiles and advance years; at most twelve
are repaired per year. Have the recorded origin start suppression despite the
entry peace and verify repair pauses. Test both outcomes:

- Origin retakes the only city: the normal WorldBox kingdom-extinction path
  removes the realm, survivor rebel identity is cleared, and walls remain.
- The bandit survives three complete years while origin strength/cities fall
  to half with serious turmoil and both current factors are non-negative: a
  successful annual roll changes only the route/name/title projections,
  permanently stops wall repair, and reuses the current rebellion-war flow.

Also load an old save containing a peasant rebel without route keys and verify
it silently behaves as founding without replaying entry effects.

- [ ] **Step 8: Record evidence and commit any test-driven correction**

For each acceptance scenario record the save name, world year, rebel kingdom
ID, selected route, city count, origin ID, active war IDs, and wall-coordinate
count in the implementation-session notes. If gameplay finds a defect, first
add a detached regression test or source guard, observe failure, make the
smallest fix, rerun Steps 1-7, redeploy, and commit:

    git add Code Tests locales
    git commit -m "fix: address peasant rebel route acceptance defect"

If no correction is needed, do not create an empty commit.
