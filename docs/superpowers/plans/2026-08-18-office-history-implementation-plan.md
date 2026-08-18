# Office History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let every central, censorial, military, and local office display permanent incumbent history with exact year ranges.

**Architecture:** Reuse the existing `CourtOfficer` table and `OfficialCareer` lifecycle. Add a bounded office-history query/read model and a small history view invoked from the shared office card; do not create a second career table or scan live actors.

**Tech Stack:** C# 11/net48, SQLite, Unity UI `AbstractWindow`, existing `CourtActorNodeView`, localization CSV, net9 rules tests and PowerShell source guards.

---

## File Map

- Create: `Code/core/court/OfficialCareerHistoryModels.cs` for immutable history rows and query scope.
- Create: `Code/core/court/OfficialCareerHistoryRules.cs` for year-range and stable ordering.
- Create: `Code/core/court/OfficialCareerHistoryQuery.cs` for indexed `CourtOfficer` reads.
- Create: `Code/ui/windows/CourtOfficeHistoryWindow.cs` for the reused court-style history list.
- Create: `Code/ui/items/CourtOfficeHistoryRow.cs` for pooled history rows.
- Modify: `Code/core/court/CourtPyramidRules.cs` to carry `OfficeLayer` and `CityId` through node clones.
- Modify: `Code/core/court/CourtReadModelService.cs` to populate the layer/city scope.
- Modify: `Code/ui/items/CourtActorNodeView.cs` to add the history action without changing the actor-window action.
- Modify: `Code/ui/windows/CourtWindow.cs` to pass the active kingdom context to history.
- Modify: `Locales/aw3_court.csv` and `Locales/aw3_window_titles.csv` for labels.
- Create: `Tests/OfficialCareerHistory.Isolated.Tests/OfficialCareerHistory.Isolated.Tests.csproj` and `Program.cs`.
- Create: `Tests/OfficialCareerHistory.Isolated.Tests/OfficialCareerHistoryRulesTests.cs`.
- Create: `Tests/OfficialCareerHistorySqlSourceGuard.ps1`.

### Task 1: Define History Scope, Row, and Year Formatting Rules

**Files:**
- Create: `Code/core/court/OfficialCareerHistoryModels.cs`
- Create: `Code/core/court/OfficialCareerHistoryRules.cs`
- Test: `Tests/OfficialCareerHistory.Isolated.Tests/OfficialCareerHistoryRulesTests.cs`

- [ ] **Step 1: Write failing pure tests.**

```csharp
var current = new OfficialCareerHistoryRow(7, 11, 91, 3,
    "city", "granary_officer", "张三", 120, -1, true, "");
Equal("120—至今", OfficialCareerHistoryRules.YearRange(current, "至今"),
    "active term range");
var ended = current.WithEnd(127, "term_expired");
Equal("120—127", OfficialCareerHistoryRules.YearRange(ended, "至今"),
    "closed term range");
True(OfficialCareerHistoryRules.IsNewer(ended, current),
    "ended row ordering uses appointment identity");
```

The isolated project links `OfficialCareerHistoryModels.cs` and
`OfficialCareerHistoryRules.cs` from `Code/core/court` and compiles
`OfficialCareerHistoryRulesTests.cs` directly.

- [ ] **Step 2: Run the isolated project and verify it fails.**

Run: `dotnet run --project Tests/OfficialCareerHistory.Isolated.Tests/OfficialCareerHistory.Isolated.Tests.csproj`

Expected: compile failure because the history model and rules are absent.

- [ ] **Step 3: Implement the pure types.** `OfficialCareerHistoryScope` stores kingdom ID, optional city ID, layer, and office ID. `OfficialCareerHistoryRow` stores officer ID, actor ID, snapshot name, start/end years, current flag, end reason, kingdom/city names, and rank metadata. `YearRange` emits `start—至今` only for active rows and `start—end` for closed rows, with `未知` for missing years.

- [ ] **Step 4: Run the isolated project.**

Expected: PASS.

- [ ] **Step 5: Commit the pure history types.**

```powershell
git add -- Code/core/court/OfficialCareerHistoryModels.cs Code/core/court/OfficialCareerHistoryRules.cs Tests/OfficialCareerHistory.Isolated.Tests
git commit -m "feat: define court office history read model"
```

### Task 2: Add Bounded SQLite History Queries

**Files:**
- Create: `Code/core/court/OfficialCareerHistoryQuery.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/OfficialCareerHistorySqlTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the SQLite test.** Build an in-memory `CourtOfficer` table with one active row, two closed rows, and an unrelated office. Assert office, layer, and optional city filters return only the requested rows and preserve the stored actor name after no live actor is available.

```csharp
IReadOnlyList<OfficialCareerHistoryRow> rows =
    OfficialCareerHistoryQuery.Read(db, new OfficialCareerHistoryScope(
        kingdomId: 7, cityId: 3, layer: "city", officeId: "granary_officer"),
        limit: 32);
