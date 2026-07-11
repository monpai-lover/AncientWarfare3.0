# AW3 Mandate Core Tooltip Invalidation Design

## Goal

Keep Mandate legal-core control totals synchronized with live city ownership so the
Mandate core map tooltip, dynasty window, and declaration rules cannot retain an
old percentage after legal-core cities change hands.

## Root Cause

Map hover selection is already live. The original cursor helper requests a new
tooltip roughly every 0.2 seconds, and `AWMapModeMetaLibrary.ShowKingdomTooltip`
passes the city and kingdom under the current zone on every request.

The stale value comes from `MandateService.ReadReport()`. It caches the dynamic
`controlled_core_count` and `core_control` fields until `MarkDirty()` is called.
The `City.setKingdom` Postfix currently notifies chronicle, occupation, fief, and
war-territory services, but does not invalidate the Mandate report. A control
ratio cached at 60 percent can therefore remain visible after all legal cores are
captured.

## Selected Design

Add a pure rule that decides whether a city transfer can change Mandate core
control. The rule returns true only when a Mandate period exists and the
transferred city is an active legal core.

Add `MandateService.OnCityTransferred(City)` as the runtime boundary. It reads
the already-loaded legal-core ID cache, applies the rule, then calls `MarkDirty()`
and `MandateCoreMapModeService.DirtyMapIfActive()` when required. Wire this method
into the existing `City.setKingdom` Postfix after ownership has changed.

The next tooltip or Mandate report read recomputes controlled cities from live
city ownership. Non-core transfers do not invalidate the cache or redraw the map.

## Data Flow

1. The game completes `City.setKingdom`.
2. The existing Postfix passes the transferred city to `MandateService`.
3. The service checks current-period existence and legal-core membership.
4. Relevant transfers dirty the Mandate report and active core map.
5. The next hover rebuilds both dynasty-wide and pointed-realm percentages.

## Error Handling

Null, destroyed, loading, no-period, and non-core cities are ignored. The hook
does not query SQLite and does not scan all legal cores during the transfer.

## Verification

- A pure regression test proves that an active legal-core transfer invalidates.
- The test proves that no-period and non-core transfers do not invalidate.
- Existing focused rule tests continue to pass.
- The complete mod builds with zero errors.
- In-game acceptance: cache a 60 percent value, transfer the remaining legal-core
  cities, and confirm the next hover reports the new count and 100 percent.

