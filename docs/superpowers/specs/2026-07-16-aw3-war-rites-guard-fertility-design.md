# AW3 War Rites, Elite Army, Temporary Slave Army, Guard Priority, And Xia Fertility Design

## Goal

Refine five related balance rules without adding unbounded or unconditional
per-frame world scans:

1. Every ordinary kingdom keeps a small high-quality standing army in peace
   and raises temporary levies only after receiving a war declaration or
   being pulled into an unannounced system war.
2. Slave armies exist only as temporary wartime vanguards and perform no
   peacetime formation, fill, target, or repair maintenance.
3. A kingdom that cannot maintain that standing-army core must not form or
   reinforce a royal guard.
4. Xia long-term reproductive capacity must be twice the human baseline.
5. A Xia kingdom, or a foreign kingdom that has adopted Xia rites, must send
   a declaration before a deliberate war and give the defender time to
   recruit and deploy its armies to the threatened frontier.

Old-save compatibility is not required. Existing runtime safety, vassal,
alliance, casus-belli, war-goal, occupation, and royal-asylum rules remain in
force.

## Confirmed Current Behavior

- Original `City.setCitizenJob` recruits the first eligible citizen it checks
  and keeps recruiting until all `warrior_slots` are filled. It has no
  peacetime elite-core or candidate-quality concept.
- `RoyalGuardService` can recruit ordinary adult men without checking whether
  the kingdom has any sustainable normal standing-army core.
- Existing royal guards are detached special-army units, so they must not be
  counted as ordinary standing-army strength.
- AW3 retirement currently considers every supported living warrior from
  `Actor.updateAge`. A temporary levy therefore needs an explicit early
  exclusion or it can incorrectly become a retired veteran during a war.
- Current slave armies are persistent and can exist once per city with no
  kingdom-wide limit. Every city periodically performs slave counting,
  captain selection, a 32-resident fill scan, identity repair, and naming.
- During war, each maintained slave army can also scan every enemy kingdom's
  actors to find a nearest target. The cost therefore scales with cities,
  slave armies, and enemy population instead of active war fronts.
- Baseline humans receive `birth_rate=3` from the
  `reproduction_sexual` subspecies trait. Xia adds another 4 and therefore
  already has an effective value of 7. Increasing this value further has
  sharply diminishing litter-size returns because `BabyMaker` stops its
  extra-birth loop after the first failed chance.
- Human `offspring` is 5. Xia currently adds only 1, producing a final value
  of 6. This is the actual long-term population cap that remains close to the
  human baseline.
- AW3 deliberate wars already pass through the repeatable
  `aw_decision_declare_war` decision. The decision reaches completion before
  `WarDecisionService` creates the real `War`.
- Writing `beh_tile_target` once is not a durable peacetime deployment order;
  normal actor AI can replace it on the next behavior cycle.

## Considered Approaches

### Permanent force model

Keeping every city permanently at 100 percent of `warrior_slots` leaves no
meaningful wartime mobilization and consumes civilians in peace. A pure
combat-score threshold produces unstable army sizes when population quality
changes. The selected model uses a fixed elite core: 30 percent of each
city's ordinary establishment, with at least one standing soldier when the
city has positive slots. The remaining establishment is filled by temporary
levies only while preparing for or fighting a notified defensive war, or
after a sudden system war has already started.

### Slave-army lifecycle

Keeping one persistent slave army per city preserves the current performance
problem. Raising 80 percent of a kingdom's entire slave population would make
pathfinding and actor update cost grow without a hard bound. The selected
model permits one temporary slave army per kingdom, caps it at 25 members,
and requires at least 80 percent of its formed roster to be slaves. It is
created only for an active notice or war and is completely disbanded after
the kingdom's final military emergency ends.

### Notification only

Emit a world-log message when the attacker starts its declaration decision.
This is cheap, but the defender gains no real preparation and its armies can
remain deep inland. It does not satisfy the requested behavior.

