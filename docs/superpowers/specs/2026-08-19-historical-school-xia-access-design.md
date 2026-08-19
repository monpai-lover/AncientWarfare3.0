# Historical School Xia Access Design

## Goal

Restrict the historical-school academy, travel and lecture systems to Xia
regions. A city is eligible when either:

- it belongs to a native Xia kingdom; or
- its persisted city Xiaization progress is at least 100 percent.

The restriction must apply to every domain entry point rather than only to UI
or annual scheduling.

## Scope

This feature covers:

- academy construction and reconstruction;
- ordinary itinerant travel and direct invitations;
- education journeys;
- lecture planning, queueing, venue claims and actor execution;
- cancellation and recovery of stale activities after a city loses access;
- automatic recovery when a city later becomes fully Xiaized.

It does not destroy an academy that already exists in a non-Xia region,
change school membership, block scholars from living in a foreign city, or
change debate behavior unless debate enters a lecture-only path.

## Authoritative Access Rule

Add a pure `HistoricalSchoolXiaAccessRules` decision surface and a runtime
adapter that resolves city facts through the existing Xia systems. The
runtime fact is true only when the city and owner are live and either
`LineageService.IsXiaKingdom(city.kingdom)` or
`XiaizationService.IsFullyXiaizedCity(city)` is true.

The rule is shared by all three features:

- `CanHostAcademy`;
- `CanReceiveSchoolTravel`;
- `CanHostLecture`.

The named methods remain separate so future policy changes can diverge
without callers implementing their own Xia checks. For this version all three
return the same city-access fact.

## Academy Construction

The access check is required before placement work begins in:

- normal academy construction;
- construction at a preferred saved tile;
- academy repair and reconstruction requests;
- lecture-driven requests to construct a missing academy.

An ineligible city must not consume placement-attempt state, inspect zones or
create an under-construction building. An academy that already exists is left
in place for save compatibility, but it is not considered a usable historical
school venue while the city is ineligible.

When the city later becomes eligible, the existing annual/rebuild scheduling
can construct or reactivate its academy without manual intervention.

## School Travel

Destination eligibility is checked at both selection and execution time:

- city candidate/index construction excludes ineligible destinations;
- ordinary physical travel preparation revalidates the destination;
- direct invitation rejects an ineligible destination;
- education-journey selection and physical travel preparation use the same
  gate;
- pending arrival completion revalidates the destination before changing
  residence.

The source city does not need to be Xiaized merely for an actor to return
home. The restriction applies to travel **to study or participate in school
activity**, not to recovery from an already-started journey. If a destination
loses eligibility in transit, the activity is cancelled and the existing
safe return/recovery path is used.

## Lectures

Lecture access is checked at every state transition:

- annual teacher/city planning;
- activity enqueue;
- dequeue and venue claim;
- actor task preparation;
- ready/commit validation.

A queued or active lecture whose city becomes ineligible is finished as a
cancelled activity. Venue reservations and task leases are released, the
actor is restored to its ordinary job/task state, and no lecture history,
persuasion effect or teaching write is committed.

The venue provider also refuses an academy in an ineligible city. This is a
last-line domain guard for future callers that bypass the current planner.

## Runtime and Performance

The annual scheduler continues to use `HistoricalSchoolRuntime`'s indexed
living-Xia city list. Direct service calls use the authoritative runtime
adapter. No caller performs a world scan or queries Xiaization repeatedly in
an inner actor loop.

City Xiaization completion already refreshes the living-Xia index. City
ownership and lifecycle invalidation must also refresh or invalidate the
relevant school destination and venue caches so an old eligibility result is
not retained.

## Compatibility and Failure Handling

- Old saves require no migration.
- Existing foreign academies are preserved but dormant.
- Database unavailability fails closed for foreign-city Xiaization; native
  Xia cities remain eligible through kingdom identity.
- Multiplayer replicas consume authoritative state and do not independently
  create buildings or commit lectures.
- Cancellation is idempotent, so repeated validation cannot double-release a
  venue or restore an actor twice.

## Tests

Pure rule tests cover native Xia, fully Xiaized foreign, partially Xiaized,
invalid city and dead-owner facts for all three access methods.

Integration/source guards cover:

- normal, preferred-tile and repair construction entry points;
- ordinary travel, invitations, education journeys and arrival completion;
- lecture planning, queue, venue, actor execution and commit;
- stale task cancellation without teaching persistence;
- automatic eligibility after full Xiaization;
- dormant academy preservation in an ineligible city.
