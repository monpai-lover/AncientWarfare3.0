# 聯合科舉武舉與 male_4 高清人物貼圖實施計劃

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在既有科舉與官場架構上加入與科舉同時開科的武舉、75 歲官員/武將退休、動態將領編制及地方武職顯示，同時讓 Xia 的 `male_4` 身體與配套 `head_male` 以高清管線正確載入。

**Architecture:** 科舉與武舉共用一個年度調度器和週期鍵，但使用獨立 session、candidate、qualification、任命和同步資料。武舉的需求計算從城市、軍隊、軍府和戰線建立職位槽位，再由殿試資格與地方資格分流到大將和小將領任命池。人物貼圖沿用 `XiaHighResolutionTextureRegistry`、`AW_HighResolutionSpritePatch` 和 `UnitAvatarLoader` 的既有像素倍率模型，以 body path profile 保存身體倍率及綁定頭部路徑。

**Tech Stack:** C#/.NET Framework 4.8, Harmony, Unity `SpriteTextureLoader`, SQLite archive tables, existing AW UI and source-guard test suite.

---

## 已完成的資產接入

**Files:**
- Create: `GameResources/actors/species/civs/Xia/male_4/` (eight supplied body frames, `head_male.png`, king-format anchors, and copied `sprites.json`)
- Modify: `Code/core/presentation/XiaHighResolutionTextureRegistry.cs`
- Modify: `Code/patch/AW_ActorVisualRolePatch.cs`
- Modify: `Code/patch/AW_XiaKingScalePatch.cs`
- Modify: `Code/core/lineage/SoldierRetirementRules.cs`
- Test update: `Tests/AncientWarfare3.Rules.Tests/Regression20260721Tests.cs.txt`

- [x] Copy the supplied PNG frames into `male_4` and copy `sprites.json` from the existing high-resolution `king` directory.
- [x] Copy `*_head` and `*_item` anchor frames referenced by that JSON so `AnimationFrameData.show_head`, `pos_head`, and `pos_item` are populated.
- [x] Register `actors/species/civs/Xia/male_4` at factor 4 with bound head `actors/species/civs/Xia/male_4/head_male`.
- [x] Apply the bound head whenever the resolved body path is the registered skin, while leaving leader, warrior, king, bandit and female special-head rules unchanged.
- [x] Reuse the same profile for avatar-panel scaling when the actor is available by `actor_id`.
- [x] Change the existing soldier hard retirement threshold from 65 to 75 and update its focused rule assertions.
- [x] Run `dotnet build AncientWarfare3.csproj --no-restore`; expected result is zero warnings and zero errors.

## Task 1: 建立共同開科調度器

**Files:**
- Create: `Code/core/court/ExamCycleService.cs`
- Modify: `Code/core/policy/KingdomAnnualWorkService.cs:349-363`
- Modify: `Code/core/lineage/LineageKeys.cs` (shared cycle/session keys)
- Test: `Tests/AncientWarfare3.Rules.Tests/ExamCycleRulesTests.cs.txt`

- [ ] **Step 1: Write failing cycle tests** covering first anchor creation, three-year cycle matching, tribute/imperial mode, and idempotent `(kingdom_id, cycle_year)` keys.
- [ ] **Step 2: Run the focused rules test** with `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`; expected failure is missing `ExamCycleService` rules.
- [ ] **Step 3: Implement `ExamCycleService`** so `RunStateGovernmentExam` calls one method that reads/creates `CIVIL_SERVICE_EXAM_ANCHOR_YEAR`, resolves mode once, and invokes civil and military session creation with the same `OpenWorldDay` and stage due-day schedule.
- [ ] **Step 4: Remove independent year gating from `CivilServiceExamService.OnKingdomYear`** and expose a creation method that accepts the resolved cycle context without changing its candidate or ranking tables.
- [ ] **Step 5: Run the focused test again**; expected result is PASS, including repeated annual calls producing one civil and one military session.
- [ ] **Step 6: Add a source guard** asserting `RunStateGovernmentExam` invokes `ExamCycleService` and does not call two independent cycle checks.

## Task 2: 武舉規則、候選人與 SQLite 持久化

