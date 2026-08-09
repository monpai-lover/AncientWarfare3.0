# RTS Synthetic Mobilization Design

## Goal

Replace AW3 wartime recruitment of real residents with bounded synthetic levy
mobilization, remove AW3's global restriction on vanilla recruitment, and make
RTS command assignment correct under both native and large-step scheduling.

The result must satisfy these player-visible rules:

- ordinary residents become warriors only through WorldBox's vanilla
  recruitment system;
- war preparation never scans, converts, removes or changes the profession of
  real residents;
- mobilization creates a synthetic levy equal to the city's real population
  snapshot multiplied by the active conscription-law percentage;
- synthetic levies join the city's selected general's ordinary army even when
  that exceeds the vanilla warrior-slot limit;
- every surviving synthetic levy is removed in bounded batches after its war
  ends, including decorated or high-kill soldiers;
- synthetic levies cannot create descendants or participate in civilian life;
- both sides receive useful RTS orders promptly, without remaining in the UI
  fallback state "waiting for orders";
- large-step scheduling produces the same logical RTS progress as the same
  number of ordinary simulation passes.

## Scope Boundaries

This change replaces AW3 preparation and wartime replenishment paths that turn
ordinary residents into temporary soldiers. It does not replace vanilla's own
autonomous recruitment logic, alter vanilla recruitment eligibility, or add a
new player-facing army-management window. AW3 permanent-army bookkeeping may
adopt warriors already recruited by vanilla, but it may not independently
scan and promote an ordinary resident into a warrior.

Explicitly scripted special systems such as slave vanguards, uprisings and
feudatory garrisons retain their own rules unless they currently call the old
ordinary-resident temporary-levy pipeline. Those call sites must not be
silently redirected into this mobilization ledger.

The existing `City.tryToMakeWarrior` prefix currently blocks vanilla calls
outside `MilitaryRecruitmentScope`. That global block is removed. Scoped
capacity bypass remains available only to explicit special creation paths,
including synthetic levies; it no longer acts as the gate that enables normal
vanilla recruitment.

## Mobilization Ledger

Each participating city owns one persisted mobilization record per active war.
This record is the only runtime ledger for both initial mobilization and
wartime replacement. `CityReservePoolService` does not continue as a parallel
reserve ledger; an old reserve snapshot may be read once for migration and is
then discarded.
The record contains:

- war, kingdom and source-city IDs;
- the city population snapshot used at mobilization time;
- the conscription-law percentage used for that snapshot;
- initial levy quota;
- initial deployment count;
- remaining numeric replacement reserve;
- cumulative replacements created;
- selected target-army ID and its intended kingdom;
- lifecycle state: `Pending`, `Mobilizing`, `Active`, `Demobilizing` or
  `Complete`;
- deterministic spawn and demobilization cursors.

The snapshot reads the vanilla city's existing population statistic in O(1)
and subtracts synthetic actors already known to the mobilization ledger. It
does not enumerate ordinary resident Actors. Later synthetic spawns cannot
increase the same war's quota. The quota is:

`floor(real population snapshot * conscription percentage / 100)`

The existing law values of 30, 50, 70 and 100 percent are authoritative. A
50-percent law therefore creates a target levy equal to half of the snapshot,
without subtracting vanilla warriors or consulting `warrior_slots`.

Initial mobilization creates up to the full quota. A separate numeric
replacement reserve starts at the same quota. Casualties consume this integer
reserve and enqueue replacement demand; no resident or reserve Actor is
searched for replenishment. Cumulative replacement spawns cannot exceed the
recorded reserve. Ending the war clears unused numeric reserve after the
surviving synthetic actors have entered demobilization; it does not modify the
city's real population because no real resident was consumed.

Records are created from bounded city work queues. Reading one city's
population statistic is one work item; cities and wars are traversed with
persistent cursors so many kingdoms cannot produce a one-frame scan. A city is
snapshotted at most once for the same war and law decision. Law changes after
mobilization apply to later wars, not retroactively to the active record.

## Army Selection And Spawn

Mobilization targets one ordinary army associated with the source city. The
selection order is deterministic:

1. the source city's live ordinary army whose captain is a live, non-synthetic
   general of the same kingdom;
2. another live ordinary army anchored to the city and led by such a general;
3. a normal army created through the existing AW3/vanilla army-creation path
   for a preselected eligible real general.

A synthetic levy can never become the fallback captain. If no eligible
general exists, the record remains `Pending` and retries through a bounded
queue; it does not spawn an uncommanded crowd. If the selected general dies or
the army is destroyed, remaining demand is rebound to another eligible army
through the same rules. Existing live synthetic members are moved in bounded
batches and RTS reconciliation is notified of the roster mutation.

