# Workflow Workspace and Dropdown Binding Design

## Goal

Make local-government office cards visible in the custom court workflow by
moving the workflow root 500 pixels left, and make every `AWStringDropdown`
popup remain attached to the button that opened it.

## Confirmed Scope

- Move the complete custom-court workflow root left by exactly 500 pixels.
- The canvas, node workspace, toolbar, and their child controls move together.
- Do not change the workflow window size, node card size, workspace size, or
  drag-to-pan behavior.
- Apply dropdown positioning globally to every `AWStringDropdown` consumer,
  including context selection, local template selection, default-kind
  selection, deletion replacement selection, JSON import selection, and
  office-setting preset selection.
- Do not implement a one-off offset for the JSON import dropdown.

## Root Cause

The custom workflow root is centered under WorldBox's native
`ContentTransform`, whose inherited content coordinate remains displaced to
the right in this wide-window configuration. Internal left anchors therefore
still render the whole workflow group too far right. Local-government offices
are present in the template and read model, but their cards are outside the
visible workspace.

`AWStringDropdown` creates its popup under a full-screen overlay. It converts
the source button's world corners into a point local to the Canvas, then writes
that center-relative local point into a popup whose anchors use the Canvas
bottom-left corner. The coordinate origins do not match, so the popup can
appear at the screen edge or outside the visible area. The JSON import control
shows the same shared bug as the local-template controls.

## Workflow Layout

`CustomCourtWorkflowWindow.ApplyLayout` keeps the existing root anchors,
pivot, and size, but sets the root anchored position to `(-500, 0)`. No child
receives a compensating offset. This preserves the relative relationship
between toolbar and canvas while bringing the local office cards back into the
visible window.

New-node centering continues to use the current canvas transform after the
root offset, so newly created local offices appear in the visible workspace.
Existing saved office coordinates remain unchanged.

## Shared Dropdown Positioning

The popup remains a child of the full-screen overlay so it can render above
window masks. Its position is derived exclusively from the opening button's
current world corners:

1. Read the source `RectTransform` world corners.
2. Select the source bottom-left or top-left edge according to available
   screen space.
3. Convert that screen point into the overlay parent's local coordinates.
4. Assign the result to popup `localPosition`, which uses the same parent-pivot
   coordinate system.
5. Clamp the popup's screen rectangle to the configured screen padding.

While the dropdown is open, its `LateUpdate` reapplies the button-derived
position after layout. This keeps the popup attached when the window is
dragged or resized and when a scroll view moves the source button. If the
source button becomes inactive, detached, or destroyed, the dropdown closes
instead of leaving an orphan popup.

Popup width, maximum height, option rows, scrolling, sorting order, disabled
options, and click-away dismissal remain unchanged.

## Local-Government Vacancy Visibility

No new office-generation path is needed. The existing pipeline already:

- resolves the selected local template;
- expands each `Layer=City` office and its slot count;
- creates vacancy nodes for unfilled seats;
- lays those nodes out and binds them to `CourtActorNodeView`.

The fix therefore addresses the displaced workflow root rather than adding
duplicate vacancy models or fallback offices.

## Tests

Focused tests and source guards cover:

- the custom workflow root using exactly `new Vector2(-500f, 0f)`;
- the old zero root offset no longer being used;
- local presets still containing all four office definitions;
- `AWStringDropdown` converting against the overlay parent and assigning
  popup `localPosition` rather than mixing it with bottom-left
  `anchoredPosition`;
- open dropdowns repositioning during `LateUpdate`;
- invalid or inactive source buttons closing their popup;
- all existing custom-court and local-government focused suites remaining
  green.

## Verification

After building and deploying:

- open the central and local custom-court editors;
- verify the workspace group is 500 pixels farther left;
- select both Civil Prefecture and Military Government and confirm their four
  office cards are visible;
- open every toolbar dropdown, especially JSON import, and confirm its popup
  touches the opening button;
- drag and resize the window and scroll the toolbar while a popup is open;
- confirm the popup follows its source control and remains on screen;
- inspect `Player.log` for compile, Harmony, null-reference, missing-key, and
  custom-court errors.
