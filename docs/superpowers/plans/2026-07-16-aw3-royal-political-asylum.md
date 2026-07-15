# AW3 Royal Political Asylum Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Evacuate non-ruling royal children during defensive wars without changing nationality, return them after the war, and naturalize them into the host realm immediately if their home realm is destroyed.

**Architecture:** A pure rule layer defines family eligibility, host eligibility/ranking, return, and extinction outcomes. A bounded service persists actor state plus a home-kingdom roster, while a dedicated no-city actor job represents foreign residence without invoking vanilla nationality-changing city APIs.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox actor jobs/tasks/statuses, the lineage history store, net9 pure-rule tests, PowerShell source guards.

---

### Task 1: Define asylum decisions as pure rules

**Files:**
- Create: Code/core/lineage/RoyalAsylumRules.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt

- [ ] **Step 1: Add the production link and failing behavior tests**

Cover king/current-heir exclusion, king's other child and heir's child inclusion, invalid actor rejection, peaceful foreign host eligibility, deterministic host ordering, return readiness, and extinction naturalization.

Use the intended API:

    True(RoyalAsylumRules.IsProtectedFamilyCandidate(
        homeAlive: true, monarchy: true, actorAlive: true,
        actorBelongsToHome: true, actorIsSlave: false,
        actorIsForeignKing: false, actorIsKing: false,
        actorIsCurrentHeir: false, isKingsDirectChild: true,
        isHeirsDirectChild: false),
        "a ruler's non-heir child is evacuated");
    Equal(false, RoyalAsylumRules.IsProtectedFamilyCandidate(
        true, true, true, true, false, false,
        false, true, true, false),
        "the current heir remains with the realm");
    True(RoyalAsylumRules.ShouldNaturalize(
        homeRealmAlive: false, hostCityValid: true),
        "realm extinction naturalizes a refugee into the host");

- [ ] **Step 2: Run tests and confirm RED**

    dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj

Expected: compilation fails because RoyalAsylumRules does not exist.

- [ ] **Step 3: Implement immutable host rank and predicates**

Create RoyalAsylumHostRank implementing IComparable with SameIsland, DistanceSquared, KingdomId, and CityId. Same island sorts first, followed by smaller distance and IDs. Implement only the tested conjunctions.

- [ ] **Step 4: Run tests and confirm GREEN**

Expected: Rule tests passed.

- [ ] **Step 5: Commit the contract**

    git add -- Code/core/lineage/RoyalAsylumRules.cs Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
    git commit -m "test: define royal asylum rules"

### Task 2: Register keys, status, job, and roaming task

**Files:**
- Modify: Code/core/lineage/LineageKeys.cs
- Create: Code/content/RoyalAsylumContent.cs
- Create: Code/ai/behaviours/actor/BehRoyalAsylumRoam.cs
- Modify: Code/content/XiaContent.cs
- Modify: Locales/others.csv
- Modify: Tests/SourceGuardTests.ps1

- [ ] **Step 1: Add failing source guards**

Require actor active/home/former-city/host/start/relocation keys, the home roster key, aw_royal_asylum_job, aw_royal_asylum_roam, localized status/task, and registration from XiaContent.Init.

- [ ] **Step 2: Run guards and confirm RED**

    powershell -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1

Expected: all new asset guards fail.

- [ ] **Step 3: Register the assets**

The actor job contains only the roam task, wait, and check_if_stuck_on_small_land. The task uses BehRoyalAsylumRoam, BehGoToTileTarget, and BehRandomWait(4f, 8f). The status uses ui/Icons/iconLoyalty, a long duration, and explicit locale IDs.

- [ ] **Step 4: Implement roaming behavior**

BehRoyalAsylumRoam stops unless the service confirms active asylum and returns a walkable, non-center, non-border tile belonging to the recorded host city. It assigns actor.beh_tile_target and continues.

- [ ] **Step 5: Add localization**

Add status_title_aw_royal_asylum, status_description_aw_royal_asylum, task_unit_aw_royal_asylum_roam, and aw_royal_asylum_host rows in Simplified Chinese, English, and Traditional Chinese.

- [ ] **Step 6: Run guards and Debug build**

Expected: guards for this slice pass and build has 0 errors.