### Decision-integrated notice and temporary deployment job (selected)

Use the existing declaration decision as the preparation window. Persist a
notice on the attacking kingdom, mobilize the defender in bounded annual
batches, and place non-guard armies under a cancellable deployment job until
they reach assigned frontier cities. This preserves all existing war-goal
creation and gives the defender visible, effective preparation without
creating enemies before the war.

### Immediate full-army teleport or direct actor targets

Teleporting armies avoids pathfinding but breaks simulation continuity.
Issuing one-time actor targets looks more natural but is not durable and
causes repeated path requests when maintenance tries to repair it. Neither is
selected.

## Peacetime Elite Standing Army

### Establishment and scope

The force model applies to every living, non-neutral ordinary kingdom. Rebel
and other system-created military states keep their dedicated mobilization
rules. Xia rites control advance notice, not access to the force model.

For each living city:

- ordinary establishment is `max(0, status.warrior_slots)`;
- peacetime core is zero when establishment is zero;
- otherwise peacetime core is
  `max(1, ceil(ordinary establishment * 0.30))`;
- only living warriors in the original `City.getArmy()` count toward the
  core;
- temporary levies and all AW3 role armies are excluded from the core count.

The normal city recruitment path is capped at the peacetime core unless the
city is under an active defensive mobilization. Existing ordinary warriors
above the core are ranked weakest first and at most two are returned to
civilian life per maintenance pass; this is a force reduction, not retirement,
and does not grant veteran state.

### Elite selection

Peacetime recruitment is controlled by a city maintenance service instead of
accepting the first citizen visited by `City.setCitizenJob`. It reuses all
original `City.checkCanMakeWarrior` eligibility and AW3 exclusions, then
ranks eligible residents by a shared deterministic military score:

`damage + warfare * 2 + health * 0.1 + armor * 2 + speed * 0.25`

Lower actor ID is the final tie-breaker. The service scans at most 64
residents and appoints at most two standing soldiers per city maintenance
pass, preserving a cursor between passes. When the core is already full, a
stronger candidate may replace the weakest ordinary standing soldier, but at
most one replacement occurs per pass. This lets the core converge toward the
city's best available soldiers without an annual full-population sort or churn.

Kings, heirs, city leaders, active officials whose office forbids ordinary
service, royal refugees, slaves barred from ordinary service, protected
historical masters, retired veterans, royal guards, and existing special
soldiers remain ineligible. Fief, border, slave, rebel, and royal-guard
services retain their own explicit recruitment contexts and are not blocked
by the peacetime cap.

## Royal Guard Priority

### Ordinary-army readiness

Readiness is computed at kingdom maintenance time from living cities only:

- required strength is each city's peacetime core, not its full wartime
  `warrior_slots`;
- filled strength is the sum of units in that city's original `City.getArmy()`;
- temporary levies do not contribute to filled strength;
- an absent or dead city army contributes zero;
- AW3 role armies, including royal guards, slave armies, and border armies,
  never contribute to ordinary standing-army strength;
- filled strength is clamped to each city's core so an overfilled
  city cannot hide another city's shortage in the national total.

A kingdom is ready only when every positive peacetime core is filled. A
kingdom with no positive core is not ready to create a royal guard. The
calculation iterates cities and direct army counts; it does not scan actors or
all world armies.

### Maintenance behavior

- If no active guard exists and the peacetime core is under establishment,
  guard formation returns before candidate collection and army creation.
- If guards already exist, they remain in service during later standing-army
  losses. Maintenance still repairs their identity, captain, job, roster, and
  detached army, but performs no candidate search, formation, or reinforcement.
- Guard formation and reinforcement are also suspended during active
  defensive mobilization and war; temporary levies can never satisfy the
  guard gate.
- Once the peacetime core is full again in peace, normal bounded guard recruitment
  resumes.
- Republic, rebel, extinction, succession, dismissal, and hard maximum rules
  keep their existing precedence.

