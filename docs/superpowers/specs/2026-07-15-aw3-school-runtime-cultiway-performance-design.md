# AW3 School Runtime and Cultiway Pathfinding Performance Design

**Status:** Approved by the user on 2026-07-15.

This specification supersedes the pathfinding architecture in
`2026-07-12-global-streaming-pathfinding-design.md` and the school-runtime architecture in
`2026-07-14-name-lineage-school-activity-root-fixes-design.md`. The creation-time naming
and lineage requirements in the latter specification remain in force.

## Goal

Remove the structural costs that make the historical-school system increase Actor and
`updateAge` time, restore a sustainable multi-generation school ecology, and replace the
partial AW3 pathfinding rewrite with the complete Cultiway movement and transport design.
The result must make pathfinding a measurable performance improvement rather than another
source of main-thread work.

This is a root reconstruction. It is not a collection of local throttles around the
current yearly scan, frame polling, full-map traversal cache, or permanent scholar job.

## Confirmed Runtime Evidence

The current development database at year 73 contains:

- 87 active school members and 99 affiliations;
- 250 arrivals, 85 disciple joins, 78 conversions, 68 debates, and 56 lectures;
- 52 non-canonical members that satisfy the current `>= 10` qualified-teacher rule;
- zero non-canonical members that satisfy the separate `>= 25` lecture rule;
- repeated conversions, including one actor with six sequential school memberships.

All 56 completed lectures were performed by canonical historical masters. The mismatch
between teacher and lecture thresholds therefore prevents later generations from teaching
after a canonical master dies. Conversion has no durable loyalty window, so members churn
between schools instead of maintaining a teaching lineage.

The current runtime also has these confirmed structural costs:

- `HistoricalSchoolRuntime.OnWorldYear()` runs synchronously inside the first
  `Kingdom.updateAge()` call of a new year;
- annual processing scans Xia cities, affiliations, memberships, offices, ledgers,
  teachers, debates, and conversion candidates before synchronously saving runtime state;
- an idle school frame allocates through `Stopwatch.StartNew()`, LINQ over activity
  dictionaries, and dirty-queue batch construction;
- debate activity processing repeatedly executes `Distinct`, `OrderBy`, `Where`, and
  `Count` over the same values;
- every affiliation save advances the global residence revision even when residence and
  influence presence did not change;
- reputation-only changes advance the same membership version used for structural and UI
  invalidation;
- the map snapshot worker runs while school map mode is inactive;
- travel and activity cleanup repeatedly assigns the permanent scholar citizen job;
- runtime operation keys, actor-year keys, death sets, and city venue caches are not all
  bounded by a lifecycle policy;
- the archive database uses `journal_mode=DELETE` and `synchronous=FULL`;
- 100 small `DELETE/FULL` transactions measured a 182.43 ms median, while
  `WAL/NORMAL` measured 1.66 ms and one batched `DELETE/FULL` transaction measured
  2.69 ms on the same machine.

The current AW3 pathfinder is not the complete Cultiway implementation:

- it pins a traversal generation and allocates a request, cancellation token, stream, and
  queue state before checking whether the actor already has the same request;
- it starts workers and creates a full-map traversal snapshot before the first real path;
- the initial traversal cache temporarily holds both a full flat snapshot and chunk arrays;
- it captures 64 live tiles in `ConsistencySweep` on every frame;
- every ordinary step calls the full vanilla `Actor.moveTo()` path;
- smooth movement repeatedly polls global dictionaries instead of retaining a ready cursor;
- transport uses a simplified per-actor taxi wrapper and omits Cultiway's reusable portal,
  boat-driver, passenger, dock-destruction, and water-connectivity lifecycle.

## Scope and Non-Goals

The change includes the school runtime, school ecology, school movement tasks, school
persistence, school snapshot invalidation, formal guest appointments, global actor
pathfinding, dock transport, diagnostics, and long-run acceptance tests.

The change does not preserve development saves. The mod is not released, so no load-time
world repair scan, schema migration, compatibility alias, or fallback state synchronizer is
allowed. A fresh world and a freshly created archive database are the acceptance baseline.

The change does not add the future academy building. It defines a venue-provider boundary
so an academy can later replace public fallback venues without changing activity logic.

Delivery has two ordered implementation slices: the event-driven school runtime first and
the complete Cultiway path/movement replacement second. Each slice has its own failing
tests and review checkpoint, but neither slice alone satisfies this specification; the
shared long-run performance acceptance is the completion gate.

