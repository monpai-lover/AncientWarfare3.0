# Regional Government and Two-Level City Administration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add runtime-only 郡/道-style city aggregation to court views and the city-administration MapMode, with configurable names, nine-rank integration, and the confirmed local-court UI fixes.

**Architecture:** A pure deterministic rules class groups live cities; a runtime service maps groups to current cities, leaders, local courts, and map geometry without persistence. Central JSON stores only one special regional-layer configuration node. Central court, local court, editor, and city MapMode consume the same read model; kingdom MapMode remains unchanged.

**Tech Stack:** C#/.NET 4.8.1, Unity UI, Newtonsoft.Json, existing SQLite court state, isolated console tests, PowerShell source guards.

---

### Task 1: Pure grouping rules

**Files:** Create `Code/core/court/RegionalGovernmentRules.cs`, `Code/core/court/RegionalGovernmentReadModel.cs`, and `Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentRulesTests.cs.txt`; modify the rules test `.csproj` and `Program.cs.txt`.

- [ ] Write failing tests for development-descending seat selection, adjacent-city selection capped at four members, population/ID tie-breakers, no cross-kingdom grouping, isolated/one-city/two-city kingdoms, deterministic ordering, and suffix removal for `州/府/城`.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --regional-government`; expect failure because the types do not exist.
- [ ] Implement immutable city facts and `RegionalGovernmentRules.Build` using development descending, population descending, ID ascending; choose the highest unassigned city as seat, then only same-kingdom direct neighbors up to four, repeating until all cities are assigned. Implement `RegionName(seatName, title)` with fallback title `郡`.
- [ ] Re-run the command; expect `Regional government rules passed.`
- [ ] Commit `feat: add deterministic regional government rules`.

### Task 2: Live aggregation service

**Files:** Create `Code/core/court/RegionalGovernmentAggregationService.cs`; create `Tests/RegionalGovernmentAggregationSourceGuard.ps1`; extend the rules tests.

- [ ] Add a failing source guard requiring `City.neighbours_cities_kingdom`, `DevelopmentMapModeService.GetCityScore`, `city.leader`, and local read-model calls, and rejecting SQLite tables, `VassalService`, and `MilitaryGovernorateStore`.
- [ ] Run the guard and expect failure because the service is absent.
- [ ] Implement `Build(Kingdom)`, `TryFindRegion(Kingdom,long,...)`, `Invalidate(Kingdom)`, and a refresh-scoped cache. Convert valid live cities to Task 1 facts, map results back to City objects, set the governor projection to the seat city's current leader ID, and attach existing `CourtReadModelService.BuildLocal` results. Never serialize regions.
- [ ] Add invalidation calls to existing city ownership/zone dirty paths and run the guard plus focused rules.
- [ ] Commit `feat: expose live regional government read model`.

### Task 3: Central JSON dynamic layer

**Files:** Modify `Code/core/court/CustomCourtTemplateModels.cs`, `CustomCourtTemplateJsonCodec.cs`, `CustomCourtTemplateRules.cs`, `CustomCourtTemplateDocumentRules.cs`, and whole-preset construction if needed; create `Tests/RegionalGovernmentTemplateRulesTests.cs.txt` and `Tests/RegionalGovernmentTemplateSourceGuard.ps1`; register tests in the rules project.

- [ ] Write failing round-trip tests for one normalized layer, default `郡/Commandery` and `郡守/Regional Governor`, separate localized names, preserved central management IDs, legacy JSON auto-upgrade, and local-document exclusion.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --regional-government-template`; expect failure.
- [ ] Add `CustomCourtRegionalGovernmentLayer` with stable ID `regional_government_layer`, localized region/governor titles, and central management IDs. Keep it outside ordinary offices so it cannot create vacancies or appointments.
- [ ] Normalize defaults and duplicate IDs, validate at most one layer, reject prerequisite edges to it, allow management links from valid central offices, upgrade old central documents, and strip it from local documents.
- [ ] Run the focused suite and commit `feat: persist configurable regional government layer`.

### Task 4: Fix local court selector, flag, and built-in office localization

**Files:** Modify `Code/core/court/CustomCourtRuntime.cs`, `Code/ui/windows/CourtWindow.cs`, and `Locales/aw3_court.csv`; create `Tests/CourtWindowRegionalRegressionSourceGuard.ps1`; extend `CustomLocalGovernmentRulesTests.cs.txt`.

