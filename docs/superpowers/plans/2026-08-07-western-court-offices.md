# Western Court Offices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the two-level Western court, complete Western office catalog, and fixed ten-year circulating Mayor lifecycle without coupling offices to government form or scanning realm actors during annual rotation.

**Architecture:** Keep `COURT_INSTITUTION` as the canonical technology-driven bureaucratic level and keep `POLICY_GOVERNMENT_STATE` as the independent appointment mode. Resolve city-leader office identity through one profile-aware rule, then reuse the existing authoritative career rows and transactional governor rotation pipeline with an explicit office ID and kingdom-level cycle end.

**Tech Stack:** C# 10 / .NET Framework 4.8, Harmony, NeoModLoader, SQLite, Unity UI, CSV localization, custom console rules tests.

---

### Task 1: Canonical Western Bureaucratic Levels

**Files:**
- Modify: `Code/core/court/CourtInstitutionRules.cs`
- Modify: `Code/core/court/WesternCourtProfile.cs`
- Modify: `Code/core/court/CourtInstitutionService.cs`
- Modify: `Code/core/court/ICourtProfile.cs`
- Modify: `Code/core/court/XiaCourtProfile.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/WesternCourtProfileRulesTests.cs.txt`

- [ ] Add failing assertions for `western_bureaucratic`, `western_feudal_bureaucratic`, cumulative office membership, and independence from elective/royal government state.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --western-policy-profile-slice` and confirm the new assertions fail on legacy IDs.
- [ ] Add canonical IDs and pure migration rules: base/elective -> bureaucratic; feudal -> feudal bureaucratic; royal direct -> advanced only when advanced technology is complete.
- [ ] Make `CourtInstitutionService.Refresh` select the level using only `WesternCourtUnlocked` and `FeudalRetainersUnlocked`; leave `POLICY_GOVERNMENT_STATE` untouched for election/manual appointment logic.
- [ ] Run the Western slice and commit `feat: decouple western court levels from government`.

### Task 2: Canonical Western Office Catalog and Migration

**Files:**
- Modify: `Code/core/court/CourtIds.cs`
- Modify: `Code/core/court/WesternCourtProfile.cs`
- Create: `Code/core/court/WesternCourtMigrationRules.cs`
- Modify: `Code/core/court/OfficialCareerPersistence.cs`
- Modify: `Code/core/court/CourtService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/WesternCourtProfileRulesTests.cs.txt`

- [ ] Add failing tests that the basic level exposes Executive, Senate Elder, High Priest, Field General, and Mayor; the advanced level adds High Justice, Treasurer, Palace Steward, Royal Constable, Marshal, Secretary, and Count.
- [ ] Add `west_royal_constable` and map only legacy `west_royal_chamberlain` to it.
- [ ] Add an idempotent SQLite migration that updates active/history career and court rows in one transaction while preserving actor, kingdom, city, appointment year, and active state.
- [ ] Verify the Western slice passes and commit `feat: canonicalize western court offices`.

### Task 3: Profile-Aware City Leader Projection

**Files:**
- Create: `Code/core/court/CourtCityOfficeRules.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/CityGovernorProjectionRepairService.cs`
- Modify: `Code/patch/AW_CityLeaderPatch.cs`
- Modify: `Code/patch/AW_PromotionPatch.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CourtCityOfficeRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] Write failing pure tests for Xia Governor, basic/advanced Western Mayor, primitive fallback, and advanced feudatory-seat Count.
- [ ] Add `ResolveCityLeaderOffice(profile, institution, isFeudatorySeat)` and `IsCityLeaderOffice`.
- [ ] Replace hard-coded Governor checks in assignment, acting assignment, restore, dismissal, candidate qualification, and deferred projection with the resolved office ID.
- [ ] Keep one active city leader; Count is selected only for a feudatory territorial seat, while normal Western cities remain Mayors and participate in circulation.
- [ ] Run the new slice plus civil-service slice and commit `feat: project western city leaders as mayors`.