## Authoritative Affiliation Model

The 41 living scholars whose vanilla city or kingdom differs from the school residence are
valid historical simulation state. They must not be repaired.

Four concepts remain separate:

1. `HomeKingdomId` and `HometownCityId` are immutable historical origin data.
2. `Actor.kingdom` and `Actor.city` are the actor's current formal vanilla affiliation.
3. `ResidenceCityId` is the current temporary school residence used for travel, teaching,
   influence, map display, and activity venue selection.
4. `ServiceKingdomId` records a current formal appointment and its host kingdom.

Ordinary travel changes only the school lifecycle, destination, and residence data. It
never calls `joinCity`, `joinKingdom`, `setCity`, or `setKingdom` and never rewrites the
historical home.

Lecture and debate planning use `ResidenceCityId`. Runtime completion verifies the actor,
membership, activity lease, living residence, claimed venue, and physical arrival at the
venue. It must not require `Actor.city == ResidenceCityId`.

### Formal appointment

A committed appointment is the only school workflow allowed to change vanilla formal
affiliation. After the durable affiliation/career tuple commits, a main-thread projection
opens a narrowly scoped transfer permit for the exact actor, host kingdom, and residence
city, then calls vanilla `joinCity(residence)`. The permit allows the nested `joinKingdom`
and `setCity` calls through the existing Harmony guards; no other actor or destination is
allowed through it.

If the durable commit succeeds but the live projection cannot complete, the appointment
stays in the bounded retry queue. Load recovery reads only that actor's committed service
tuple and reapplies the scoped projection. It does not scan or rewrite unrelated actors.

When service ends, the affiliation row and career row close atomically. The court office,
guest-service status, and `ServiceKingdomId` are cleared, but the actor remains formally
affiliated with the former host city and kingdom. Dismissal does not deport the actor or
restore the historical home. The school lifecycle becomes `Resident`, and later travel can
change only the temporary school residence again.

## Event-Driven School Runtime

### Year boundary

The `Kingdom.updateAge` hook performs one operation: enqueue or coalesce the current year
token. It performs no world scan, membership scan, LINQ query, SQLite command, actor task
change, map invalidation, or log emission.

`HistoricalSchoolScheduler` owns all school work. `MapBox.Update` first checks one pending
bit mask. When no work is pending, it returns without allocating. When work exists, it
drains deterministic work items with `Stopwatch.GetTimestamp()` and a 0.75 ms main-thread
budget. No `Stopwatch` object is created. At most one durable database batch and one visible
actor-state transition are processed in a frame.

If simulation speed advances another year before the previous maintenance finishes, the
scheduler coalesces arithmetic work such as ledger decay to the newest year. It does not
queue duplicate full-year scans. Work that represents a visible event keeps its original
year and idempotent operation key.

### Incremental indexes

`HistoricalSchoolRuntimeIndex` is updated by authoritative events:

- membership join, conversion, close, reputation change, and standing change;
- affiliation arrival, departure, presence change, service start, and service end;
- actor death and committed historical-master descent;
- city creation, destruction, kingdom transfer, and Xia-status change;
- institution creation or removal and court vacancy changes.

The index provides direct buckets by school, residence city, standing, eligible teacher,
traveller, service host, and living Xia city. Annual work consumes these buckets; it does
not rebuild them from all world actors or all membership rows.

Membership revisions are split into:

- structural revision: join, conversion, close, teacher link, standing, or lineage change;
- score revision: reputation or influence-weight change;
- residence revision: actual residence-city or influence-presence change;
- service revision: appointment or dismissal;
- lecture/debate revision: durable activity history only.

A state save with identical residence and presence does not advance the residence revision.
A score-only change does not rebuild lineage layout or the global resident index.

### Work fairness

Teacher, travel, conversion, debate, rediscovery, and guest-office candidates use stable
round-robin cursors. A low actor ID or dictionary insertion order cannot monopolize the
annual budget. Cursor state is small, persisted where gameplay-visible, and reset only on a
fresh world.

## School Standing and Ecology

Each active membership stores one explicit standing:

- `Member`;
- `Disciple`;
- `Teacher`;
- `Leader`;
- `CanonicalMaster`.

`CanonicalMaster` is reserved for a living historical master and remains limited to one
actor per school. `Leader` is the living school head shown in the school hierarchy. A
canonical master is leader while eligible; after death, the longest-serving qualified
teacher becomes leader. The historical master remains visible as a dead founder record but
does not occupy the live slot.