- [ ] Add failing assertions that built-in `minzhou/junfu` are selectable and named, local summary calls `KingdomFlagBuilder.Build`, and selector visibility uses the resolved catalog rather than a custom snapshot.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --custom-local-government`; expect failure.
- [ ] Add one resolved-catalog helper used by `TryGetLocalTemplate`, `TrySetLocalTemplate`, and `OfficeDisplayName`; preserve manual built-in IDs.
- [ ] Call `KingdomFlagBuilder.Build` from `UpdateLocalSummary`; ensure unresolved sprites disable images and clear stale sprites/colors. Make `UpdateLocalTemplateOptions` list built-in or custom templates. Add exact compatibility keys for the four logged Minzhou office IDs.
- [ ] Run the rules suite and source guard; commit `fix: restore local court selector and flag rendering`.

### Task 5: Court read models and dynamic projections

**Files:** Modify `Code/core/court/CourtReadModelService.cs`, `CourtPyramidRules.cs`, `LocalCourtReadModel.cs`, `Code/ui/components/CourtCityGovernmentCard.cs`, and `Code/ui/items/CourtActorNodeView.cs`; create `Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentCourtRulesTests.cs.txt` and `Tests/RegionalGovernmentCourtSourceGuard.ps1`.

- [ ] Write failing tests for one dynamic projection per region, seat leader ID reuse, no appointment/vacancy row, grouped local cards, dual governor/州牧 display, and local superior visibility.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --regional-government-court`; expect failure.
- [ ] Extend local models with region identity/title, regional governor title/Actor ID, and a non-appointable superior flag. In `CourtReadModelService.Build`, expand the configured dynamic layer into computed regional projection nodes and links to member local-government cards. In `BuildLocal`, add a non-deletable superior projection and automatic links to local top-level offices.
- [ ] Reuse the existing actor node/portrait path; render region, seat, member count, and localized tooltips. Run tests/guard and commit `feat: render regional governors across court views`.

### Task 6: Custom editor support

**Files:** Modify `Code/ui/windows/CustomCourtWorkflowWindow.cs`, `Code/ui/components/CourtWorkflowVacancyCard.cs`, `CourtWorkflowCanvas.cs`, and `Locales/aw3_court.csv`; create `Tests/CustomCourtRegionalEditorSourceGuard.ps1`.

- [ ] Add a failing guard for a central-only dynamic card, separate region/governor name inputs, dynamic marker, management-only links, protected delete/settings/grade/slot/effect actions, and JSON persistence.
- [ ] Run the guard and expect failure.
- [ ] Create/normalize the special card on central mode, render existing card style with dashed border and marker, add localized Chinese/English title inputs/tooltips, and save only the regional-layer fields and management IDs. Keep local mode's projection read-only.
- [ ] In `ApplyLayout`, use `-510f` for central mode and retain `-530f` for local mode; do not change window size or close/back controls. Run workflow/template suites and commit `feat: edit dynamic regional government node`.

### Task 7: Two-level city administration MapMode

**Files:** Modify `Code/core/policy/HierarchicalVassalMapModeOptionRules.cs`, `HierarchicalVassalMapModeState.cs`, `HierarchicalVassalMapModeService.cs`, `HierarchicalVassalMapLabelRuntime.cs`, and `HierarchicalVassalMapModeLabelLayer.cs`; create `Tests/CityAdministrationMapModeRulesTests.cs.txt` and `Tests/CityAdministrationMapModeSourceGuard.ps1`.

- [ ] Write failing tests for independent city-mode `Regions/Cities` levels, region push/pop, outside-click return, existing city inspection, cache keys containing kingdom and seat IDs, and no country-mode state changes.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --city-administration-mapmode`; expect failure.
- [ ] Add a separate city-administration breadcrumb state; leave country-mode state untouched. At region level aggregate member zones and publish region labels through existing geometry/font/pooling. At focused level publish only member city labels.
- [ ] Branch `HandleZoneClick` only for city mode: region click pushes, focused-region/outside click pops, member-city click calls existing `TryInspectCity`. Reuse dirty/invalidation hooks and native/runtime label key formats.
- [ ] Run the suite/guard and commit `feat: add two-level city administration map mode`.

### Task 8: Nine-rank local entry and compatibility

**Files:** Modify `Code/core/court/LocalOfficialCandidateRules.cs`, `OfficialCareerRankRules.cs`, `LocalCourtAppointmentService.cs`, and `CourtService.cs`; create `Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentNineRankRulesTests.cs.txt`.

- [ ] Write failing tests for lowest local entry at ninth/secondary-ninth after unlock, higher office floors/service history, unchanged pre-unlock behavior, and automatic governor projection without a second appointment record.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --regional-government-nine-rank`; expect failure.
- [ ] Use existing `NineRankRules`, office-grade floors, and career history for local appointments. Do not add a `regional_governor` database office; any current seat leader is automatically projected as governor. Run existing court/civil-service suites and commit `feat: connect nine-rank entry to local offices`.

### Task 9: Full verification and deployment

**Files:** Only concrete fixes in the files above, tests, or localization; do not stage the pre-existing `Code/core/schools/HistoricalSchoolDescentService.cs` change.

- [ ] Run focused rules: `--regional-government`, `--regional-government-template`, `--custom-local-government`, `--regional-government-court`, `--city-administration-mapmode`, `--regional-government-nine-rank`, `--custom-court-template`, and `--custom-court-multiplayer`; every command must exit 0.
- [ ] Run all five new source guards plus `Tests/ReportedLocalizationCoverageSourceGuard.ps1`; every guard must print PASS.
- [ ] Build with `dotnet build AncientWarfare3.csproj -c Release -p:TargetFrameworkVersion=v4.8.1`; expect 0 errors and 0 warnings.
- [ ] Deploy changed release/localization files to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0` after creating `.aw3-deploy-backups\<timestamp>-regional-government`; verify SHA-256 hashes.
- [ ] Smoke-test flags, built-in selector, editor dynamic node/name changes, local superior projection, central grouping, city MapMode region-to-city drill-down, unchanged kingdom MapMode, leader replacement, and logs free of missing localization/resource errors. Commit only verified fixes.
