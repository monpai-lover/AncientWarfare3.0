# Peasant Rebel Dual-Route Design

## Goal

Give each newly created peasant rebellion one of two AI-selected routes:

- the existing state-founding rebel route, whose current behavior remains intact;
- a new bandit route that trades expansion and offensive diplomacy for a fixed
  fortified hideout and may later convert into the state-founding route.

This feature is a route framework, not a one-off branch inside
`MandateRebelService`. Future rebel archetypes must be able to add another route
without duplicating creation, persistence, annual dispatch, or transition code.

## Scope

- Apply only to peasant rebellions created through the current Mandate rebel
  creation path.
- Preserve the existing `peasant_rebel` state-founding behavior, including
  aligned-city recruitment, rebellion war creation, mobilization, Mandate
  claims, and government settlement.
- Do not change other internal-war types, ordinary kingdoms, guard armies, RTS
  armies, or the paused grand-strategy-army worktree.
- Reuse the vanilla `TopTileLibrary.wall_wild` top-tile asset. Do not register a
  custom wall asset.
- Route selection and route transition are AI decisions. No player prompt or
  new route-selection UI is required.

## Route Framework

Introduce a common peasant-rebel route coordinator with route-specific
behaviors.

### Coordinator

`PeasantRebelRouteService` owns:

- creation-time fact capture and route selection;
- persistence and old-save migration;
- annual dispatch to the active route;
- the one-way bandit-to-founding transition;
- route cleanup when the kingdom or its founding city is destroyed.

The coordinator is the only component that changes a kingdom's route. Runtime
patches ask the coordinator for route permissions rather than duplicating
bandit-marker checks.

### Route Contract

`PeasantRebelRouteBehavior` defines the route boundary:

- creation and entry effects;
- annual update;
- offensive-war permission;
- incoming-war permission;
- city-acquisition permission;
- state-name and ruler-title projection;
- exit and destruction cleanup.

`FoundingRebelRoute` delegates to the current Mandate rebel flow.
`BanditRebelRoute` owns bandit peace, diplomacy, city restriction, wall repair,
and founding-transition evaluation.

### Persisted State

Store the following authoritative facts in `Kingdom.data`:

- active route ID: `founding` or `bandit`;
- origin kingdom ID;
- founding city ID;
- one rebel-name root drawn at creation;
- creation year and last route-evaluation year;
- origin city count, normalized strength, capital ID, and ruler ID snapshots
  needed by later weakness and turmoil evaluation;
- original bandit-wall tile coordinates and repair progress.

Runtime caches may accelerate lookup but are not authoritative and must be
rebuildable after loading a save.

## Creation Flow

Creation occurs in this order:

1. Create the rebel kingdom through the current `City.makeOwnKingdom` path.
2. Mark its common peasant-rebel identity.
3. Draw a fresh root from the existing rebel-name vocabulary. Do not reuse the
   origin kingdom's name.
4. Persist the origin, founding city, creation year, and origin-strength
   snapshot.
5. Calculate the state-founding probability and draw one route.
6. Enter the selected route atomically.

The state-founding route then runs the existing aligned-city pull and rebellion
war flow unchanged.

The bandit route must not pull aligned cities. If route selection happens after
the native kingdom split temporarily associates another city with the new
realm, route entry retains only the founding city before exposing the completed
state to annual simulation.

## Initial Route Selection

Use a weighted probability rather than a deterministic threshold.

- Start from a neutral 50 percent state-founding tendency. Add a leader factor
  in the range -15 through +15, a founding-city factor in the range -15 through
  +15, an origin-relative-strength factor in the range -20 through +20, and an
  origin-turmoil factor in the range 0 through +10.
- The leader factor normalizes existing combat, administration, and
  ambition/personality facts. A capable and ambitious founder is positive; a
  weak and conservative founder is negative.
- The city factor normalizes population and available defensive force relative
  to the origin kingdom's current cities. A city at the origin median is
  neutral, one at or below half that median reaches -15, and one at or above
  one-and-a-half times that median reaches +15, with interpolation between.