A direct or later disciple becomes teacher-eligible after at least three membership years
and reputation 10. The current contradictory reputation-25 lecture gate is removed.
Promotion is event-driven when membership age or reputation crosses the threshold. An
eligible teacher can lecture even when the original canonical master is dead.

Lecture allocation is fair across schools. Every school with a living, available teacher
receives one opportunity before another school receives its second opportunity. The global
maximum remains eight lectures per year, so fourteen eligible schools must all receive an
opportunity within two years unless every candidate is physically unavailable.

Conversion requires all of the following:

- no available teacher of the actor's current school in the residence for five years;
- meaningful rival influence rather than a non-zero rounding residue;
- a completed loyalty window;
- no active service, lecture, debate, voyage, or critical vanilla task;
- the global bounded conversion budget.

After joining or converting, an actor receives a twelve-year loyalty window. The actor
cannot convert again inside that window. Conversion resets the actor to `Member` unless a
durable teacher relationship justifies `Disciple`.

If a school has no living members but has a preserved work and an eligible reader,
rediscovery is scheduled from the preserved-work city index. It does not scan every school
member or every city resident. With a valid reader, rediscovery must complete within five
simulation years.

## Activities, Jobs, and Venues

Only a living canonical historical master retains the permanent scholar citizen job.
Ordinary members, disciples, teachers, leaders, and serving officials keep their normal
vanilla citizen job.

Travel, lecture, debate, and short school work use `Actor.scheduleTask(taskId, tile)`. A
task lease records actor ID, activity ID, expected school, venue, and expiry. Completion,
interruption, death, invalid membership, or timeout releases the lease. No cleanup path
calls `setCitizenJob` for a non-canonical scholar.

A serving scholar can teach when no court duty, combat, voyage, or other critical task is
active. Formal service is not itself a blanket lecture exclusion.

The venue provider chooses, in order:

1. a future academy building that advertises a compatible school-work slot;
2. an unoccupied stable public tile inside the residence city;
3. a bounded local fallback near the actor that is still inside the residence city.

The city center is never the universal fallback. Venue claims are indexed by activity and
city, released on every terminal path, and pruned when a city dies. Lectures do not summon
all disciples. Recruitment uses at most 48 frozen candidate IDs from one bounded city
sample and validates them only after lecture completion.

After a scheduled school task, the actor returns to its vanilla job and local movement.
Canonical masters use a bounded local-wander task around their residence when idle. Path
failure, debate waiting, lecture waiting, or border arrival cannot leave a scholar in a
permanent standstill state.

## Persistence Architecture

Every archive connection is configured immediately after opening with:

- `PRAGMA journal_mode=WAL`;
- `PRAGMA synchronous=NORMAL`;
- a bounded busy timeout;
- an explicit automatic-checkpoint size.

`HistoricalSchoolWriteBuffer` groups independent school events into one ordered transaction
per active persistence frame. Atomic tuples such as membership conversion, debate plus
ledger updates, appointment plus career, and dismissal plus career remain single logical
operations inside that transaction. Runtime projections execute only after the containing
transaction is proven committed or replayed.

Ledger decay is lazy. Reading or writing a ledger computes decay arithmetically from
`LastDecayYear`; no annual statement updates every ledger row. Only ledgers whose effective
value or visible snapshot changes are written and invalidated.

Saving first flushes committed school operations, performs a passive WAL checkpoint, and
uses SQLite backup to create the save copy. Unfinished movement is not teleported or
settled merely because a save begins. Unknown commit outcomes stay in a bounded retry queue
and are resolved by operation key.

## Snapshot and UI Invalidation

City school snapshots are rebuilt only when a relevant city is dirty and one of these is
true:

- school map mode is visible;
- the school window requests that city;
- a court or AI consumer explicitly requests a fresh snapshot.

An inactive map mode does not rebuild one city per frame. The bottom bar is not polled when
the mode is inactive and no initialization is pending.

The dirty queue exposes allocation-free `TryDequeue` operations. Empty processing does not
construct a `List`, array, LINQ iterator, or retry batch. Batch context loads ledgers once
for the actual non-empty city set.

Residence and score events invalidate only affected old/new cities. School-roster caches
are keyed by the selected school's structural, residence, score, and activity revisions,
not by one global membership version.

## Complete Cultiway Pathfinding Replacement

The authoritative reference is:

- `Cultiway-Reborn-master/Source/Core/Pathfinding/`;
- `Cultiway-Reborn-master/Source/Patch/PatchAboutPathfinding.cs`;
- `Cultiway-Reborn-master/Source/Utils/PriorityQueuePreview.cs`.

