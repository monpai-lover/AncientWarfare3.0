# Bandit Stronghold City And Food Raids Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the bandit route's city whitelist with one real wall-bound stronghold city, add an authoritative test god power, and run physical food-raiding parties with persistent, conservative resource settlement.

**Architecture:** Pure rule classes decide geometry, naming, raid eligibility, ranking, quantities, and state transitions. `PeasantRebelBanditStrongholdService` is the sole WorldBox mutation boundary for preflight, commit, rollback, restore, government conversion, and stronghold fall; `PeasantRebelBanditRaidService` owns actor missions and food custody. Existing route, occupation, annual-authority, multiplayer-restore, god-power, and shared title entry points call those services rather than duplicating state changes.

**Tech Stack:** C# 11/net48, WorldBox `CityManager`/`City`/`Actor` APIs, Newtonsoft.Json state stored in `BaseSystemData`, Harmony patches, NML god powers and UI, net9 detached rules tests, PowerShell source guards.

---

### Task 1: Stronghold geometry, naming, and raid rules

**Files:**
- Create: `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`
- Create: `Code/core/lineage/PeasantRebelBanditRaidRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditRaidRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing detached rules tests.**

  Add test cases using small integer-coordinate fixtures. Assert that flood fill keeps only wall-interior zones connected to the center, a split needs at least one retained zone on each side, bandit growth accepts only persisted zones, and display strings compose as `root + \"\\u5be8\"`, `root + \"\\u5be8\\u5927\\u5f53\\u5bb6\"`, and `root + \"\\u5be8\\u5c11\\u5f53\\u5bb6\"`. Assert raid trigger `< population * 2`, stock target `population * 5`, party clamp `1..8`, quantity minimum, one-year cooldown, three-year suppression expiry, and candidate ordering by route distance then stealable food.

- [ ] **Step 2: Run the focused slice and confirm RED.**

  Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold`

  Expected: compile failure because `PeasantRebelBanditStrongholdRules` and `PeasantRebelBanditRaidRules` do not exist.

- [ ] **Step 3: Add minimal pure implementations.**

  Define immutable `BanditZoneFact`, `BanditRaidCandidate`, and rule methods with these stable signatures:

  ```csharp
  public static HashSet<string> SelectInteriorZoneKeys(
      IReadOnlyList<BanditZoneFact> zones, string centerKey);
  public static bool IsViableSplit(int interiorCount, int exteriorCount);
  public static bool CanAcquireZone(bool bandit, string zoneKey,
      ISet<string> fixedZoneKeys);
  public static string ComposeStrongholdName(string root);
  public static string ComposeCeremonialTitle(string root, bool heir);

  public static bool NeedsRaid(int food, int population);
  public static int PartySize(int availableWarriors);
  public static int StealableFood(int strongholdFood, int strongholdPopulation,
      int targetFood, int targetPopulation);
  public static bool CooldownExpired(int currentYear, int cooldownUntilYear);
  public static int SuppressionExpiryYear(int currentYear);
  public static IReadOnlyList<BanditRaidCandidate> RankTargets(
      IEnumerable<BanditRaidCandidate> candidates);
  ```

- [ ] **Step 4: Run the focused slice and confirm GREEN.**

  Run the command from Step 2.

  Expected: `PeasantRebelBanditStrongholdRulesTests passed` and `PeasantRebelBanditRaidRulesTests passed`.

- [ ] **Step 5: Commit the rule layer.**

  ```powershell
  git add Code/core/lineage/PeasantRebelBanditStrongholdRules.cs Code/core/lineage/PeasantRebelBanditRaidRules.cs Tests/AncientWarfare3.Rules.Tests
  git commit -m "feat: define bandit stronghold and raid rules"
  ```

### Task 2: Persistent stronghold and raid state

**Files:**
- Create: `Code/core/lineage/PeasantRebelBanditStrongholdState.cs`
- Create: `Code/core/lineage/PeasantRebelBanditStateStore.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Create: `Tests/BanditStrongholdPersistenceSourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard.**

  Require JSON fields for schema version, phase, stronghold/mother/origin IDs, fixed zone keys, wall points, raid mission stage/member IDs/target/carried food/cooldown, and temporary suppression expiries. Require reads to tolerate missing and malformed legacy values and writes to occur only through `PeasantRebelBanditStateStore`.

- [ ] **Step 2: Run the guard and confirm RED.**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdPersistenceSourceGuard.ps1`

  Expected: failure reporting missing model/store files.

