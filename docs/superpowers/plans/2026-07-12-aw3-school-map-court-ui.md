# AW3 School Map Mode And Court UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add fixed-school city snapshots, list/detail UI, school map mode, and correct court card/link placement.

**Architecture:** Use a static school registry and event-driven membership/city indexes. Reuse AW3 map-mode infrastructure and religion-window interaction patterns, while keeping all school objects static. Make layout and link geometry pure top-left-coordinate rules.

**Tech Stack:** C# net48, Unity UI, Harmony, AW3 map-mode meta objects, WorldBox religion UI references, temporary net9 rule harness.

---

## File Structure

- Create `Code/core/court/CourtSchoolDefinition.cs`: fixed definition model.
- Create `Code/core/court/CourtSchoolRegistry.cs`: fourteen definitions.
- Create `Code/core/court/CitySchoolInfluenceRules.cs`: pure weights/ties.
- Create `Code/core/court/CitySchoolSnapshotService.cs`: dirty queue and indexes.
- Create `Code/core/policy/SchoolMapModeService.cs`: overview/focus colors and tooltip data.
- Modify `Code/core/policy/AWMapModeMeta*`: school meta type and coordinator registration.
- Create `Code/ui/windows/SchoolWindow.cs`: fixed list and details.
- Create `Code/ui/items/SchoolListItem.cs`: pooled list row.
- Create `Code/ui/items/SchoolInfluenceBar.cs`: city composition row.
- Modify `Code/core/court/CourtPyramidRules.cs`: top-left bounds and orthogonal segments.
- Modify `Code/ui/windows/CourtWindow.cs`: one coordinate origin and pooled links.
- Modify `Code/ui/items/CourtActorNodeView.cs`: no-school display and clickable school icon.
- Modify `Locales/trait.csv`, `Locales/aw3_court.csv`, and `Locales/others.csv`.
- Modify `F:/tmp/AW3CourtExpansionRuleTests/*`: rules and source regression tests.

### Task 1: Failing Registry, Snapshot, And Layout Tests

- [ ] Add tests for fourteen unique definitions, required icon/color/locale data, elite weights, multi-role de-duplication, deterministic ties, neutral empty city, and dirty coalescing.
- [ ] Add layout assertions for `OffsetX = padding - minX` and orthogonal segments:

```csharp
Check(Math.Abs(bounds.OffsetX - 70f) < 0.01f, "top-left offset must include padding");
var segments = CourtPyramidRules.BuildOrthogonalLinks(parent, children, 52f);
Check(segments.All(s => s.From.X == s.To.X || s.From.Y == s.To.Y),
    "every court link must be orthogonal");
```

- [ ] Add source assertions that canvas, cards, and links all use `(0,1)` anchors and no link uses `(0.5,1)`.
- [ ] Run the court harness and verify the new tests fail.

### Task 2: Fixed Registry And Membership Index

- [ ] Implement definitions for Ru, Mohist, Dao, Legalist, Military, Diplomat, Agrarian, YinYang, Logician, Medical, Syncretist, Merchant, Craftsman, and Historian.
- [ ] Include locale keys, existing icon paths, fixed colors, direction vectors, and compatible offices.
- [ ] Add an event-driven living membership index updated by school assignment/change/death/world unload.
- [ ] Run registry tests and commit with `git commit -m "feat: register fixed Hundred Schools"`.

### Task 3: City Snapshots

- [ ] Implement weights 8 king/capital, 5 heir, 5 leader, 4 central officer, 3 general, and 2-4 local officer.
- [ ] Apply at most 20 percent ability adjustment and highest-role-only de-duplication.
- [ ] Implement deterministic dominant-school tie order and neutral empty snapshots.
- [ ] Mark affected cities dirty on role/school/lifecycle events; coalesce and process a bounded number per frame.
- [ ] Aggregate kingdom totals from snapshots, never population scans.
- [ ] Run tests and commit with `git commit -m "feat: cache city school influence"`.

### Task 4: Fix Court Coordinates And Links

- [ ] Change canvas anchor/pivot to top-left and reset it to a small left inset below the summary.
- [ ] Change `CalculateCanvasBounds.OffsetX` to `padding - minX`; keep vertical top padding explicit.
- [ ] Add `CourtPyramidLinkSegment` and `BuildOrthogonalLinks` producing parent stem, horizontal bus, and child stems.
- [ ] Pool horizontal/vertical link Images, anchor each at top-left, and render without rotation.
- [ ] Recalculate links from the same final node positions used by cards after refresh/resize/zoom.
- [ ] Run layout/source tests and commit with `git commit -m "fix: align court cards and hierarchy links"`.

### Task 5: School Map Mode

- [ ] Register a school map meta type and toolbar button through `AWMapModeMetaLibrary`/coordinator patterns.
- [ ] Overview uses dominant-school color; focus uses selected-school share intensity and desaturates other cities.
- [ ] Tooltip resolves the current pointed city every time and shows dominant plus top three shares.
- [ ] Opening SchoolWindow stores previous map mode; closing restores it.
- [ ] Run map color/tooltip tests and commit with `git commit -m "feat: add Hundred Schools map mode"`.

### Task 6: School List And Detail UI

- [ ] Build a wide SchoolWindow based on religion list/detail structure without runtime Religion objects.
- [ ] Pool fourteen list rows; support fixed, influence, and city-count sorting.
- [ ] Add school doctrine/direction/representatives/cities/kingdoms detail and city composition/contributor detail.
- [ ] Wire actor, city, court, family-tree, and biography navigation.
- [ ] Hide icons for no-school and display localized `无学派`/`No school`/`無學派`.
- [ ] Build and commit with `git commit -m "feat: add Hundred Schools browser"`.

### Task 7: Localization And Resource Validation

- [ ] Add all fourteen trait name/info pairs in three languages.
- [ ] Add all SchoolWindow, map, sort, city detail, direction, no-school, and missing primitive-court keys.
- [ ] Add a completeness rule requiring ID, locale, icon, color, and direction for every definition.
- [ ] Run resource tests and both builds; commit with `git commit -m "feat: localize Hundred Schools UI"`.

### Task 8: Verification

- [ ] Run court and correctness harnesses plus both net48 builds.
- [ ] Open/reopen/switch kingdoms/resize/drag/zoom the court; verify cards stay left-aligned and every line stays attached with no right-side ghost lines.
- [ ] Verify overview and focused school map colors, current-city tooltip refresh, and neutral gray cities.
- [ ] Verify list sorting, school detail, city detail, representative actor navigation, and no-school presentation.
- [ ] Profile map/window open and confirm no world population scan or all-portrait rebuild.

