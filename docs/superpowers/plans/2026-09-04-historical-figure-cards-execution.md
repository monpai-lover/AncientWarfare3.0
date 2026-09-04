# Historical Figure Cards Distributed Execution Plan

> For agentic workers: execute this document as the master plan. The two source
> plans remain the detailed requirements:
> - docs/superpowers/plans/2026-09-04-historical-figure-cards.md
> - docs/superpowers/plans/2026-09-04-historical-figure-card-recycle-window.md

**Goal:** Execute the historical figure card system and standalone recycle window
as one coordinated change, with Monarch and Minister crate roles, role-aware
deployment, and same-rarity recycle filtering.

**Execution model:** Use isolated worktrees or sub-branches for each workstream.
Each workstream may modify only its owned files. The integration workstream is the
only one allowed to combine changes in shared UI, localization, and test-harness
files.

---

## 1. Coordination rules

1. Start from the same current branch revision and record the baseline commit.
2. Read both source plans before starting a workstream.
3. Do not run two workstreams that edit the same file at the same time.
4. Every workstream writes tests before production code for its pure rules.
5. Every workstream runs its focused test command before handing off.
6. Commits must contain only files owned by that workstream.
7. Do not reset, checkout, or clean unrelated existing worktree changes.
8. The integration workstream resolves conflicts by preserving behavior from both
   plans, not by choosing one entire file version.
9. Do not deploy or package until the final integration gate passes.

The known rules-test baseline has unrelated missing-type failures involving
AW3HistoryEventPublisher and KingdomAnnualWorkStage.StateGovernment. Record these
separately; they are not permission to ignore new card failures.

---

## 2. File ownership

### Workstream A: domain and catalogue

Owns:

- Code/content/figures/HistoricalFigureCardModels.cs
- Code/content/figures/HistoricalFigureCardCatalog.cs
- Code/content/figures/HistoricalFigureCardCrates.cs
- Code/core/lineage/HistoricalFigureCardRoleRules.cs
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardCatalogRulesTests.cs.txt
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRoleRulesTests.cs.txt

Contract delivered:

- Stable HistoricalFigureCardRole.Monarch and Minister values.
- Every card has an explicit role.
- Existing period crate IDs remain unchanged.
- Role-filtered period pools are available.
- Catalogue parent, fame, rarity, and historical-name validation remains intact.

### Workstream B: collection and recycle domain

Owns:

- Code/core/lineage/HistoricalFigureCardCollectionStore.cs
- Code/core/lineage/HistoricalFigureCardDrawService.cs
- Code/content/figures/HistoricalFigureCardRecycleRules.cs
- Code/content/figures/HistoricalFigureCardInventoryRules.cs
- Code/content/figures/HistoricalFigureCardRecycleSelectionRules.cs
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardCollectionRulesTests.cs.txt
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDrawRulesTests.cs.txt
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardInventoryRulesTests.cs.txt
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRecycleRulesTests.cs.txt
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRecycleSelectionRulesTests.cs.txt

Contract delivered:

- Collection persistence and source-crate accounting remain atomic.
- Blue, purple, and pink require ten inputs; red requires five.
- Gold cannot be recycled.
- Selection starts with all eligible non-gold cards.
- The first selected card locks the quality.
- Visible cards then contain only the locked quality.
- Clearing the final slot or pressing reset removes the lock.
- Same-card repetition is limited by owned quantity.

### Workstream C: deployment and official-candidate integration

Owns:

- Code/core/lineage/HistoricalFigureCardDeploymentRules.cs
- Code/core/lineage/HistoricalFigureCardDeploymentService.cs
- Code/core/lineage/LineageKeys.cs
- Code/core/court/CivilServiceQualificationService.cs
- Code/core/court/LocalCourtAppointmentService.cs
- Code/core/court/OfficerCandidateCatalog.cs
- Code/core/lineage/HistoricalFigureCardIdentityService.cs
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDeploymentRulesTests.cs.txt
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDeploymentServiceSourceGuardTests.cs.txt

Contract delivered:

