# Custom Court Editor Docked Toolbar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the custom court canvas fill the window, dock a full-height scrollable toolbar to the left edge, and keep the Eastern Zhou whole-court preset available when policy-profile resolution is temporarily unavailable.

**Architecture:** Put all geometry and profile-fallback decisions in pure rules inside `CustomCourtWholePresetRules.cs`, then make the Unity window consume those tested results. Use stretch anchors for the canvas and toolbar height so resizing is driven by parent geometry rather than fixed coordinates; retain the existing workspace, node cards, and pan handler.

**Tech Stack:** C# 9, Unity `RectTransform`/`ScrollRect`, WorldBox court profiles, .NET 9 rules executable, PowerShell source guards.

---

## File Map

- `Code/core/court/CustomCourtWholePresetRules.cs`: pure editor layout facts and preset-profile fallback rules.
- `Code/core/court/CourtProfileRegistry.cs`: maps a resolved `CourtProfileId` back to the existing Xia or Western profile singleton.
- `Code/ui/windows/CustomCourtWorkflowWindow.cs`: applies stretch anchors, full-height left toolbar, unobscured canvas center, and fallback preset profile.
- `Tests/AncientWarfare3.Rules.Tests/CustomCourtWholePresetRulesTests.cs.txt`: behavioral tests for geometry, profile fallback, and Eastern Zhou availability.
- `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`: wiring guard that rejects old fixed offsets and confirms tested helpers are used.

### Task 1: Add failing layout-rule tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWholePresetRulesTests.cs.txt`
- Modify: `Code/core/court/CustomCourtWholePresetRules.cs`

- [ ] **Step 1: Define the expected pure layout API in tests**

Add calls from `Run()` and tests equivalent to:

```csharp
private static void CanvasAlwaysMatchesViewport()
{
    CustomCourtWorkflowLayout layout = CustomCourtWorkflowLayoutRules.Resolve(
        518f, 302f, 164f, 0.8f, 6f);
    Equal(518f, layout.CanvasWidth, "canvas width follows viewport");
    Equal(302f, layout.CanvasHeight, "canvas height follows viewport");
    Equal(137.2f, layout.ToolbarViewportWidth,
        "toolbar reserves scaled panel and scrollbar width");
    Equal(302f, layout.ToolbarViewportHeight,
        "toolbar height follows viewport");
}

private static void ToolbarDoesNotDriftWithWindowWidth()
{
    CustomCourtWorkflowLayout narrow = CustomCourtWorkflowLayoutRules.Resolve(
        378f, 222f, 164f, 0.8f, 6f);
    CustomCourtWorkflowLayout wide = CustomCourtWorkflowLayoutRules.Resolve(
        858f, 592f, 164f, 0.8f, 6f);
    Equal(0f, narrow.ToolbarLeft, "narrow toolbar left edge");
    Equal(narrow.ToolbarLeft, wide.ToolbarLeft,
        "toolbar left edge is independent of window width");
    Equal(narrow.ToolbarViewportWidth * 0.5f,
        narrow.VisibleCanvasCenterOffsetX,
        "new nodes center in canvas area not covered by toolbar");
}
```

- [ ] **Step 2: Run the focused suite and verify RED**

Run:

```powershell
dotnet build Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -t:Compile --no-incremental
dotnet Tests\AncientWarfare3.Rules.Tests\bin\Debug\net9.0\AncientWarfare3.Rules.Tests.dll --custom-court-multiplayer
```

Expected: compilation fails because `CustomCourtWorkflowLayout` and
`CustomCourtWorkflowLayoutRules.Resolve` do not exist. The repository may
subsequently report the known missing
`Tests/CultiwayPerfSchedulerCompletionSourceGuard.ps1`; the compile error must
be observed before that unrelated guard failure.

- [ ] **Step 3: Implement the minimal pure layout result**

Replace the old height-only helper with a value type and resolver:

