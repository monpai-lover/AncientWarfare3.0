# Custom Court Editor Docked Toolbar Design

## Goal

Refactor the custom court editor so its node canvas always fills the window,
its toolbar remains docked to the full-height left edge, and an Eastern Zhou
whole-court preset remains available for Xia courts even when runtime policy
profile resolution is temporarily unavailable.

## Confirmed Layout

- The node canvas stretches to all four edges of the editor content area.
- The toolbar is a separate overlay docked to the left edge of that canvas.
- The toolbar height always matches the editor content height.
- The toolbar width is stable and does not depend on the current window width.
- Resizing the wide window changes the canvas and toolbar height through
  anchors and derived dimensions; no fixed right-edge position is used.
- The toolbar remains above the canvas in sibling order.
- Toolbar controls retain the existing 80 percent visual scale.
- The toolbar owns a vertical scrollbar. Controls that do not fit remain
  reachable instead of being clipped.
- The existing window limits, node card size, 2000 by 1500 workspace, and
  drag-to-pan behavior remain unchanged.

## Canvas Interaction

The canvas occupies the entire editor content area, including the area behind
the toolbar. This satisfies the requirement that the canvas match the window
size while keeping the toolbar fixed at the far left.

New nodes and loaded whole-court layouts are centered in the unobscured area
to the right of the toolbar. Existing saved node coordinates are not rewritten
when the window is resized. The workspace can still be dragged beneath the
toolbar, but the toolbar consumes pointer input and therefore does not initiate
canvas dragging.

## Toolbar Geometry

The toolbar viewport uses left-edge stretch anchors:

- horizontal anchor and pivot at the left edge;
- vertical anchors from bottom to top;
- zero vertical inset;
- width equal to the scaled panel width plus scrollbar width;
- height inherited from the stretched parent content area.

The toolbar content remains top-aligned inside the viewport. Its unscaled
content height is based on the last control and status region, while its
display scale remains 0.8. The scrollbar is anchored to the viewport's right
edge and spans its full height.

This removes the current coupling between window width and the fixed
`-864f` right-anchor offset, and removes the positive top offset that clips
the toolbar's top and bottom controls.

## Whole-Court Preset Availability

Preset availability has two separate concerns:

1. Resolve which court profile the kingdom uses.
2. Unlock only historical stages at or below the kingdom's current built-in
   institution.

The normal source remains `CourtProfileRegistry.For(kingdom)`. If that source
is temporarily unavailable but the kingdom already has a known non-Western
court institution, the editor falls back to the Xia court profile. A known
Western institution falls back to the Western profile. This fallback is local
to preset selection and does not mutate the kingdom's policy profile.

For a Xia profile, Eastern Zhou is always unlocked because it is rank zero.
Han, Tang, and Song remain gated by the kingdom's current institution rank.
Applying a custom court does not remove the underlying institution or the
ability to return to an unlocked built-in preset.

The editor only reports that no whole-court preset is available when neither a
normal profile nor a profile inferred from a known institution can be
resolved, or when the resolved profile genuinely defines no preset offices.

## Tests

Focused rule tests cover:

- full-height toolbar geometry at default, minimum, and maximum window sizes;
- toolbar x-position remaining fixed when window width changes;
- canvas dimensions matching the content viewport;
- usable canvas center excluding the toolbar-covered region;
- Xia fallback from a known Eastern Zhou institution;
- Western fallback from a known Western institution;
- no fallback from an unknown institution;
- Eastern Zhou remaining unlocked at rank zero.

Source guards verify that the Unity window consumes the tested layout and
profile-resolution helpers instead of reintroducing fixed right-edge offsets.
The production build and focused custom-court test suites must pass before
deployment.

## Deployment Verification

After building and deploying to the active WorldBox mod directory:

- open the custom court editor at default size and resize it in both axes;
- confirm the canvas always fills the content area;
- confirm the toolbar stays on the far left and fills the height;
- scroll to the final Apply and status controls;
- create a node and confirm it appears in the unobscured canvas area;
- open a Xia kingdom at Eastern Zhou stage and cycle/load the Eastern Zhou
  whole-court preset;
- inspect `Player.log` for compile, Harmony, null-reference, missing-key, and
  custom-court runtime errors.
