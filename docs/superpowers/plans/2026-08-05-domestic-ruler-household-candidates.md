# Domestic Ruler Household Candidates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players and AI fill ruler households from domestic nobles, permit slaves only as consorts, and let vassals formally offer consorts to their suzerain.

**Architecture:** Make candidate eligibility kind-aware, extend query inputs for noble/slave candidate classes, and route domestic commits through a dedicated AW3 authority command without creating self-diplomacy. Expose domestic selection through the household UI and staggered AI maintenance; vassal-to-suzerain offerings continue through the formal diplomacy proposal and authority pipeline.

**Tech Stack:** C#, SQLite, Unity UI, AW3 diplomacy/lineage services, .NET rules tests.

---

### Task 1: Add kind-aware eligibility and ranking tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt`
- Modify: `Code/core/lineage/RulerHouseholdRules.cs`

- [ ] Add RED cases for noble principal/noble consort allowed, slave principal rejected, slave consort allowed, commoner rejected, age bounds 18/33 accepted and 17/34 rejected.
- [ ] Add RED ranking cases: ruling-clan daughter, ruling-clan member, other noble, slave.
- [ ] Introduce pure rules for candidate class and kind, then run focused tests GREEN.

### Task 2: Extend candidate query and runtime validation

**Files:**
- Modify: `Code/core/lineage/RulerHouseholdQuery.cs`
- Modify: `Code/core/lineage/RulerHouseholdService.cs`

- [ ] Pass `RulerHouseholdKind` into candidate reads and runtime eligibility.
- [ ] Principal-wife queries return nobles only; consort queries return nobles followed by `slave_lineage` candidates.
- [ ] Revalidate at commit time so a later slave status cannot enter the principal-wife path.
- [ ] Keep commoners excluded and preserve all existing relationship/kinship checks.
- [ ] Run focused tests.

### Task 3: Add the authority-routed domestic placement path

**Files:**
- Modify: `Code/core/lineage/RulerHouseholdService.cs`
- Modify: `Code/core/lineage/RulerHouseholdRules.cs`
- Modify: `Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs`
- Modify the existing `AW3MultiplayerCommandFacade` source

- [ ] Add `BuildDomesticCandidatePool(Kingdom, RulerHouseholdKind)` and `TryCommitDomestic(Kingdom, actorId, kind, out reason)`.
- [ ] Refactor the existing accepted-offer relationship insertion into a shared core used by foreign and domestic commits.
- [ ] Add a domestic-household authority command and facade method; UI and AI callers submit that command instead of calling `TryCommitDomestic` directly.
- [ ] Domestic commit must not create diplomacy proposals or relationship bonuses; it keeps citizenship domestic and moves the actor to the capital only when necessary.
- [ ] Preserve real lover links for principal wives and existing household metadata for consorts.
- [ ] Add tests for self-diplomacy absence and relationship creation.

### Task 4: Add staggered AI domestic filling

**Files:**
- Modify: `Code/core/lineage/RulerHouseholdService.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`

- [ ] Before generating a foreign request, try one domestic vacancy action.
- [ ] If no principal wife, try a domestic noble principal first; if unavailable, leave the principal slot empty but allow a consort action.
- [ ] Fill at most one person per staggered cycle and stop at capacity.
- [ ] Only generate the existing foreign request when no domestic candidate can fill the current need.
- [ ] Add tests for domestic-first, foreign fallback and one-per-cycle behavior.

### Task 5: Add household-window domestic selection buttons

**Files:**
- Modify: `Code/ui/windows/RulerHouseholdWindow.cs`
- Modify: `Code/ui/windows/RulerHouseholdOfferWindow.cs`
- Modify: localization sources used by these windows

- [ ] Add “册立正妻” and “纳妾” buttons to the household window.
- [ ] Add a domestic mode to the existing candidate window; domestic confirm calls the multiplayer facade, back returns to the household window, and no diplomacy proposal is created.
- [ ] Hide/disable the principal button when a principal exists and the consort button at capacity, returning localized reasons.
- [ ] Keep foreign offer/request modes unchanged.
- [ ] Add source guards for domestic routing and slave/principal rejection.

### Task 6: Add vassal-to-suzerain consort offerings

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Code/core/lineage/RulerHouseholdRules.cs`
- Modify household localization sources
- Modify: `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt`

- [ ] Write RED tests proving only a direct vassal may proactively offer a consort to its suzerain, the direction never selects principal wife, and full capacity, pending duplicates, rejection cooldown or a stale vassal relation block creation.
- [ ] Select one 18–33 candidate from the vassal realm using ruling-lineage, other-noble, then slave ordering; commoners remain excluded.
- [ ] Build a normal `HouseholdOffering` proposal with consort detail and fixed candidate identity, then pass it through the existing async proposal and authority command paths.
- [ ] Bound generation to one offering per vassal per staggered diplomacy cycle and preserve the suzerain's right to reject.
- [ ] Add localized request, rejection and stale-condition reasons; run the focused tests GREEN.

### Task 7: Regression verification

- [ ] Run focused household tests and source guards.
- [ ] Run complete rules tests; expected `Rule tests passed.`
- [ ] Run `git diff --check`.
- [ ] Do not compile the main mod DLL.
