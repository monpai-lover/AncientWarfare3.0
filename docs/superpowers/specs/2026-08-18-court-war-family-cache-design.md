# Court Layout, Mobilization Caps, and Family Tree Cache Design

## Goal

Fix the custom court editor viewport and preset interaction, prevent synthetic
wartime soldiers from exceeding the ordinary pre-war force, exclude royal
guards from no-army peace checks, preserve the AW3 settings warning, and make
repeated family-tree queries within one clan fast without showing stale data.

## Design

### Custom court editor

The editor keeps the user-tuned horizontal positions. Its root viewport is the
single source of visible height. The canvas uses the full measured viewport
height and compensates the existing root/child Y offsets so the top and bottom
remain inside the `RectMask2D`. The toolbar is rendered at 80% scale; its
logical height is divided by 0.8 so the scaled toolbar still fills the visible
height. The window dimensions remain unchanged.

The whole-court preset selector becomes a button with a text label. Each click
advances through the existing preset list and wraps at the end. Existing
unlock checks and feedback remain authoritative. The JSON import selector stays
a dropdown because it selects among files rather than cycling a fixed command.

### Wartime ordinary-force cap

When a kingdom first enters an AW3-managed war while it has no other active
AW3-managed war, the system records its ordinary living warrior count. Royal
guard armies are excluded from this count; synthetic soldiers are also excluded
from the baseline because they are created by the system after mobilization.
The baseline is persisted with the kingdom/war runtime state needed to survive
save/load.

Every synthetic initial-spawn and replacement path shares the kingdom baseline.
Immediately before reservation and again immediately before actor creation, the
service computes:

`remaining = max(0, preWarOrdinaryCount - currentOrdinaryCountExcludingRoyalGuards)`.

The requested batch is clamped to `remaining`. Multiple cities and simultaneous
wars therefore cannot each spend an independent quota. When the last relevant
war ends, the baseline is cleared after normal demobilization bookkeeping.

### Peace/no-army settlement

The settlement service retains the vanilla attacker/defender warrior counts as
the source, then subtracts living royal-guard members for each side. The result
is clamped at zero and is the only count used by the no-army rule. A kingdom
whose only remaining soldiers are royal guards therefore satisfies the no-army
condition.

### AW3 settings warning

The native localized title remains untouched. The AW3 settings window receives a
separate warning `Text` child under the title parent. It displays the localized
`aw_settings_experimental_warning` string only for AW3 and is hidden for other
mods. This avoids the native `LocalizedText.Start()` lifecycle overwriting the
warning after the Harmony postfix runs.

### Family-tree clan snapshot cache

Family-tree reads use a bounded LRU cache of up to four immutable
`LineageBulkSnapshot` instances. A cache key contains world generation, the
current family-tree projection revision, and the clan/shi scope used by the
entry. A family-tree open within the same clan first checks whether the cached
snapshot contains the requested actor; if so, it reuses the snapshot and only
rebuilds the small-tree layout around the new center. The cache is bypassed when
the scope or revision differs and is cleared on world reset.

The existing per-window child-id cache remains a layout optimization. The new
cache owns only read-model snapshots and never mutates them. Any family
structure, life-status, identity/title, or world-generation change invalidates
the matching cache through the existing projection revision/world lifecycle.

## Error handling

Missing baselines, invalid kingdom IDs, unavailable snapshots, and negative
counts resolve to zero or a cache miss. No UI path throws for a cache miss; it
falls back to the current asynchronous/synchronous read pipeline. Actor creation
must never proceed with an unverified batch.

## Testing

- Rule tests cover viewport height/scale geometry and preset index wrapping.
- Rule tests cover royal-guard subtraction and ordinary-force cap clamping,
  including multiple cities and an all-guards kingdom.
- Source guards verify the AW3 warning uses an independent text element and that
  the preset dropdown is replaced while JSON import remains a dropdown.
- Cache rule tests cover same-scope hits, revision/world-generation misses,
  bounded eviction, and actor-not-present fallback.
- Full rules tests, adversarial RTS simulation, production build, and source
  deployment verification are run before release.