- Monarch cards use the existing city or unowned-tile kingdom-founding flow.
- Minister cards require an existing living civil kingdom and a city.
- Minister cards never create, rename, or take ownership of a kingdom.
- Minister actors use the target city's species and join that city.
- Ministers enter the normal official candidate catalogue.
- Ministers receive a fixed positive candidate score bonus of 50.
- The bonus is applied in both candidate-table build and reposition paths.
- Existing king, heir, adult, alive, slave, existing-office, and affiliation
  safety gates remain active.
- Card identity, parentage, biography, and appointment history are committed
  before the card is consumed.

### Workstream D: card draw and recycle UI

Owns after Workstreams A-C are merged:

- Code/ui/windows/HistoricalFigureDrawWindow.cs
- Code/ui/windows/HistoricalFigureRecycleWindow.cs
- Code/ui/items/HistoricalFigureCardListItem.cs
- Code/ui/AW_LineageTab.cs
- Code/ui/AW_LineageWindowIds.cs
- Code/ui/HistoricalFigureCardPlacementPowerService.cs
- Code/patch/AW_HistoricalFigureCardPatch.cs

Contract delivered:

- The lineage tab opens the draw system.
- The draw window shows Monarch and Minister top-level crate categories.
- Period crates under a category use role-filtered pools.
- The inventory has browse, sort, details, and deployment only.
- The inventory opens the dedicated recycle window.
- The recycle window has a real left scroll list and fixed right slots.
- Clicking the first card immediately filters the left list to its quality.
- Reset or clearing all slots immediately restores all eligible qualities.
- Recycle success uses existing source weighting and result details.
- Monarch and Minister deployment paths use the service contract from C.
- Map deployment uses the target city's species for cities and the existing Xia
  spawn power only for valid unowned land Monarch deployment.

### Workstream E: audio, localization, test harness, documentation, and assets

Owns after Workstream D is merged:

- Code/core/lineage/HistoricalFigureCardAudioService.cs
- Locales/aw3_historical_cards.csv
- Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardAcceptanceSourceGuardTests.cs.txt
- Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRecycleWindowSourceGuardTests.cs.txt
- docs/api/historical-figure-cards.md
- THIRD_PARTY_NOTICES.md
- GameResources/ui/historical_cards/
- GameResources/sounds/historical_cards/

Contract delivered:

- All new UI text has Simplified Chinese, English, and Traditional Chinese rows.
- New test sources and production sources are registered exactly once.
- The full test runner invokes every new card suite.
- Audio remains optional and never blocks draw, recycle, or deployment.
- The public API documentation describes Monarch and Minister deployment.
- Third-party notices cover only included audio/assets and existing provenance.

---

## 3. Execution waves

### Wave 0: baseline coordinator

Owner: coordinator

- [ ] Record branch, commit, and worktree state.
- [ ] Run the existing mod build.
- [ ] Run the existing window regression script.
- [ ] Run the rules test executable and record unrelated baseline failures.
- [ ] Create one coordination note containing the baseline results.

Commands:

~~~powershell
git branch --show-current
git log -1 --oneline
git status --short --branch
dotnet build AncientWarfare3.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WindowUiRegressionTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
~~~

Gate: baseline output is saved in the coordination note and no baseline file is
reverted.

### Wave 1: independent domain work

Run Workstream A and Workstream B in parallel. Workstream B may use the
existing model/rule APIs as they stand, but it must not edit A-owned files. If a
constructor or type contract must change, stop at the handoff and report the
required contract instead of editing the other workstream's files.

Workstream A tasks:

- [ ] Execute the domain/catalogue tasks from the source card plan.
- [ ] Add the role tests before role production code.
- [ ] Verify every catalogue card has one role and every role-filtered pool is
  non-empty where the data requires it.
- [ ] Commit with a domain-specific message.

Workstream B tasks:

- [ ] Execute the collection/store tasks from the source card plan.
- [ ] Add selection-state tests before selection production code.
- [ ] Verify source accounting and recycle transaction tests.
- [ ] Commit with a collection-specific message.

Handoff gate:

~~~powershell
git diff --check
git status --short
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
~~~

The coordinator reviews both diffs and merges A before C. B may be merged before
or after A only when its compile-time model dependencies remain compatible.

### Wave 2: deployment and court integration

Run Workstream C after the role contract from A is available. Do not run C in
parallel with A.

- [ ] Add failing role-aware deployment tests.
- [ ] Add failing minister candidate-score tests.
- [ ] Implement role-aware deployment validation.
- [ ] Implement Minister deployment without kingdom creation.
- [ ] Register ministers in the existing candidate catalogue and event-driven
  refresh path.
