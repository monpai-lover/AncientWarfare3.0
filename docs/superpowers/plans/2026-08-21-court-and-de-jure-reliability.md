# Court And De Jure Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix local court presentation/localization, make court vacancies self-healing with city-officer promotion priority, and automatically assign newly founded cities to a same-kingdom de jure state.

**Architecture:** Keep `CourtWindow` as the shared central/local view but give local mode an explicit summary-button layout. Move static fallback strings into `Locales/aw3_court.csv`. Add a bounded city-foundation/de jure assignment service with pure ranking rules, and trigger city-leader repair through the existing Harmony leader lifecycle with a local-officer-first candidate pass.

**Tech Stack:** C#/.NET Framework, Harmony, Unity UI, NeoModLoader localization, existing text-based rules regression harness.

---

### Task 1: Add failing regression/source-guard tests

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/DeJureNewCityAssignmentRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/CourtLocalLayoutAndLeaderRepairSourceGuardTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write tests for the pure de jure ranking contract**

  Cover adjacent-state preference, shared-adjacency count, nearest-member fallback, seat/region-ID tie breaks, foreign/retired/stronghold exclusion, no-candidate result, and idempotent assignment.

- [ ] **Step 2: Write source guards for integration contracts**

  Assert that `AW_ChroniclePatch` calls the new city assignment service, `DeJureRegionStore` contains the automatic assignment reason and revision/version mutation, `CourtWindow` has local layout branches and no active local “fill vacancies” button, `AW_CityLeaderPatch` contains a same-city `CourtOfficeLayer.City` candidate pass, and the expected CSV keys exist.

- [ ] **Step 3: Register both test files in the text harness**

  Add their `RunAll()` calls to `Program.cs.txt` and run the harness before implementation. Expected result: the new source guards fail because the production methods and wiring do not yet exist.

### Task 2: Implement pure de jure candidate rules

**Files:**
- Create: `Code/core/court/DeJureNewCityAssignmentRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/DeJureNewCityAssignmentRulesTests.cs.txt`

- [ ] **Step 1: Define immutable facts and result types**

  Add facts for the new city, region ID, member IDs, adjacent member count, nearest member squared distance, seat squared distance, active/eligible/current-kingdom flags, and a deterministic `Select` method returning the winning region ID or `-1`.

- [ ] **Step 2: Implement ranking**

  Filter invalid candidates; order candidates with adjacent candidates first, then descending shared-adjacency count, ascending nearest-member distance, ascending seat distance, and ascending region ID. Return no candidate when the filtered set is empty.

- [ ] **Step 3: Run the focused rules harness**

  Expected result: all pure ranking tests pass; integration source guards remain red.

### Task 3: Implement runtime new-city assignment and store mutation

**Files:**
- Create: `Code/core/court/DeJureNewCityAssignmentService.cs`
- Modify: `Code/core/court/DeJureRegionStore.cs`
- Modify: `Code/patch/AW_ChroniclePatch.cs`

- [ ] **Step 1: Add an explicit automatic store mutation**

  Add `AssignCityAutomatically(long pTargetRegionId, City pCity, string pReason, out string pError)`. Under the existing store lock, revalidate eligibility, active target, current membership, and city ID; append only when unassigned; add history with `FromRegionId=-1`, increment target `Version` and `StoreRevision`, and clear `RegionalGovernmentAggregationService`.

- [ ] **Step 2: Add runtime candidate resolution**

  Resolve the city, refresh its neighbour zones/cities, enumerate active regions, keep only live eligible members currently owned by the new city’s kingdom, build pure facts, and commit with reason `city_created_auto_assign`. Never create a region or revive a retired region.

- [ ] **Step 3: Add one-shot bounded retry**

  Add a deduplicated city-ID queue with one deferred retry for initialization instability. Treat “no candidate region” as final; retry only missing/stale city/kingdom/tile/neighbour readiness. Clear the queue on world reset.

- [ ] **Step 4: Wire the foundation postfix**

  Call the service from `City.newCityEvent` after the existing city-foundation notifications. On success, mark the kingdom hierarchy dirty and refresh de jure presentation once; failures must not interrupt city creation.

- [ ] **Step 5: Run focused source guards and full rules harness**

  Expected result: de jure tests and source guards pass without changing unrelated dirty files.

### Task 4: Correct local court layout and CSV localization

**Files:**
- Modify: `Code/ui/windows/CourtWindow.cs`
- Modify: `Locales/aw3_court.csv`
- Modify: `Code/core/court/CourtLocalizationRepair.cs`
- Modify: `Code/core/court/CourtImmediateVacancyLocalization.cs`

