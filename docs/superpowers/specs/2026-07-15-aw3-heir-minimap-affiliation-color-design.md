# AW3 Heir Minimap Affiliation Color Design

## Problem

The heir minimap patch currently colors the marker with the kingdom that owns the
succession record. That legal realm can temporarily differ from the actor's live
`Actor.kingdom` after a transfer of affiliation, so the marker can retain the old
realm color.

The legal heir relationship and the actor's visual affiliation are separate
concerns. Changing marker color must not transfer, clear, or recalculate the
succession record.

## Behavior

- A stored legal heir remains the source of the marker.
- The marker uses the heir actor's current `Actor.kingdom` color when available.
- If the actor temporarily has no kingdom, the legal heir kingdom supplies the
  fallback color and hover-scale anchor.
- The same resolved visual kingdom supplies both marker color and the capital
  fallback used for hover scaling.
- The marker reads current state during the existing `drawKings` pass, so an
  affiliation change appears on the next draw without an ownership-event hook.
- The implementation remains O(number of kingdoms) and never scans kingdom
  units.

## Components

- `HeirMinimapVisualRules` expresses the current-affiliation-first fallback rule
  as a Unity-independent function.
- `HeirService.PeekStoredHeirForMinimap` reads the stored heir actor without the
  succession eligibility check that rejects a temporary foreign affiliation.
  It does not mutate succession state.
- `AW_HeirMinimapPatch` resolves one visual kingdom per marker and uses it for
  both color and scale anchoring.

## Error Handling

Dead, missing, unmarked, king, leader, or tile-less actors are not rendered. A
missing current kingdom falls back to the legal kingdom; if neither is usable,
the marker is skipped. No exception-driven fallback or global repair scan is
introduced.

## Verification

- Rule test: current affiliation overrides legal realm.
- Rule test: legal realm is used when current affiliation is absent.
- Source guards: the marker uses the minimap-specific stored lookup and resolved
  visual kingdom, and still contains no kingdom-unit scan.
- Existing rules, source guards, Debug build, Release build, and `git diff
  --check` must pass.