This prevents war losses from causing repeated guard dissolution and
reappointment, guarantees that a kingdom cannot have a newly formed guard but
no ordinary army, and preserves the requested peacetime elite force.

## Xia Fertility

Xia keeps its current effective `birth_rate` behavior: the inherited sexual
reproduction value of 3 plus the Xia genome delta of 4. The Xia genome
`offspring` delta changes from 1 to 5, producing a final value of 10 against
the human value of 5.

This makes long-term reproductive capacity exactly twice the human baseline
without increasing the already high multiple-birth loop or changing noble and
heir-urge trait bonuses. Comments and rule tests must describe the actual
subspecies contribution so future changes do not treat the human baseline as
zero again.

## War-Rite Applicability

A notice is required only for a deliberate AW3 declaration when the main
attacker is either:

- a Xia kingdom; or
- a non-Xia kingdom whose Xiaization level includes adopted Xia rites.

The following do not send advance notice:

- independence wars;
- mandate-rebel, general-rebellion, and fief-independence wars;
- system-forced wars;
- joining an ally's or suzerain's existing war;
- a war that already exists between the pair.

Claim, core-recovery, restoration, mandate, mandate-conquest, force-vassal,
and no-casus-belli declarations do use the notice when the attacker follows
Xia rites. A no-casus-belli war is still dishonorable in its political cost,
but it is not an unannounced attack.

## Notice Lifecycle

### Issuing the declaration

The notice is issued when `aw_decision_declare_war` becomes the current
decision, not while it is merely waiting in the decision queue. It stores a
stable signature containing attacker, defender, goal, target city, and issue
year. It also stores:

- `notice_year`;
- `earliest_war_year = notice_year + 1`;
- `forced_war_year = notice_year + 3`;
- whether the issue log and history records were written.

Current-decision fields and `KingdomDecisionQueueItem` both carry this state.
Preemption and resumption therefore do not duplicate the notice or reset its
deadlines. Runtime indexes are rebuilt from kingdom decision state after a
load or archive switch.

### Declaration gate

The declaration decision may reach full progress, but real war creation is
held until:

- the current year is at least `earliest_war_year`; and
- every living non-guard army that still has warriors is ready and has reached
  its assignment, or
  the current year is at least `forced_war_year`.

Thus a defender always receives at least one full year and deployment can
delay war for at most three years. Cross-island path failure, insufficient
population, destroyed armies, or unreachable cities can never postpone war
forever. While held, progress remains at the decision cost and no additional
political points are spent. The three-year deadline limits deployment delay;
it does not bypass the attacker's own declaration progress. If political
progress takes longer than three years, deployment cannot add another delay
once the decision finally reaches full progress.

Immediately before war creation, AW3 revalidates the pair, alliance and
vassal restrictions, casus belli, source claim/core/restoration record, target
city, and war goal. Failed validation cancels the declaration without
consuming its claim.

### Cancellation and completion

The notice and its pre-war deployment assignments close when:

- the real war starts;
- the declaration is canceled or replaced without being queued;
- attacker or defender becomes invalid or loses all cities;
- the kingdoms join the same alliance;
- the war pair or selected goal becomes invalid.

On real war start, actors leave the deployment job before ordinary war AI is
allowed to choose combat targets. Temporary levy records transfer atomically
from the notice signature to the new war ID; the soldiers remain mobilized
until their kingdom has no remaining war or notice that requires them. On
cancellation, notice-only levies demobilize and surviving permanent armies
return to their valid home city when one exists; otherwise their normal job
selection resumes in place.

## Temporary Levy Mobilization

The defender is not made hostile during the notice period. Receiving a valid
notice activates temporary levy authority. Once per kingdom year, a bounded
preparation pass:

1. identifies ordinary city armies below their full wartime
   `warrior_slots` establishment;
2. prioritizes threatened frontier cities, then other cities;
3. visits at most four cities and scans at most 64 residents in total using
   stable city and actor cursors;