### Task 4: Fixed Ten-Year Western Mayor Cycle

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Create: `Code/core/court/WesternMayorTermRules.cs`
- Modify: `Code/core/court/OfficialCirculationRules.cs`
- Modify: `Code/core/court/OfficialCareerStateService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/WesternMayorTermRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/OfficialCirculationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] Add failing tests for exactly ten years, shared-cycle inheritance, expired cycle advance, one-city annual deferral, deterministic ring, and native-city exclusion.
- [ ] Store `WESTERN_MAYOR_CYCLE_END_YEAR` on the kingdom and make formal Mayor appointment use the live shared cycle end instead of the general term law.
- [ ] Generalize rotation plan and persistence methods to accept an office ID; retain indexed career-state reads and live city enumeration only.
- [ ] Mark Mayor rotation due independently of the Xia nine-rank law; rotate all eligible due Mayors transactionally, then advance the common cycle by ten years.
- [ ] If fewer than two valid Mayors/cities exist, set retry to the following year without partially moving anyone. Replacement and newly founded city appointments inherit the current common end year.
- [ ] Run Mayor, circulation, civil-service, and Western slices; commit `feat: add ten year western mayor circulation`.

### Task 5: Central and Military Vacancy Lifecycle

**Files:**
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/WesternCourtElectionRules.cs`
- Modify: `Code/core/court/CourtOfficerMilitaryTransitionService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/WesternCourtElectionRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/WesternCourtOfficeLifecycleSourceGuard.ps1`

- [ ] Add failing tests that government mode changes appointment behavior but never changes office availability; kings cannot stand; election terms apply only to elective central offices.
- [ ] Include central and military catalog vacancies in the bounded vacancy queue; route military appointments through the existing active-military release hook.
- [ ] Preserve royal manual appointment checks and feudal candidate weights based on `POLICY_GOVERNMENT_STATE`.
- [ ] Add source guards forbidding actor-wide enumeration and hard-coded Western office lists outside `WesternCourtProfile`.
- [ ] Run Western slices and source guard; commit `feat: complete western office vacancy lifecycle`.

### Task 6: Restrained Cached Office Effects

**Files:**
- Create: `Code/core/court/WesternCourtOfficeEffectRules.cs`
- Modify: `Code/core/court/CourtDirectionService.cs`
- Modify: `Code/core/policy/KingdomPolicyEffectRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/WesternCourtOfficeEffectRulesTests.cs.txt`

- [ ] Add failing pure tests for bounded central administration, military organization, and local administration contributions; vacancies contribute zero.
- [ ] Aggregate from `GetActiveOfficers`/existing officer cache only, cap each category, and add the values to policy effects without replacing policy modifiers.
- [ ] Run effect and Western slices; commit `feat: apply cached western office effects`.

### Task 7: Wide Court UI and Localization

**Files:**
- Modify: `Code/ui/windows/CourtWindow.cs`
- Modify: `Code/ui/windows/KingdomWindowAddition.cs`
- Modify: `Locales/aw3_court.csv`
- Create: `Tests/AncientWarfare3.Rules.Tests/WesternCourtUiSourceGuard.ps1`

- [ ] Add a failing source guard requiring Western title/level labels, central/military/local section labels, duty text keys, and all three CSV language columns.
- [ ] Keep the existing draggable wide `CourtWindow`; set its title from the active profile and render military separately from central while preserving the local section.
- [ ] Replace the Western button fallback that currently says Eastern Zhou Six Ministers/Court of the Hundred Schools.
- [ ] Add simplified Chinese, English, and traditional Chinese entries for both levels, all offices including Royal Constable, duties, vacancy, rotation, and migration biography strings.
- [ ] Run the UI guard and Western slice; commit `feat: localize western court window`.

### Task 8: Verification and Source-Only Deployment

**Files:**
- Modify only if required by verification: `Tests/VerifySourceDeployment.ps1`

- [ ] Run all new Western/city/Mayor slices and existing civil-service/election slices.
- [ ] Run `dotnet build AncientWarfare3.csproj -nologo` and record warnings separately from errors.
- [ ] Run all Western source guards and `Tests/VerifySourceDeployment.ps1` against `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`.
- [ ] Deploy changed source/CSV files only, preserve `Assemblies`, and confirm no `AncientWarfare3.dll` was copied.
- [ ] Compare SHA256 for every deployed changed file and commit `chore: verify western court source deployment` only if verification metadata changes.
