# Runtime, MapMode, and No-Force War Collapse Design

## Status

Design for the next implementation pass. Existing map modes, RTS simulation,
and ordinary war settlement behavior remain unchanged unless they pass through
the new invalidation or no-force settlement gates.

## Goals

1. Remove the performance cost of AW3 diagnostics from the normal Actor update
   path and keep detailed measurements useful when explicitly enabled.
2. Keep the experimental hierarchical vassal MapMode responsive on large maps
   without reducing Actor, movement, city, or war simulation frequency.
3. End late wars that have genuinely exhausted one whole side's military
   potential instead of allowing a country to remain in a dead war forever.
4. Make a total-war surrender (including Zhulu) transfer the defeated side's
   entire eligible territory, not a partial peace settlement.

## Non-Goals

- Do not move Unity or WorldBox object reads to worker threads.
- Do not replace RTS pathfinding, Army decisions, or native Actor scheduling.
- Do not change the meaning of ordinary peace, war goals, or separate peace.
- Do not deploy compiled DLLs; deployment remains source-folder only.

## Performance Design

### Actor diagnostics

`AW_ActorAiBenchmarkPatch` must have a zero-work path when neither detailed
runtime sampling nor `Bench.bench_enabled` is active. That path must not read
the current task, allocate a task string, start a stopwatch, or enter nested
diagnostic scopes.

When `Benchmark All` is enabled, full `BatchActors` stage timing remains active
for `b6_updateAI` and the other fixed stages. Per-Actor task/race detail uses a
deterministic bounded sample with a fixed per-frame budget. Samples are recorded
without changing Actor behavior or update frequency. Race and sprite diagnostics
follow the same gate, so the profiling instrumentation cannot dominate the
`actors` measurement it is intended to explain.

### Hierarchical MapMode invalidation and caches

The MapMode owns a monotonic dirty generation and separate dirty sets for
physical cities, kingdoms, hierarchy relations, and labels.

- Existing lifecycle, city-zone, ownership, title, vassal, save-load, and mode
  transition hooks mark the smallest relevant dirty set.
- A city geometry cache stores visible zones, land tiles, area, centroid, and
  label metrics. Kingdom and hierarchy snapshots aggregate those cached city
  entries instead of rereading every tile on each refresh.
- A visible snapshot is rebuilt only when its generation or focused hierarchy
  changes. Labels are rebuilt only when the visible entries or camera layer
  require it.
- A fallback validator runs only while this MapMode is active. It advances a
  bounded city/kingdom cursor over frames and completes within two seconds on a
  normal game clock. It compares lightweight metadata signatures and never
  performs a full-world scan in one frame.
- Mode exit, world reset, and load clear all cursors and caches. Mode-off frames
  perform no MapMode validation, geometry, label, or minimap work.

### QuantumSprite minimap filtering

On MapMode entry, the patch records the original `render_map` value for each
non-whitelisted QuantumSprite asset, temporarily disables map rendering before
`QuantumSpriteManager.update` dispatches draw calls, and clears those groups once
at the transition. Army flags, boats, required selection/highlight markers,
and AW3 country labels remain enabled. On exit/reset the exact original values
are restored. The update postfix must not scan active groups and call
`clearFull()` every frame.

## No-Force War Collapse

### Military potential

The existing military potential services are the source of truth. For every
participant on one side of a War, the runtime computes:

- operational field soldiers;
- soldiers in that side's reserve pools;
- eligible recruitable/force-mobilizable population after the wartime force
  recruitment pass.

The side is `NoForce` only when all three totals are zero and no valid Army with
the minimum operational force remains. A temporarily empty or retreating Army
does not by itself satisfy `NoForce` while the side can still replenish it.

### Trigger

The rule is evaluated through the existing annual war-settlement assessment,
after wartime recruitment/replenishment has run:

- war duration is at least three years;
- the requester side is `NoForce`;
- the opposing side still has positive military potential;
- the War is active and the participant has not already submitted a settlement
  for the current year.

The trigger is side-wide, not based on only the war leader's Army. This prevents
an exhausted minor participant from forcing its allies to surrender while they
still have troops. It also prevents a false surrender during a single failed
reinforcement cycle.

### Ordinary wars

For a normal war, the war leader creates the existing surrender settlement
using the current war score and active war goals. The no-force rule supplies a
high-priority surrender decision; it does not bypass validation, protected
goals, or the existing settlement transaction.

### Total wars and Zhulu

Total-war/protected-war types, including Zhulu, do not create an ordinary peace
proposal. When the defeated side satisfies `NoForce`, the runtime executes a
total-war surrender settlement:

- the surviving side is recorded as the winner;
- every eligible city/territory controlled by the defeated side is transferred
  according to the existing total-war occupation/annexation transfer path;
- no partial war-goal selection is offered;
- all Army missions, occupation locks, and participant records are closed by
  the same end-war transaction used by other authoritative war endings.

If the defeated side has no eligible territory left, the normal extinction
cleanup path is used. A total war with no-force on both sides does not trigger
until one side has positive military potential or a deterministic tie-breaker
from the existing war-score winner is available.

## Data Flow and Error Handling

All new calculations are pure-rule helpers where possible. Runtime services
must tolerate destroyed kingdoms, stale Army IDs, missing reserve rows, and
mid-year world changes by treating invalid entries as zero and retrying the
next annual assessment. Settlement creation and total-war transfer remain
authoritative main-thread operations and are idempotent by war/participant ID.

## Tests and Acceptance

Source-guard and rules tests must prove:

1. Actor diagnostics do no task lookup when disabled and respect the per-frame
   sample budget when enabled.
2. MapMode fallback validation is bounded, mode-off is idle, and non-whitelist
   QuantumSprite map rendering is toggled only on mode transitions.
3. A retreating/empty Army with reserve or recruitable soldiers is not `NoForce`.
4. A side with zero active, reserve, and recruitable soldiers after year three
   selects surrender when the enemy has force.
5. Ordinary wars use the existing surrender settlement path.
6. Zhulu/total-war no-force surrender transfers all eligible defeated territory
   and closes missions/occupation state atomically.
7. Existing map modes, ordinary peace, and RTS simulation tests remain green.

Runtime verification uses the existing large-map save and a controlled war
scenario. With diagnostics disabled, the map mode must remain playable after
extended time; with diagnostics enabled, the diagnostic overhead must be
bounded and visible in the benchmark output rather than silently changing
simulation cadence.