4. uses original `City.checkCanMakeWarrior` eligibility plus a strict
   enlistment age below 65;
5. recruits at most eight temporary levy warriors for the kingdom that year;
6. never recruits royal refugees, protected historical masters, slaves barred
   from ordinary service, kings, heirs, leaders, or existing special soldiers.

When a system, rebellion, or joined war starts without advance notice, real
war start activates the same bounded levy authority for the newly threatened
participant. This does not delay or recreate that war; it only permits later
reinforcement while the war remains active.

No combat-quality threshold applies to levies: age below 65 is the only extra
criterion beyond original ordinary-warrior eligibility and the listed AW3
identity protections. Recruitment stops at the city's existing
`warrior_slots` establishment. Temporary slave armies, royal guards, and
other special armies neither consume nor satisfy this ordinary wartime target.

Each recruited actor stores a temporary-levy flag, mobilizing kingdom ID,
notice signature, original city ID, and later the real war ID. Recruitment
uses a scoped context so ordinary enlistment side effects do not convert the
actor into a permanent soldier. Levy formation writes a levy-specific history
event instead of a permanent enlistment or retirement record.

### Retirement isolation and demobilization

Temporary levies never enter AW3's retirement mechanism:

- `Actor.updateAge` retirement returns before expensive retirement state and
  database work when the temporary-levy flag is present;
- city retirement fallback scans also reject temporary levies;
- levy service does not accumulate permanent soldier service time;
- a levy never receives retired-soldier state or the veteran trait merely for
  being demobilized.

The age limit is checked only at enlistment. A levy who reaches age 65 during
mobilization or war remains in service until demobilization, preventing
mid-war roster churn.

Temporary levies form one kingdom-scoped pool. When a notice is canceled or a
war ends, the pool remains mobilized while that kingdom has any active incoming
notice, outgoing notice, or real war. After its final military emergency,
deferred cleanup demobilizes at most eight levies per work item. Each living
levy leaves its army, stops being a warrior, clears all temporary military
fields, and resumes civilian life in its original city.
If that city is gone or foreign-owned, the service chooses the actor's current
same-kingdom city, then the kingdom capital; it never changes nationality or
teleports the actor. Dead, captured, or foreign-naturalized actors have stale
temporary fields cleared without changing their new valid state.

An ordinary city army is ready to deploy at the original game's 70 percent
send threshold (`City.isOkToSendArmy`). A valid non-guard special army is
ready when it has a living captain and at least one living warrior. A slave
vanguard is not created until its minimum four slaves and captain satisfy the
80 percent composition rule. Armies that become ready during the notice are
assigned immediately. A living army with warriors that remains below its
readiness threshold continues to block
the deployment-complete gate until it becomes ready or the forced-war year is
reached. Empty or destroyed army objects do not block the gate.

## Temporary Slave Vanguard

### Activation and composition

`SLAVE_ARMY_ENABLED` remains a kingdom capability, not proof that an army is
currently active. A kingdom may form a slave army only when slavery and that
capability are enabled and the kingdom has a military emergency:

- it has issued or received an active advance notice; or
- it is a living participant in an active real war, including a system war
  that did not use advance notice.

Peaceful kingdoms have no slave-army object and no slave soldiers. Each
kingdom may have at most one temporary slave army, with at most 25 members.
A non-slave captain is mandatory. The formed roster must contain at least 80
percent slaves; any remaining members are the captain and non-slave command
cadres. At least four eligible slaves and one captain are therefore required
to create the army. Eligible slaves must be living adults below age 65 and
must pass the existing retired-soldier, royal-refugee, historical-master,
boat, citizenship, and special-role exclusions.

The ratio applies to army composition, not to the kingdom's whole slave
population. Formation never attempts to enlist 80 percent of all slaves.
Slave soldiers use a temporary-service flag and do not count toward the
ordinary peacetime core or wartime `warrior_slots` target. Scoped special
recruitment and warrior-limit checks prevent the original city army cap from
either blocking this army or dismissing it as ordinary over-strength.

