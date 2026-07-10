# AW3 Guard And Slave Runtime Performance Design

## Goal

Reduce the remaining runtime cost of royal guards and slavery without changing guard limits, slave-army composition, capture eligibility, combat behavior, or government rules. Ordinary kingdoms that do not use these systems should pay only constant-time maintenance gates.

## Confirmed Root Causes

1. Slave-labor maintenance runs for every city and counts the full population before checking whether slavery is enabled.
2. Normal city-army maintenance repeatedly scans the same army to strip guards, classify slave armies, and refresh fief names.
3. Every guard and slave catcher searches every unit in every enemy kingdom even though both searches have a bounded spatial radius.
4. Every slave army independently searches all enemy units for a frontline target and reissues movement orders to every member.
5. Slave merit synchronously queries and updates SQLite on every slave kill.
6. Special-army duplicate cleanup scans every world army whenever an existing special army is ensured.
7. The bounded slave fill pass is preceded by an unbounded exact slave count.
8. Existing benchmark labels measure slave-army maintenance as slave-catcher work and do not measure actor-AI target searches.

## Considered Approaches

### Interval-only throttling

Increase maintenance and AI cooldown intervals. This is low risk, but it only makes the same full scans less frequent and can make guards, captures, and army movement visibly less responsive.

### Spatial queries, fast gates, and bounded caches (selected)

Reuse the original game's public `Finder.getUnitsFromChunk` spatial query for bounded guard and catcher searches. Add cheap feature gates, reuse or avoid repeated classification work, cache only the slave-army frontline target that cannot be expressed as a fixed-radius query, and move persistence away from every ordinary kill. This removes the root causes while preserving gameplay behavior.

### New global spatial index and event bus

Maintain a mod-owned index of units, armies, and slave counts with full event-driven invalidation. This offers the best theoretical complexity but duplicates original-game state, creates save/load invalidation risks, and is too invasive for the current problem.

## Design

### Slave-labor gate and scheduling

`CheckCitySlaveLabor` will return before reading timers or scanning residents when the city's kingdom has slavery disabled. Enabled cities will use the existing staggered city-maintenance rule so cities do not align their first or repeated checks. Exact slave counting remains acceptable here because it runs once for a slave city/kingdom pairing and supplies the chronicle count.

Food quota checks only need to know whether at least one slave exists, so they will stop after the first match instead of calculating an exact count.

### Normal-army maintenance fast paths

The maintenance call sequence remains centralized in `AW_RetirementPatch`, but each subsystem must reject irrelevant armies before enumerating members:

- Guard stripping skips normal armies when the owning kingdom has no guard army, guard roster, or guard-record hint.
- Slave-army renaming uses the explicit special-army role first and skips composition inference when the owning kingdom has slavery disabled.
- Fief naming checks whether the city is an active fief before asking whether the army is a slave army.

This makes ordinary non-slavery, non-fief kingdoms constant-time. Composition inference remains as a compatibility fallback for legacy unmarked slave armies.

### Spatial guard and catcher searches

Guard search keeps the current direct-attacker fast path. When no direct attacker exists, it queries nearby units around the king using the 10-tile protection radius and around the guard using the 4-tile follow radius. Duplicate candidates are harmless because selection compares actor IDs and distance. Enemy, island, life-state, and eligibility validation remains unchanged.

Slave catchers query nearby units around the catcher with the existing 80-tile radius. The chunk radius is derived from the original game's fixed 16-by-16 chunk size and the exact tile-radius filter is still passed to `Finder`, so targets outside 80 tiles are never accepted. Capture eligibility and health thresholds remain unchanged.

The existing per-actor miss cooldown stays in place for behavior pacing, but stale entries are removed on known guard dismissal/death and pruned opportunistically when throttle maps grow. Dedicated benchmark IDs measure the real actor-AI scan bodies.

### Shared slave-army frontline target