- The origin-relative-strength factor compares the origin's existing normalized
  realm strength with the new rebel realm. An overwhelming origin reaches -20;
  parity or rebel advantage reaches +20, with bounded interpolation.
- Origin turmoil contributes +5 for a second simultaneous hostile war and +5
  when the original capital is lost or no valid ruler exists, capped at +10.
- Clamp the final state-founding probability to 10 through 90 percent, then use
  the world's deterministic random source so save/replay behavior remains
  compatible with the simulation.

Leader, city, and origin contributions are separate detached facts. Their
weights are named balance constants so they can be tuned without changing the
route lifecycle. Missing optional facts contribute zero rather than forcing a
route.

## State-Founding Route

This route preserves the current behavior:

- its visible name is `<rebel name root> + 义军`;
- it may pull aligned cities during creation;
- it starts the existing rebellion war against the origin kingdom;
- it may acquire cities and declare or join wars under existing rules;
- `MandateRebelService.OnKingdomYear` continues government enforcement,
  mobilization, and Mandate evaluation;
- current war-end and Mandate-claim settlement behavior remains authoritative.

Existing peasant rebels loaded without a route ID migrate to `founding`. This
is the compatibility default and must not rerun creation effects.

## Bandit Route Entry

Bandit route entry is one transaction:

1. Enter a coordinator-owned prospective-bandit transaction scope, retain only
   the founding city, and skip aligned-city recruitment.
2. End every active war involving the new bandit kingdom. War-end callbacks
   treat the prospective route as bandit and therefore do not call
   `SettleRebelGovernment` for the entry peace.
3. Clear stale targets and temporary state belonging to those ended wars using
   existing war-end cleanup.
4. Set the visible name to `<rebel name root> + 贼`.
5. Snapshot the union perimeter of all city zones owned by the founding city at
   that moment.
6. Place `TopTileLibrary.wall_wild` on eligible perimeter tiles and persist the
   exact attempted wall positions.
7. Refresh name and ruler-title projections and record the route choice in
   history.

Ending all wars is a one-time entry effect, not a permanent universal truce.
The authoritative `bandit` route marker commits only after identity, peace, and
the one-city invariant are established. Wall placement remains best-effort and
does not decide whether the route commits. The transaction scope is bound to
the specific kingdom and call; it is not a process-wide Boolean flag.

## Bandit Territory Rules

A bandit realm may own only its founding city.

The authoritative city-acquisition gate rejects a second city before mutation,
regardless of source:

- war capture or rebellion direct transfer;
- voluntary defection or aligned-city pull;
- inheritance or succession transfer;
- vassal, suzerain, or peace-settlement transfer;
- scripted event or other AW3 ownership handoff.

The gate must be shared by the ordinary capture, direct-rebellion transfer,
`City.joinAnotherKingdom`, settlement, vassal, and event paths. A rejected
transfer leaves the city with its current owner; it must not transfer and then
be rolled back.

The founding city may continue normal zone growth. New `cityzone` tiles are not
new cities and are therefore allowed. Zone growth does not move or extend the
original wall ring, even when the city grows beyond it.

An annual audit detects a bypass caused by another mod or an old malformed
save. It reports the invariant violation and prevents further acquisitions; it
must not silently delete or arbitrarily reassign an already owned city without
a known previous owner.

## Bandit Diplomacy

- A bandit realm cannot initiate a war.
- A kingdom other than the recorded origin kingdom cannot directly declare war
  on that bandit realm.
- The origin kingdom may declare a new suppression war at any time after the
  entry peace, subject only to engine-level validity such as both kingdoms
  still existing. This explicit suppression permission bypasses any truce
  produced by bandit route entry; "at any time" does not mean waiting for that
  truce to expire.
- Other kingdoms may participate only indirectly through an existing alliance,
  call-to-war, or an explicit future event. They do not gain direct declaration
  permission.
- The bandit realm may defend itself and remains a normal war participant once
  a valid origin-led suppression war begins.