Spawning reuses the proven sequence
`createNewUnit -> mark synthetic -> joinCity -> makeWarrior -> AddToArmy`.
The synthetic marker is written before any gameplay system can observe the
new actor. The dedicated recruitment scope bypasses `warrior_slots`, so the
entire quota can join the selected general's army. Creation is batch-limited
and resumable; failure of one actor returns only that uncreated unit to pending
demand and never rolls back already valid actors.

## Synthetic Actor Restrictions

Synthetic levy status is a hard lifecycle identity, not a temporary AI hint.
It survives saves and cannot be cleared by military success, profession
changes or army reassignment. The old promotion path that turned decorated
synthetic soldiers into permanent residents is removed.

The only permitted activity classes are:

- combat, target acquisition and combat positioning;
- army movement, rallying, formation movement and transport;
- eating and drinking needed to avoid starvation;
- sleeping or resting;
- necessary healing and recovery;
- bounded military idle while awaiting a valid RTS mission.

All other jobs and task transitions are rejected at the shared actor-job
selection boundary. This includes civilian work, construction, harvesting,
trade, social visits, celebration, marriage, romance, pregnancy, child care,
school, court office, city leadership, kingship, clan creation or membership,
inheritance, personal chronicle generation and autonomous migration. A
rejected task returns the levy to its army's current military task or bounded
military idle; it must not spin job selection every frame.

Descendant prevention is defense in depth:

1. synthetic actors cannot select relationship, marriage or reproduction
   tasks;
2. spouse and mate eligibility rejects either synthetic participant;
3. every direct pregnancy, egg, birth and offspring-creation boundary rejects
   a synthetic parent even if another mod bypasses jobs;
4. synthetic actors never enter clan or lineage parent-edge archival paths;
5. load reconciliation removes any invalid spouse or pregnancy state attached
   to a synthetic actor before simulation resumes.

These checks guarantee that a synthetic levy cannot leave a child even when a
save is loaded mid-war or another system directly requests reproduction.

## Casualties And Demobilization

Synthetic deaths decrement live deployment accounting exactly once and add no
ordinary personal-history or lineage record. Replenishment demand is based on
the difference between the record's active target and current live synthetic
count, capped by remaining numeric reserve. It is fulfilled in bounded spawn
batches into the fixed or rebound general's army.

When the associated war ends, changes identity, or no longer includes the
recorded kingdom, the record enters `Demobilizing`. All surviving synthetic
actors belonging to that record are queued by stable actor ID and removed in
bounded batches through the existing no-personal-history removal path. Rank,
kills, traits and decorations never exempt an actor. Demobilization continues
across frames and save/load until the live count reaches zero, then the record
is marked `Complete` and its unused reserve is discarded.

City capture does not transfer a synthetic levy to the conqueror. Levies stay
bound to their recorded kingdom and war while that side remains active;
otherwise they demobilize. Kingdom destruction immediately starts bounded
demobilization. World unload clears runtime queues, while persisted records
and actor markers rebuild those queues after load.

## RTS Lifecycle Repair

"Waiting for orders" remains presentation text only. It must not represent a
stable RTS lifecycle state.

Every RTS logical pulse performs a bounded reconciliation step before mission
planning. It discovers eligible ordinary armies that are missing lifecycle or
projection records, including newly created armies, newly mobilized armies,
late war participants, replacement captains and armies restored from a save.
Discovery uses persistent war, kingdom and army cursors and does not scan all
armies in one frame.

War start and participant changes enqueue both attacker and defender command
work at high priority. The queue alternates sides for the same war. Within at
most two RTS logical pulses, each side that owns at least one valid led army
must have at least one army with a concrete rally, defend, advance or attack
mission. Ordinary lower-priority front planning continues afterward. A side
with no valid led army receives an explicit bounded wait reason and is retried
when roster or captain state changes, rather than being polled every frame.

The director retains bounded `Wars -> Armies -> Fronts -> Publish` work, but a
new-war fast path creates minimal lifecycle and mission projections before the
full front refinement completes. The fast path does not invent targets: it
uses the same legal enemy, territory and reachability rules as normal mission
planning.

Objective handoff is a transactional state change. The controller may set its
completion latch and unregister the stall watchdog only after a replacement
mission is published, or it must invalidate the old controller mission and
remain in a bounded reassignment queue. Retaining the same strategic mission
after a handoff clears the completion latch, resets route ownership and
registers the watchdog exactly as a save/load rehydration does. Therefore an
army cannot require reloading the save to resume movement.

Formation, movement and roster reconciliation no longer stop permanently at
the first 128 members. Each army stores a member cursor; every work item
processes a bounded slice and resumes until all current members have been
covered. Roster version changes restart or safely rebase the cursor without a
full-list allocation.