The existing MIT notice and per-file attribution remain. `THIRD_PARTY_NOTICES.md` is
updated to describe the completed port rather than the removed immutable full-map cache.

### Request and worker lifecycle

AW3 retains only its Cultiway-owner arbitration around the port. When AW3 owns movement:

- `Actor.goTo` validates the actor and checks same-target/options reuse before allocating a
  request, stream, cancellation token, or queue item;
- workers start lazily on the first non-reused request;
- one indexed actor slot owns the latest request, so rapid retargeting cannot leave an
  unbounded queue of cancelled tasks;
- cancelled work is discarded before entering the generator;
- world generation is captured by ID so stale output from a cleared world cannot be
  consumed;
- world clear, actor disposal, death, and ownership yield cancel requests and release
  worker resources deterministically;
- a late real Cultiway Harmony owner stops AW3 workers and movement interception without
  unpatching the other mod.

### Search

The port preserves Cultiway's path semantics:

- direct local search followed by corridor-bounded long fallback;
- multi-label dominance over time, stamina, health, and risk;
- Cultiway node limits, hazard costs, cancellation checks, and portal estimates;
- live `TileTraversalInfo` lookup as used by Cultiway, followed by main-thread step
  revalidation;
- no AW3 full-map traversal snapshot, copy-on-write generation, dirty-chunk rebuild, or
  fixed `ConsistencySweep`.

Cultivation and ECS fields are removed. The core search workspace uses worker-local reusable
buffers for nodes, heap storage, tile information, and label slots. This preserves Cultiway
route decisions while avoiding one managed `PathNode` allocation per expanded label. After
warm-up, search memory is capacity reuse rather than repeated large object graphs.

### Movement

The Harmony movement chain ports Cultiway's `ReadyPathCursor` and optimized smooth movement.
One dictionary lookup opens a cursor; multiple tile boundaries in the same movement update
consume that cursor directly.

Safe ordinary ground steps use Cultiway's fast tile transition. Vanilla `Actor.moveTo()` is
called only for boats or steps whose tile action or flora law requires its full side
effects. Fire, flora, step actions, damaged terrain, movement batches, tile dirtiness,
facing, and target calibration retain the selective side-effect replay from Cultiway.

Every consumed step is revalidated on the main thread. Invalid or stale steps enter bounded
Cultiway recovery; exhausted recovery cancels the current behavior. Ordinary actors are
never teleported and do not silently fall back to vanilla global path generation.

### Docks and boats

Cultiway portal definitions are adapted to lightweight AW3 wrappers around vanilla dock
buildings. No Cultiway ECS component or custom building is required.

The port includes:

- dock registration and removal from building state changes;
- water-connectivity rebuild only after relevant dirty-region events;
- reusable passenger requests across compatible dock routes;
- boat-driver request selection;
- passenger loading, ready-step consumption, sailing, unloading, and land-route recovery;
- dead passenger, dead boat, and destroyed dock cleanup;
- request repair to the next valid dock when possible;
- complete world-clear and ownership-yield cleanup.

The historical-school timed-voyage fallback remains a school-level last resort after
bounded physical transport failures. It does not alter global pathfinding for other actors.

## Bounded Runtime State

Every runtime collection has an owner and a pruning rule:

- operation and actor-year keys retain only the current and previous simulation year;
- activity and venue entries exist only while queued, active, or awaiting durable commit;
- candidate and venue tile caches are limited to living cities and use a fixed-capacity LRU;
- handled-death state is removed after the matching durable death and successor operation;
- travel reservations exist only for active travel or voyage state;
- path slots exist only for actors with a pending, streaming, recovery, or portal request;
- portal requests are removed on completion or cancellation;
- diagnostics use fixed counters and bounded exception samples.

Fresh-world clear empties all school, snapshot, path, portal, retry, and UI state.

## Failure Semantics

- Durable state is authoritative; UI, jobs, tasks, statuses, logs, and vanilla affiliation
  are projections applied only after a proven commit.
- Every operation key is idempotent. Replaying a commit cannot recruit, convert, lecture,
  debate, appoint, dismiss, or move an actor twice.
- Clean persistence failure releases reservations and task leases.
- Unknown persistence outcome retains one bounded retry entry with exponential backoff.
- A failed school stage does not prevent unrelated queued stages from advancing.
- Path generator exceptions are reported through a bounded main-thread diagnostic queue.
- No per-frame diagnostic log is allowed.

