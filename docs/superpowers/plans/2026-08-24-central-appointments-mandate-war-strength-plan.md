# Central Appointments, Mandate War, and Army Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make central/local appointments transactional, isolate Zhulu wars on the vanilla war path, transfer Mandate and its capital ring correctly, strengthen Mandate armies through vanilla enlistment, and prevent repeated scans or invalid reorganization.

**Architecture:** Add narrow rule/services at existing Court, WarPatch, Mandate, and army-replenishment boundaries. War type gates decide whether a war is vanilla, lightweight-history, or AW3-special before any lifecycle service runs. Candidate and war indexes cache IDs, while all live eligibility and ownership checks remain dynamic.

**Tech Stack:** C#/.NET, Harmony patches, WorldBox runtime types, SQLite persistence, existing source-guard/rules test harness.

---

### Task 1: Establish war-type ownership gates

**Files:**
- Modify: `Code/core/lineage/ZhuluWarRules.cs`
- Modify: `Code/core/lineage/ZhuluWarService.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Modify: `Code/core/lineage/WarTerminalSettlementCoordinator.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/WarTypeOwnershipSourceGuardTests.cs.txt`

- [x] Add explicit predicates for vanilla-only Zhulu startup/end/capture paths.
- [x] Return from `AW_WarPatch` Zhulu branches after lightweight identity/history persistence; do not call RTS, logistics, levy, garrison, reserve, coalition, negotiation, or mod war-score lifecycle services.
- [x] Gate Zhulu-specific capture and terminal settlement hooks so native occupation/termination remains authoritative.
- [x] Add source guard coverage asserting Zhulu starts cannot invoke AW3 army, logistics, or levy lifecycle methods.
- [ ] Run the focused rules/source guards and commit the gate change.

### Task 2: Bound Zhulu declaration work

**Files:**
- Modify: `Code/core/lineage/ZhuluAgeRules.cs`
- Modify: `Code/core/lineage/WarDecisionAI.cs`
- Modify: `Code/core/lineage/ZhuluAgeDirectorService.cs`
- Modify: `Code/core/lineage/ZhuluWarService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/ZhuluAgeRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/ZhuluPerformanceSourceGuard.ps1`

- [x] Replace `int.MaxValue` Zhulu candidate limits with a configured bounded limit, preserving adjacent-first ordering.
- [x] Cache the monthly realm/subject score snapshot and invalidate it on war, capture, vassal, or mandate changes.
- [x] Bound the Zhulu director alliance/unification checks and pause new declarations at the configured active-war limit.
- [x] Bound repeated Zhulu capture checks through the active-war index; declaration counters remain intentionally diagnostic-only.
- [ ] Run focused tests and commit.

### Task 3: Add Mandate capital transfer and phase reconciliation

**Files:**
- Modify: `Code/core/lineage/MandateService.cs`
- Modify: `Code/core/lineage/MandateCoreTransferRules.cs`
- Modify: `Code/core/lineage/ZhuluAgeDirectorService.cs`
- Modify: `Code/core/lineage/MandatePhaseService.cs`
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MandateCoreTransferRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/MandatePhaseRulesTests.cs.txt`

- [x] Persist the war-start Mandate capital ID and use it even if the live capital later moves.
- [x] On confirmed Mandate-capital capture, transfer the one-hop adjacent, original-Mandate-controlled legal-core ring before installing the new Mandate.
- [x] Make the transfer idempotent by war ID, retain the war-start capital ID, and route city changes through the existing map/core refresh hooks.
- [x] Reconcile self-founded/restored Mandate state to `Renewal`, clear stale `Chaos`, and leave active claimant/Zhulu states untouched.
- [x] Add tests for capital capture, repeated capture callbacks, and restored-phase recovery; third-party-held ring cities remain excluded by live ownership checks.

### Task 4: Enforce reserve-aware army reorganization