- [ ] **Step 7: Commit content registration**

    git add -- Code/core/lineage/LineageKeys.cs Code/content/RoyalAsylumContent.cs Code/ai/behaviours/actor/BehRoyalAsylumRoam.cs Code/content/XiaContent.cs Locales/others.csv Tests/SourceGuardTests.ps1
    git commit -m "feat: register royal asylum actor state"

### Task 3: Implement bounded evacuation and return

**Files:**
- Create: Code/core/lineage/RoyalAsylumService.cs
- Modify: Code/patch/AW_WarPatch.cs
- Modify: Code/patch/AW_KingdomPolicyPatch.cs
- Modify: Code/patch/AW_SavePatch.cs
- Modify: Tests/SourceGuardTests.ps1

- [ ] **Step 1: Add failing engine-invariant guards**

Require setCity(null) during evacuation; prohibit foreign setCity; require formal joinCity only for return/naturalization; require bounded roster parsing and war-start/year/load/new-world hooks.

- [ ] **Step 2: Run guards and confirm RED**

- [ ] **Step 3: Implement family and war reconciliation**

OnWarStarted evacuates eligible children for defensive participants and relocates refugees hosted by any new participant. OnKingdomYear checks only that kingdom's bounded roster and current king/heir child lists. HasActiveDefensiveWar enumerates only wars involving that kingdom and requires war.isDefender(home).

- [ ] **Step 4: Implement deterministic host selection**

Scan living civilization kingdoms only when a transition needs a host. Reject home, wild, neutral, enemy, rekt, cityless, and warring candidates. Rank one stable city per kingdom using RoyalAsylumHostRank.

- [ ] **Step 5: Implement bounded host tiles**

Cache at most 48 valid tiles per host city. Invalidate by city object, owner ID, zone count, and center coordinates. Choose a stable starting tile by actor ID; roam selection may rotate by current year.

- [ ] **Step 6: Implement evacuation**

Dismiss city leadership, court office, general, royal guard, army, and warrior work. Call actor.setCity(null), assert actor.kingdom still equals home, write actor fields and the home roster, spawn on the host tile, apply status, and call actor.ai.setJob(RoyalAsylumContent.ActorJobId).

- [ ] **Step 7: Implement relocation and return**

Relocation changes only host IDs/tile and records a new host. Return chooses former city, capital, then nearest living home city, calls same-kingdom joinCity, and clears asylum state.

- [ ] **Step 8: Implement load/reset**

LoadRuntimeState scans kingdom rosters and resolves actor IDs directly; it never scans every actor. It repairs indexes, job, and status. ClearRuntime clears only runtime indexes and tile caches. Wire both into AW_SavePatch.

- [ ] **Step 9: Run guards, rules, and Debug build**

Expected: all pass with 0 build errors.

- [ ] **Step 10: Commit the state machine**

    git add -- Code/core/lineage/RoyalAsylumService.cs Code/patch/AW_WarPatch.cs Code/patch/AW_KingdomPolicyPatch.cs Code/patch/AW_SavePatch.cs Tests/SourceGuardTests.ps1
    git commit -m "feat: evacuate royal family during defensive wars"

### Task 4: Naturalize before survivors become nomads

**Files:**
- Modify: Code/patch/AW_KingdomExtinctionPatch.cs
- Modify: Code/core/lineage/RoyalAsylumService.cs
- Modify: Tests/SourceGuardTests.ps1

- [ ] **Step 1: Add a failing extinction-order guard**

Require RoyalAsylumService.NaturalizeBeforeExtinction(__instance) before makeSurvivorsToNomads.

- [ ] **Step 2: Run source guards and confirm RED**

- [ ] **Step 3: Add ordered naturalization**

At the existing no-city stable-index branch, naturalize valid roster members into their recorded living host before FormerHeirService.ArchiveAndClear and makeSurvivorsToNomads. Revalidate host ownership immediately before joinCity; choose a peaceful replacement when the recorded host is invalid.

- [ ] **Step 4: Run guards and Debug build**

Expected: ordering guard passes and build has 0 errors.

- [ ] **Step 5: Commit extinction ordering**

    git add -- Code/patch/AW_KingdomExtinctionPatch.cs Code/core/lineage/RoyalAsylumService.cs Tests/SourceGuardTests.ps1
    git commit -m "fix: naturalize royal refugees before realm removal"