## Diagnostics

School counters expose:

- year-token enqueue time;
- queued and completed work by stage;
- main-thread scheduler time and allocations;
- SQL batches, statements, commit time, and retry count;
- snapshot rebuild count and cause;
- active lectures, debates, travel tasks, and task leases;
- membership, teacher, leader, conversion, and rediscovery counts by school;
- sizes of every bounded cache.

Path counters expose:

- generated, reused, superseded, cancelled, completed, and failed requests;
- pending actor slots and worker utilization;
- first-step latency, expanded nodes, fallback searches, and recovered paths;
- cursor-consumed steps, fast steps, and vanilla `moveTo` steps;
- active portal requests, waiting passengers, drivers, and repaired routes;
- worker workspace capacity and exceptional allocations.

Counters are queried by tests or emitted only on an explicit diagnostic request or threshold
breach.

## Verification and Acceptance

### Automated structural and rule tests

Tests must fail before implementation and prove:

- year enqueue performs no annual work or SQL;
- idle school processing allocates zero bytes after warm-up;
- residence, presence, score, structure, service, and activity revisions invalidate only
  their intended consumers;
- a foreign resident can lecture without changing vanilla affiliation;
- ordinary travel cannot obtain a formal transfer permit;
- committed appointment performs the exact scoped formal transfer;
- dismissal clears service but retains host vanilla affiliation;
- teacher promotion, leader succession, fair lecture rotation, conversion loyalty, and
  rediscovery deadlines;
- one live canonical master per school;
- activity and cache bounds plus fresh-world cleanup;
- `WAL/NORMAL` connection configuration and save checkpoint ordering;
- request reuse occurs before request/task/token allocation;
- lazy worker start, bounded latest-request slots, cursor consumption, recovery limits, and
  world-generation rejection;
- obstacle, diagonal, water, lava, health, stamina, corridor fallback, portal, dock repair,
  boat loading, and unloading behavior;
- no full traversal snapshot, consistency sweep, permanent non-canonical scholar-job
  restoration, or bad source guard remains.

Debug and Release builds, pure rule tests, source guards, localization checks, and
`git diff --check` must all pass.

### Fresh-world 50/100/200-year ecology runs

Using fixed seeds and recorded world settings, capture checkpoints at years 50, 100, and
200. Acceptance requires:

- no school ever has more than one live canonical master;
- every school with at least one living eligible teacher has a leader within one annual
  scheduler cycle;
- every continuously eligible school receives a lecture opportunity within two years;
- no actor converts twice within twelve years;
- an extinct school with a preserved work and valid reader is rediscovered within five
  years;
- no school remains canonical-master-only after qualified disciples have reached the
  three-year teacher threshold;
- activity, retry, venue, candidate, death, operation-key, path, and portal collection sizes
  stay within their documented bounds;
- no scholar remains indefinitely at a city center or border after its school task ends.

### Performance runs

Record the current HEAD baseline before replacing behavior, then repeat the same fresh-world
seed, save, camera state, speed, and actor population after implementation.

Acceptance requires:

- the school `Kingdom.updateAge` hook performs only year-token coalescing, with no school SQL
  and a 0.10 ms or lower p95 on the test machine;
- idle school frame processing reports zero managed bytes after warm-up;
- active school main-thread work stays within the 0.75 ms frame budget and clears a normal
  annual backlog within 120 frames;
- no inactive map-mode snapshot rebuild occurs;
- 100 representative school writes use batched `WAL/NORMAL` transactions and, across at
  least five runs, do not regress from the measured 1.66 ms median by more than 25 percent
  on the same machine;
- pathfinding creates no full-map traversal allocation and performs no fixed per-frame tile
  sweep;
- 10,000 repeated same-target requests after the first create no new request, task,
  cancellation token, or worker item;
- pending path work never exceeds the number of actors with active path slots plus active
  workers;
- at least 90 percent of ordinary safe ground steps use the fast Cultiway transition in the
  benchmark scenario;
- with 500 concurrent walkers, main-thread Actor movement p95 is at least 20 percent lower
  than current HEAD and is not worse than the vanilla-path baseline;
- total Actor and `updateAge` p95 at years 100 and 200 is lower than current HEAD, with no
  new long GC spike attributable to school or path runtime.

Compilation and narrow rule tests are not sufficient evidence for these runtime claims.
The final completion report must include the measured baseline and post-change numbers, and
must identify any live-game check that could not be executed.
