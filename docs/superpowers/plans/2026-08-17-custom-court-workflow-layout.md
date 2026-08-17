# Custom Court Workflow Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the custom court workflow editor's court-sized layout and replace tiny text nodes with court-style vacancy cards.

**Architecture:** Keep `CustomCourtWorkflowWindow` responsible for viewport, toolbar, persistence, and canvas orchestration. Add a workflow-only vacancy card component that owns its visual hierarchy and drag/click/delete events, while continuing to bind only to `CustomCourtOffice`. Keep the large pannable workspace below a clipped central viewport so its size never participates in toolbar anchoring.

**Tech Stack:** Unity uGUI (`RectTransform`, `Image`, `Text`, `Button`, `ScrollRect`, `RectMask2D`), existing WorldBox court UI assets/helpers, C# source-guard tests, .NET 9 rules test runner, Release .NET Framework 4.8 build.

---

### Task 1: Add layout and vacancy-card regression guards

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing source assertions**

Extend `CustomCourtWorkflowSourceGuardTests.Run()` to read `CourtWorkflowVacancyCard.cs` and assert these exact contracts:

```csharp
Contains(window, "new Vector2(920f, 620f)");
Contains(window, "CourtWorkflowVacancyCard.Create");
Contains(window, "_toolPanel.anchorMin = _toolPanel.anchorMax = new Vector2(1f, 1f)");
Contains(window, "_canvasRect.offsetMax = new Vector2(-172f, -4f)");
Contains(card, "CourtWorkflowVacancyCard");
Contains(card, "empty_slot");
Contains(card, "LocalizedTextManager.current_font");
Contains(card, "deleteRequested");
```

Read the new card file through `Path.Combine(root, "Code", "ui", "components", "CourtWorkflowVacancyCard.cs")`. The current implementation must fail because it has no vacancy card and still uses compact window dimensions.

- [ ] **Step 2: Run the focused test and verify the expected failure**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --custom-court-multiplayer
```

Expected: `InvalidOperationException` naming a missing vacancy-card or wide-window source contract.

- [ ] **Step 3: Commit the red test**

```powershell
git add Tests\AncientWarfare3.Rules.Tests\CustomCourtWorkflowSourceGuardTests.cs.txt
git commit -m "test: guard custom court card layout contracts"
```

### Task 2: Implement the court-style vacancy card

**Files:**
- Create: `Code/ui/components/CourtWorkflowVacancyCard.cs`
- Modify: `Code/ui/components/CourtWorkflowCanvas.cs`
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`

- [ ] **Step 1: Define the card surface and events**

Create `CourtWorkflowVacancyCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler` with:

```csharp
public CustomCourtOffice Office { get; private set; }
public static CourtWorkflowVacancyCard Create(
    Transform parent, CustomCourtOffice office,
    Action<CourtWorkflowVacancyCard> clicked,
    Action<CourtWorkflowVacancyCard> deleteRequested,
    Action<CourtWorkflowVacancyCard> dragEnded)
```

The object hierarchy must contain a dark court-style background, an avatar `Image`, a title `Text`, a subtitle `Text`, and a top-right delete `Button`. Bind every dynamic `Text.font` to `LocalizedTextManager.current_font`; set the subtitle with `AW_L10n.Text("aw_court_no_officer", "Vacant")`. Use the existing `SpriteTextureLoader` to load `civ/icons/minimap_figure` and fall back to `ui/icons/iconClan`; never create an `Actor`.

- [ ] **Step 2: Implement drag and click behavior**

Track the pointer offset on `OnBeginDrag`, move only the card `RectTransform.anchoredPosition` in `OnDrag`, and invoke `dragEnded(this)` from `OnEndDrag`. The card's `Button.onClick` invokes `clicked(this)`. The delete button invokes `deleteRequested(this)` and uses the existing red close/delete visual style.

- [ ] **Step 3: Switch canvas ownership to vacancy cards**

Change `CourtWorkflowCanvas`'s internal list, `Cards` property, `AddCard`,
`RemoveCard`, and `Clear` signatures from `CourtWorkflowOfficeCard` to
`CourtWorkflowVacancyCard`. Do not change edge ownership or template models.