Frontline search has no fixed radius, so it retains the global enemy fallback but caches its result per kingdom and island for a short interval. Slave armies on the same island reuse a live, still-hostile target. Invalid, dead, wrong-island, or expired targets trigger a fresh search. Cache entries are bounded and opportunistically pruned, so destroyed kingdoms and islands cannot create unbounded static state.

Movement orders are only reissued when a unit's current actor target differs, or when the unit is no longer moving toward that target. This preserves recovery from interrupted movement while avoiding identical path requests for the whole army every maintenance pass.

### Slave fill without repeated exact counts

An existing, valid slave army no longer performs an exact city slave count before its bounded fill pass. A full army remains an immediate fast path. An underfilled army uses the existing 32-candidate cursor scan; reaching the end without additions proves that pass has no more candidates.

Creating a new slave army only needs to prove that at least three slaves exist. The formation check stops as soon as that threshold is reached. Cities below the threshold retain the existing failed-maintenance cooldown, preventing frequent full scans of cities with zero or very few slaves.

### Special-army deduplication lifecycle

`EnsureArmy` records whether it created a new army. Duplicate cleanup runs only for newly created armies, explicit re-anchoring, and the post-load repair phase. Cache hits for an already valid army no longer scan every world army. Post-load repair performs one deterministic deduplication sweep so legacy duplicate armies remain recoverable.

### Merit persistence

Actor data remains the authoritative live merit value. Ordinary one-point kills do not synchronously upsert the archive on every event. Persistence occurs when merit crosses a fixed milestone, an important kill grants multiple points, the slave is freed, or another existing lifecycle event already writes slave state. Freedom at the configured merit threshold is still immediate.

This reduces battle-time database traffic while keeping the archive close to live state and exact at all state transitions that affect gameplay or history.

### Benchmark correction

The city benchmark currently named `SlaveCatchers` will be replaced at its call site by a dedicated slave-army maintenance ID. Separate IDs will cover guard threat spatial scans, catcher target spatial scans, frontline global fallback scans, and merit persistence. The unused city-level catcher assignment method remains available but is no longer treated as evidence that actor-AI scans are measured.

## Error Handling And Compatibility

- If `Finder` cannot run because a tile or world map is unavailable, searches return no target and retain current AI wait behavior.
- Legacy slave armies without an AW3 role are still recognized by composition when slavery is enabled.
- Cache lookup always revalidates actor existence, hostility, island, and eligibility before reuse.
- Cache failure never blocks the original fallback search after expiry.
- No new data is required for save compatibility; runtime caches are rebuildable.

## Testing Strategy

Pure rule tests will be added before production changes for:

- slavery-disabled labor gating and stagger selection;
- fixed-radius-to-chunk-radius conversion;
- ordinary-army fast-path decisions;
- frontline cache reuse and invalidation decisions;
- special-army duplicate-cleanup lifecycle decisions;
- threshold-only slave counting behavior;
- merit persistence milestones;
- movement-order suppression when the current target and movement state are still valid.

Each test must be observed failing before its implementation is added. After every focused change, its owning rule-test project will run, followed by the complete repository test set and `dotnet build`.

## Success Criteria

1. Non-slavery cities do not enumerate residents for slave labor, slave food, or slave-army naming.
2. Ordinary kingdoms without guard state do not copy or enumerate normal-army members for guard cleanup.
3. Guard and catcher target searches enumerate spatial chunks instead of all enemy kingdoms.
4. Slave armies on the same kingdom/island reuse a validated frontline target and do not repeat identical path orders.
5. Existing underfilled slave armies never require an exact full-city slave count before bounded fill.
6. Valid special-army cache hits do not trigger global duplicate scans.
7. Ordinary slave kills do not produce one synchronous archive upsert per kill.
8. Performance benchmark labels correspond to the code they measure.
9. All existing rule tests and the mod build pass without changing gameplay constants or eligibility rules.
