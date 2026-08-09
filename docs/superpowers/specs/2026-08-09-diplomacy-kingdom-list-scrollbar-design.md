# Diplomacy Kingdom List Scrollbar Design

## Goal

Add a visible vertical scrollbar to the left-side kingdom list in the diplomacy conversation window so long lists can be navigated by dragging the handle as well as by using the mouse wheel.

## Scope

- Reuse the diplomacy window's existing vertical scrollbar builder and visual style.
- Retain the left list's existing `ScrollRect`, clipping, content layout, and relation-priority sorting.
- Keep the scrollbar permanently visible and reserve eight pixels from the list viewport so it does not cover kingdom entries.
- Do not change diplomacy actions, kingdom entry contents, window dimensions, or sorting behavior.

## Implementation

Store the left kingdom list's `ScrollRect` when `CreateScrollArea` builds it, then attach a scrollbar through the existing `CreateVerticalScrollbar` helper. Keep references to both components for source-level verification and future layout maintenance. The helper already binds the scrollbar, uses clamped vertical movement, and narrows the viewport to avoid overlap.

## Verification

- Add a source guard that fails until the left list retains its `ScrollRect` and attaches a vertical scrollbar.
- Run the focused source guard and the existing rules test project.
- Build the mod project with no compile errors.
- Deploy only the changed source file to the WorldBox mod source folder; do not deploy DLL files.

