# Court Window Layout Alignment Design

## Scope

Adjust two existing court UI windows without changing their data behavior,
window width, node-card dimensions, or canvas interaction model.

## Office History Window

- Move the complete content root 30 pixels to the right.
- Reduce the root's usable width by the same 30 pixels so rows remain inside
  the viewport.
- Keep the window frame, title, close button, resize handle, row height, and
  vertical positions unchanged.

## Custom Court Workflow Window

- Match the court window's size contract:
  - default: 560 x 360
  - minimum: 420 x 280
  - maximum: 900 x 650
- Preserve the existing window width and resizable wide-window chrome.
- Render the toolbar controls at 80 percent of their current visual scale.
- Give the toolbar its own vertical scrolling viewport so all controls remain
  reachable at default and minimum heights.
- Keep toolbar scrolling independent from canvas panning.
- Preserve the canvas position, node-card size, edge behavior, and workspace
  drag behavior.
- Keep click regions aligned with the scaled controls.

## Verification

- Add source-level regression assertions for the 30-pixel history inset,
  court-matched workflow sizes, 80-percent toolbar scale, and independent
  toolbar scrolling.
- Run the focused custom-court and office-history guards.
- Run the complete rules suite and production build.
- Deploy to the WorldBox Mods directory, restart WorldBox, and check the NML
  log for compilation or Harmony failures.