### Task 5: Block military and office selection abroad

**Files:**
- Modify: Code/patch/AW_EnlistPatch.cs
- Modify: Code/patch/AW_CityLeaderPatch.cs
- Modify: Code/core/court/CourtService.cs
- Modify: Code/core/lineage/GeneralService.cs
- Modify: Code/core/lineage/RoyalGuardService.cs
- Modify: Code/core/lineage/SlaveService.cs
- Modify: Tests/SourceGuardTests.ps1

- [ ] **Step 1: Add failing selector guards**

Require active-asylum rejection at ordinary enlistment, city leadership, central/local court selection, general selection/validation, royal guard selection, and slave-army cadre selection.

- [ ] **Step 2: Run guards and confirm RED**

- [ ] **Step 3: Add minimal gates**

Use RoyalAsylumService.IsActive(actor) at each existing candidate boundary. Add a City.makeWarrior prefix returning false for active refugees. Add an Actor.getNextJob prefix returning the asylum job so vanilla no-city jobs cannot naturalize them.

- [ ] **Step 4: Run source guards and full builds**

Expected: guards pass and Debug/Release have 0 errors.

- [ ] **Step 5: Commit role isolation**

    git add -- Code/patch/AW_EnlistPatch.cs Code/patch/AW_CityLeaderPatch.cs Code/core/court/CourtService.cs Code/core/lineage/GeneralService.cs Code/core/lineage/RoyalGuardService.cs Code/core/lineage/SlaveService.cs Tests/SourceGuardTests.ps1
    git commit -m "fix: isolate royal refugees from offices and armies"

### Task 6: Add biography and actor-window projection

**Files:**
- Modify: Code/core/lineage/ChronicleKeys.cs
- Modify: Code/core/lineage/HistoryLocalizationRules.cs
- Create: Code/core/lineage/RoyalAsylumHistoryService.cs
- Modify: Code/core/lineage/RoyalAsylumService.cs
- Modify: Code/patch/AW_UnitWindowPatch.cs
- Modify: Locales/others.csv
- Modify: Tests/SourceGuardTests.ps1

- [ ] **Step 1: Add failing history/UI guards**

Require royal_asylum_started, royal_asylum_relocated, royal_asylum_returned, and royal_asylum_naturalized person events plus a logical host-city row.

- [ ] **Step 2: Run guards and confirm RED**

- [ ] **Step 3: Record transitions once**

Each actual state transition writes through HistoryWriter.RecordPerson. Annual reconciliation writes nothing when state is unchanged. Naturalization uses the stored home name after the home object enters extinction.

- [ ] **Step 4: Show logical host residence**

When active, AW_UnitWindowPatch shows aw_royal_asylum_host with the resolved host city name even though vanilla actor.city is intentionally null. Nationality and lineage rows continue to use actor.kingdom.

- [ ] **Step 5: Run all verification**

    dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
    powershell -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1
    dotnet build AncientWarfare3.csproj -c Debug
    dotnet build AncientWarfare3.csproj -c Release

Expected: rule tests and source guards pass; both builds have 0 errors.

- [ ] **Step 6: Commit history and UI**

    git add -- Code/core/lineage/ChronicleKeys.cs Code/core/lineage/HistoryLocalizationRules.cs Code/core/lineage/RoyalAsylumHistoryService.cs Code/core/lineage/RoyalAsylumService.cs Code/patch/AW_UnitWindowPatch.cs Locales/others.csv Tests/SourceGuardTests.ps1
    git commit -m "feat: record and display royal asylum"

### Task 7: Deploy and runtime-verify

**Files:**
- Deploy: bin/Debug/net48/AncientWarfare3.dll
- Deploy: bin/Release/net48/AncientWarfare3.dll

- [ ] **Step 1: Re-run all verification fresh**

- [ ] **Step 2: Deploy while preserving .runtime/aw3_lineage_archive.db**

- [ ] **Step 3: Compare source and deployed DLL SHA-256 hashes**

- [ ] **Step 4: Runtime acceptance**

Verify evacuation retains home nationality; host-war relocation occurs; the last defensive war ending returns refugees; home extinction immediately joins the recorded host realm; refugees cannot be enlisted or appointed; save/load restores job/status; and Player.log has no new null-kingdom, join-city, pathfinding, or Harmony exceptions.

