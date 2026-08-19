# Regional Government Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the regional-government feature with separated place and administrative-level names, reliable court/map projections, formal-official ennoblement, complete guards, merge, deployment, and runtime verification.

**Architecture:** Extend the existing central `CustomCourtRegionalGovernmentLayer` rather than adding another configuration object. Keep regions runtime-only and derived from city adjacency; presentation composes place names and localized level metadata without mutating `CityName`. Route formal ennoblement through the existing committed appointment boundary and `LineageService`.

**Tech Stack:** C# 11/.NET Framework 4.8.1, Unity UI, Newtonsoft.Json, Harmony, existing rule-test console project, PowerShell source guards, git worktrees.

---

## File Map

- Modify `Code/core/court/CustomCourtTemplateModels.cs`: persist the localized lowest city-level title.
- Modify `Code/core/court/CustomCourtTemplateJsonCodec.cs`: normalize legacy JSON to `州/Prefecture`.
- Modify `Code/core/court/CustomCourtRuntime.cs`: resolve all three localized administrative titles.
- Modify `Code/core/court/RegionalGovernmentRules.cs`: derive a region place name without appending its level.
- Modify `Code/core/court/RegionalGovernmentReadModel.cs`: expose member count and separated labels.
- Modify `Code/core/court/RegionalGovernmentAggregationService.cs`: populate separated labels and maintain bounded invalidation.
- Modify `Code/core/court/LocalCourtReadModel.cs`, `CourtReadModelService.cs`, and `CourtPyramidRules.cs`: project separated regional/local labels and upper links.
- Modify `Code/ui/windows/CustomCourtWorkflowWindow.cs`: add separate Chinese/English inputs for region, governor, and city levels.
- Modify `Code/ui/windows/CourtWindow.cs`, `Code/ui/components/CourtCityGovernmentCard.cs`, and `Code/ui/items/CourtActorNodeView.cs`: show region level, city level, seat, and member count.
- Modify `Code/core/policy/HierarchicalVassalMapModeService.cs` and label runtime files: complete region-to-city navigation and separated map labels.
- Modify `Code/patch/AW_PromotionPatch.cs`: invalidate regional projections on leader changes.
- Modify `Code/core/court/CourtService.cs`: ennoble only committed formal appointments.
- Modify `Locales/aw3_court.csv`: add all labels and tooltips.
- Extend focused rule tests and create the two missing source guards.

### Task 1: Separate Place Names From Administrative Levels

**Files:**
- Modify: `Code/core/court/CustomCourtTemplateModels.cs`
- Modify: `Code/core/court/CustomCourtTemplateJsonCodec.cs`
- Modify: `Code/core/court/CustomCourtRuntime.cs`
- Modify: `Code/core/court/RegionalGovernmentRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentTemplateRulesTests.cs.txt`

- [ ] **Step 1: Write failing naming and migration tests.** Assert `RegionName("临淄州") == "临淄"`, the city display name remains exactly `即墨`, legacy central JSON receives `州/Prefecture`, and Chinese/English regional, governor, and city-level titles round-trip independently.
- [ ] **Step 2: Run the two focused slices and verify expected assertion failures.**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --regional-government
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --regional-government-template
```

- [ ] **Step 3: Add `LocalLevelTitle` to the existing regional layer.** Use `CustomCourtLocalizedText` with Chinese `州` and English `Prefecture`; normalize missing values without overwriting populated language values.
- [ ] **Step 4: Change region-name derivation.** Strip one recognized legacy suffix and return only the stem. Add a pure presentation helper that returns `place + " · " + level` without modifying either source value.
- [ ] **Step 5: Re-run focused slices and commit.**

```powershell
git add -- Code/core/court/CustomCourtTemplateModels.cs Code/core/court/CustomCourtTemplateJsonCodec.cs Code/core/court/CustomCourtRuntime.cs Code/core/court/RegionalGovernmentRules.cs Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentTemplateRulesTests.cs.txt
git commit -m "feat: separate administrative levels from place names"
```

### Task 2: Complete Editor And Court Presentation

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Code/ui/components/CourtWorkflowVacancyCard.cs`
- Modify: `Code/core/court/RegionalGovernmentReadModel.cs`
- Modify: `Code/core/court/LocalCourtReadModel.cs`
- Modify: `Code/core/court/CourtReadModelService.cs`
- Modify: `Code/ui/windows/CourtWindow.cs`
- Modify: `Code/ui/components/CourtCityGovernmentCard.cs`
- Modify: `Code/ui/items/CourtActorNodeView.cs`
- Modify: `Locales/aw3_court.csv`
- Test: `Tests/CustomCourtRegionalEditorSourceGuard.ps1`
- Test: `Tests/RegionalGovernmentCourtSourceGuard.ps1`

