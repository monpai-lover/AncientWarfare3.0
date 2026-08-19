# Workflow Workspace and Dropdown Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the custom-court workflow group 500 pixels left and keep every shared string-dropdown popup attached to its opening button.

**Architecture:** Preserve the existing workflow hierarchy and apply one root offset in `ApplyLayout`. Repair popup placement once in `AWStringDropdown` by using overlay-parent local coordinates and refreshing after Unity layout in `LateUpdate`; every current and future consumer receives the fix automatically.

**Tech Stack:** C# 9, Unity `RectTransform`, WorldBox/NeoModLoader UI, .NET 9 source-guard tests.

---

### Task 1: Lock the expected source contracts

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`

- [ ] **Step 1: Add failing assertions**

Require the workflow source to contain:

```csharp
_root.anchoredPosition = new Vector2(-500f, 0f);
```

Require `AWStringDropdown` to contain:

```csharp
private void LateUpdate()
PositionPopup();
_popup.localPosition = local;
RectTransform overlayRect = _overlay?.transform as RectTransform;
```

Reject `_popup.anchoredPosition = local;` and `PositionPopup(canvas);`.

- [ ] **Step 2: Run the focused suite and verify RED**

```powershell
dotnet build Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -t:Compile --no-incremental
dotnet Tests\AncientWarfare3.Rules.Tests\bin\Debug\net9.0\AncientWarfare3.Rules.Tests.dll --custom-court-multiplayer
```

Expected: source guard fails because the root still uses zero and the shared
dropdown still assigns `anchoredPosition`.

- [ ] **Step 3: Commit the failing guard**

```powershell
git add Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt
git commit -m "test: require workflow dropdown position binding"
```

### Task 2: Move the workflow group left

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`

- [ ] **Step 1: Apply the exact root offset**

Replace the zero root position with:

```csharp
_root.anchoredPosition = new Vector2(-500f, 0f);
```

Do not change canvas anchors, toolbar anchors, workspace dimensions, window
dimensions, or office node coordinates.

- [ ] **Step 2: Run focused tests**

Expected: the root-offset assertion passes while the dropdown assertions still
fail.

- [ ] **Step 3: Commit the layout correction**

```powershell
git add Code/ui/windows/CustomCourtWorkflowWindow.cs
git commit -m "fix: move custom court workflow into view"
```

### Task 3: Bind all dropdown popups to their source buttons

**Files:**
- Modify: `Code/ui/components/AWStringDropdown.cs`

- [ ] **Step 1: Remove the mismatched Canvas parameter path**

Change `PositionPopup(Canvas canvas)` to `PositionPopup()`. Resolve the active
Canvas and overlay parent inside the method, and return early when the source,
Canvas, overlay, or popup is invalid.

- [ ] **Step 2: Use one coordinate system**

Keep the existing source world-corner, screen-edge selection, scale, popup
size, and screen-padding calculation. Convert the final screen point against
the overlay parent's `RectTransform`, then assign:

```csharp
_popup.localPosition = local;
```

Do not assign the center-relative result to a bottom-left anchored position.

- [ ] **Step 3: Follow the source after layout**

Add `LateUpdate`. When a popup is open and the source component is active in
the hierarchy, call `PositionPopup()`. If the source is inactive or detached,
close the dropdown. Keep Escape handling in `Update`.

- [ ] **Step 4: Verify focused and adjacent suites**

Run:

```powershell
dotnet build Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -t:Compile --no-incremental
dotnet Tests\AncientWarfare3.Rules.Tests\bin\Debug\net9.0\AncientWarfare3.Rules.Tests.dll --custom-court-multiplayer
dotnet Tests\AncientWarfare3.Rules.Tests\bin\Debug\net9.0\AncientWarfare3.Rules.Tests.dll --custom-local-government
dotnet build AncientWarfare3.csproj
git diff --check
```

Expected: all commands pass with zero compile errors; local presets still
contain four offices each.

- [ ] **Step 5: Commit the shared dropdown fix**

```powershell
git add Code/ui/components/AWStringDropdown.cs
git commit -m "fix: bind dropdown popups to source controls"
```

### Task 4: Integrate, deploy, and verify

**Files:**
- No additional production files expected.

- [ ] **Step 1: Review the complete branch diff**

Confirm only the plan, source guard, workflow window, and shared dropdown were
changed. Verify no bandit, corruption, RTS, office model, or save data files
are present.

- [ ] **Step 2: Fast-forward into master and rebuild**

Preserve unrelated dirty files in the main worktree. Run the focused suites and
production build again from the merged master.

- [ ] **Step 3: Deploy clean committed source**

Use `deploy-local.ps1` with a clean source root so unrelated uncommitted work is
not mirrored to the Steam mod directory.

- [ ] **Step 4: Runtime and visual verification**

Launch WorldBox visibly. Open the custom workflow and verify the local office
cards are visible after the 500-pixel shift. Open context, local template,
default kind, replacement, JSON import, and office preset dropdowns; verify
each popup touches and follows its own button while the window moves, resizes,
or scrolls. Inspect `Player.log` for new compile, Harmony, null-reference,
missing-key, or custom-court errors.

- [ ] **Step 5: Push master after verification**

Push only the committed feature changes after runtime evidence is clean.
