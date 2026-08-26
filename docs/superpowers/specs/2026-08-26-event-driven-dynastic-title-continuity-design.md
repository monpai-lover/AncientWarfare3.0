# Event-Driven Dynastic Title Continuity Design

## Goal

Remove the annual global traversal of active male title holders while preserving reliable hereditary title succession, including restoration when loading an existing save.

## Current Problem

`DynasticMaleLineContinuityService.OnKingdomYear` runs once for every kingdom. Each invocation traverses the global `ActiveMaleTitleHolders` set, so the same holders are repeatedly resolved and queued. The resulting work grows approximately with kingdom count multiplied by active male title-holder count.

The traversal does not perform the actual transfer of a title. Title transfer already runs directly from the actor death path through `NobleRankService.OnActorDying`, including its persistence transaction and retry queue. The traversal only refreshes the living holder's expected successor and heir-continuity state.

## Design

Use targeted events as the sole source of title-holder continuity refreshes:

- Title projection granted, transferred, or cleared: enqueue that actor.
- Child born: enqueue each hereditary parent.
- Male child dying: enqueue each hereditary parent.
- Holder dying: remove transient continuity state; actual title succession remains in `NobleRankService.OnActorDying`.
- Actor loaded from a save: enqueue the actor if they are a hereditary holder. If the loaded actor is male, also enqueue each loaded hereditary parent. This repairs either actor-loading order without scanning the world.

`DirtyHolders` and `EnqueuedHolders` continue to coalesce duplicate events. `ProcessAuthorityCycle` remains bounded to eight holder refreshes per authority cycle.

`OnKingdomYear` retains only its bounded kingdom-local fallback for the king, registered heir, and indexed feudatory princes. It no longer traverses `ActiveMaleTitleHolders`.

## Save-Load Ordering

The load path must work in both orders:

1. If the holder loads after their child, loading the holder queues the holder directly.
2. If the child loads after the holder, loading the male child queues the loaded parent.

If a referenced parent is not loaded yet, no search is performed; the parent's own later load event queues them. This provides eventual consistency without a global reconciliation pass.

## Succession Guarantee

Actual inheritance remains synchronous with the holder's death event:

1. `AW_ActorDeathPatch` invokes `NobleRankService.OnActorDying`.
2. Direct eligible sons are resolved first.
3. The bounded lineage index is queried for collateral candidates only when no direct successor exists.
4. The title transfer is committed transactionally.
5. Failed persistence work remains in the existing death-succession retry queue.

The continuity queue is therefore not a prerequisite for transferring a title. Its role is to maintain expected-successor state and request an heir while the holder is alive.

## Failure Handling

- Invalid, dead, or no-longer-hereditary holders are removed when their queued refresh runs.
- Duplicate notifications are coalesced by holder ID.
- Missing actors during load ordering are ignored until their own load event occurs.
- Existing succession persistence and retry behavior is unchanged.

## Verification

Add source guards and focused rule tests covering:

- `OnKingdomYear` does not enumerate `ActiveMaleTitleHolders`.
- Loading a hereditary holder queues that holder.
- Loading a male child queues loaded hereditary parents.
- Birth and death events continue to refresh only affected holders.
- The death patch still invokes `NobleRankService.OnActorDying`.
- The authority-cycle batch limit remains eight.

Run the focused rules test project and the main project build. Commit this performance fix independently from unrelated working-tree changes.
