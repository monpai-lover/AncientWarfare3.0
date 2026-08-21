# Bandit Island Migration and Piracy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Preserve an established bandit kingdom while evacuating a weak stronghold to an unoccupied island and raiding coastal cities by temporary boat.

**Architecture:** Add pure migration/target rules, versioned migration state, a bounded island candidate scanner, and a bandit transport adapter. Integrate one migration or raid step into the existing authority cycle; reuse the existing dock route registry, temporary boat production, P0 lifecycle, cargo accounting, and history helpers.

**Tech Stack:** C#/.NET Framework 4.8, Unity/WorldBox APIs, Harmony, Newtonsoft.Json, AW3 rules executable.

---

### Task 1: Pure rules and tests

**Files:**
- Create `Code/core/lineage/PeasantRebelBanditIslandRules.cs`.
- Create `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditIslandRulesTests.cs.txt`.
- Modify the rules test project and `Program.cs.txt` with `--bandit-island-migration`.

- [ ] Add failing tests for: 60% strength escape threshold, population below 4 fallback, two-cycle requirement, island eligibility, and coastal-only piracy targets.
- [ ] Run the focused test and confirm it fails because the production rules type is absent.
- [ ] Implement `ShouldStartEvacuation`, `NextThreatCycles`, `IsEligibleIsland`, and `IsEligiblePiracyTarget` as pure methods.
- [ ] Run the focused test and confirm the pass message.
- [ ] Commit `test: define bandit island migration rules`.

### Task 2: Persisted migration state

**Files:**
- Modify `Code/core/lineage/PeasantRebelBanditStrongholdState.cs`.
- Modify `Code/core/lineage/PeasantRebelBanditStateStore.cs`.
- Extend the Task 1 tests.

- [ ] Add failing tests for legacy defaults, valid stage transitions, and non-empty manifests after boarding.
- [ ] Add schema version 7, `BanditStrongholdKind`, `BanditMigrationStage`, and a normalized migration record containing old city, island/landing tile, year, threat cycles, member IDs, request/boat IDs, and failure count.
- [ ] Add `TryResolveOperational` so active and migration states are readable without weakening legacy active-state checks.
- [ ] Run all bandit rule tests and commit `feat: persist bandit island migration state`.

### Task 3: Bounded island candidate scanner

**Files:**
- Create `Code/core/lineage/PeasantRebelBanditIslandCandidateService.cs`.
- Create `Code/core/lineage/PeasantRebelBanditIslandCandidate.cs`.
- Add candidate rule tests and test dispatch.

- [ ] Add failing ranking tests for invalid islands, safety, route cost, buildable area, distance, and stable ID ordering.
- [ ] Implement a rotating authority-cycle scan over `World.world.islands_calculator.islands`; require no city/stronghold, buildable land, coastal landing, and a live `AWDockTransportService` route.
- [ ] Cache by simulation generation and dock topology revision; never scan from actor ticks.
- [ ] Run candidate tests and commit `feat: discover safe unoccupied bandit islands`.

### Task 4: Bandit transport adapter

**Files:**
- Create `Code/core/lineage/PeasantRebelBanditTransportService.cs`.
- Create `Code/core/lineage/PeasantRebelBanditTransportState.cs`.
- Modify `AWDockTaxiRouteService`, `ArmyRtsTransportProductionService`, and the boat lifecycle patch only where needed for external request callbacks.
- Add transport tests.

- [ ] Add failing tests for manifest filtering, one request per kingdom, boarding/landing completion, timeout rollback, and temporary-boat cleanup.
- [ ] Implement route resolution through `AWDockTransportService`, a registered external `TaxiRequest`, temporary boat provisioning through `TryProvisionAtRoute`, existing boat task IDs, and explicit abort cleanup.
- [ ] Do not create an Army object and do not fake movement with `spawnOn`.
- [ ] Run adapter tests plus existing RTS transport tests and commit `feat: add bandit external transport adapter`.

### Task 5: Migration state machine

**Files:**
- Create `Code/core/lineage/PeasantRebelBanditIslandMigrationService.cs`.
- Modify `PeasantRebelBanditStrongholdService.cs`, `PeasantRebelBanditStrongholdPopulationService.cs`, `PeasantRebelBanditRoute.cs`, and the existing bandit history helper.
- Add migration transition tests.

- [ ] Add failing tests for `None -> Evaluating -> Boarding -> Voyaging -> Founding -> Completed`, invalid transitions, rollback, and single-active-stronghold invariant.
- [ ] In one bounded authority step, update threat cycles, lock an island, pause raids/population fall, start transport, advance one stage, and persist after every transition.
- [ ] At `Founding`, create the city on stable landing, move transported members, switch `StrongholdCityId`, then clean the old city.
- [ ] Preserve an occupied old city owner; return an unoccupied old city to the origin kingdom; never delete the city; record `BanditStrongholdAbandoned`.
- [ ] Extend runtime restore for boarding/voyage/founding and commit `feat: evacuate weak bandit strongholds to islands`.

### Task 6: Island piracy

**Files:**
- Modify `PeasantRebelBanditRaidRules.cs` and `PeasantRebelBanditRaidService.cs`.
- Extend raid tests.

- [ ] Add failing tests for coastal-only targets, transport-owned outbound/return stages, cargo delivery only after return, and failure cooldown.
- [ ] For `StrongholdKind=Island`, select coastal targets, run outbound and return through the adapter, retain existing loot/suppression/cargo rollback, and clean every request/boat on terminal paths.
- [ ] Run bandit and transport tests and commit `feat: let island bandits raid coastal cities`.

### Task 7: Verification and handoff

**Files:** no additional production files.

- [ ] Run `git diff --check`.
- [ ] Build the main project. If the isolated tree lacks the .NET 4.8 reference pack, verify using the repository's working reference path and record the environment limitation; do not retarget the project.
- [ ] Build the rules DLL and run `--bandit-island-migration`, `--bandit-stronghold`, and `--rts-transport-p0`.
- [ ] Review `git diff --stat master...HEAD`; keep unrelated RTS edits out of the branch.
- [ ] Commit final verification notes, then use the finishing-branch workflow for merge/push/deploy choice.