- [ ] **Step 4: Replace workflow rendering with vacancy cards**

In `CustomCourtWorkflowWindow.RenderCards()`, create the new component under `_workspaceRect`, register it in the canvas list, and update `FindCard`, `SelectCard`, `DeleteOffice`, and edge rendering to accept `CourtWorkflowVacancyCard`. Preserve the existing `CustomCourtOffice` and edge models, position defaults, and save/import code. Set a stable card size of `132f x 104f`, matching `CourtActorNodeView` and the existing court screenshot.

- [ ] **Step 5: Run the focused test and verify it passes**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --custom-court-multiplayer
```

Expected: `Custom court multiplayer rules passed.`

- [ ] **Step 6: Commit the card implementation**

```powershell
git add Code\ui\components\CourtWorkflowVacancyCard.cs Code\ui\components\CourtWorkflowCanvas.cs Code\ui\windows\CustomCourtWorkflowWindow.cs Tests\AncientWarfare3.Rules.Tests\CustomCourtWorkflowSourceGuardTests.cs.txt
git commit -m "feat: render custom court offices as vacancy cards"
```

### Task 3: Restore wide layout and isolate the pannable workspace

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Code/ui/items/TreeDragPanHandler.cs` only if viewport drag routing requires a guard
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`

- [ ] **Step 1: Restore court window dimensions**

Set `DefaultSize` to `new Vector2(920f, 620f)`, `MinimumSize` to `new Vector2(620f, 420f)`, and `MaximumSize` to `new Vector2(1400f, 900f)`, matching the existing court wide-window range.

- [ ] **Step 2: Anchor the toolbar to the viewport**

Keep `_root` stretched to the fixed content viewport. Anchor `_toolPanel` to `(1, 1)` with a fixed width of `164f`, and calculate its height from `viewportHeight`, never `_root.sizeDelta` or `_workspaceRect.sizeDelta`. Keep `_canvasRect.offsetMax = new Vector2(-172f, -4f)` so the central viewport reserves the toolbar column.

- [ ] **Step 3: Clip and pan only the workspace**

Keep `_workspaceRect` at `2000f x 1500f` beneath `_canvasRect`, add/retain `RectMask2D` on the viewport, and call `TreeDragPanHandler.Setup(_workspaceRect, _canvasRect)`. Do not attach `ContentSizeFitter` or a layout group to the parent content layer. Edges remain non-raycast targets so blank-space drag reaches the viewport handler.

- [ ] **Step 4: Run layout source guards**

Run the focused custom workflow command again and confirm the wide dimensions, fixed toolbar anchor, and viewport offsets are found.

- [ ] **Step 5: Commit the layout implementation**

```powershell
git add Code\ui\windows\CustomCourtWorkflowWindow.cs Code\ui\items\TreeDragPanHandler.cs Tests\AncientWarfare3.Rules.Tests\CustomCourtWorkflowSourceGuardTests.cs.txt
git commit -m "fix: constrain custom court workflow to a wide viewport"
```

### Task 4: Full verification and local deployment

**Files:**
- Verify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Verify: `Code/ui/components/CourtWorkflowVacancyCard.cs`
- Verify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`

- [ ] **Step 1: Run the complete rules suite**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-build --no-restore
```

Expected: `Rule tests passed.`

- [ ] **Step 2: Build the Release assembly**

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: 0 warnings and 0 errors.

- [ ] **Step 3: Deploy and verify source parity**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1 `
  -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0\.worktrees\custom-court-workflow' `
  -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

Verify the deployed `CustomCourtWorkflowWindow.cs` and
`CourtWorkflowVacancyCard.cs` SHA256 hashes match the worktree files. Confirm a
timestamped `.aw3-deploy-backups` directory was created before reporting the
deployment.

- [ ] **Step 4: Commit verification-only changes if any**

```powershell
git status --short
```

Only the intended implementation and test files may remain modified. Do not
commit generated `bin` or `obj` output.