- [ ] **Step 1: Add explicit local summary layout**

  Keep central order unchanged. In local mode place Statistics immediately left of Back to Kingdom, place Custom Local Government directly below Back to Kingdom, and hide central-only Examination, Household, and central vacancy controls. Recompute positions after every refresh.

- [ ] **Step 2: Remove the manual vacancy button from the shared window**

  Do not render or activate `_centralVacancyButton` as a user action. Retain backend vacancy APIs only for compatibility with existing multiplayer commands until their callers are migrated.

- [ ] **Step 3: Normalize localization keys in CSV**

  Add missing `aw_back_to_kingdom` and all `aw_court_fill_vacancies_*` keys to `Locales/aw3_court.csv`, correct language columns, and remove the obsolete `aw_back_to_court` fallback usage. Static UI labels must resolve through `AW_L10n.Text` and CSV.

- [ ] **Step 4: Stop adding static court strings in repair dictionaries**

  Remove entries duplicated by CSV from both repair classes. Keep no new code-side translations; leave the classes only if a verified non-CSV runtime fallback remains necessary.

- [ ] **Step 5: Run CSV uniqueness and localization source guards**

  Expected result: no duplicate keys, all UI keys exist in CSV, and no local screenshot label falls back to English under Chinese locale.

### Task 5: Make city-leader vacancies immediate and local-officer-first

**Files:**
- Modify: `Code/patch/AW_CityLeaderPatch.cs`
- Modify: `Code/patch/AW_PromotionPatch.cs`
- Create: `Code/core/court/CityLeaderVacancyRepairService.cs`
- Create: `Code/core/court/CityLeaderVacancyRepairRules.cs`

- [ ] **Step 1: Add failing pure priority tests**

  Assert that eligible same-city `CourtOfficeLayer.City` officers outrank all realm candidates; among local officers order by office grade, career rank, merit, ability, then actor ID; ineligible/dead/foreign/central officers are excluded; realm fallback is selected only when local candidates are empty.

- [ ] **Step 2: Implement pure local candidate priority rules**

  Use a small candidate fact type and deterministic comparator. The rule must not mutate actors or cities.

- [ ] **Step 3: Implement immediate repair service**

  On a removed/dead leader, validate the city is live, owned, not being captured, and leaderless. Enumerate `pCity.getUnits()` and retain only current-city candidates whose persisted office layer is `CourtOfficeLayer.City`, whose office belongs to the current local institution, and who pass existing appointment qualification. Promote the best candidate through the existing `CourtService.TryAssignCityGovernor` path; otherwise invoke the existing realm candidate pipeline. Use a coalesced deferred retry only when the death/leader pointer is still unstable.

- [ ] **Step 4: Wire death/removal lifecycle**

  In `AW_PromotionPatch.RemoveLeader_Postfix`, call the repair service after career cleanup and before only-on-next-native-tick behavior. Avoid recursion by using the existing governor rotation/runtime scopes and return immediately when the removal is part of a replacement transaction.

- [ ] **Step 5: Preserve automatic behavior for all vacancy sources**

  Keep central and local vacancy repair invoked by city/office mutation services, civil-service completion, custom-court apply, and leader death. Remove the UI dependency on `FillCentralVacanciesImmediately`.

- [ ] **Step 6: Run leader priority tests and source guards**

  Expected result: local priority tests pass and the source guards prove no manual-button dependency remains.

### Task 6: Build, regression test, and deploy

**Files:**
- Modify only generated/build output as required by the repository build scripts.

- [ ] **Step 1: Run the complete rules test harness**

  Run the repository’s existing `AncientWarfare3.Rules.Tests` command. Expected result: all existing and new tests pass.

- [ ] **Step 2: Build the Release configuration**

  Run the existing Release build command and require zero errors and zero warnings attributable to these changes.

- [ ] **Step 3: Inspect the final diff**

  Verify only the intended implementation, tests, CSV, and design/plan files are staged; preserve unrelated user modifications.

- [ ] **Step 4: Deploy to the configured WorldBox Mods directory**

  Deploy the built DLL and assets to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0` with a timestamped backup.

- [ ] **Step 5: Report verification evidence**

  Report test/build/deploy results and the exact manual checks: local statistics placement/localized labels, automatic leader replacement from a local officer, adjacent and isolated new-city de jure assignment, and save/load persistence.
