# Local Government Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert each city's abstract bureau into a real, circulating local government with exam-based staffing, 10-to-15-year terms, hometown patronage, and the existing court window reused for navigation and display.

**Architecture:** Keep `CityBureauState` as the aggregate cache, but derive its filled count and officer IDs from real `CourtOfficer` appointments. Add a small local-office definition/rules layer, extend the existing annual city slice, and add a city context to `CourtWindow`; all local cards and nodes use the same court presentation components and parameter types as central offices.

**Tech Stack:** C# 11/net48, SQLite, existing deferred annual work, `OfficialCareerService`, civil-service candidate tables, Unity UI, net9 rules/SQL tests, PowerShell source guards.

---

## File Map

- Create: `Code/core/court/LocalCourtOfficeRules.cs` for local office IDs, slot mapping, and definition selection.
- Create: `Code/core/court/LocalOfficialTermRules.cs` for deterministic 10-to-15-year terms.
- Create: `Code/core/court/LocalOfficialCandidateRules.cs` for hard gates, exam admission, scoring, and hometown bonus.
- Create: `Code/core/court/LocalCourtReadModel.cs` for city card/context snapshots.
- Create: `Code/ui/components/CourtCityGovernmentCard.cs` for the national-view city card using existing court styles.
- Modify: `Code/core/court/CourtIds.cs` and locale files for local office IDs/names.
- Modify: `Code/core/court/CourtRules.cs` and `CourtCityOfficeRules.cs` for real local capacity and leader recognition.
- Modify: `Code/core/court/CityBureauAnnualWorkService.cs` to reconcile local appointments in city slices.
- Modify: `Code/core/court/OfficialCareerStateService.cs` and `OfficialCirculationRules.cs` for unconditional finite local terms and rotation.
- Modify: `Code/core/court/CivilServiceExamRules.cs`, `CivilServiceExamService.cs`, `CivilServiceWaitingPoolQuery.cs`, and `CivilServiceExamCandidatePoolQuery.cs` for expanded local demand and lower-stage eligibility.
- Modify: `Code/core/court/CivilServiceQualificationService.cs` and `Code/core/court/OfficialCareerService.cs` to permit the explicit local lower-stage appointment path without weakening central appointment gates.
- Modify: `Code/core/court/CourtReadModelService.cs`, `Code/core/court/CourtPyramidRules.cs`, and `Code/ui/windows/CourtWindow.cs` for national city cards and city context.
- Modify: `Code/ui/items/CourtActorNodeView.cs` only where shared appointment/history actions need city context.
- Modify: `Code/patch/AW_CityTabPatch.cs` to add a direct city-detail entry that calls the shared city context.
- Modify: `Code/core/court/CustomCourtTemplateModels.cs`, codec, validation, instance, and runtime files to persist one central template plus multiple local templates.
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs` to switch the existing editor between the central template and selected local templates.
- Create: `Code/core/court/CustomLocalCourtTemplateRules.cs` for template validation, legacy migration, automatic assignment, manual override, and replacement rules.
- Modify: `Code/core/lineage/LineageKeys.cs` and `Code/core/db/CityBureauStateTableItem.cs` for stable city-template binding and override persistence.
- Create: `Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj`, `Program.cs`, and pure rule tests.
- Create: `Tests/LocalGovernmentSqlSourceGuard.ps1` and `Tests/LocalGovernmentUiSourceGuard.ps1`.

### Task 1: Define Local Offices and Finite Terms

**Files:**
- Create: `Code/core/court/LocalCourtOfficeRules.cs`
- Create: `Code/core/court/LocalOfficialTermRules.cs`
- Modify: `Code/core/court/CourtIds.cs`
- Modify: `Code/core/court/CourtRules.cs`
- Test: `Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRulesTests.cs`

- [ ] **Step 1: Write failing pure tests.**

```csharp
Equal(CourtOfficeId.Governor,
    LocalCourtOfficeRules.OfficeForSlot(0, CourtProfileId.Xia),
    "city leader is the root office");
