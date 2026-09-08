# Military Governorate Subject Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce non-nesting military governorates, allow the direct suzerain to split or reclaim one city at a time, and let high-central-power AI reclaim governorate territory.

**Architecture:** Keep relation authority in `VassalService`/pure rules, put city mutation and last-city cleanup in a dedicated military-governorate administration service, and route player actions through the existing authoritative multiplayer command path. The annual AI calls the same service with a bounded one-city budget.

**Tech Stack:** C#/.NET Framework 4.8, Unity/WorldBox runtime APIs, SQLite-backed lineage stores, source-guard/rules test harness.

---

### Task 1: Add failing pure-rule coverage

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` only if a new rules file is introduced

- [ ] **Step 1: Write failing assertions** for creator kinds, direct-suzerain authorization, protected seat/last-city cases, and central-power reclaim threshold.
- [ ] **Step 2: Run** `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` and confirm failures are caused by missing rules.
- [ ] **Step 3: Add minimal APIs** in `MilitaryGovernorateRules`/`VassalRelationRules` to express those decisions.
- [ ] **Step 4: Re-run** the focused test project and confirm green.
- [ ] **Step 5: Commit** `test: cover military governorate subject governance rules`.

### Task 2: Block nested creation at every authority boundary

**Files:**
- Modify: `Code/core/lineage/VassalRelationRules.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Code/core/lineage/MilitaryGovernorateCreationService.cs`
- Modify: `Code/core/multiplayer/commands/AW3RealmCommandHandler.cs`

- [ ] **Step 1:** Pass subject-kind and suzerain-kind facts into the relation validator; reject ordinary vassal or military-governorate creators and reject any non-direct suzerain.
- [ ] **Step 2:** Make seat eligibility accept only a mother kingdom’s own city or a city in its direct military governorate, never a nested subject’s city or a governorate seat.
- [ ] **Step 3:** Remove the command handler’s unconditional `city.kingdom == country` restriction only for an authorized direct-governorate split, while retaining general ownership checks.
- [ ] **Step 4:** Add regression/source-guard assertions that both UI and command paths cannot bypass the same checks.
- [ ] **Step 5:** Run focused rules/source-guard tests and commit.

### Task 3: Implement one-city split/reclaim service

**Files:**
- Create: `Code/core/lineage/MilitaryGovernorateAdministrationService.cs`
- Create: `Code/core/lineage/MilitaryGovernorateAdministrationRules.cs` if pure decisions are not suitable in existing rules
- Modify: `Code/core/lineage/MilitaryGovernorateStore.cs` only for state-end/seat repair helpers
- Modify: `Code/core/lineage/VassalService.cs` for projection/index refresh hooks
- Modify: `Code/core/lineage/ChronicleEvents.cs` and `Code/core/lineage/HistoryLocalizationRules.cs` for split/reclaim records

- [ ] **Step 1:** Add failing service/source-guard tests for authorized direct suzerain, one-city mutation, seat protection, and final-city relation closure.
- [ ] **Step 2:** Implement `CanSplitCity`/`TrySplitCity` using the existing creation transaction and preserve the old governorate when it retains at least one city.
- [ ] **Step 3:** Implement `CanReclaimCity`/`TryReclaimCity` to transfer exactly one non-seat city to the suzerain, invalidate projections, and dirty maps/caches.
- [ ] **Step 4:** Implement final-seat handling: end state/relation, clear governorate projection, transfer the remaining city, and avoid partial mutation on failure.
- [ ] **Step 5:** Run focused tests and commit.

### Task 4: Expose suzerain operations through commands and UI

**Files:**
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalogModels.cs`
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalog.cs`
- Modify: `Code/core/multiplayer/commands/AW3RealmCommandHandler.cs`
- Modify: `Code/ui/windows/MilitaryGovernorateWindow.cs` or add a dedicated management window
- Modify: `Code/ui/items/VassalRelationListItem.cs`
- Modify: `Locales/ch.json`, `Locales/cz.json`, `Locales/en.json`

- [ ] **Step 1:** Add command request factories/descriptors for split and reclaim with country/subject/city IDs.
- [ ] **Step 2:** Add authoritative handlers that resolve the direct suzerain and call the administration service.
- [ ] **Step 3:** Add a city list/action in the governorate management flow; show actions only to the mother kingdom and hide them for ordinary vassals/governorates.
- [ ] **Step 4:** Add localized success/failure text and refresh the relation window after accepted commands.
- [ ] **Step 5:** Run catalog/source-guard tests and commit.

### Task 5: Add central-power AI reclamation

**Files:**
- Modify: `Code/core/lineage/MilitaryGovernorateAiService.cs`
- Modify: `Code/core/lineage/MilitaryGovernorateRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateRulesTests.cs.txt`

- [ ] **Step 1:** Add failing tests for threshold `>= 80`, one reclaim per realm-year, and no reclaim below threshold.
- [ ] **Step 2:** Enumerate direct military governorates deterministically, choose one eligible non-seat city, and call `TryReclaimCity` once per annual evaluation.
- [ ] **Step 3:** If a governorate has only its seat, call the same service’s relation-end path; reset cursors/state after successful reclaim.
- [ ] **Step 4:** Run focused tests and commit.

### Task 6: Verify the full change

**Files:** None.

- [ ] **Step 1:** Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`.
- [ ] **Step 2:** Run `dotnet build AncientWarfare3.csproj`.
- [ ] **Step 3:** Run `git diff --check` and inspect only intended diffs; preserve unrelated pre-existing working-tree changes.
- [ ] **Step 4:** Record commands/results before claiming completion.