### Event-driven formation

Notice issue/receipt and real-war start enqueue formation for both relevant
kingdoms. Formation uses stable city and actor cursors, scans at most one city
and 32 residents per deferred work item, and normally changes at most four
actors per item. Initial creation is the sole exception: one valid captain
and four eligible slaves are attached atomically as a five-actor roster, so a
partially formed army below the 80 percent rule is never exposed. It continues
only while the military emergency remains valid. The army is anchored to the
highest-priority facing city, or the same coastal, capital, and lowest-city
fallbacks used by pre-war deployment.

Formation reuses the existing batched slave-state persistence path and emits
one aggregated history entry per completed batch. It never performs a
kingdom-wide synchronous actor scan, creates more than one army, or writes a
world log for every enlisted slave.

Member death, capture, manumission, or kingdom change enqueues the same
bounded formation work through existing lifecycle hooks. The service updates
cached slave and cadre counts incrementally and never adds a cadre when doing
so would take the formed roster below 80 percent slaves. Casualties may make
the ratio temporarily lower between events. The next queued item adds slaves,
releases excess cadres, or disbands a roster that can no longer restore its
minimum; no periodic actor scan is used to repair it.

### Vanguard behavior

The temporary slave army receives the highest-priority threatened city before
ordinary armies are load-balanced. Its stable deployment positions lie on
valid tiles toward the threatened edge of that city, while ordinary armies
use positions farther behind it. With multiple notices, the same earliest
forced-year, issue-year, and attacker-ID ordering chooses its one active
front.

At real-war start, a dedicated `aw_slave_vanguard` job replaces the current
`DriveSlaveArmyFrontline` actor-target loop. The job resolves one stable enemy
city from cached border-city data. The attacker's vanguard prefers the valid
war-goal city; the defender's vanguard prefers the closest attacker-owned city
facing the threatened front. Both use the established coastal, capital, and
stable-city fallbacks. The job issues the vanguard order before staged
ordinary armies are released and then lets local combat AI engage nearby
enemies. It never enumerates enemy actors. Movement uses the existing
Cultiway-backed navigation and suppresses duplicate path requests while the
destination remains valid.

If the slave army cannot form, has no reachable destination, or is destroyed,
ordinary deployment and war continue normally. Vanguard failure never blocks
the one-year minimum or three-year forced-war deadline.

### Disbandment

When a notice is canceled or a war ends, the army remains active only if its
kingdom still has another notice or real war. After the final military
emergency, deferred cleanup processes at most four members per item:

- surviving slave soldiers leave the army, stop being warriors, clear
  temporary-service state, and resume slave labor in their valid original
  city or the deterministic same-kingdom fallback;
- they remain slaves and receive neither retired-soldier nor veteran state;
- the non-slave captain and cadres restore their recorded original army,
  city, profession, and job when those references remain valid;
- if those non-slave references are invalid, they return to a valid
  same-kingdom city or capital and resume normal profession/job selection;
- merit and lawful manumission earned during war remain authoritative; an
  actor freed before cleanup is restored as a free civilian, not re-enslaved;
- after the roster is empty, the special army object and its runtime index
  entry are removed exactly once.

Disabling slavery or slave-army capability, kingdom extinction, and load
reconciliation use the same idempotent disbandment path.

### Peacetime performance removal

The recurring `SlaveService.EnsureSlaveArmy(pCity)` call is removed from city
army maintenance. `EnforceSlaveControl` retains labor and food enforcement but
does not form armies. Normal city cleanup no longer performs slave-army
captain, composition-inference, fill, rename, or frontline work.

The old city scheduling, failed-maintenance cooldown, continuation cursor,
frontline target cache, and global enemy-actor target scan are removed from
the active path. Runtime work is indexed by the small set of kingdoms with
active notices or wars. A load performs one bounded reconciliation of marked
special armies; peaceful annual, `Actor.update`, redraw, and ordinary city
maintenance paths perform zero slave-army work.