Equal(3, rows.Count, "all terms for one city office");
Equal("张三", rows[0].ActorName, "stored name snapshot survives");
True(rows[0].IsCurrent, "current row is retained");
```

- [ ] **Step 2: Run the rules project and verify the new test fails.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --office-history`

Expected: compile failure until the test entry and query exist.

- [ ] **Step 3: Implement the query with explicit projection and a hard limit.** Use `CourtOfficerTableItem.GetTableName()`, `KINGDOM_ID`, `LAYER`, `OFFICE_ID`, and optional `CITY_ID` predicates. Order by `APPOINTED_YEAR DESC, APPOINTED_TIME DESC, OFFICER_ID DESC` and cap `pLimit` at 128. Select actor name and year fields directly from SQLite; never resolve actors to render history.

- [ ] **Step 4: Add the runner entry and execute it.** Wire `OfficialCareerHistorySqlTests.Run()` to `--office-history`; expected output is `Office history SQL tests passed.`

- [ ] **Step 5: Commit the query slice.**

```powershell
git add -- Code/core/court/OfficialCareerHistoryQuery.cs Tests/AncientWarfare3.Rules.Tests/OfficialCareerHistorySqlTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: query bounded court office histories"
```

### Task 3: Put History on Every Shared Office Card

**Files:**
- Modify: `Code/core/court/CourtPyramidRules.cs`
- Modify: `Code/core/court/CourtReadModelService.cs`
- Modify: `Code/ui/items/CourtActorNodeView.cs`
- Create: `Code/ui/windows/CourtOfficeHistoryWindow.cs`
- Create: `Code/ui/items/CourtOfficeHistoryRow.cs`
- Modify: `Code/ui/windows/CourtWindow.cs`
- Modify: `Locales/aw3_court.csv`
- Modify: `Locales/aw3_window_titles.csv`
- Test: `Tests/OfficialCareerHistorySqlSourceGuard.ps1`

- [ ] **Step 1: Add the view-model scope fields.** Add `OfficeLayer` to `CourtPyramidNodeModel`, copy it in `Clone`, and populate it from `CourtOfficerView.layer` in `CourtReadModelService`. Keep `CityId` on all local nodes.

- [ ] **Step 2: Add the card action without replacing existing actions.** In `CourtActorNodeView.Bind`, clear and bind a history button to:

```csharp
CourtOfficeHistoryWindow.Open(
    pKingdom.id, pNode.CityId, pNode.OfficeLayer, pNode.OfficeId);
```

The actor portrait still calls `ActionLibrary.openUnitWindow(actor)`, vacancy management still calls `CourtAppointmentWindow`, and the history action is disabled only when `OfficeId` or layer is empty.

- [ ] **Step 3: Implement the reused court-style history window.** Use `AbstractWindow<CourtOfficeHistoryWindow>`, `WideWindowChrome`, `AW_UIStyle`, the same font, spacing, button styling, and pooled row pattern as `CourtWindow`. Each row shows office title, actor name snapshot, city (when present), and `OfficialCareerHistoryRules.YearRange`. Do not add a new layout language or a second navigation framework.

- [ ] **Step 4: Add localization.** Add keys for history button, history title, current range, unknown year, end reasons, and empty history in all three locale columns. Add the window title to `aw3_window_titles.csv`.

- [ ] **Step 5: Add source guards.** The guard must require `OfficialCareerHistoryQuery.Read`, `CourtOfficeHistoryWindow.Open`, `pNode.OfficeLayer`, and `YearRange`; reject `World.world.units_only_alive`, `getSimpleList()`, and `foreach (Actor` in history code.

- [ ] **Step 6: Run UI/source verification and commit.**

Run: `powershell -ExecutionPolicy Bypass -File Tests/OfficialCareerHistorySqlSourceGuard.ps1`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --office-history`

Run: `dotnet build AncientWarfare3.csproj`

```powershell
git add -- Code/core/court/CourtPyramidRules.cs Code/core/court/CourtReadModelService.cs Code/ui/items/CourtActorNodeView.cs Code/ui/windows/CourtOfficeHistoryWindow.cs Code/ui/items/CourtOfficeHistoryRow.cs Code/ui/windows/CourtWindow.cs Locales/aw3_court.csv Locales/aw3_window_titles.csv Tests/OfficialCareerHistorySqlSourceGuard.ps1
git commit -m "feat: expose permanent history on court office cards"
```

## Plan Self-Check

- The existing `OfficialCareer` table is reused; no duplicate persistence is
  introduced.
- All four office layers use the same query and UI action.
- Historical names work after actor death because the query uses snapshots.
- Local context is carried explicitly by layer and city ID.
