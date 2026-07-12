# Historical School Descent, Roster, and Runtime Stability Design

## Objective

Make historical school masters descend reliably, prevent failed descent attempts from
poisoning the WorldBox actor container, and expose every living real school member in a
dedicated hierarchy window.

## Confirmed Failures

The current log proves two linked defects:

1. `HistoricalSchoolStore.TryRecordDescent` uses `@year` in the affiliation insert but
   does not bind the parameter. SQLite rejects the command and rolls back the descent.
2. `HistoricalSchoolDescentService.TryDescend` handles that failure by calling
   `Actor.Dispose()` directly. In the original game, direct disposal clears `data`,
   `kingdom`, and `current_tile`, but does not remove the actor from `ActorManager` and
   does not set its alive flag to false. The invalid actor is then visited by
   `ActorManager.prepareForMetaChecks`, `IslandsCalculator.recalcActors`, actor parallel
   updates, and population queries, producing an exception loop that appears as a frozen
   simulation.

The current school browser also has a presentation gap: it deliberately caps living
representatives at five and sorts them by raw ability. It therefore cannot answer who
actually belongs to a school or show the school's teacher-disciple hierarchy.

## Descent Transaction Boundary

`TryDescend` remains the owner of the complete spawn transaction:

1. Create a real adult Xia actor on a valid home-city tile.
2. Join the home city and verify the actor has live data, a tile, the expected city, and
   the expected kingdom.
3. Apply canonical identity and open the authoritative membership.
4. Record master state and affiliation in one SQLite transaction.
5. Register runtime affiliation and mark the in-memory descent ledger only after durable
   writes succeed.
6. Announcements, chronicles, and archive projection remain best-effort side effects
   after the committed core state.

Every failure before completion rolls back durable rows and membership, marks the actor
dead, skips further actor updates, then calls `World.world.units.scheduleDestroyOnPlay`.
This preserves the original `ActorManager.destroyObject` path, including job-batch,
asset-unit, avatar, container, and deferred-disposal cleanup. Direct `removeObject` and
direct `Actor.Dispose()` are both forbidden in this path.

## School Member Read Model

`SchoolMembershipService` is the sole source of current school identity. The roster never
infers membership from traits, statistics, city influence, office, clan, or historical
registry entries.

For one selected fixed school, the read model resolves every active membership to a live
Actor and records:

- actor and membership IDs;
- school, source, generation, reputation, and start year;
- teacher actor ID and direct-disciple count;
- canonical-master and qualified-teacher flags;
- display kingdom, residence city, ability, name, and stable actor ID;
- a translated standing tier and a teacher link when the teacher is also visible.

Dead, disposed, missing, or mismatched actors are excluded and surfaced through a compact
diagnostic count rather than rendered as fake people.

## Standing and Stable Ordering

The pyramid uses explicit school standing rather than office or raw ability:

1. canonical historical master;
2. qualified teacher;
3. direct disciple;
4. later-generation disciple;
5. converted, rediscovered, or other authenticated member.

Within a tier, members sort by reputation descending, direct-disciple count descending,
learning/intelligence descending, membership start year ascending, then actor ID. The
ordering is deterministic and does not use random jitter.

Canonical masters occupy the apex row. Teachers form the next rows. Disciples and other
members form successively lower rows. A teacher-disciple connector is rendered only when
both endpoints are visible and the membership's `TeacherActorId` matches the teacher.
Members without a live visible teacher remain in their correct standing row and receive
no invented connector.

## User Interface

The existing `SchoolWindow` remains responsible for school descriptions, cities,
historical galleries, institutions, works, debates, and lineage summaries.

A new `SchoolRosterWindow` receives its own AW3 tab button and window ID. It contains:

- a fixed-school selector using the 14 registered schools;
- a summary strip with school name, living-member count, teacher count, and excluded-row
  diagnostics;
- a pan-and-zoom canvas that reuses the court/family-tree interaction pattern;
- pooled member nodes rendered in batches so large late-game rosters do not create all
  portraits in one frame;
- true live `UiUnitAvatarElement` portraits, actor display-kingdom color, standing,
  generation, reputation, residence, and teacher information;
- click-through to the original unit window and to the existing school detail window.

The window refreshes on open, school selection, and membership dirty-version changes. It
does not scan every actor every frame. Membership joins, conversions, rollbacks, deaths,
and index reloads increment the version.

## Performance and Failure Handling

- UI reads the in-memory membership book and direct-disciple count map once per refresh.
- Portrait nodes are pooled and populated in bounded per-frame batches.
- Links are pooled and rebuilt only when the selected roster changes.
- SQLite is not queried per portrait or per frame.
- Missing portraits degrade to a text card rather than aborting the window.
- Runtime annual school services keep bounded budgets and one guarded top-level call each.
- Each dirty city-snapshot batch builds one shared resident index and performs one
  parameterized multi-city ledger read. The resident index is batch-local and is never
  reused across frames because ordinary member movement has no versioned invalidation
  signal.
- The audit will flag direct actor disposal, unbounded world scans, per-frame database
  access, incomplete SQL parameter sets, and cache invalidation that can turn a committed
  transaction into a reported failure.

## Verification

The historical-school harness must prove:

- the descent affiliation SQL binds `@year`;
- failed descent schedules the complete actor-manager destruction pipeline and contains
  no direct low-level removal or actor disposal;
- standing tier and stable ordering rules;
- every authenticated living member is retained by the roster rules;
- teacher links never connect missing or mismatched actors;
- membership mutations advance a dirty version.

The final gate also runs Debug and Release rebuilds against .NET Framework 4.8 reference
assemblies, the pathfinding harness, `git diff --check`, and a fresh runtime-log review.