- [ ] **Step 3: Implement versioned JSON state.**

  Add `BanditStrongholdPhase { None, Creating, Active, Falling, Completed }`, `BanditRaidStage { None, Outbound, Looted, Returning, Cooldown }`, serializable coordinate and mission records, and `TryRead`, `Write`, `Clear`, `TryResolveActive` store methods. Persist one JSON document under `LineageKeys.MANDATE_REBEL_BANDIT_STRONGHOLD_STATE`; never replay creation from state alone.

- [ ] **Step 4: Run the guard and focused rules slice.**

  Expected: both commands exit 0.

- [ ] **Step 5: Commit persistence.**

  ```powershell
  git add Code/core/lineage/LineageKeys.cs Code/core/lineage/PeasantRebelBanditStrongholdState.cs Code/core/lineage/PeasantRebelBanditStateStore.cs Tests/BanditStrongholdPersistenceSourceGuard.ps1
  git commit -m "feat: persist bandit stronghold lifecycle"
  ```

### Task 3: Preflight and transactional stronghold creation

**Files:**
- Create: `Code/core/lineage/PeasantRebelBanditStrongholdPlan.cs`
- Create: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Code/core/lineage/CultiwayStyleCityWallService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditWallService.cs`
- Create: `Tests/BanditStrongholdTransactionSourceGuard.ps1`

- [ ] **Step 1: Write a failing transaction source guard.**

  Assert that preflight calls `CultiwayStyleCityWallService.TryPlan`, validates `TopTileLibrary.wall_wild`, selects complete `TileZone` objects, requires interior and exterior zones, rejects active strongholds/mothers, and performs no mutation. Assert commit calls `CityManager.newCity`, the native new-city initialization boundary, `City.addZone`, actor city/kingdom migration, ruler relocation, mother-city return, wall placement, and reverse-order rollback.

- [ ] **Step 2: Run the guard and confirm RED.**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1`

  Expected: failure reporting the absent stronghold service.

- [ ] **Step 3: Implement preflight.**

  Add:

  ```csharp
  internal static bool TryPlan(City mother, Kingdom bandit,
      Kingdom origin, Actor ruler, out PeasantRebelBanditStrongholdPlan plan,
      out string failureKey);
  internal static bool TryCreate(PeasantRebelBanditCreationContext context,
      out City stronghold, out string failureKey);
  ```

  Resolve the wall from the mother city core, map wall-interior connected zones, reserve an exterior adult and civic-core site when necessary, and snapshot every city/kingdom/actor/zone/top-tile datum that commit can mutate.

- [ ] **Step 4: Implement ordered commit and rollback.**

  Create and initialize the real city, set `<root>寨`, transfer whole zones and actors, force the ruler into the stronghold, restore the mother and all other rebel cities to the origin, place recorded wooden walls, write active state, and only then publish bandit government/route metadata. On exception, unwind top tiles, actors, zones, city ownership, new city, and state in reverse order.

- [ ] **Step 5: Run guard, focused tests, and net48 build.**

  Run:

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
  dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
  dotnet build AncientWarfare3.csproj -c Release --no-restore
  ```

  Expected: all exit 0.

- [ ] **Step 6: Commit creation transaction.**

  ```powershell
  git add Code/core/lineage Tests/BanditStrongholdTransactionSourceGuard.ps1
  git commit -m "feat: create wall-bound bandit strongholds"
  ```

### Task 4: Route conversion and fixed-zone enforcement

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditRoute.cs`
- Modify: `Code/core/lineage/PeasantRebelGovernmentTransitionService.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs`
- Create: `Code/patch/AW_BanditStrongholdZonePatch.cs`
- Create: `Tests/BanditStrongholdRouteSourceGuard.ps1`

- [ ] **Step 1: Write a failing route source guard.**

  Require automatic and manual bandit entry to call `PeasantRebelBanditStrongholdService.TryCreate`; forbid direct calls from routes to the old territory capture and per-city wall build. Require `City.addZone` interception to deny only active bandit strongholds acquiring a non-persisted zone and allow the mother city and converted founding route to grow normally.