- [ ] Apply the bonus in both candidate-build and candidate-reposition paths.
- [ ] Preserve all existing heir and king exclusion behavior.
- [ ] Add source guards for no accidental calls to kingdom creation in the
  Minister branch.
- [ ] Commit deployment and court changes.

Required assertions:

~~~csharp
True(HistoricalFigureCardDeploymentRules.CanDeployMinister(
    hasValidCity: true, hasLivingKingdom: true),
    "minister requires an existing civil city");
False(HistoricalFigureCardDeploymentRules.CanDeployMinister(
    hasValidCity: false, hasLivingKingdom: false),
    "minister cannot deploy to unowned land");
Equal(50, HistoricalFigureCardRoleRules.MinisterCandidateBonus,
    "minister score bonus is fixed");
~~~

Handoff gate:

~~~powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore
git diff --check
~~~

### Wave 3: unified UI integration

Run Workstream D only after A, B, and C are merged into the integration branch.

- [ ] Build or reconcile the draw-window state machine from the source card plan.
- [ ] Add role category selection without changing persisted period crate IDs.
- [ ] Move all recycle controls out of the inventory view.
- [ ] Add the standalone recycle window from the recycle source plan.
- [ ] Bind left-list filtering to the pure selection-state contract.
- [ ] Bind repeated card selection and fixed slot removal.
- [ ] Reuse existing TryRecycle and deployment service calls.
- [ ] Preserve card reveal animation, result details, placement state, and map click
  guards.
- [ ] Ensure Minister placement rejects unowned land before world mutation.
- [ ] Ensure Monarch placement keeps the existing Xia/unowned-land behavior.
- [ ] Commit UI integration as one commit because the draw window and list item
  are shared surfaces.

Required UI state invariants:

~~~csharp
IReadOnlyList<HistoricalFigureCardDefinition> visible =
    HistoricalFigureCardRecycleSelectionRules.FilterVisible(
        catalogue, ownedCounts, state.LockedRarity);

if (state.HasInputs)
    Debug.Assert(visible.All(p => p.Rarity.Equals(state.LockedRarity)));
~~~

Handoff gate:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WindowUiRegressionTests.ps1
dotnet build AncientWarfare3.csproj --no-restore
git diff --check
~~~

### Wave 4: localization, audio, tests, and documentation

Run Workstream E after the unified UI is stable.

- [ ] Add all localization rows used by the draw and recycle windows.
- [ ] Add Monarch/Minister category labels and deployment errors.
- [ ] Register new test files and runner calls without duplicate entries.
- [ ] Add source guards for the dedicated window and removed embedded recycle mode.
- [ ] Add audio bindings and optional-resource fallbacks.
- [ ] Update API usage documentation with both deployment paths.
- [ ] Update third-party notices for included resources.
- [ ] Commit the integration support files.

Handoff gate:

~~~powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WindowUiRegressionTests.ps1
git diff --check
~~~

---

## 4. Final coordinator gate

- [ ] Review the combined diff against both source plans.
- [ ] Confirm only the integration branch contains changes to shared files.
- [ ] Confirm no card deployment path writes automatic FigureStateStore identity.
- [ ] Confirm no Minister deployment creates or renames a kingdom.
- [ ] Confirm no Gold card appears in the recycle list.
- [ ] Confirm first recycle click filters the left list immediately.
- [ ] Confirm reset and complete clear restore all eligible qualities.
- [ ] Confirm source-crate IDs remain backward-compatible.
- [ ] Confirm a valid Monarch deployment creates the historical kingdom.
- [ ] Confirm a valid Minister deployment joins an existing city and enters the
  official candidate pool.
- [ ] Confirm candidate score bonus does not override heir or king exclusion.
- [ ] Confirm a failed recycle or deployment does not consume a card.
- [ ] Confirm localized labels and error messages exist for all new controls.
- [ ] Confirm optional audio failure does not block gameplay.

Final commands:

~~~powershell
dotnet build AncientWarfare3.csproj --no-restore
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WindowUiRegressionTests.ps1
git diff --check
git status --short --branch
~~~

Only after this gate may the coordinator deploy code, package a release, or push
the integrated branch.