**Files:**
- Create: `Code/core/court/MilitaryExamRules.cs`
- Create: `Code/core/court/MilitaryExamCandidateQuery.cs`
- Create: `Code/core/court/MilitaryExamPersistence.cs`
- Create: `Code/core/db/MilitaryExamSessionTableItem.cs`
- Create: `Code/core/db/MilitaryExamCandidateTableItem.cs`
- Create: `Code/core/db/MilitaryOfficerTableItem.cs`
- Modify: `Code/core/lineage/LineageKeys.cs` (qualification and appointment keys)
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryExamRulesTests.cs.txt`

- [ ] **Step 1: Write failing rule tests** for eligibility (`isAlive`, adult, non-king/heir/prince/slave, not retired, `getAge() < 75`), score clamping, stage qualification, and the imperial/tribute final-stage difference.
- [ ] **Step 2: Implement score calculation** with weights `warfare 35`, `damage 20`, `strength 15`, `speed 10`, `diplomacy 10`, and existing military merit 10; clamp every component and total to 0–100 and use the same deterministic random adjustment policy as civil exams.
- [ ] **Step 3: Implement candidate query** by adapting civil local/foreign resident filters and excluding active central officials, military-governorate officers, army captains and existing generals; snapshot age and scores at insertion.
- [ ] **Step 4: Define tables and migration** with unique session `(KINGDOM_ID, CYCLE_YEAR, EXAM_KIND)` and candidate `(SESSION_ID, ACTOR_ID)` keys, stage scores/results, qualification, final rank, appointment state, and age snapshot.
- [ ] **Step 5: Run rules and SQLite round-trip tests**; expected result is PASS and a second insert is rejected without deleting the first result.

## Task 3: 武舉流程、殿試分流與任命

**Files:**
- Create: `Code/core/court/MilitaryExamService.cs`
- Create: `Code/core/court/MilitaryOfficerAppointmentService.cs`
- Create: `Code/core/court/MilitaryEstablishmentRules.cs`
- Create: `Code/core/court/MilitaryEstablishmentService.cs`
- Modify: `Code/core/lineage/GeneralService.cs`
- Modify: `Code/core/lineage/ActiveMilitaryLifecycleService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryEstablishmentRulesTests.cs.txt`

- [ ] **Step 1: Write failing establishment tests** for strategic army, military prefecture, capital defence, independent war-front, garrison-city and oversized-army deputy slots; invalid/disbanded armies contribute zero.
- [ ] **Step 2: Implement dynamic slots** and calculate vacancies as `target - qualified active - locked appointment`, clamped at zero; reserve target is `max(minimum reserve, ceil((general vacancy + local vacancy)/2))` only when a valid military organization exists.
- [ ] **Step 3: Implement annual order**: retire first, release slots, recalculate demand, create joint sessions, then process due stages.
- [ ] **Step 4: Implement stage transitions** reusing civil stage timing but storing independent records; local/prefectural/metropolitan passes produce military qualifications, palace passes produce `武進士` only in imperial mode.
- [ ] **Step 5: Implement appointment transaction**: fill general vacancies from palace passes first, then local vacancies from palace failures with complete local qualification; recheck age, life, kingdom, retirement and active office immediately before commit.
- [ ] **Step 6: Add idempotency and recovery** keyed by session/candidate/slot, and register runtime rebuild in `AW3RuntimeRestorePipeline`.
- [ ] **Step 7: Run establishment, appointment and restore tests**; expected result is PASS with no duplicate officer record after retry.

## Task 4: 75 歲官員退休與軍職釋放

**Files:**
- Create: `Code/core/court/OfficialRetirementService.cs`
- Modify: `Code/core/court/OfficialCareerStateService.cs` (annual retirement pass)
- Modify: `Code/core/lineage/GeneralService.cs` (retirement release)
- Modify: `Code/patch/AW_RetirementPatch.cs` (shared retirement gate)
- Test: `Tests/AncientWarfare3.Rules.Tests/OfficialRetirementRulesTests.cs.txt`

- [ ] **Step 1: Write failing tests** proving `actor.getAge() >= 75` retires both official and general, while age 74 remains eligible; retired actors retain history but cannot enter exam candidates or new appointments.
- [ ] **Step 2: Implement one retirement predicate** that reads the original actor age only; do not derive a second age from `created_time` or mutate `age_overgrowth`.
- [ ] **Step 3: Run retirement before annual demand and exam scheduling**, clear active office/general records atomically, and mark released slots for the same annual pass.
- [ ] **Step 4: Add source guards** ensuring candidate queries and appointment validation call the shared retirement predicate.

## Task 5: 聯合考試 UI、多人命令與本地化

**Files:**
- Create: `Code/ui/items/MilitaryExamCandidateRow.cs`
- Modify: `Code/ui/windows/CivilServiceExamWindow.cs` (exam-kind tabs and military summary)
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalogModels.cs`
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalog.cs`
- Modify: `Code/api/multiplayer/AW3MultiplayerUiFacade.cs`
- Modify: `Code/core/multiplayer/commands/AW3CourtCommandHandler.cs`
- Create/modify: `Locales/aw3_military_exam.csv`
- Test: `Tests/CivilServiceExamUiSourceGuard.ps1` and `Tests/MilitaryExamUiSourceGuard.ps1`

- [ ] **Step 1: Write source-guard tests** for `[科舉] [武舉] [歷屆考試]`, independent candidate rows, score columns, vacancy summary and ranking submit command.
- [ ] **Step 2: Add a military read model** with shared cycle year/stage fields plus general target, local target, general vacancy and local vacancy.
- [ ] **Step 3: Add the military tab** without changing civil candidate ordering, portrait queue or civil ranking submission; show age and years-to-75 and military qualification labels.
- [ ] **Step 4: Add multiplayer request/response and permission checks** mirroring civil ranking commands, with session kind included in every request.
- [ ] **Step 5: Add Traditional Chinese and fallback English localization** for stage names, qualifications, vacancy reasons and appointment results.
- [ ] **Step 6: Run UI source guards and build**; expected result is PASS and zero compile errors.

## Task 6: 地方官場武職投影

**Files:**
- Modify: `Code/core/court/LocalCourtReadModel.cs`
- Modify: `Code/core/court/CourtReadModelService.cs`
- Modify: `Code/ui/windows/CourtWindow.cs` or `Code/ui/windows/KingdomWindowAddition.cs` (local court card)
- Modify: `Code/ui/items/CourtActorNodeView.cs`
- Create: `Code/ui/items/LocalMilitaryRosterView.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/LocalCourtMilitaryRosterSourceGuard.ps1`

- [ ] **Step 1: Add independent read-model fields** `MilitaryGenerals`, `MilitaryGeneralTarget`, `MilitaryGeneralVacancies`, and `MilitaryCommandName`; keep `ActiveSeats` and `TotalSeats` unchanged.
- [ ] **Step 2: Project `GeneralService.GetActiveGeneralsForReadModel()`** by appointment city, garrison city or military-prefecture jurisdiction, filtering dead, retired, destroyed-city, foreign-kingdom and age-75 actors; preserve the appointment city while an army is travelling.
- [ ] **Step 3: Render a separate local military section** with general/local-general cards and vacancy cards only when the city has military demand; reuse actor portrait queue and actor IDs.
- [ ] **Step 4: Wire vacancy click to military candidate read model and existing permission/idempotent appointment command**; show an explicit reason when no candidate is eligible.
- [ ] **Step 5: Run source guards and UI smoke checks** at 100%, 125% and a small window size; expected result is no overlap, scrolling remains usable and civil seat counts are unchanged.

## Task 7: 高清貼圖驗收

**Files:**
- Modify: `Code/core/presentation/XiaHighResolutionTextureRegistry.cs` only if additional skins are registered
- Test: `Tests/AncientWarfare3.Rules.Tests/XiaHighResolutionTextureRegistryTests.cs.txt`

- [ ] **Step 1: Add registry tests** for normalized `male_4` body path, factor 4, bound `head_male`, disabled profile behavior and king profile backward compatibility.
- [ ] **Step 2: Verify resource files**: every name in `male_4/sprites.json` exists, body sizes are 32×44 (walk) or 32×28 (swim), `head_male.png` is 62×48, and the copied JSON matches the king schema.
- [ ] **Step 3: Build and deploy the DLL**, launch the game with a Xia civilian using `male_4`, and inspect map, actor panel, family tree and local court portrait for one consistent body/head pair.
- [ ] **Step 4: Inspect `Player.log`** for missing sprite, negative rect, duplicate session, null-reference or UI layout errors; fix and rerun the affected guard before claiming completion.

## Self-review against the design

- Shared timing is isolated in Task 1; civil and military records remain independent in Tasks 2–3.
- Stage qualification and palace/local appointment split are explicit in Tasks 2–3.
- Dynamic general/local slots, vacancies and changing army/city/war conditions are covered in Task 3.
- The original age source and 75-year retirement order are covered in Task 4 and enforced again at appointment time.
- UI, multiplayer, persistence and local-court projection are covered in Tasks 5–6.
- The supplied asset’s required `sprites.json` and head binding are covered in the completed asset section and Task 7.