```csharp
public readonly struct CustomCourtWorkflowLayout
{
    public CustomCourtWorkflowLayout(float canvasWidth, float canvasHeight,
        float toolbarViewportWidth, float toolbarViewportHeight)
    {
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
        ToolbarViewportWidth = toolbarViewportWidth;
        ToolbarViewportHeight = toolbarViewportHeight;
    }

    public float CanvasWidth { get; }
    public float CanvasHeight { get; }
    public float ToolbarLeft => 0f;
    public float ToolbarViewportWidth { get; }
    public float ToolbarViewportHeight { get; }
    public float VisibleCanvasCenterOffsetX => ToolbarViewportWidth * 0.5f;
}

public static class CustomCourtWorkflowLayoutRules
{
    public static CustomCourtWorkflowLayout Resolve(float contentWidth,
        float viewportHeight, float toolbarWidth, float toolbarScale,
        float scrollbarWidth)
    {
        float canvasWidth = Math.Max(1f, contentWidth);
        float canvasHeight = Math.Max(1f, viewportHeight);
        float toolbarViewportWidth = Math.Max(1f,
            toolbarWidth * toolbarScale + scrollbarWidth);
        return new CustomCourtWorkflowLayout(canvasWidth, canvasHeight,
            toolbarViewportWidth, canvasHeight);
    }
}
```

- [ ] **Step 4: Re-run the focused suite and verify GREEN**

Run the same compile and direct-DLL commands. Expected: layout tests pass; only
the separately identified missing PowerShell guard may fail after compilation.

- [ ] **Step 5: Commit the layout rules**

```powershell
git add Code/core/court/CustomCourtWholePresetRules.cs Tests/AncientWarfare3.Rules.Tests/CustomCourtWholePresetRulesTests.cs.txt
git commit -m "fix: define stable custom court editor layout"
```

### Task 2: Add failing preset-profile fallback tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWholePresetRulesTests.cs.txt`
- Modify: `Code/core/court/CustomCourtWholePresetRules.cs`
- Modify: `Code/core/court/CourtProfileRegistry.cs`

- [ ] **Step 1: Express the fallback rules in tests**

Add these cases and call them from `Run()`:

```csharp
private static void KnownInstitutionRecoversMissingProfile()
{
    Equal(CourtProfileId.Xia,
        CustomCourtWholePresetRules.ResolveProfile(CourtProfileId.None,
            CourtInstitutionId.Zhou),
        "Eastern Zhou recovers the Xia profile");
    Equal(CourtProfileId.Western,
        CustomCourtWholePresetRules.ResolveProfile(CourtProfileId.None,
            CourtInstitutionId.WesternBureaucratic),
        "Western institution recovers the Western profile");
    Equal(CourtProfileId.None,
        CustomCourtWholePresetRules.ResolveProfile(CourtProfileId.None,
            "unknown"),
        "unknown institution does not guess a profile");
}

private static void ExplicitProfileWinsOverInstitutionFallback()
{
    Equal(CourtProfileId.Xia,
        CustomCourtWholePresetRules.ResolveProfile(CourtProfileId.Xia,
            CourtInstitutionId.WesternBureaucratic),
        "runtime profile remains authoritative");
}
```

Also explicitly assert that `Options(CourtProfileId.Xia,
CourtInstitutionId.Zhou)` returns Eastern Zhou as its sole unlocked option.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: compilation fails because `ResolveProfile` does not exist.

- [ ] **Step 3: Implement the minimal fallback rule**

Add:

```csharp
public static CourtProfileId ResolveProfile(CourtProfileId runtimeProfileId,
    string institutionId)
{
    if (runtimeProfileId != CourtProfileId.None) return runtimeProfileId;
    if (!CourtInstitutionRules.IsKnown(institutionId))
        return CourtProfileId.None;
    return institutionId.StartsWith("western_",
        StringComparison.Ordinal)
        ? CourtProfileId.Western
        : CourtProfileId.Xia;
}
```

Add `CourtProfileRegistry.For(CourtProfileId profileId)` that returns the same
existing Xia/Western singleton instances and returns `null` for `None`.