- [ ] **Step 1: Strengthen source guards so they fail.** Require six language-specific inputs (`RegionZh/En`, `GovernorZh/En`, `LocalLevelZh/En`), forbid assigning one input value to both localized fields, require member-count presentation, and require the read-only regional card in local editing.
- [ ] **Step 2: Run both guards and verify failures identify the missing inputs and member count.**
- [ ] **Step 3: Add six compact localized inputs.** Bind each input to exactly one `CustomCourtLocalizedText` property. Keep the existing central/local window dimensions and mode offsets.
- [ ] **Step 4: Complete court labels.** Regional nodes show `region name · region level`, governor title, seat city, and localized member count. City cards keep the exact `CityName` and show the configured local level separately. Local views keep the non-deletable upper governor projection and automatic links.
- [ ] **Step 5: Add localization keys and rerun both guards plus `--regional-government-court`.**
- [ ] **Step 6: Commit.**

```powershell
git add -- Code/ui/windows/CustomCourtWorkflowWindow.cs Code/ui/components/CourtWorkflowVacancyCard.cs Code/core/court/RegionalGovernmentReadModel.cs Code/core/court/LocalCourtReadModel.cs Code/core/court/CourtReadModelService.cs Code/ui/windows/CourtWindow.cs Code/ui/components/CourtCityGovernmentCard.cs Code/ui/items/CourtActorNodeView.cs Locales/aw3_court.csv Tests/CustomCourtRegionalEditorSourceGuard.ps1 Tests/RegionalGovernmentCourtSourceGuard.ps1
git commit -m "feat: complete regional court presentation"
```

### Task 3: Repair Cache Invalidation And Map Navigation

**Files:**
- Modify: `Code/core/court/RegionalGovernmentAggregationService.cs`
- Modify: `Code/patch/AW_PromotionPatch.cs`
- Modify: `Code/core/policy/CityAdministrationMapModeRules.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapLabelRuntime.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CityAdministrationMapModeRulesTests.cs.txt`
- Test: `Tests/CityAdministrationMapModeSourceGuard.ps1`

- [ ] **Step 1: Write failing pure rules tests.** Add a decision rule where a click inside the focused region inspects the city, a mapped city outside it pops to region level, and unmapped terrain/back also pop one level.
- [ ] **Step 2: Strengthen the source guard.** Require `City.setLeader` and `City.removeLeader` hooks to invalidate the old/new kingdom regional cache and require outside-region clicks to pop and refresh.
- [ ] **Step 3: Run the focused slice and guard; verify failures.**
- [ ] **Step 4: Implement the decision rule and wire it into city-mode clicks.** Kingdom hierarchy state must remain untouched. Region labels use separated level metadata; city labels use exact `CityName`.
- [ ] **Step 5: Invalidate on leader assignment/removal and city removal.** Keep invalidation kingdom-scoped and avoid any actor/world scan.
- [ ] **Step 6: Re-run tests/guard and commit.**

```powershell
git add -- Code/core/court/RegionalGovernmentAggregationService.cs Code/patch/AW_PromotionPatch.cs Code/core/policy/CityAdministrationMapModeRules.cs Code/core/policy/HierarchicalVassalMapModeService.cs Code/core/policy/HierarchicalVassalMapLabelRuntime.cs Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs Tests/AncientWarfare3.Rules.Tests/CityAdministrationMapModeRulesTests.cs.txt Tests/CityAdministrationMapModeSourceGuard.ps1
git commit -m "fix: refresh regional projections and map navigation"
```

### Task 4: Make Formal Officials Permanent Nobles

**Files:**
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/CourtOfficerRecordRules.cs`
- Modify: `Code/core/lineage/LineageService.cs` only if the existing admission boundary does not cover a supported naming profile.
- Test: `Tests/AncientWarfare3.Rules.Tests/OfficialCareerNobleIdentityRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Create: `Tests/FormalOfficialNobleIdentitySourceGuard.ps1`