## Frontier Selection And Army Distribution

### Destination cities

For a land war, threatened frontier cities are defender cities whose
`neighbours_kingdoms` contain the notifying attacker. Existing neighbor data
is reused; no zone graph is rebuilt every year.

If no direct land frontier exists, destinations are selected in this order:

1. the valid defender-owned target city named by the war goal;
2. defender coastal cities on an island reachable from an attacker city,
   ranked by distance;
3. the defender capital;
4. the lowest-ID living defender city as a deterministic fallback.

The destination set is calculated once per notice and revalidated when a city
changes owner or is destroyed.

### Whole-army assignments

Army cohesion is preserved: one army receives one destination city. Eligible
armies are all living defender armies except the royal-guard role, including
ordinary standing armies, border armies, slave armies, and fief armies.

The one temporary slave vanguard, when present, takes the highest-priority
facing city first. Remaining armies are then load-balanced without allowing
their strength to displace that vanguard assignment.

Assignments use stable army-ID order and a greedy score that prefers:

- the same island;
- shorter path distance;
- fewer already assigned warriors relative to the destination's capacity;
- lower city ID as the final tie-breaker.

This spreads forces across the threatened cities instead of sending every
army to the capital or a single border settlement. If there are fewer armies
than cities, the highest-threat and least-defended cities receive armies
first. If there are more armies, destinations are reused by the load score.

### Multiple incoming declarations

A defender may receive several notices. Active notices are indexed by
defender. One army can have only one preparation assignment. Priority is:

1. earliest forced-war year;
2. earliest issue year;
3. lower attacker kingdom ID.

A later notice cannot repeatedly steal an army from an earlier imminent war.
When the higher-priority notice closes, the next annual preparation pass can
assign the army to the next notice.

## Deployment Task

AW3 registers a temporary actor job and task for pre-war deployment. An actor
is eligible only while it is a living warrior in an assigned non-guard army,
belongs to the attacker or defender named by the notice, and the assignment
still matches that active notice. Ordinary attacker armies are not staged;
the attacker-side use is limited to its temporary slave vanguard.

The task resolves a stable tile from the assigned city using actor ID, army
ID, notice signature, and city zones. It avoids liquid, lava, blocked tiles,
the exact city center, and map-border tiles. Units in the same army receive
nearby but distinct positions. On arrival they patrol within a small bounded
radius instead of standing on one tile.

Movement uses the existing `BehGoToTileTarget`/actor navigation path, which is
already routed through AW3's Cultiway-based pathfinding. The preparation
system does not implement another pathfinder and does not issue a fresh path
request when the actor is already moving to, or has reached, the valid
assignment.

Royal guards are rejected both when assignments are built and when the task
validates an actor. They remain with the king throughout notice, deployment,
and war transition.

Both temporary ordinary levies and temporary slave soldiers are rejected by
retirement before any expensive age, service-time, trait, archive, history,
or database work.

## Persistence And Runtime Cost

- The attacker's kingdom decision data is the authoritative notice record.
- Queue codec fields preserve a preempted declaration.
- Army data stores the active notice signature and destination city ID.
- Actor data stores temporary levy ownership fields, when applicable, and the
  task assignment needed to resume a partially completed move.
- A temporary slave army stores its mobilizing kingdom, active emergency,
  anchor city, vanguard destination, and formed slave/cadre counts. Its
  members store temporary service plus original city, army, profession, and
  job data required for deterministic restoration.
- A bounded runtime index maps defender IDs to active notices and is rebuilt
  once after load; it is cleared on a new world or archive switch.
- A second bounded index maps notice and war IDs to temporary levy actor IDs;
  enlistment and demobilization update it incrementally. Load reconciliation
  rebuilds it from affected city armies, not from every world actor.
