# Name, Lineage, and School Activity Root-Fix Design

**Status:** Approved for implementation by the user's 2026-07-14 "start immediately" instruction.

## Goal

Remove the shared-generic meta-window name corruption at its source, make Xia meta names
and royal clan identities valid at creation time, restore the NeoModLoader tab skin, and
replace synchronous yearly school lecture/debate settlement with visible, bounded actor
tasks processed across frames.

## Scope Boundaries

This change has two independently verifiable slices:

1. **Creation and display invariants**: meta-window binding, Xia names, royal lineage, and
   the AW3 tab skin.
2. **School activity runtime**: lecture/debate tasks, frame queue, venue distribution,
   cached recruitment, localization, and one active canonical master per school.

Old-save repair is explicitly out of scope. Valid state transitions such as completing
full Xiaization may rename one affected object, but loading a save or creating a map must
not scan the world to repair names.

## Creation and Display Invariants

- Never patch `WindowMetaGeneric<TObject, TData>.loadNameInput()` or `OnDisable()` through
  a closed reference-type generic. Vanilla owns name-field binding for War, Kingdom, Clan,
  City, Culture, Religion, Language, and Subspecies windows.
- Keep the `WorldLog.logNewKing` null guard as an isolated Harmony patch.
- Remove the Kingdom-window name Postfix. A selected meta object's `data.name` must already
  be valid before the window opens.
- Xia creation hooks must reject empty names, `NAME`, `NO_NAME`, `#NO_NAME#`, and the
  Chinese anonymous-clan placeholders. Each hook produces a valid generated value or a
  deterministic local fallback in the same creation call.
- A royal actor resolves lineage in this order: own valid branch, father, current royal
  family, same-father sibling, then a newly allocated branch. Intentional enfeoffed and
  king-founded cadet branches remain separate.
- A king-founded branch freezes lineage/shi/name fields before calling vanilla `newClan()`.
  The clan receives its final name during its first naming callback.
- The AW3 tab keeps the normal and selected sprites copied by NeoModLoader's tab factory;
  code must not replace them with `tab_main` sprites.

## School Activity Architecture

### Annual planning

`HistoricalSchoolRuntime.OnWorldYear()` builds one annual member snapshot and asks the
lecture and debate planners for immutable requests. Planning is bounded before expensive
work: at most eight lectures and a small debate budget. It does not write SQLite, emit
world logs, scan `City.units`, move actors, or mutate membership.

Each request freezes actor IDs, school IDs, city ID, year, cached candidate actor IDs, and
the operation key needed for idempotent persistence.

### Frame execution

`HistoricalSchoolActivityQueue` owns pending requests. `MapBox.Update` processes at most
one transition per frame and stops when its strict elapsed-time budget is reached. Queue
entries validate actors, city, membership, and lifecycle again before every transition.
Cancellation releases the venue and returns surviving actors to their scholar job.

### Visible actor tasks

Register dedicated `BehaviourTaskActor` assets:

- `aw_historical_school_lecture`
- `aw_historical_school_debate_travel`
- `aw_historical_school_debate`
- `aw_historical_school_debate_receiving`

A lecture claims a stable public tile, walks there, waits briefly, commits teaching once,
applies a nearby presentation effect, recruits only from the frozen candidate IDs, then
releases the tile. It never summons all disciples.

A debate claims one venue, assigns initiator and receiver tasks, waits until both are
present, commits the existing atomic debate/ledger transaction once, applies statuses,
then releases both actors and the venue.

### Venue distribution

`HistoricalSchoolVenueService` indexes occupied tiles by city and actor/activity. It chooses
from deterministic offsets around valid city tiles, rejects occupied or invalid tiles, and
never defaults every activity to `City.getTile()`. Death, cancellation, load, city change,
and completion release claims.

### Canonical-master slot

`HistoricalSchoolActiveMasterSlots` indexes `schoolId -> canonical master`. Descent reserves
the school before actor creation. A committed descent activates the slot; a clean failure
releases it. A committed death releases it. Load reconstructs slots from persisted master
records and living actor state, without an annual world-unit scan. A school with an active
or pending slot cannot select another canonical master.

## Localization and Presentation

English, Simplified Chinese, and Traditional Chinese locale rows are required for every
new task. Actor panels therefore display a meaningful task name plus vanilla task duration,
never `???` and never generic `task_unit_move`. Lecture world logs continue using the
actual school's icon.

## Failure Handling

- Persistence is the commit boundary. Presentation effects and logs occur only after a
  successful durable commit.
- Unknown persistence outcomes remain queued with bounded retry/backoff; clean failures
  roll back reservations and actor task state.
- Duplicate operation keys are idempotent and never recruit or settle twice.
- Save flush drains durable pending commits but does not force unfinished movement to
  teleport or settle.

## Verification

- Source guards reject shared-generic meta-window patches, Kingdom name rebinding,
  `EnsureWorldNames()` lifecycle calls, and `tab_main` sprite replacement.
- Pure rule tests cover royal-family source priority, queue budgets, venue uniqueness,
  activity transitions, and school-level canonical-master reservation/release.
- Task registration/localization tests cover every new task ID in all three locale columns.
- Debug and Release builds complete with zero errors; `git diff --check` is clean.
- Live acceptance checks War/Kingdom/Clan/City/Culture windows, tab switching, two lectures
  in one city using different tiles, visible task text/duration, debate arrival/settlement,
  and blocking a second living canonical master of the same school.