- [ ] **Step 4: Re-run focused tests and verify GREEN**

Expected: all whole-preset rule tests pass, including Eastern Zhou rank-zero
availability and unknown-institution rejection.

- [ ] **Step 5: Commit preset recovery rules**

```powershell
git add Code/core/court/CustomCourtWholePresetRules.cs Code/core/court/CourtProfileRegistry.cs Tests/AncientWarfare3.Rules.Tests/CustomCourtWholePresetRulesTests.cs.txt
git commit -m "fix: recover built-in court preset profiles"
```

### Task 3: Guard the required Unity wiring

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`

- [ ] **Step 1: Replace obsolete source assertions**

Delete assertions that require `ToolbarTopOffset`, `ToolbarBottomInset`,
`new Vector2(-864f, 46f)`, `new Vector2(-480f, 50f)`, right-side toolbar
anchors, and `VisibleCanvasHeight`.

Add assertions requiring:

```csharp
Contains(window, "CustomCourtWorkflowLayoutRules.Resolve(");
Contains(window, "_toolViewport.anchorMin = new Vector2(0f, 0f)");
Contains(window, "_toolViewport.anchorMax = new Vector2(0f, 1f)");
Contains(window, "_toolViewport.anchoredPosition = Vector2.zero");
Contains(window, "_canvasRect.anchorMin = Vector2.zero");
Contains(window, "_canvasRect.anchorMax = Vector2.one");
Contains(window, "_canvasRect.offsetMin = Vector2.zero");
Contains(window, "_canvasRect.offsetMax = Vector2.zero");
Contains(window, "ResolveWholePresetProfile(");
Contains(window, "CustomCourtWholePresetRules.ResolveProfile(");
DoesNotContain(window, "new Vector2(-864f, 46f)");
DoesNotContain(window, "new Vector2(-480f, 50f)");
```

- [ ] **Step 2: Run focused tests and verify RED**

Run the direct DLL suite after compiling. Expected: the source guard fails on
the first missing stretch-anchor or profile-resolution string.

- [ ] **Step 3: Commit the failing source guard**

```powershell
git add Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt
git commit -m "test: require docked custom court editor wiring"
```

### Task 4: Rewire the editor layout

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`

- [ ] **Step 1: Remove obsolete offset constants**

Delete `ToolbarTopOffset` and `ToolbarBottomInset`. Keep `ToolbarScale`,
`ToolbarWidth`, `ToolbarContentHeight`, and `ToolbarScrollbarWidth`.

- [ ] **Step 2: Stretch the canvas to the full content root**

In `ApplyLayout`, resolve the tested geometry and set:

```csharp
CustomCourtWorkflowLayout layout = CustomCourtWorkflowLayoutRules.Resolve(
    contentWidth, viewportHeight, ToolbarWidth, ToolbarScale,
    ToolbarScrollbarWidth);
_canvasRect.anchorMin = Vector2.zero;
_canvasRect.anchorMax = Vector2.one;
_canvasRect.pivot = new Vector2(0.5f, 0.5f);
_canvasRect.anchoredPosition = Vector2.zero;
_canvasRect.sizeDelta = Vector2.zero;
_canvasRect.offsetMin = Vector2.zero;
_canvasRect.offsetMax = Vector2.zero;
```

Do not change `_workspaceRect.sizeDelta`, `TreeDragPanHandler`, card sizes, or
window min/default/max values.

- [ ] **Step 3: Dock the toolbar to the full-height left edge**

Set the viewport anchors to `(0, 0)` and `(0, 1)`, pivot to `(0, 0.5)`,
position to zero, width to `layout.ToolbarViewportWidth`, and vertical
`sizeDelta` to zero. Top-align `_toolPanel` at `(0, 1)` with zero anchored
position, preserve `ToolbarContentHeight`, and keep `localScale = 0.8`.
Leave the permanent scrollbar anchored to the viewport's right edge.