Equal(CourtOfficeId.GranaryOfficer,
    LocalCourtOfficeRules.OfficeForSlot(1, CourtProfileId.Xia),
    "second city slot is granary administration");
Equal(CourtOfficeId.Constable,
    LocalCourtOfficeRules.OfficeForSlot(2, CourtProfileId.Xia),
    "third city slot is local constable");
True(LocalOfficialTermRules.IsValidTermLength(
    LocalOfficialTermRules.TermLength(ability: 20, merit: 80, age: 35,
        actorId: 10, appointmentYear: 100)),
    "local terms are always ten to fifteen years");
True(OfficialCirculationRules.IsRotatingCityOffice(
    CourtOfficeId.GranaryOfficer, xiaCirculationUnlocked: false),
    "all local offices circulate regardless of central law");
```

- [ ] **Step 2: Run the isolated project and verify it fails.**

Run: `dotnet run --project Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj`

Expected: compile failure for the new local rules.

- [ ] **Step 3: Implement slot and term rules.** Keep `CityOfficeSlots` as the total number of local seats (1 through 3). Slot 0 maps to the profile's city leader; slots 1 and 2 map to `granary_officer` and `constable`. Generate a term from ability, merit, age fitness, and a stable actor/year jitter, clamped to `[10,15]`; no central `CourtTermLaw` value may return lifetime for a local office.

- [ ] **Step 4: Update office identity helpers.** `CourtCityOfficeRules.IsCityLeaderOffice` recognizes all profile city-leader IDs; `LocalCourtOfficeRules.IsLocalOffice` recognizes every local slot ID. Preserve western mayor/count IDs and map them to the same local slot semantics.

- [ ] **Step 5: Run the isolated project and commit.**

```powershell
dotnet run --project Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj
git add -- Code/core/court/LocalCourtOfficeRules.cs Code/core/court/LocalOfficialTermRules.cs Code/core/court/CourtIds.cs Code/core/court/CourtRules.cs Code/core/court/CourtCityOfficeRules.cs Tests/LocalGovernmentRules.Isolated.Tests
git commit -m "feat: define circulating local court offices"
```

### Task 2: Add Candidate, Examination, and Hometown Rules

**Files:**
- Create: `Code/core/court/LocalOfficialCandidateRules.cs`
- Modify: `Code/core/court/CivilServiceExamRules.cs`
- Modify: `Code/core/court/CivilServiceWaitingPoolQuery.cs`
- Modify: `Code/core/court/CivilServiceExamCandidatePoolQuery.cs`
- Test: `Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRulesTests.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CivilServiceExamRulesTests.cs.txt`

- [ ] **Step 1: Write failing candidate tests.**

```csharp
True(LocalOfficialCandidateRules.CanEnter(
    alive: true, adult: true, slave: false, alreadyOfficial: false,
    examinationEnabled: true, qualification: "juren",
    participatedAndFailedHigherStage: false),
    "local-stage pass enters local pool");
True(LocalOfficialCandidateRules.CanEnter(
    alive: true, adult: true, slave: false, alreadyOfficial: false,
    examinationEnabled: true, qualification: "none",
    participatedAndFailedHigherStage: true),
    "higher-stage non-finalist remains locally employable");
Equal(25, LocalOfficialCandidateRules.HometownBonus, "乡党 bonus is explicit");
True(LocalOfficialCandidateRules.Score(60, 50, sameNativeCity: true) >
     LocalOfficialCandidateRules.Score(90, 50, sameNativeCity: false),
     "qualified same-native-city recommendation is material");