- [ ] **Step 2: Run the guard and confirm RED.**

  Expected: old `CaptureCurrentCities`/`CaptureAndBuild` calls are reported.

- [ ] **Step 3: Integrate the single creation entry point.**

  Route entry and government transition construct the same creation context. Bandit-to-founding conversion preserves the stronghold city and walls, changes the route/class, marks state inactive/unlocked, and does not merge zones into the mother.

- [ ] **Step 4: Add fixed-zone Harmony interception.**

  Prefix the native acquisition boundary, consult `PeasantRebelBanditStrongholdRules.CanAcquireZone`, and fail open for malformed legacy state so ordinary cities are never blocked.

- [ ] **Step 5: Run focused tests, guard, and build.**

  Expected: all exit 0.

- [ ] **Step 6: Commit route integration.**

  ```powershell
  git add Code/core/lineage Code/patch/AW_BanditStrongholdZonePatch.cs Tests/BanditStrongholdRouteSourceGuard.ps1
  git commit -m "feat: bind bandit government to one stronghold"
  ```

### Task 5: Idempotent stronghold fall and restoration

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Code/api/multiplayer/AW3MultiplayerStrategicStateModels.cs`
- Modify: `Code/core/multiplayer/AW3MultiplayerStrategicStateCoordinator.cs`
- Create: `Tests/BanditStrongholdFallRestoreSourceGuard.ps1`

- [ ] **Step 1: Write a failing fall/restore guard.**

  Require occupation interception before normal transfer, `Falling` persisted before mutation, survivor migration to the current mother kingdom, zone return, geometry recalculation, `CityManager.removeObject`, and `Completed` after removal. Require duplicate callbacks to return success without repeating work. Restore must validate IDs/zone coordinates and resume or safely cancel raids without recreating the city, wall, or food reward. Strategic snapshots must carry the durable stronghold/raid JSON to replicas without invoking authoritative mutation services.

- [ ] **Step 2: Run the guard and confirm RED.**

  Expected: missing stronghold fall interception and restore hook.

- [ ] **Step 3: Implement fall settlement.**

  Add `TryHandleCapture(City stronghold, Kingdom occupier, out bool handled)`. Move living residents first, return zones second, recalculate mother/neighbours, remove the city through `World.world.cities.removeObject`, persist completion on the bandit kingdom, then let the existing cityless-kingdom lifecycle run.

- [ ] **Step 4: Implement non-replaying restore.**

  Add `RestoreRuntime()` to rebuild mother/stronghold indexes, repair safe ownership caches, unlock stale converted states, and cancel invalid missions without changing inventories. Call it once from the existing authoritative restore pipeline. Extend strategic snapshot capture/apply with the stored JSON and apply it as replica state only.

- [ ] **Step 5: Run focused tests, guard, and build.**

  Expected: all exit 0.

- [ ] **Step 6: Commit fall and restore.**

  ```powershell
  git add Code/core/lineage/PeasantRebelBanditStrongholdService.cs Code/patch/AW_CityOccupationAccelerationPatch.cs Code/core/multiplayer/AW3RuntimeRestorePipeline.cs Code/api/multiplayer/AW3MultiplayerStrategicStateModels.cs Code/core/multiplayer/AW3MultiplayerStrategicStateCoordinator.cs Tests/BanditStrongholdFallRestoreSourceGuard.ps1
  git commit -m "feat: settle and restore bandit strongholds"
  ```

### Task 6: Shared ceremonial titles

**Files:**
- Modify: `Code/core/lineage/RulerAppellationService.cs`
- Modify: `Code/core/lineage/HeirTitleRules.cs`
- Modify: `Code/core/lineage/CeremonialTitleResolver.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt`
- Create: `Tests/BanditStrongholdTitleSourceGuard.ps1`

- [ ] **Step 1: Write failing title assertions.**

  Assert canonical `Kingdom.name` stays `虎踞`, kingdom display stays `虎踞贼`, stronghold city is `虎踞寨`, and ruler/heir ceremonial displays are `虎踞寨大当家`/`虎踞寨少当家`. Source guard all living, genealogy, archive, and household title consumers through the shared resolver.

- [ ] **Step 2: Run tests and confirm RED.**

  Expected: title composer lacks the stronghold prefix at at least one shared consumer.

- [ ] **Step 3: Route all title consumers through the composer.**

  Resolve the canonical outlaw root, call `ComposeCeremonialTitle(root, heir)`, and keep suffix/title text out of the stored kingdom name.

- [ ] **Step 4: Run focused tests and source guard.**

  Expected: exit 0 and exact Unicode assertions pass.

- [ ] **Step 5: Commit title consistency.**

  ```powershell
  git add Code/core/lineage Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt Tests/BanditStrongholdTitleSourceGuard.ps1
  git commit -m "fix: unify bandit stronghold titles"
  ```

### Task 7: Authoritative test god power and localization

**Files:**
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Code/ui/AW_LineageTab.cs`
- Modify: `Locales/zh.json`
- Modify: `Locales/en.json`
- Create: `Tests/BanditStrongholdGodPowerSourceGuard.ps1`

