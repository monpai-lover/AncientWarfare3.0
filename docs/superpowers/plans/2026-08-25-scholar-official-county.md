# Scholar-Official Identity and Virtual County Implementation Plan

> For agentic workers: use `superpowers:subagent-driven-development` or `superpowers:executing-plans` and complete each checkbox in order.

**Goal:** Add the `士大夫` social identity for ordinary officials and a persistent, virtual county layer inside cities without changing vanilla city ownership, population, warfare, or pathfinding.

**Architecture:** Keep `guizu` as the noble/royal identity and add an idempotent social-identity projection service that separates noble eligibility from office holding. Store counties as a JSON sidecar keyed by existing city and zone IDs; expose county court records through a dedicated county office scope and drill into counties only after a city is selected in the hierarchy map.

**Tech Stack:** C#/.NET Framework 4.8, Harmony, SQLite archive tables, JSON sidecars, existing Unity map-mode and court UI abstractions, focused source-guard/rules tests.

---

### Task 1: Social identity rules and trait registration

**Files:**
- Create: `Code/core/lineage/SocialIdentityRules.cs`
- Create: `Code/core/lineage/SocialIdentityService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/content/XiaTraits.cs`
- Modify: `Code/content/TraitIconUsageRules.cs`
- Create: `Locales/aw3_social_identity.csv`
- Test: `Tests/AncientWarfare3.Rules.Tests/SocialIdentityRulesTests.cs.txt`

- [ ] Write rules tests for ordinary officials, kings/heirs, active titled actors, royal relatives, acting appointments, idempotence, and mutually exclusive traits.
- [ ] Add `TRAIT_SHIDAFU = "shidafu"`, an `aw_social_identity` actor-data key, and explicit values `noble`/`scholar_official`.
- [ ] Register `shidafu` through `NewSocialIdentity` so native trait persistence and the existing opposite list remain in use.
- [ ] Implement a pure eligibility predicate that treats king, registered heir, active enfeoffment/noble rank, and royal-family markers as noble; ordinary office alone is never noble.
- [ ] Implement one idempotent projection method that adds/removes `shidafu` and `guizu` together with the actor-data identity, without changing lineage archive status.
- [ ] Add CSV labels/descriptions for both Chinese and English keys and route the trait icon through `TraitIconUsageRules`.
- [ ] Run the focused rules test and commit `feat: add scholar-official identity rules`.

### Task 2: Route all appointment and title transitions through the identity service

**Files:**
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/patch/AW_PromotionPatch.cs`
- Modify: `Code/core/lineage/WesternLineageAdmissionService.cs`
- Modify: `Code/core/lineage/WesternLineageMigrationService.cs`
- Modify: `Code/core/lineage/LineageDispositionService.cs`
- Modify: `Code/core/lineage/VirtualNobleTitleService.cs`
- Modify: `Code/core/lineage/NobleRankService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/SocialIdentitySourceGuardTests.cs.txt`

- [ ] Keep lineage/shi/clan creation for officials, but stop `OnActorPromoted(...Official...)` from unconditionally setting `LINEAGE_STATUS=NOBLE` or adding `guizu`.
- [ ] Call the projection service only after a committed appointment; acting appointments must not receive a permanent identity.
- [ ] Preserve `guizu` for king/heir, titled, royal-family, and formal noble-grant paths; switch only ordinary officials to `shidafu`.
- [ ] Separate Western `pOfficial` from `pNoble` admission arguments and preserve non-Xia behavior.
- [ ] Replace new direct ordinary-official `guizu` writes with the service while leaving explicit title/grant writes intact.
- [ ] Add a bounded old-save migration pass over active court rows, correcting only actors with office evidence and no formal noble evidence; never rewrite archive history rows.
- [ ] Run source guards and identity tests; commit `fix: separate official and noble identity transitions`.

### Task 3: Create the virtual county data model and deterministic partitioning

**Files:**
- Create: `Code/core/county/CountyModels.cs`
- Create: `Code/core/county/CountyZonePartitionRules.cs`
- Create: `Code/core/county/CountyZonePartitionService.cs`
- Create: `Code/core/county/CountyNameService.cs`
- Create: `Code/core/county/CountyAdministrationStore.cs`
- Create: `name_generators/Xia/historical_admin.json`
- Test: `Tests/AncientWarfare3.Rules.Tests/CountyZonePartitionRulesTests.cs.txt`

- [ ] Define a county record containing stable ID, parent city ID, ordinal, name/source, zone IDs, leader ID, active/manual flags, timestamps, and revision.
- [ ] Implement `<=25 zones -> one county`; otherwise `ceil(zoneCount/25)` counties with no county exceeding 25 zones.
- [ ] Use deterministic connected flood-fill seeds and retain existing valid zone assignments; assign new zones to the nearest non-full adjacent county before creating a new ordinal.
- [ ] Validate every persisted zone against `zone.city == parent city`; repair invalid or duplicated IDs without touching `TileZone.city`.
- [ ] Resolve names from structured historical county pools, append `县`, preserve manual names, and use a stable city/ordinal fallback without duplicates.
- [ ] Persist sidecar JSON with atomic temporary-file replacement, world-generation scoping, and inactive records for destroyed/merged cities.
- [ ] Add tests for 0/1/25/26/50 zones, contiguity, stable IDs, incremental assignment, duplicate names, and invalid loaded zones.
- [ ] Run focused county tests and commit `feat: add persistent virtual county partitioning`.

### Task 4: Integrate county sidecar into save/load and dirty lifecycle

**Files:**
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/core/multiplayer/AW3WorldLoadCoordinator.cs`
- Modify: `Code/patch/AW_ChroniclePatch.cs`
- Modify: `Code/core/county/CountyAdministrationStore.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CountyPersistenceSourceGuardTests.cs.txt`