- A kingdom-to-slave-army index contains only active military emergencies and
  enforces the one-army-per-kingdom invariant.
- Mobilization and assignment run at most once per affected kingdom year.
- Destination discovery runs once per notice and on explicit invalidation.
- Deferred mobilization and disbandment process at most one work item per
  frame under the existing runtime work budget. Ordinary levy cleanup changes
  at most eight actors. Initial slave-army formation is an atomic five-actor
  item; later slave fill and cleanup items change at most four actors.
- No scan is added to `Actor.update` or redraw. Per-frame world update only
  drains an already-indexed active-emergency work item; it never starts an
  unconditional actor, army, city, or pathfinding scan.

## Presentation And History

A dedicated world-log asset announces that the attacker has delivered a war
declaration to the defender, with both kingdom colors and the selected reason
or goal. Both kingdoms receive history entries. Levy mobilization and final
demobilization use aggregated kingdom or city history entries rather than one
log per actor. Temporary slave-army formation and disbandment use the same
aggregated rule. Cancellation records a history entry but does not spam a
second world log unless the player-visible notice had already been announced.

The attacker's decision summary shows issue year, earliest war year, forced
war year, and deployment state. The defender's kingdom detail shows the most
urgent incoming declaration and remaining preparation years. All new world
log, history, status, task, and UI strings are localized in Simplified
Chinese, English, and Traditional Chinese.

## Error Handling

- A missing world-log asset never blocks state creation or deployment.
- A missing target city falls through to coastal, capital, and stable-city
  selection.
- A failed path request leaves the assignment active for later retry and is
  bounded by the forced-war deadline.
- A dead captain or empty army is excluded from readiness and does not block
  the three-year deadline.
- A city ownership change invalidates only assignments to that city; other
  armies keep their valid destinations.
- A levy whose original city becomes invalid remains attached to its current
  force until demobilization, then uses the deterministic same-kingdom fallback.
- A stale levy flag without a valid notice or war is reconciled by bounded
  demobilization after load; it never becomes a retired veteran.
- A marked slave army found without an active notice or war is queued for the
  same bounded disbandment used at normal peace. Duplicate marked armies are
  merged into the stable lowest-ID keeper before any new recruitment.
- Missing slave captain, fewer than four eligible slaves, or inability to
  maintain the 80 percent formation ratio means no slave army is created; it
  never falls back to recruiting arbitrary civilians.
- Notice-index loss is repaired from persistent current and queued decision
  state. Slave-army index loss is repaired from marked special-army data;
  neither requires a scan of every actor.
- All cleanup paths are idempotent so war start, cancellation, extinction,
  and load reconciliation can safely overlap.

## Testing Strategy

Tests are written and observed failing before production changes.

Pure rule tests cover:

- 30 percent peacetime-core rounding, minimum-one, zero-slot, full,
  underfilled, absent, and overfilled city armies;
- deterministic elite score and ranking, bounded replacement, and
  special-role exclusions;
- preservation of existing guards while recruitment is blocked;
- final Xia `offspring=10` against human `offspring=5`, while effective
  `birth_rate` is not increased again;
- notice applicability and every excluded war type;
- one-year minimum and three-year maximum declaration gates;
- cancellation and revalidation decisions;
- ordinary and special-army readiness;
- temporary-levy eligibility below age 65, rejection at age 65, wartime cap,
  notice-to-war transfer, multi-war retention, and final demobilization;
- retirement and veteran-state exclusion for temporary levies;
- retirement and veteran-state exclusion for temporary slave soldiers;
- slave-army emergency activation, one-per-kingdom and 25-member limits,
  minimum four-slave formation, and at-least-80-percent composition math;
- slave-vanguard front priority, multi-emergency retention, bounded refill,
  captain/member restoration, manumission preservation, and final disbandment;
- royal-guard exclusion;
- stable frontier ranking, load-balanced assignments, and multi-notice
  priority;