- [ ] **Step 1: Write a failing god-power guard.**

  Require a stable `SPAWN_BANDIT_STRONGHOLD` ID, existing rebel/bandit icon reuse, localized name/description/failure keys, a lineage-tab button, exact clicked-zone city resolution with no nearest-city fallback, authority/replica guard, and the same `TryCreate` service used by route conversion.

- [ ] **Step 2: Run the guard and confirm RED.**

  Expected: missing power registration/button/localization.

- [ ] **Step 3: Register `在此地放出土匪`.**

  Validate ordinary city, non-stronghold, no existing child stronghold, and an eligible adult. Create a new civ kingdom using the native kingdom manager, seed the shared creation context, and show localized feedback without mutating on invalid clicks.

- [ ] **Step 4: Add the button and localization.**

  Reuse the current rebellion icon, add Chinese and English labels plus all failure tips, and keep displayed strings out of production C# except localization keys.

- [ ] **Step 5: Run localization coverage, source guard, focused tests, and build.**

  Expected: all exit 0.

- [ ] **Step 6: Commit the god power.**

  ```powershell
  git add Code/content/GodPowerLibrary.cs Code/ui/AW_LineageTab.cs Locales Tests/BanditStrongholdGodPowerSourceGuard.ps1
  git commit -m "feat: add bandit stronghold god power"
  ```

### Task 8: Physical raid mission runtime

**Files:**
- Create: `Code/core/lineage/PeasantRebelBanditRaidService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditRoute.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Create: `Tests/BanditRaidRuntimeSourceGuard.ps1`

- [ ] **Step 1: Write a failing raid runtime guard.**

  Require one active mission per stronghold, annual shortage scheduling, land-reachable non-allied non-stronghold targets, distance/food ranking, 3-8 real warriors (or one), general preference, no forced ruler/heir, native `Actor.goTo` travel, no occupation/war call on arrival, and stage persistence before each side effect.

- [ ] **Step 2: Run the guard and confirm RED.**

  Expected: missing raid runtime service.

- [ ] **Step 3: Implement candidate and party selection.**

  Use existing path reachability facts, stable city IDs, and actor eligibility. Persist member IDs, leader ID, target ID, outbound destination, and stage before issuing native movement.

- [ ] **Step 4: Implement outbound/return progress.**

  Tick active missions through the existing bounded authority cycle. Arrival requires a surviving member physically inside the target; returning requires a survivor inside the stronghold. Invalid targets or paths switch to safe return, and total party loss clears carried food and starts cooldown.

- [ ] **Step 5: Run focused rules, guard, and build.**

  Expected: all exit 0.

- [ ] **Step 6: Commit raid movement.**

  ```powershell
  git add Code/core/lineage/PeasantRebelBanditRaidService.cs Code/core/lineage/PeasantRebelBanditRoute.cs Code/core/performance/AWAuthorityCycleService.cs Tests/BanditRaidRuntimeSourceGuard.ps1
  git commit -m "feat: move physical bandit raid parties"
  ```

### Task 9: Food custody and suppression rights

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditRaidService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditRoute.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs`
- Create: `Tests/BanditRaidSettlementSourceGuard.ps1`