- [ ] Publish the county snapshot during world save and observe it during load using the same directory/generation hooks as de-jure sidecars.
- [ ] Rebuild missing county records after world load before map labels or court read models are requested.
- [ ] Mark only the affected city dirty from `City.addZone`, zone removal, city transfer, capture, and city destruction hooks; coalesce repeated marks.
- [ ] Clear all county runtime caches on new-world and failed-load paths.
- [ ] Add source guards proving no county path calls `City.addZone`, `setCity`, or population/war/RTS mutation APIs.
- [ ] Run source guards and save/load rule tests; commit `feat: persist and repair county sidecar`.

### Task 5: Add county court scope and county magistrate appointment

**Files:**
- Modify: `Code/core/court/CourtIds.cs`
- Modify: `Code/core/db/CourtOfficerTableItem.cs`
- Modify: `Code/core/court/OfficialCareerStateService.cs`
- Modify: `Code/core/court/LocalCourtAppointmentService.cs`
- Modify: `Code/core/court/LocalLowOfficeVacancyRules.cs`
- Modify: `Code/core/court/OfficialCareerRankRules.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/LocalCourtReadModel.cs`
- Modify: `Code/core/court/CourtReadModelService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CountyCourtRulesTests.cs.txt`

- [ ] Add `CourtOfficeLayer.County`, `CourtOfficeId.CountyMagistrate`, and `COUNTY_ID` with a default of `-1` for old rows; update schema/index/read/write paths.
- [ ] Make county magistrate a fixed grade-30 local office with entry rank restricted to eight/nine and the existing low-office vacancy fallback.
- [ ] Reuse the officer candidate catalog, civil-service qualification, local appointment scoring, and bounded vacancy queue; never scan the whole world per county or per frame.
- [ ] Queue county appointments when a county is created, its leader dies, its term ends, or its city zones change; close career rows before reassignment.
- [ ] Build county nodes/edges under the city governor/first managed office, preserving existing city-leader and regional-governor bindings.
- [ ] Add tests for grade/rank, candidate fallback, term renewal without duplicate history, leader death replacement, old-row migration, and fixed parent edges.
- [ ] Run focused court tests and commit `feat: appoint persistent county magistrates`.

### Task 6: Generalize custom administrative layers for fixed county cards

**Files:**
- Modify: `Code/core/court/CustomCourtTemplateModels.cs`
- Modify: `Code/core/court/CustomCourtTemplateJsonCodec.cs`
- Modify: `Code/core/court/CustomCourtTemplateRules.cs`
- Modify: `Code/core/court/CustomCourtRuntime.cs`
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Code/ui/windows/CourtWindow.cs`
- Modify: `Code/ui/components/CourtCityGovernmentCard.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomAdministrativeLayerRulesTests.cs.txt`

- [ ] Generalize the existing regional fixed-card model to support a county layer without duplicating UI code.
- [ ] Bump template schema and migrate old templates by adding a default county layer while preserving every existing office, edge, and history entry.
- [ ] Force the county card parent to the first valid managed office under the city/regional governor; reject deletion or reparenting of the fixed relation.
- [ ] Render county cards with the existing city/regional card components, actor-window callback, office history callback, and scroll behavior.
- [ ] Ensure applying a local template cannot mutate central offices and applying a central template cannot mutate county/local offices.
- [ ] Run template round-trip and fixed-edge tests; commit `feat: add fixed county administrative cards`.

### Task 7: Add city-to-county hierarchy map drilldown and palette rendering

**Files:**
- Modify: `Code/core/policy/CityAdministrationMapModeRules.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapLabelRuntime.cs`
- Modify: `Code/core/policy/HierarchicalVassalLabelDiscoveryJob.cs`
- Modify: `Code/core/policy/AWMapModeMetaLibrary.cs`
- Modify: relevant `Code/patch/AW_HierarchicalVassalMap*Patch.cs` files
- Test: `Tests/AncientWarfare3.Rules.Tests/CountyMapNavigationRulesTests.cs.txt`

- [ ] Add a county breadcrumb/focus state after a selected city; preserve country -> region -> city behavior and empty-space no-op behavior.
- [ ] Return county metadata by zone ID through the existing hierarchical map meta path; do not add a new map power.
- [ ] Put county labels at a stable county representative zone/centroid, include county revision in cache keys, and use the existing dirty/zone-budget label jobs.
- [ ] Derive county colors from the vanilla kingdom palette plus stable city/county ordinal offsets; cache color assets and never allocate them every frame.
- [ ] Add click/back tests for every level, cross-city clicks, unmapped terrain, destroyed counties, and stale cache invalidation.
- [ ] Run map rules/source guards and commit `feat: drill hierarchy map into virtual counties`.

### Task 8: Integration verification and release gate

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/ScholarOfficialCountyIntegrationTests.cs.txt`
- Modify: `docs/superpowers/specs/2026-08-25-scholar-official-county-design.md` only if approved corrections are needed

- [ ] Add integration cases for fresh worlds, old saves, city capture/transfer, zone growth past 25, county magistrate death/renewal, noble-to-official and commoner-to-official transitions, map drilldown, and custom-template round trips.
- [ ] Run focused tests, all available rules/source guards, and the release build; record pre-existing build blockers separately from feature failures.
- [ ] Verify no full-world county scan occurs in the hot path and that county sidecar failure degrades to a city-only view with a vacancy rather than a crash.
- [ ] Request code review, then merge the isolated branch to `master` only after explicit deployment approval.

