# Event-Driven Court Vacancy Reconciliation Design

## Goal

Replace periodic central and local officer vacancy scans with one event-driven
reconciliation pipeline. An office is filled when its vacancy is created or
when that kingdom's candidate pool materially changes. If no eligible
candidate exists, the office remains vacant without annual fallback scanning.

This change covers both central courts and local government offices. It must
preserve the existing nine-rank rules, custom court and local-government
templates, Western elections, governor rotation, and explicitly supported
concurrent offices.

## Current Problem

Central and local vacancies are currently driven by overlapping mechanisms:

- `CourtService.OnKingdomYear` detects central vacancies, builds a yearly
  roster, validates incumbents, and attempts to fill offices.
- The native city `do_checks` task runs `CityBehCheckLeader` periodically.
- `AW_PromotionPatch.RemoveLeader_Postfix` also requests
  `CityLeaderVacancyRepairService`, which can retry up to eight times.
- The same-frame branch in `CityLeaderVacancyRepairService` reschedules with
  the original attempt value, allowing repeated delayed work without forward
  progress.
- A local retry set named `PendingVacancyRetries` is written and cleared but
  has no active consumer.

These paths can repeatedly perform SQLite reconciliation, inspect bounded
candidate lists, invalidate the candidate catalog, and rebuild and sort a
kingdom roster for the same unchanged vacancy.

## Architecture

### Unified Runtime Vacancy Registry

Introduce one runtime-only vacancy registry shared by central and local
offices. Vacancy state is not added to save persistence. Durable appointment
rows remain the source of truth; the registry is an execution index.

Vacancy keys are:

- Central: `kingdomId + officeLayer + officeId`
- Local: `kingdomId + cityId + officeLayer + officeId`
- County-scoped local office: `kingdomId + cityId + countyId + officeLayer +
  officeId`

Each registry entry also stores the current missing-seat count. This preserves
custom templates that contain more than one seat with the same `officeId`
without treating repeated event notifications as additional vacancies. The
count is refreshed from the current template and durable active rows whenever
the key is reconciled.

Duplicate event registrations coalesce by key. Registry ordering is
deterministic:

1. Central offices
2. Local chief offices
3. Other local offices

Within a class, use stable office and city identifiers so identical world
state produces identical appointment order.

### Unified Reconciliation Service

All vacancy and candidate-change events call one kingdom-scoped
reconciliation service. A reconciliation run:

1. Reads the registered vacancies for one kingdom.
2. Builds or obtains one candidate snapshot for that kingdom.
3. Processes vacancies in the fixed priority order.
4. Removes each appointed actor from the available snapshot unless an
   existing explicit concurrency rule permits the combination.
5. Registers the actor's former office when promotion or transfer creates a
   new vacancy.
6. Continues cascading until all possible vacancies are filled or no eligible
   candidate remains.

The run is bounded by the number of currently valid offices in the kingdom and
tracks processed vacancy keys. This prevents cycles caused by incompatible
templates or replacement callbacks.

By default, an actor may hold one office. Existing explicit concurrency, such
as a regional superior concurrently serving as the capital city's local
chief, remains supported by the existing compatibility rules.

## Event Sources

### Vacancy Creation

Register a concrete vacancy after the durable incumbent row has been closed:

- officer death;
- dismissal or voluntary removal;
- term expiry;
- successful office promotion or transfer;
- court or local-government template migration;
- city leader removal;
- city ownership change.

Destroying a city, destroying a kingdom, removing an office from a template,
or transferring a city removes registry keys that can no longer be valid.

Specialized term systems retain ownership of their behavior. Western
elections and governor rotation continue to decide when and how an incumbent
leaves. They enter the unified pipeline only when their committed operation
produces a real vacancy.

### Candidate Pool Changes

Retry only the registered vacancies of the affected kingdom when eligibility
can actually change:

- actor adulthood, hooked from native `Actor.eventBecomeAdult`;
- civil-service examination qualification granted or upgraded;
- actor joining or leaving a kingdom;
- an officer leaving office and becoming available;
- office template, nine-rank qualification, or office eligibility changes.

Actor adulthood must use the native event. It must not add all actors to a new
age-tracking table or scan actor ages.

An actor death invalidates that actor in the candidate catalog and registers
only offices actually vacated by the death. It does not rebuild candidates for
unrelated kingdoms.

## No-Candidate And Failure Behavior

No eligible candidate is a stable result:

- keep the vacancy registered;
- do not enqueue delayed work;
- do not retry annually;
- wait for a real candidate-pool change event.

A technical failure is treated differently. A database or runtime projection
failure may schedule one coalesced retry on the next frame for that kingdom.
If the retry also fails, retain the vacancy and write `LogError`. There is no
third retry and no self-rescheduling loop.

## Save Restore And External Compatibility

After save restoration completes, perform one initialization pass per living
kingdom. Compare valid offices with durable active appointments and register
existing central and local vacancies. Mark that world restore generation as
initialized so repeated restore stages cannot repeat the scan.

After initialization, the registry is maintained only by events.

`CityBehCheckLeader` remains a compatibility detector for native or external
code that removes a city leader without notifying AW3. It only registers the
local-chief vacancy. Once the key exists, later checks are O(1) and must not
perform SQLite reconciliation or candidate search.

## Existing Annual Work

Annual court work retains duties that are genuinely annual:

- career evaluation and merit;
- term and election decisions;
- faction and court snapshot maintenance;
- local efficiency, corruption, and bureau snapshot maintenance.

Remove vacancy-driven appointment work from annual paths:

- `CourtService.OnKingdomYear` must not call the central fill chain because
  `HasCentralVacancy` is true.
- `CityBureauAnnualWorkService` may emit a vacancy when annual term processing
  closes an appointment, but it must not periodically search candidates.
- Remove the eight-attempt `CityLeaderVacancyRepairService` loop.
- Remove the unused `PendingVacancyRetries` state.

`OfficerCandidateCatalog` remains kingdom-scoped and event-invalidated. A
single reconciliation run may rebuild it at most once and must reuse that
snapshot throughout cascading appointments.

## Testing

Add focused pure-rule and source-guard coverage for:

- deterministic central, local-chief, and local-office ordering;
- duplicate vacancy coalescing;
- cascading promotion and refill;
- single-office enforcement and explicit concurrency preservation;
- stable vacancies when no candidate exists;
- kingdom-scoped retry after adulthood, examination, or kingdom transfer;
- exactly one technical retry and no retry for no-candidate outcomes;
- one initialization scan per restore generation;
- stale-key cleanup on city transfer, city destruction, and kingdom death;
- absence of central vacancy filling from `CourtService.OnKingdomYear`;
- absence of periodic local candidate search;
- `CityBehCheckLeader` acting only as a vacancy registrar;
- removal of `CityLeaderVacancyRepairService` retry wiring and
  `PendingVacancyRetries`.

Verification must run the focused court tests, the complete rules test suite,
and a full project build. The implementation must not include unrelated dirty
workspace changes in its commits.

## Acceptance Criteria

- Central and local appointments use one event-driven reconciliation path.
- Vacancies are filled immediately after a relevant event when eligible
  candidates exist.
- A no-candidate vacancy causes no recurring work.
- Old saves discover existing vacancies once after restore.
- Appointment priority is central, then local chief, then other local offices.
- Promotion cascades settle in one bounded reconciliation transaction.
- Existing institutional and template behavior remains intact.
- No world-wide scan is introduced.