- [ ] **Step 1: Write failing conservation and diplomacy guards.**

  Assert loot uses `City.takeResource`, remains only in persisted mission custody until return, enters the stronghold via `City.addResourcesToRandomStockpile`, is lost on total party death, and never applies twice after load/retry. Assert victims receive a refreshed three-year suppression expiry, origin rights never expire, rights only permit normal declaration, and the raid itself does not create a war.

- [ ] **Step 2: Run guards and confirm RED.**

  Expected: missing native inventory settlement and temporary suppression checks.

- [ ] **Step 3: Implement transactional loot and delivery.**

  Compute the rule minimum, snapshot observed inventories, remove target food, persist `Looted` plus carried amount, then return. On successful delivery add exactly the carried amount once, clear custody, and start one-year cooldown. Restore observed inventories if the native mutation throws before the phase is durable.

- [ ] **Step 4: Implement suppression rights.**

  Persist victim expiry by kingdom ID, prune expired entries annually, refresh on later raids, and extend `CanReceiveDirectWar` to accept the origin permanently or an unexpired victim. Continue through the normal war manager.

- [ ] **Step 5: Run rules, guards, and net48 build.**

  Expected: all exit 0.

- [ ] **Step 6: Commit settlement.**

  ```powershell
  git add Code/core/lineage Tests/BanditRaidSettlementSourceGuard.ps1
  git commit -m "feat: settle bandit food raids conservatively"
  ```

### Task 10: Regression verification, deployment, and visible launch

**Files:**
- Modify only files required by failures attributable to this feature.

- [ ] **Step 1: Run all feature slices and source guards.**

  ```powershell
  dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
  Get-ChildItem Tests/Bandit*.ps1 | ForEach-Object { & powershell -NoProfile -ExecutionPolicy Bypass -File $_.FullName; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
  ```

  Expected: all exit 0.

- [ ] **Step 2: Run the complete detached suite and record unrelated baselines.**

  Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

  Expected: no new failures; if the known `ArmyRtsCaptainCombatRulesTests.cs.txt:146` baseline remains, record it verbatim and verify the feature slice independently passes.

- [ ] **Step 3: Build the production mod.**

  Run: `dotnet build AncientWarfare3.csproj -c Release --no-restore`

  Expected: build exits 0 for net48.

- [ ] **Step 4: Commit any verification-only corrections.**

  ```powershell
  git status --short
  git add Code/core/lineage/PeasantRebelBandit* Code/patch/AW_BanditStrongholdZonePatch.cs Code/patch/AW_CityOccupationAccelerationPatch.cs Code/content/GodPowerLibrary.cs Code/ui/AW_LineageTab.cs Code/core/performance/AWAuthorityCycleService.cs Code/core/multiplayer/AW3RuntimeRestorePipeline.cs Code/api/multiplayer/AW3MultiplayerStrategicStateModels.cs Code/core/multiplayer/AW3MultiplayerStrategicStateCoordinator.cs Locales Tests/Bandit* Tests/AncientWarfare3.Rules.Tests
  git commit -m "test: verify bandit stronghold lifecycle"
  ```

- [ ] **Step 5: Deploy with backup and hash parity.**

  Stop only the currently running WorldBox process after identifying it, copy the existing deployed mod to a timestamped `.aw3-deploy-backups` directory, mirror source-controlled runtime assets and Release binaries into `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`, and compare SHA-256 hashes for the built/deployed DLL and all changed source/localization files.

- [ ] **Step 6: Launch WorldBox visibly and inspect logs.**

  Start the game executable without `-WindowStyle Hidden`, confirm the process owns a visible main window, wait for AW3's `Loaded` log line, and search the fresh log tail for exceptions mentioning `Bandit`, `Stronghold`, `PeasantRebel`, `GodPower`, `City.addZone`, or `Actor.goTo`.

- [ ] **Step 7: Report the manual scenario checklist.**

  Verify in game: god power on an ordinary city; mother/outer zones immediately restored; exactly one fixed-zone `虎踞寨`; canonical/display/title strings; conversion unlock; fall return/removal; food-shortage physical outbound/loot/return/cooldown; save/load during active stronghold and raid; victim/origin suppression declarations.
