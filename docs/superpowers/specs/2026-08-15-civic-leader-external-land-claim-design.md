# Civic Leader External Land Claim Design

## Problem

The native `claim_land` task selects a zone, walks to its target tile, plays the
claim animation sequence, and only then calls
`BehClaimZoneForCityActorBorder.tryClaimZone`.

The current AW3 workaround intercepts that final claim call. When a king is
still inside the city, it replaces `beh_tile_target` and returns
`BehResult.Continue`. This cannot make the actor walk to the new target because
`AiSystem` advances to the next behavior after `Continue`; it does not return to
the earlier `BehGoToTileTarget`. The animation has already played, and the task
then ends. The workaround also excludes city leaders.

The result is that kings can claim land while standing inside existing city
territory, while city leaders retain the unchanged native behavior.

## Goal

For kings and city leaders, a land claim must follow this invariant:

1. Select an unoccupied zone directly adjacent to the actor's city border.
2. Walk into that selected external zone using the native movement behavior.
3. Revalidate arrival before any claim animation starts.
4. Claim only if the same zone remains legal at the final claim step.

If no legal, reachable external zone exists, or if the target becomes invalid
before arrival, the task stops without playing the claim animation.

Ordinary residents keep the native `claim_land` selection and execution flow.

## Scope

This change applies to actors who satisfy either condition:

- `actor.isKing()` is true.
- `actor.city.leader == actor` is true.

It does not change city growth limits, Xia expansion weights, ordinary resident
behavior, pathfinding, claim rewards, or the native animation sequence.

## Design

### 1. External target selection

At the beginning of `claim_land`, a civic leader receives a dedicated target
selector. It scans zones neighboring the city's current border zones and accepts
a candidate only when all of these conditions hold:

- The actor and city are valid.
- The candidate zone and its center tile are valid.
- The candidate has no owning city.
- The candidate is directly adjacent to at least one zone owned by the actor's
  city.
- The candidate is on the same island as the city tile.
- `city.isZoneToClaimStillGood(actor, candidate, cityTile)` returns true.

The selector writes `actor.beh_tile_target` to the candidate center tile and
returns `Continue`. If no candidate passes, it returns `Stop`, preventing the
movement and animation stages from running.

Candidate iteration follows the existing city-border collection order. This
keeps the change deterministic relative to native collection ordering and avoids
adding a separate global search or pathfinding pass.

Ordinary residents continue through the original native selector unchanged.

### 2. Arrival guard before animation

A focused behavior is inserted immediately after the native
`BehGoToTileTarget` in the existing `claim_land` task and before the first
animation behavior.

For civic leaders, the guard returns `Continue` only when:

- `actor.current_tile.zone` is the zone represented by the selected
  `beh_tile_target`.
- The current zone is not owned by the actor's city and has no other city owner.
- The current zone still directly touches the actor city's border.
- The zone remains on the same island as the city.
- `city.isZoneToClaimStillGood(...)` still returns true.

Otherwise it returns `Stop`. Therefore an interrupted walk, a stale target, a
border change, or another city claiming the zone cannot produce a claim
animation from inside existing territory.

The guard is a separate behavior rather than a late claim patch so its position
in the task sequence expresses the required ordering directly.

### 3. Final claim validation

The existing final prefix remains responsible for the Xia city-zone allowance
and bounded neighbor-claim behavior. Before calling `city.addZone`, it also
reuses the same civic-leader zone predicate used by the arrival guard.

The current late-target workaround (`TrySetKingClaimBorderTarget` and its
`BehResult.Continue` branch) is removed. Final validation never changes the
movement target. If the zone becomes invalid after the animation began, the
claim stops without mutating city ownership or awarding loot.

### 4. Task installation

The existing Harmony patch on `BehActorCheckZoneTarget.execute` becomes the
selection branch point. Its prefix handles civic leaders with the external-zone
selector and skips the original method; for every other actor it returns control
to the original method unchanged. This intercepts the exact native behavior at
index zero instead of replacing or wrapping the whole task.

`XiaExpansionDecisionContent.Init()` then extends the already registered native
`claim_land` task after assets are available:

- Locate the task by ID.
- Locate the native `BehGoToTileTarget` entry.
- Insert the arrival guard directly after it.
- Confirm that the task starts with `BehActorCheckZoneTarget`, matching the
  selector interception point.

Initialization must be idempotent. Repeated `Init()` calls must not insert a
duplicate guard. If the native task, selector entry, or expected movement entry
is missing, log one diagnostic warning and leave the native task usable instead
of partially modifying it.

Any behavior inserted directly into the public task list must receive the same
behavior ID initialization that `addBeh` normally performs.

## Component Boundaries

`XiaExpansionDecisionRules` owns pure decisions that can be tested without game
runtime objects: civic-leader applicability and boolean composition of external
zone validity.

A new actor behavior owns only the pre-animation arrival check and converts the
validation result into `BehResult.Continue` or `BehResult.Stop`.

`XiaExpansionDecisionContent` owns native asset lookup, task-shape validation,
guard placement, and idempotent installation.

`AW_XiaExpansionPatch` owns final claim enforcement and Xia zone-limit mutation.
It no longer attempts to redirect actors after movement has finished.

## Execution Flow

```text
claim_land selected
  -> civic leader?
       no: original BehActorCheckZoneTarget
       yes: Harmony prefix selects legal adjacent external zone or stops and
            skips the original selector
  -> native target check
  -> native BehGoToTileTarget
  -> civic-leader arrival guard
       valid arrival: continue
       invalid/stale/not arrived: stop
  -> unchanged native claim animation sequence
  -> final claim validation and city.addZone
  -> native border effect and task end
```

## Failure Handling

- Missing actor, city, city tile, zone, or target: stop civic-leader task.
- No adjacent legal external zone: stop before movement.
- Movement cannot reach the target: native movement stops the task; the guard
  and animation are not reached.
- Target changes ownership or ceases to border the city: arrival guard stops
  before animation.
- Target invalidates during animation: final validation stops the mutation.
- Native task shape changed and no movement step is found: log a warning and do
  not install a misplaced guard.

These failures are fail-closed for civic leaders. Ordinary residents remain on
the native path.

## Test Strategy

Focused rules tests will cover:

- Kings and matching city leaders are classified as civic leaders.
- Ordinary residents and leaders of another city are not.
- A candidate must be unoccupied, adjacent, same-island, and accepted by native
  claim validation.
- A civic leader already inside owned territory fails the arrival predicate.
- A civic leader in the selected legal external zone passes it.
- A stale selected zone, newly occupied zone, detached border zone, or invalid
  native claim result fails it.

Task-installation tests or a narrow test seam will verify:

- The guard appears exactly once immediately after `BehGoToTileTarget`.
- Repeated initialization does not add another guard or wrapper.
- Missing native task structure leaves the task unchanged.

Runtime verification will confirm:

- A king and a city leader visibly walk beyond their own border before the flag
  animation.
- No flag animation occurs if the external target becomes invalid en route.
- The claimed zone is the zone occupied at animation start.
- Ordinary resident land claims still behave as before.
- Existing Xia zone-cap and expansionist neighbor-claim behavior still pass.

## Acceptance Criteria

- Kings and city leaders never begin the land-claim animation while standing in
  a zone owned by their city.
- They claim only an unoccupied zone directly adjacent to that city's current
  border and on the same island.
- They use native movement to reach the selected zone.
- Invalid or unreachable targets cancel cleanly without animation, ownership
  mutation, or loot.
- Ordinary residents retain native behavior.
- Initialization is idempotent and does not corrupt the native task when its
  expected structure is unavailable.