## Large-Step Scheduling

RTS progress is keyed by a monotonically increasing logical simulation-pass
token. Exactly one RTS logical pulse may consume each token.

Large-step mode invokes the bounded RTS pulse for every internal logical pass,
including intermediate passes that currently skip the final AW3 authority
stage. The complete AW3 authority cycle remains cooperative and runs only at
its intended boundary; it does not run all authority services for every
internal pass. Native mode consumes the same token at its corresponding
boundary.

Scheduler ownership (`Native` or `AW3`) is frozen when the world session
starts. Both entry paths share one exact-once gate, so a runtime setting change
cannot execute RTS twice or omit a pass. Paused, replica and world-loading
passes follow existing authority rules and do not mutate authoritative RTS or
mobilization state. The token and in-progress cursors are restored or rebuilt
deterministically after load.

Logical equivalence means that N admitted large-step passes produce N bounded
RTS pulses in the same order as N ordinary passes. It does not require all
queued RTS work to finish in one rendered frame.

## Persistence And Multiplayer

The host is authoritative for snapshots, quota calculation, synthetic spawn,
army binding, mission assignment, casualties and demobilization. Replicas
apply resulting actor, army and projection changes and never run independent
mobilization scans.

Persisted city-war records and actor markers are the source of truth. On load,
reconciliation:

- validates war, kingdom, city and army IDs;
- reconstructs live counts from marked actors in bounded slices;
- rebinds valid armies or queues demobilization;
- clamps replacement reserve and cumulative counters to the original quota;
- resumes partial spawn and demobilization cursors idempotently;
- removes stale synthetic actors whose record cannot be recovered.

No synthetic actor may be reclassified as an ordinary resident merely because
its record or army is temporarily unavailable.

## Performance Budgets

All population snapshot, spawn, reconciliation, formation and demobilization
work is cooperative. No work item may scan a whole kingdom or city in one
frame. Initial implementation constants are conservative and independently
tunable:

- no ordinary resident Actor inspection during mobilization;
- at most 8 synthetic actors spawned per mobilization work item;
- at most 8 replacement actors spawned per work item;
- at most 16 synthetic actors removed per demobilization work item;
- at most 128 army members processed per formation or roster slice;
- at most one bounded RTS pipeline step per logical simulation-pass token.

Queues are fair round-robin by war and city, with new-war attacker/defender
command work as the only temporary priority lane. Diagnostics are sampled and
respect the player's diagnostic-output setting.

## Failure Handling

- A failed actor creation preserves remaining demand and retries with backoff.
- Missing templates or generals keep mobilization pending without scanning the
  city every frame.
- Invalid armies trigger deterministic rebind; they never strand synthetic
  actors in civilian jobs.
- Duplicate war-start or load events are idempotent by city-war record key.
- Ledger underflow and overflow are clamped and reported through sampled
  diagnostics without creating additional soldiers.
- A fault in one city or war work item advances the fair queue and cannot stop
  command assignment for every other war.

## Verification

Rules and source guards must prove:

- vanilla `City.tryToMakeWarrior` is no longer globally blocked by AW3;
- no preparation or replenishment path scans real residents for conversion;
- quota arithmetic uses the real-population snapshot and 30/50/70/100-percent
  law values without `warrior_slots` or existing vanilla-warrior subtraction;
- synthetic actors can exceed vanilla army capacity and always join a valid
  real general's ordinary army;
- no decorated synthetic actor is promoted or retained after war;
- every non-whitelisted task is rejected and all reproduction entry points
  reject synthetic parents;
- synthetic actors create no clan, parent edge or personal chronicle data;
- casualties consume only the numeric replacement reserve and cannot replenish
  beyond its initial cap;
- city capture, general death, army destruction, kingdom destruction and
  save/load converge without orphaned synthetic actors;
- attacker and defender each receive a valid first mission within two logical
  RTS pulses when each has an eligible army;
- newly created and late-joining armies receive lifecycle records without a
  30-second polling dependency;
- armies larger than 128 members are fully processed across cursor slices;
- N native logical passes and N large-step logical passes consume identical RTS
  tokens and produce equivalent deterministic projections;
- host and replica do not both execute authoritative mobilization;
- production compilation, rules tests, adversarial RTS simulations and
  Cultiway scheduler non-regression guards pass.

Runtime verification uses large cities, several simultaneous wars and armies
above 128 members. Frame diagnostics must show bounded mobilization and RTS
phases with no recurring whole-city or whole-kingdom spike, while post-war
inspection confirms zero surviving synthetic actors and zero descendants.