These checks belong in the authoritative war-permission path. AI target filters
and UI availability are secondary reflections of the same decision.

If the origin kingdom captures the bandit realm's only city, the bandit kingdom
is destroyed through the normal kingdom-extinction lifecycle. Survivors lose
route-specific identity and remain ordinary population. Existing wall tiles are
not removed and decay or change only through normal world behavior.

## Wooden Wall Lifecycle

Boundary discovery reuses the filtering pattern already present in
`MandateBorderDefenseService`, but stores a fixed list of original bandit-wall
positions.

- Initial placement covers the union perimeter of the founding city's zones at
  bandit entry.
- Only tiles valid for the vanilla wall top tile are changed.
- City-zone growth or shrinkage never recalculates, moves, or extends the ring.
- While the bandit realm is at peace, a bounded annual repair budget restores
  missing `wall_wild` tiles at recorded positions over time.
- While an origin-led suppression war is active, automatic repair is paused.
- Repair resumes after peace if both the bandit realm and founding city still
  exist.
- A position that has become permanently invalid is skipped without aborting
  the remaining repair pass.
- Route conversion stops all automatic wall maintenance. Existing walls remain
  in the world.

Repair work is staggered and bounded; route updates must not scan the world or
rewrite the complete perimeter in one frame.

## Bandit Identity, Names, And Titles

The bandit route remains a peasant-rebel-derived government. Ending its entry
wars must not call `SettleRebelGovernment` merely because no rebellion war is
active.

One name root is drawn at rebellion creation and persisted:

- founding route: `<root> + 义军`;
- bandit route: `<root> + 贼`;
- bandit-to-founding transition: keep the same root and replace only the
  suffix.

Bandit-specific living titles are:

- king: `大当家`;
- heir: `少当家`.

Other offices retain their existing titles. The route is exposed as a fact to
the existing ruler-appellation and household projection services; individual
windows must not hard-code replacement strings. Route entry, succession,
renaming, save restore, and route conversion call the existing projection
refresh boundary so kingdom, actor, genealogy, history, and tooltip surfaces
agree.

Add dedicated localization keys with Chinese values above and stable fallback
text for every shipped locale. Do not compose localized titles by editing
already formatted display strings.

## Bandit-To-Founding Transition

The transition is one-way. A founding rebel never returns to the bandit route.

Evaluate a bandit realm once per world year. The realm must have spent at least
three complete years as a bandit before an origin-weakness conversion can
occur. A missing or destroyed origin kingdom bypasses the waiting period and
authorizes immediate conversion on the next safe route update.

If the origin still exists, conversion eligibility requires all of these:

- origin city count or the existing normalized kingdom-strength measure has
  fallen to no more than half of the persisted creation snapshot;
- the origin is under serious turmoil: it has at least two simultaneous hostile
  wars, its snapshotted capital is no longer owned by it, or it has no valid
  ruler;
- the founding-city factor used by initial selection is currently neutral or
  positive;
- the current ruler's leader factor used by initial selection is currently
  neutral or positive.

Eligible conversion is still probabilistic. Start at 20 percent per eligible
year. Add 20 points when origin city count or strength has fallen to one quarter
of its snapshot, add 10 points per hostile war beyond the first up to 20, add 15
points if the snapshotted capital is lost, and add up to 15 points each from the
positive founding-city and leader factors. Clamp the result to 20 through 90
percent. Leadership succession replaces the leader facts on the next
evaluation but does not reset bandit age, name root, walls, or origin snapshot.

Conversion runs atomically:

1. change the route to `founding`;
2. remove offensive-war and second-city restrictions;
3. stop wall repair without deleting walls;
4. rename the realm from `<root> + 贼` to `<root> + 义军`;
5. replace `大当家` and `少当家` projections with the existing rebel titles;
6. if the origin still exists, start the current Mandate rebellion-war flow;
7. enter the current founding-rebel annual lifecycle and record the transition
   in history.