- movement-order suppression for actors already following a valid assignment.

Source guards require:

- the royal-guard recruitment gate before candidate scanning;
- the original passive recruitment path capped at the peacetime core and a
  scoped bypass for explicit special recruitment contexts;
- notice state in both current decisions and queue codec;
- load/reset hooks for the notice runtime index;
- use of the dedicated deployment task rather than one-time target writes;
- royal-guard exclusion at assignment and task validation;
- bounded yearly recruitment and no unconditional per-frame world scans;
- ordinary mobilization limited to four cities, 64 candidates, and eight
  recruits per kingdom-year, with eight-actor deferred demobilization;
- temporary levies and temporary slave soldiers rejected before expensive
  `Actor.updateAge` retirement work, with no per-actor levy demobilization
  database write;
- no `EnsureSlaveArmy` call from recurring city maintenance or peaceful slave
  control, and no active `DriveSlaveArmyFrontline` enemy-actor scan;
- one indexed temporary slave army per kingdom, the dedicated
  `aw_slave_vanguard` job, atomic five-actor creation, and four-actor later
  formation/cleanup bounds;
- complete localization keys.

Runtime acceptance covers:

1. peaceful cities converge to a 30 percent elite ordinary core and do not
   refill the remaining slots with random permanent warriors;
2. a kingdom without its ordinary core never forms or reinforces a guard,
   while an existing guard remains stable;
3. filling the peacetime core re-enables bounded guard recruitment;
4. Xia actor stats expose an offspring limit of 10 and population growth no
   longer stalls near the human family cap;
5. a Xia/rites attacker sends a visible declaration before war;
6. the defender raises only temporary levies below age 65, fills toward the
   wartime establishment, splits armies across facing border cities, uses
   dispersed positions, and excludes the royal guard;
7. war cannot begin in the issue year, begins after full deployment when at
   least one year has passed, and cannot be delayed by deployment beyond the
   third year even with an unreachable army once declaration progress is full;
8. canceled declarations release assignments, demobilize notice-only levies,
   and do not consume claims;
9. ending the kingdom's final related war demobilizes surviving levies back
   to valid home cities without retired or veteran state;
10. a peaceful slave-owning kingdom runs for decades with no slave army and
    no slave-army city-maintenance or enemy-target-scan benchmark activity;
11. issuing or receiving a notice creates at most one 25-member slave army,
    keeps at least 80 percent slaves, stages it ahead of ordinary armies, and
    starts its stable city-target vanguard order first;
12. the final war or notice ending restores surviving slaves and cadres,
    removes the army object, and preserves slave merit and lawful freedom;
13. save/load during mobilization restores one notice and the same stable army
    destinations without duplicate logs;
14. repeated declarations introduce no new exceptions or actor/update-age
    performance spikes in `Player.log` and benchmark output.

## Success Criteria

1. Peaceful ordinary cities retain a 30 percent high-quality standing core
   instead of permanently filling their wartime establishment.
2. The standing core has absolute manpower priority over new royal guards,
   so newly formed guards cannot coexist with no ordinary army.
3. Existing guards are not churned by temporary war losses.
4. Xia long-term offspring capacity is exactly twice the human baseline.
5. Xia-rite deliberate wars are announced at least one year in advance.
6. The defender receives real bounded temporary mobilization and all ready
   non-guard armies deploy across threatened frontier cities.
7. Temporary levies are enlisted only below age 65, never retire while
   mobilized, and return to civilian life after the final related war.
8. Peace performs zero slave-army formation, fill, target, or repair work;
   war uses at most one 25-member vanguard per kingdom with at least 80
   percent slaves and fully disbands it after the final emergency.
9. Royal guards remain with the king and never enter pre-war deployment.
10. Deployment cannot delay war beyond three years.
11. Existing war goals, claims, vassal/alliance permissions, occupation, and
    war history continue to use their current authoritative services.
12. No new unbounded per-frame scan or repeated path-request storm is added.