- [ ] **Step 1: Add a failing pure decision test.** Define `CourtOfficerRecordRules.ShouldGrantNobleIdentity(committed, acting)` and assert only `(true, false)` grants; failed and acting appointments do not.
- [ ] **Step 2: Add a failing source guard.** Require the grant after the committed-result check and forbid it inside acting paths or dismissal code.
- [ ] **Step 3: Run the new focused slice and guard; verify failures.**
- [ ] **Step 4: Implement the decision and committed hook.** Call the existing idempotent `LineageService.EnsureOfficialShiAndClan` only for formal appointments. Remove the unconditional acting grant. Do not grant a noble rank, fief, or virtual title, and do not revoke nobility on dismissal.
- [ ] **Step 5: Add bounded restore coverage.** Active formal appointments receive repair through existing career projection restoration/annual reconciliation; acting rows remain excluded and no full-world scan is added.
- [ ] **Step 6: Re-run tests/guard and commit.**

```powershell
git add -- Code/core/court/CourtService.cs Code/core/court/CourtOfficerRecordRules.cs Code/core/lineage/LineageService.cs Tests/AncientWarfare3.Rules.Tests/OfficialCareerNobleIdentityRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt Tests/FormalOfficialNobleIdentitySourceGuard.ps1
git commit -m "feat: grant noble identity on formal appointment"
```

### Task 5: Add Missing Guards And Complete Verification

**Files:**
- Create: `Tests/RegionalGovernmentAggregationSourceGuard.ps1`
- Create: `Tests/RegionalGovernmentTemplateSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentTemplateRulesTests.cs.txt`

- [ ] **Step 1: Create the aggregation guard.** Require city adjacency, shared development score, seat leader projection, kingdom-scoped invalidation, and no SQLite/vassal/military-governor persistence.
- [ ] **Step 2: Create the template guard.** Require one normalized central regional layer, independent bilingual titles including local level, central management IDs, legacy upgrade, and local-document exclusion.
- [ ] **Step 3: Expand pure coverage.** Add cap-at-four, cross-kingdom, isolated city, one/two-city, stable ordering, bilingual round trip, and idempotent normalization tests.
- [ ] **Step 4: Run all focused slices and every regional guard.** Each must exit 0.
- [ ] **Step 5: Run civil-service, office-history, localization coverage, `git diff --check`, and the production Release build.** Build must report zero warnings and zero errors.
- [ ] **Step 6: Commit verification assets.**

```powershell
git add -- Tests/RegionalGovernmentAggregationSourceGuard.ps1 Tests/RegionalGovernmentTemplateSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentTemplateRulesTests.cs.txt
git commit -m "test: complete regional government verification"
```

### Task 6: Merge, Deploy, And Smoke-Test

**Files:**
- Merge the verified feature commits into `master`.
- Deploy only tracked mod source, localization, resources, and Release DLL to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`.

- [ ] **Step 1: Confirm the feature worktree is clean except the pre-existing `Code/core/schools/HistoricalSchoolDescentService.cs` change.** Do not stage, restore, or merge that file.
- [ ] **Step 2: Merge the feature branch into `master` without overwriting unrelated dirty files.** Abort and report if a real conflict touches user changes.
- [ ] **Step 3: Re-run focused tests and Release build from `master`.**
- [ ] **Step 4: Create `.aw3-deploy-backups/<timestamp>-regional-government-completion` in the deployed mod, then deploy with the repository's established script or verified file-copy manifest.**
- [ ] **Step 5: Compare SHA-256 hashes for the DLL, changed C# source, and localization files between `master` and the deployed mod.**
- [ ] **Step 6: Start WorldBox with the existing launcher, verify a visible game window, and inspect logs for missing localization, resource, null-reference, or regional-government errors.** Smoke-test central/local court hierarchy, editor bilingual values, member count, map region drill-down and cross-region return, leader replacement, and formal-versus-acting noble identity.
- [ ] **Step 7: Commit any verified smoke-test fixes, rebuild, redeploy, and repeat hashes/log inspection until clean. Push only when explicitly requested.**

## Plan Self-Review

- Every approved naming, court, map, cache, noble-identity, test, merge, and deployment requirement maps to a task.
- The plan preserves runtime-only regions and the existing lineage authority.
- No task changes window dimensions, persists regional offices, appends suffixes to `CityName`, ennobles acting officials, or stages `HistoricalSchoolDescentService.cs`.
- There are no deferred implementation placeholders; every verification command and commit boundary is explicit.