If the origin is gone, no fabricated opponent is created. The route enters the
existing founding-rebel lifecycle and lets its existing government and Mandate
settlement rules determine later identity.

## Failure Handling And Save Compatibility

- Existing rebel realms without route data become `founding` without rerunning
  route entry.
- A saved `bandit` realm with a missing origin converts to `founding` on its
  next safe annual update.
- A missing founding city or extinct bandit kingdom cancels repair and route
  evaluation and clears only runtime caches.
- Missing or malformed wall-coordinate data disables repair but does not alter
  the city's zones or fail save loading.
- A failed wall placement skips that tile; it does not roll back the kingdom or
  peace transaction.
- A failed route-entry transaction must not leave the kingdom marked `bandit`
  with founding-route wars or extra cities. Discard the prospective route and
  fall back to the existing founding route if required identity, peace, or the
  founding-city invariant cannot commit.
- Multiplayer replicas consume persisted route state and presentation updates;
  only simulation authority selects routes, ends wars, changes ownership, or
  repairs walls.

## History And Presentation

Record distinct history events for:

- choosing the state-founding route;
- becoming a bandit hideout;
- the origin kingdom beginning a suppression war;
- converting from bandit to state-founding rebel;
- destruction of the bandit realm during suppression.

No new management window is in scope. Existing kingdom, actor, genealogy, and
history surfaces display the projected name and titles through their shared
read models.

## Verification

Add detached rule tests and focused runtime/source guards proving:

1. route probability starts neutral, applies leader/city/origin facts, clamps to
   10 through 90 percent, and uses deterministic simulation randomness;
2. a founding-route result executes the current aligned-city and rebellion-war
   behavior unchanged;
3. a bandit result keeps only the founding city and ends every current war;
4. only the origin can directly declare a later suppression war, while the
   bandit cannot declare any war;
5. every city-transfer entry point rejects a second bandit city before
   ownership changes;
6. the founding city may gain zones, but its recorded wall coordinates never
   move or expand;
7. wall repair is bounded, runs only in peace, pauses during suppression, and
   stops permanently after conversion;
8. destroying the only city ends the bandit realm, preserves wall tiles, and
   clears route-specific identity from survivors;
9. the missing-origin and origin-severely-weakened conversion paths both work,
   respect the three-year rule where applicable, and never transition backward;
10. route entry, succession, conversion, save/load, and window refresh display
    the correct name, `大当家`, and `少当家` projections;
11. old saves without route data retain current founding-rebel behavior;
12. multiplayer replicas cannot make authoritative route, war, city, or wall
    mutations.

Run the complete standalone rules test project and the repository's focused
PowerShell source guards. Build the source repository and deployed mod copy
with zero errors before gameplay validation.

## Acceptance Scenarios

### State-Founding Result

Create a peasant uprising whose weighted draw selects `founding`. Its name uses
a newly drawn rebel root plus `义军`; it recruits aligned cities, begins the
current rebellion war, expands, mobilizes, and settles exactly as the current
implementation does.

### Bandit Result

Create a peasant uprising whose weighted draw selects `bandit`. It retains only
the founding city, immediately leaves all wars, is named with the same newly
drawn root plus `贼`, calls its king `大当家` and heir `少当家`, and receives one
fixed ring of vanilla wooden walls around the entry-time city-zone perimeter.
The city may grow zones outside that ring, but no code path can give the realm a
second city.

### Suppression And Repair

At peace, destroy several recorded wall tiles and observe gradual repair. Have
a non-origin kingdom attempt direct war and verify rejection. Have the origin
declare suppression, verify repair pauses, then retake the only city and verify
normal bandit extinction while surviving walls remain.

### Founding Opportunity

Allow a bandit realm to survive at least three years while its origin falls
below half its creation strength and enters serious turmoil. Once the hideout
and current ruler meet their floors, the annual weighted check may convert it
to `founding`. The realm keeps its name root, changes the suffix to `义军`,
restores existing rebel titles and permissions, stops wall maintenance, and
begins the existing rebellion-war flow if the origin still exists.
