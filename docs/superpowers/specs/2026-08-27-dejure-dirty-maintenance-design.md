# De Jure Dirty Maintenance Design

## Goal

Move de jure repair and migration work out of read paths and into a bounded,
event-driven maintenance queue. Reads must return a stable snapshot without
scanning or mutating the world, while world-load repair and explicit changes
remain correct and recoverable.

## Scope and invariants

- `ObserveLoadDirectory` only loads JSON, normalizes the store shape, and
  records load state. It does not repair regions, assign cities, migrate
  history, or synchronize names.
- `RepairAfterWorldLoaded` is the single full-repair entry point for old saves.
- Region reads (`ActiveRegions`, `TryGetForCity`, `TryGetBySeat`, and `Revision`)
  are read-only snapshot operations. They never create regions, move cities,
  synchronize names, or enqueue work.
- Explicitly retired regions remain inactive and empty. Automatic maintenance
  must never recreate them.
- `StoreRevision` increases only after a complete, effective transaction is
  committed.

## Architecture

### DeJureRegionMaintenanceService

Add a focused service under `Code/core/court` that owns two coalescing queues:

- `DirtyKingdomIds`: ownership, capital, and city-roster changes.
- `DirtyRegionIds`: region membership, seat, name, retirement, and merge
  changes.

Each queue entry contains an identity, reason flags, first-seen cycle, retry
count, and next-attempt cycle. Enqueuing the same identity merges flags rather
than adding another item. The service exposes:

- `MarkKingdomDirty(long kingdomId, reason)`
- `MarkRegionDirty(long regionId, reason)`
- `ProcessAuthorityCycle(int itemBudget)`
- `Reset()` and `ClearRuntime()`

`ProcessAuthorityCycle` drains a bounded number of due entries. It resolves
ownership and region snapshots once per item, then invokes existing repair and
assignment helpers with explicit inputs. No helper may enumerate the whole
world unless it is called by the world-load repair path.

### Store integration

`DeJureRegionStore` keeps persistence and atomic commits. Mutators mark the
affected kingdom/region dirty after changing in-memory intent, while the
maintenance service performs derived repairs. `ObserveLoadDirectory` leaves
the loaded store untouched apart from structural normalization. The existing
full repair method runs once after world objects are available, then seeds the
incremental queues for any unresolved references.

`Revision` and all read APIs obtain an immutable cloned snapshot under the
existing store lock. They do not call maintenance or any mutating helper.
`SyncSeatName` is allowed only from an explicit city-name-change event, where
the affected region is already known.

### Authority-cycle scheduling

Add a bounded `DeJureMaintenance` stage to
`AWAuthorityCycleService`, after world/ownership changes and before consumers
that build regional aggregates. The stage receives a small item budget and
never runs on every render frame. A successful commit invalidates dependent
read models once:

- `RegionalGovernmentAggregationService`
- hierarchical map mode snapshots/labels
- de jure war-goal cache

Consumers then rebuild lazily from the new revision.

## Failure recovery and atomicity

Each dirty item is processed against a temporary working copy. Validation
checks that referenced cities, kingdoms, and seats are available and that an
explicitly retired region is not being revived. On success, the working copy
is swapped into the store under the store lock and dependent caches are marked
dirty. If no effective value changed, no revision increment occurs.

On failure:

1. Keep the old published snapshot and all existing bindings intact.
2. Keep the dirty item queued and apply cycle backoff of 1, 2, 4, 8, and 16.
3. Retry at most five times. A missing world object is treated as transient.
4. After the fifth failure, retain a dormant dirty marker for the next world
   load or an explicit related event; do not publish partial data or delete
   valid old data.
5. Log the identity, reason flags, retry count, and exception category without
   serializing the entire world or flooding per-frame logs.

World-load repair may use a larger budget and complete scans because it is an
explicit recovery boundary. It still commits each region atomically and leaves
unresolved entries queued for later incremental retries.

## Event sources

The following events mark only affected identities:

- region create, transfer, merge, retirement, and seat change;
- city creation, ownership change, destruction, and capital change;
- explicit city-name change for seat-name synchronization;
- world load completion for the one-time full repair.

No periodic read-side repair or broad polling is introduced.

## Tests and acceptance criteria

Add focused rule/source tests covering:

1. Repeated reads do not invoke maintenance or mutate revision.
2. World-load maintenance executes once, while repeated load-directory reads
   remain normalization-only.
3. Repeated marks for one kingdom/region coalesce into one queue item.
4. A failed transaction preserves the old snapshot, bindings, and revision.
5. Backoff and retry limits are deterministic; a later explicit event wakes a
   dormant item.
6. Explicitly retired regions remain absent from active reads after all repair
   passes.
7. City ownership and seat changes dirty only related regions/kingdoms.
8. Revision increments exactly once for an effective successful commit and not
   for a no-op.
9. A successful commit invalidates regional government, map, and war-goal
   consumers exactly once.
10. Legacy saves with unassigned cities repair through the load boundary, while
    normal map/UI reads perform no full-world enumeration.

## Non-goals

- No changes to RTS movement, pathfinding algorithms, army combat behavior,
  diplomacy rules, or UI layout.
- No redesign of persisted region JSON beyond fields needed to preserve the
  existing retirement/history semantics.
- No removal of existing diagnostics; only duplicate read-path work is moved
  behind the bounded maintenance stage.