**Files:**
- Modify: `Code/core/lineage/ArmyReplenishmentOperationService.cs`
- Modify: `Code/core/lineage/ArmyRtsRules.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/ArmyReplenishmentOperationRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsRulesTests.cs.txt`

- [x] Require positive reserve and positive live shortage before creating or refreshing a reorganization operation.
- [x] Apply a bounded no-reserve yearly runtime cooldown/reason and stop repeated empty-candidate scans.
- [x] Permit a new operation only after reserve availability is observed again.
- [x] Add tests proving zero reserve never creates a reorganization loop.

### Task 5: Strengthen Mandate armies through vanilla enlistment

**Files:**
- Create: `Code/core/lineage/MandateMilitaryStrengthRules.cs`
- Create: `Code/core/lineage/MandateMilitaryStrengthService.cs`
- Modify: `Code/patch/AW_EnlistPatch.cs`
- Modify: `Code/core/lineage/ArmyReplenishmentOperationService.cs`
- Modify: `Code/core/lineage/MandateMilitaryPhaseService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MandateMilitaryStrengthRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/MandateMilitaryStrengthSourceGuardTests.cs.txt`

- [ ] Apply configurable Mandate military and mobilization multipliers only to effective war power and replenishment budgets.
- [ ] Set the wartime army target to `min(900, configured target)` and stop when the target is reached.
- [x] Use native city enlistment flow (`checkCanMakeWarrior` then `makeWarrior`) with widened adult-local eligibility; do not add a world-wide actor scan or synthetic population for this path.
- [x] Exclude king, heir, city leader, royal guard, existing army members, babies, dead actors, and actors already owned by another army.
- [ ] Stop immediately when native candidates or reserve are exhausted and emit a bounded diagnostic reason.

### Task 6: Transactional central/local appointments and candidate catalog

**Files:**
- Create: `Code/core/court/OfficerCandidateCatalog.cs`
- Create: `Code/core/court/OfficerAppointmentTransferService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/LocalCourtAppointmentService.cs`
- Modify: `Code/core/court/CivilServiceQualificationService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CentralCourtVacancySourceGuardTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/OfficerCandidateCatalogRulesTests.cs.txt`

- [x] Build the candidate catalog once per kingdom/year and keep live qualification checks at appointment time.
- [x] Before central appointment, release any local office, write local end history, assign the central office, then enqueue local replacement.
- [x] Refill local office from same office, same city/state, then national catalog; western governments retain election queues.
- [ ] Keep live qualification checks at selection time and prevent duplicate office ownership.
- [ ] Add transaction-order source guards and candidate-cache tests.

### Task 7: Imperial cession preference and de jure label refresh

**Files:**
- Modify: `Code/core/lineage/WarPeaceDefaultOfferRules.cs`
- Modify: `Code/core/lineage/WarPeaceSettlementRuntime.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapLabelRuntime.cs`
- Modify: `Code/core/court/DeJureRegionReadModelService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/WarPeaceDefaultOfferRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/HierarchicalDeJureMapSourceGuardTests.cs.txt`

- [x] Detect imperial-level border wars and rank legal cession candidates before tributary terms without bypassing validation.
- [x] Use effective domestic seats for labels, legal seats for global labels, and refresh only affected regions.
- [x] Preserve empty-map behavior after region retirement and synchronize renamed capitals/regions.
- [ ] Add tests for imperial attacker/defender cession preference and seat fallback.

### Task 8: Matrix verification and delivery

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarTypeOwnershipMatrixTests.cs.txt`

- [x] Test ordinary, Zhulu, Mandate, rebel/restoration, succession-dispute, and other vanilla-occupation war ownership gates through focused source/rules coverage.
- [ ] Run `git diff --check`.
- [ ] Run the focused PowerShell source guards and rules test project; record existing workspace build blockers separately.
- [ ] Review the diff for accidental changes to deleted performance files or unrelated user work.
- [ ] Commit implementation in bounded changes, then report verification results before any push/deployment.