- [ ] **Step 4: Center new content in the unobscured canvas area**

In `CanvasCenterLayout`, transform `canvas.rect.center + new Vector2(
layout.VisibleCanvasCenterOffsetX, 0f)` into workspace coordinates. Store the
current layout result as a field or recompute it from the current content
geometry; do not rewrite existing office coordinates.

- [ ] **Step 5: Run source guard and focused rules**

Expected: the new source guard and all custom court whole-preset tests pass.

- [ ] **Step 6: Commit Unity layout wiring**

```powershell
git add Code/ui/windows/CustomCourtWorkflowWindow.cs
git commit -m "fix: dock custom court toolbar to left edge"
```

### Task 5: Wire profile recovery into both preset actions

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`

- [ ] **Step 1: Add one profile resolution helper**

Add a private helper that reads the institution first, gets the normal
runtime profile, resolves a fallback `CourtProfileId`, and maps it through
`CourtProfileRegistry.For(CourtProfileId)`. It must not write policy state:

```csharp
private static ICourtProfile ResolveWholePresetProfile(Kingdom kingdom,
    string institutionId)
{
    ICourtProfile runtime = CourtProfileRegistry.For(kingdom);
    CourtProfileId resolved = CustomCourtWholePresetRules.ResolveProfile(
        runtime?.Id ?? CourtProfileId.None, institutionId);
    return runtime ?? CourtProfileRegistry.For(resolved);
}
```

- [ ] **Step 2: Use the helper in refresh and click paths**

Update both `RefreshWholePresetOptions` and `CycleWholePreset` to calculate
`currentInstitution` once, resolve the same profile, and pass the same
institution into `Options`. This prevents the button state and click behavior
from disagreeing.

- [ ] **Step 3: Run focused rules and production build**

Run:

```powershell
dotnet build Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -t:Compile --no-incremental
dotnet Tests\AncientWarfare3.Rules.Tests\bin\Debug\net9.0\AncientWarfare3.Rules.Tests.dll --custom-court-multiplayer
dotnet build AncientWarfare3.csproj
git diff --check
```

Expected: focused tests pass; production build reports zero errors. Record the
known missing Cultiway guard separately if it still affects only the rules
project's `BeforeTargets=Build` phase.

- [ ] **Step 4: Commit preset UI recovery**

```powershell
git add Code/ui/windows/CustomCourtWorkflowWindow.cs
git commit -m "fix: keep Eastern Zhou preset selectable"
```

### Task 6: Integrate, deploy, and verify

**Files:**
- No additional production files expected.

- [ ] **Step 1: Review the branch diff**

Confirm only the five files in the file map and this plan changed. Verify no
bandit, corruption, RTS, or unrelated localization changes are present.

- [ ] **Step 2: Merge into the current master without overwriting user work**

Commit the feature branch, then integrate its commits into `master`. If the
main worktree's formatting-only change in `CustomCourtWorkflowWindow.cs`
overlaps, preserve the functional feature version and the user's formatting
intent without staging unrelated dirty files.

- [ ] **Step 3: Build and deploy**

Run `dotnet build AncientWarfare3.csproj`, then deploy using the repository's
existing `deploy-local.ps1` workflow to:

```text
D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0
```

- [ ] **Step 4: Runtime verification**

Open the editor at default size, minimum size, and an enlarged size. Confirm:

- canvas fills the full content area;
- toolbar remains at the far left and matches the content height;
- scrolling reaches Apply and status controls;
- adding an office places it to the right of the toolbar;
- Eastern Zhou cycles and loads for a Xia kingdom;
- canvas dragging and node dragging still work.

Inspect `C:\Users\24908\AppData\LocalLow\mkarpenko\WorldBox\Player.log` for
C# compile failures, Harmony failures, `NullReferenceException`,
`KeyNotFoundException`, missing custom-court localization, or new AW3 errors.

- [ ] **Step 5: Push only after verification**

Push `master` after the focused tests, production build, deployment, runtime
log check, and visual verification are complete.