```

Link the isolated project to `LocalCourtOfficeRules.cs`,
`LocalOfficialTermRules.cs`, `LocalOfficialCandidateRules.cs`,
`OfficialCirculationRules.cs`, and the existing `CourtIds.cs` production files;
the project must not copy production logic into the test directory.

- [ ] **Step 2: Run the isolated project and the civil-service slice.**

Run: `dotnet run --project Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --civil-service-exam-slice`

Expected: new local tests fail while the unchanged civil-service baseline passes.

- [ ] **Step 3: Implement the local eligibility boundary.** Examination-enabled local service accepts `juren`, `gongshi`, `jinshi`, and an explicit `participatedAndFailedHigherStage` fact. It still rejects dead, non-adult, slave, already-active-official, king, heir, and office-ineligible actors. Without examination technology, call the existing formal candidate rules.

Add an optional `pAllowLocalLowerQualification` argument to
`OfficialCareerService.Appoint` and `PrepareAppointment`. Pass it only from
`LocalCourtAppointmentService`; `CivilServiceQualificationService` accepts
`juren` or the stored higher-stage-participant marker when that flag is true,
while central callers keep the existing formal `gongshi`/`jinshi` gate.

- [ ] **Step 4: Expand exam demand without removing final-rank scarcity.** Add `LocalVacancyCount` and include all real local office vacancies in `CivilServiceExamRules.FinalAdmissionQuota`. Keep palace/national quotas unchanged; local/prefectural admission and waiting reserve use the bounded vacancy target. `CivilServiceWaitingPoolQuery` accepts the expanded local qualifications, while `CivilServiceExamCandidatePoolQuery` excludes only currently active officials and already-consumed final appointments.

- [ ] **Step 5: Add hometown score inputs.** Read `OfficialCareerState.NativeCityId` for the city leader and candidate native city. Apply `HometownBonus` only after hard gates; store the recommending actor ID and source reason in the appointment request model. No candidate is appointed solely because of birthplace.

- [ ] **Step 6: Run both test slices and commit.**

```powershell
dotnet run --project Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --civil-service-exam-slice
git add -- Code/core/court/LocalOfficialCandidateRules.cs Code/core/court/CivilServiceExamRules.cs Code/core/court/CivilServiceWaitingPoolQuery.cs Code/core/court/CivilServiceExamCandidatePoolQuery.cs Tests/LocalGovernmentRules.Isolated.Tests Tests/AncientWarfare3.Rules.Tests/CivilServiceExamRulesTests.cs.txt
git commit -m "feat: expand local civil-service recruitment and hometown preference"
```

### Task 3: Reconcile Real City Appointments in Sliced Annual Work

**Files:**
- Modify: `Code/core/court/CityBureauAnnualWorkService.cs`
- Modify: `Code/core/court/OfficialCareerService.cs`
- Modify: `Code/core/court/OfficialCareerStateService.cs`
- Create: `Code/core/court/LocalCourtAppointmentService.cs`
- Modify: `Code/core/db/CityBureauStateTableItem.cs` only if an explicit vacancy/recommender snapshot is required.
- Test: `Tests/LocalGovernmentSqlSourceGuard.ps1`

- [ ] **Step 1: Add a failing SQL/source guard for real appointments.** Require `OfficialCareerService.Appoint(`, `OfficialCareerService.EndForOffice(`, `LocalOfficialTermRules.TermLength(`, city-scoped `CourtOfficer` reads, and `HistoricalWriteService.TryUpsertState`; reject synthetic-only writes that set `OFFICER_ACTOR_IDS` without a career record.

- [ ] **Step 2: Implement `LocalCourtAppointmentService.ReconcileCity`.** For one valid city, load capacity, current local officers, and due terms. Close dead/transferred/expired incumbents with explicit reasons, then fill empty slots from the bounded candidate pool. Use `OfficialCareerService.Appoint(actor, kingdom, CourtOfficeLayer.City, officeId, schoolId, city)` so persistence and hot-state projection stay atomic.

- [ ] **Step 3: Derive aggregate bureau state from appointments.** Serialize only active officer IDs that match the city and kingdom. `OFFICE_SLOTS`, local school, and efficiency remain cached summary fields; the filled count is `activeLocalOfficerCount`, never a random or synthetic number.

- [ ] **Step 4: Wire the service into `CityBureauAnnualWorkService.ProcessCity`.** Keep `CitiesPerSlice`, retry counters, and coalescing unchanged. A failed appointment write returns `false` and uses the existing city retry path; a candidate shortage commits the summary with an explicit vacancy and retries next maintenance cycle.

- [ ] **Step 5: Add city-transfer and destruction guards.** On city ownership change or destruction, close active local careers for that city with `city_transferred` or `city_destroyed`; invalidate the city read model. Do not delete completed `CourtOfficer` rows.

- [ ] **Step 6: Run source guard and production build.**

Run: `powershell -ExecutionPolicy Bypass -File Tests/LocalGovernmentSqlSourceGuard.ps1`

Run: `dotnet build AncientWarfare3.csproj`

```powershell
git add -- Code/core/court/CityBureauAnnualWorkService.cs Code/core/court/OfficialCareerService.cs Code/core/court/OfficialCareerStateService.cs Code/core/court/LocalCourtAppointmentService.cs Code/core/db/CityBureauStateTableItem.cs Tests/LocalGovernmentSqlSourceGuard.ps1
git commit -m "feat: staff city bureaus with real court careers"
```

### Task 4: Rotate Every Local Official After 10-15 Years

**Files:**
- Modify: `Code/core/court/OfficialCareerStateService.cs`
- Modify: `Code/core/court/OfficialCirculationRules.cs`
- Modify: `Code/core/court/CityBureauAnnualWorkService.cs`
- Test: `Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRulesTests.cs`

- [ ] **Step 1: Add failing term and rotation tests.** Assert a local subordinate office rotates even when the central term law is lifetime, no local term is below 10 or above 15 years, and a same-city renewal is not automatic when another valid destination/vacancy exists.

- [ ] **Step 2: Run the isolated project and verify the new tests fail.**

Run: `dotnet run --project Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj`

- [ ] **Step 3: Apply local-term precedence.** In `OfficialCareerStateService.OnKingdomYear`, branch on `LocalCourtOfficeRules.IsLocalOffice(state.OfficeId)` before central `CourtTermLaw` handling. Set `TermEndYear` from `LocalOfficialTermRules`; mark every local office as rotation-eligible, not only governor or Western mayor.

- [ ] **Step 4: Build a bounded intercity rotation plan.** Reuse `OfficialCirculationRules` matching, but include all local offices and never assign an actor to their native city when the existing circulation rule forbids it. If no valid destination exists, close and refill the same vacancy through the ordinary candidate path rather than extending the old term indefinitely.

- [ ] **Step 5: Run tests and commit.**

```powershell
dotnet run --project Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj
dotnet build AncientWarfare3.csproj
git add -- Code/core/court/OfficialCareerStateService.cs Code/core/court/OfficialCirculationRules.cs Code/core/court/CityBureauAnnualWorkService.cs Tests/LocalGovernmentRules.Isolated.Tests
git commit -m "feat: rotate all local officials on finite terms"
```

### Task 5: Add National City Cards and Reused City Court Context

**Files:**
- Create: `Code/core/court/LocalCourtReadModel.cs`
- Create: `Code/ui/components/CourtCityGovernmentCard.cs`
- Modify: `Code/core/court/CourtReadModelService.cs`
- Modify: `Code/core/court/CourtPyramidRules.cs`
- Modify: `Code/ui/windows/CourtWindow.cs`
- Modify: `Code/ui/items/CourtActorNodeView.cs`
- Modify: `Locales/aw3_court.csv`
- Create: `Tests/LocalGovernmentUiSourceGuard.ps1`
- Modify: `Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRulesTests.cs`

- [ ] **Step 1: Write a source/UI guard before implementation.** Require `CourtWindow.OpenCity(`, `LocalCourtReadModel`, `CourtCityGovernmentCard`, shared `CourtActorNodeView`, and `CourtOfficeHistoryWindow`; reject a second window class with independent layout constants or a second portrait/navigation path.

- [ ] **Step 2: Add city snapshot models.** `LocalCourtReadModel` contains kingdom ID, city ID/name, leader node, active local officer count, total slots, efficiency, and native-school ID. It contains no live Actor references except IDs and persisted name snapshots.

- [ ] **Step 3: Replace national flat local nodes with city cards.** `CourtReadModelService.Build` keeps central/military/censor nodes and returns city snapshots for the local section. `CourtCityGovernmentCard` creates a child `CourtActorNodeView` for the leader and binds the same existing office-card model, `AW_UIStyle`, text/icon/tooltip parameters, and history action; it does not call private portrait helpers or create a second portrait path. It displays city identity, leader, term, and `filled/slots`.

- [ ] **Step 4: Add city context to `CourtWindow`.** Preserve `Open(long kingdomId)` for national view and add:

```csharp
public static void OpenCity(long pKingdomId, long pCityId)
{
    _kingdomId = pKingdomId;
    _cityId = pCityId;
    OpenInternal(pRefreshImmediately: true);
}
```

The same `CourtWindow` instance renders `LocalCourtReadModel` nodes when `_cityId >= 0`; all layout, zoom, button, portrait, and history code paths remain shared. A stale city returns to `Open(kingdomId)`.

- [ ] **Step 5: Add local office definitions to the existing profile resolver.** Local nodes use `CourtOfficeLayer.City`, existing grade/school/effect parameters, and the same node card. Do not create a local-only UI styling system.

- [ ] **Step 6: Add a direct entry to the original city window.** Extend `AW_CityTabPatch` with a second stable button on `Tabs Right`. Resolve the current city and kingdom IDs when binding the click listener, then call only `CourtWindow.OpenCity(kingdomId, cityId)`. Reuse the existing button construction style and localize its tooltip as `aw_city_local_court_entry` / `aw_open_city_local_court`.

- [ ] **Step 7: Write failing local-template rules tests.** Assert legacy city-layer offices migrate into one generated local template; a package can retain multiple distinct local templates; civil and military automatic assignment select their marked defaults; stable-ID fallback is deterministic; a manual city override wins; deleting an in-use template requires and returns a replacement binding; and the displayed city type equals the selected template name.

- [ ] **Step 8: Add the multi-template package model and migration.** Extend `CustomCourtTemplate` with a bounded `LocalTemplates` collection of `CustomLocalCourtTemplate` values containing stable ID, localized name, default kind, city-layer offices, and internal edges. Bump the schema with backward-compatible import: move legacy city-layer offices and their internal edges into a generated `local_default` template while retaining central offices and archiving cross-layer edges. Validation rejects duplicate template IDs/names, non-city offices inside local templates, cross-template edges, missing offices, and more than 16 local templates.

- [ ] **Step 9: Persist and resolve city template bindings.** Add `CITY_LOCAL_COURT_TEMPLATE_ID` and `CITY_LOCAL_COURT_TEMPLATE_MANUAL` runtime keys plus `LOCAL_TEMPLATE_ID` and `LOCAL_TEMPLATE_MANUAL` columns on `CityBureauState`. `CustomCourtRuntime.TryGetLocalTemplate(kingdom, city, out template)` resolves a valid manual binding first, then a military/civil default, then stable-ID fallback. The local appointment and read-model services use only this resolver. The local city-type label is `template.Name`, never a separate classification string.

- [ ] **Step 10: Add the shared workflow switch and local-template controls.** In `CustomCourtWorkflowWindow`, add the central/local segmented control. Local mode adds a bounded template dropdown and create, duplicate, rename, default-kind, and delete actions while reusing the existing canvas and office settings. Selection changes clear stale edge endpoints. Save/import/export/apply operate on the complete package transaction.

- [ ] **Step 11: Add city assignment controls.** In shared `CourtWindow` city context, add a local-template dropdown using existing dropdown/button styles. Player selection persists a manual override and refreshes the same city read model. Deleting an in-use template requires a replacement and rebinds affected city rows before package save commits.

- [ ] **Step 12: Extend the UI source guard.** Require the city patch to call `CourtWindow.OpenCity(`, the custom workflow and city context to use `CustomLocalCourtTemplateRules`, and runtime/read models to call `TryGetLocalTemplate`; reject a second local court window, independent local UI style, or unbounded template list.

- [ ] **Step 13: Run UI guard, isolated rules tests, and build.**

Run: `powershell -ExecutionPolicy Bypass -File Tests/LocalGovernmentUiSourceGuard.ps1`

Run: `dotnet run --project Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj`

Run: `dotnet build AncientWarfare3.csproj`

```powershell
git add -- Code/core/court/LocalCourtReadModel.cs Code/core/court/CustomLocalCourtTemplateRules.cs Code/core/court/CustomCourtTemplateModels.cs Code/core/court/CustomCourtTemplateJsonCodec.cs Code/core/court/CustomCourtTemplateRules.cs Code/core/court/CustomCourtInstanceModels.cs Code/core/court/CustomCourtInstanceCodec.cs Code/core/court/CustomCourtRuntime.cs Code/ui/components/CourtCityGovernmentCard.cs Code/core/court/CourtReadModelService.cs Code/core/court/CourtPyramidRules.cs Code/ui/windows/CourtWindow.cs Code/ui/windows/CustomCourtWorkflowWindow.cs Code/ui/items/CourtActorNodeView.cs Code/patch/AW_CityTabPatch.cs Code/core/lineage/LineageKeys.cs Code/core/db/CityBureauStateTableItem.cs Locales/aw3_court.csv Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRulesTests.cs Tests/LocalGovernmentUiSourceGuard.ps1
git commit -m "feat: show city governments in the shared court window"
```

### Task 6: Integrate, Migrate, and Verify Long-Running Behavior

**Files:**
- Modify: `Code/core/court/CityBureauAnnualWorkService.cs`
- Modify: `Code/core/court/CourtReadModelService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs` only for explicit migration/version keys.
- Test: `Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRulesTests.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/OfficialCareerCrossLayerPersistenceSqlTests.cs.txt`

- [ ] **Step 1: Add migration tests.** Load a legacy `CityBureauState` with leader-only data and assert first annual maintenance creates real leader history, then subordinate vacancies without deleting the old aggregate state. Run two maintenance cycles and assert no duplicate active officer per city/office.

- [ ] **Step 2: Add long-run simulation assertions.** Simulate 30 years with three cities and verify every local term stays within 10-15 years, histories contain closed rows, city cards remain bounded, and same-native-city preference appears only among qualified candidates.

- [ ] **Step 3: Run all focused tests.**

Run: `dotnet run --project Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --civil-service-exam-slice`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --office-history`

Run: `powershell -ExecutionPolicy Bypass -File Tests/LocalGovernmentSqlSourceGuard.ps1`

Run: `powershell -ExecutionPolicy Bypass -File Tests/LocalGovernmentUiSourceGuard.ps1`

Run: `dotnet build AncientWarfare3.csproj`

- [ ] **Step 4: Run the existing relevant guard set and inspect performance.**

Run: `powershell -ExecutionPolicy Bypass -File run_relevant_guards.ps1`

Expected: all existing guards pass; no new guard reports a full-world actor enumeration in annual work or court rendering.

- [ ] **Step 5: Commit the integration slice.**

```powershell
git add -- Code/core/court Code/core/db/CityBureauStateTableItem.cs Code/ui/windows/CourtWindow.cs Code/ui/components/CourtCityGovernmentCard.cs Code/ui/items/CourtActorNodeView.cs Locales Tests/LocalGovernmentRules.Isolated.Tests Tests/LocalGovernmentSqlSourceGuard.ps1 Tests/LocalGovernmentUiSourceGuard.ps1
git commit -m "feat: complete circulating local government courts"
```

## Plan Self-Check

- Local offices, 10-15-year terms, exam expansion, lower-stage recruitment,
  hometown scoring, real appointments, national city cards, shared city
  context, save migration, and performance bounds each have a task.
- The plan never introduces a second office table or a separate local window
  layout framework.
- Every code path that can create a local official goes through
  `OfficialCareerService` and therefore writes permanent history.
