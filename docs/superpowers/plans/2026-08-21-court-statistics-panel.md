# 官场统计面板 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为中央官场、州首府官署和普通地方官署提供统一的分层人口与经济统计面板。

**Architecture:** 核心层新增只读聚合服务和范围规则，UI 层新增统一统计窗口并由 `CourtWindow` 通过一个按钮打开。经济数据只复用 `CityEconomyService.GetSnapshot`，不在 UI 中直接访问数据库。

**Tech Stack:** C#, Unity UI, existing CourtWindow/AbstractWindow, existing rule-test console.

---

### Task 1: Define statistics scope and snapshot rules

**Files:**
- Create: `Code/core/court/CourtStatisticsRules.cs`
- Create: `Code/core/court/CourtStatisticsService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Add `CourtStatisticsScope` values `National`, `Region`, `City` and a pure resolver accepting `hasCentralCourt`, `isRegionSeat`, `hasRegion`, returning the safe fallback scope.
- [ ] Add immutable snapshot fields for population, city count, tax, policy, technology, manpower, food stability, unrest risk, and `HasEconomyRecord`/`FallbackReason`.
- [ ] Add service aggregation over an explicit city list, using `city.getPopulationPeople()` and `CityEconomyService.GetSnapshot(city)`.
- [ ] Add failing console assertions for national/region/city scope and missing-region fallback.
- [ ] Run the rules test and confirm it fails before implementation, then rerun after implementation.

### Task 2: Resolve city sets without changing existing court logic

**Files:**
- Modify: `Code/core/court/CourtStatisticsService.cs`
- Test: `Tests/CourtStatisticsSourceGuard.ps1`

- [ ] Resolve national cities from `pKingdom.getCities()` with live-owner filtering.
- [ ] Resolve region cities through `DeJureRegionStore.TryGetForCity` and `MemberCityIds`, requiring live cities owned by the same kingdom.
- [ ] Resolve city scope to the selected live city only.
- [ ] Return city scope when a region is missing, inactive, foreign, or empty.
- [ ] Add a source guard that rejects direct SQL/database access from the UI window and requires `CityEconomyService.GetSnapshot` in the service.

### Task 3: Build the reusable statistics window

**Files:**
- Create: `Code/ui/windows/CourtStatisticsWindow.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs` or the existing localization fallback file

- [ ] Add `OpenNational`, `OpenRegion`, and `OpenCity` entry points that all route to one internal context.
- [ ] Render title, scope subtitle, eight metrics, city count, and fallback/no-record messages with the existing `AW_UIStyle`/`AbstractWindow` conventions.
- [ ] Use a scroll container and fixed metric rows so long localized labels do not overlap.
- [ ] Keep the window read-only and add a close/back action returning to `CourtWindow`.

### Task 4: Add the single button to central and local court views

**Files:**
- Modify: `Code/ui/windows/CourtWindow.cs`

- [ ] Add one `统计` button to the existing summary button row.
- [ ] Route central context to national statistics.
- [ ] Route a regional seat to region statistics.
- [ ] Route any other city context to city statistics.
- [ ] Reuse the existing city/kingdom context and do not add a second local-court layout.

### Task 5: Verify build and source contracts

**Files:**
- Modify: `AncientWarfare3.csproj` only if new files are not included by the project convention

- [ ] Run `dotnet build AncientWarfare3.csproj -c Release --nologo`.
- [ ] Run the rules test executable and `Tests/CourtStatisticsSourceGuard.ps1`.
- [ ] Confirm no database writes, annual economy refreshes, or changes to existing court/DeJure behavior.
