# City And State Dual Rename Design

## Goal

Make a de jure state's name stable after creation while giving the player a
single city rename workflow with two independent fields:

- the native city name;
- the city's containing de jure state name.

The state name is initialized from the seat city when the state is created.
It changes later only when the player explicitly submits a tracked rename for
that state's legal seat city.

## Naming Rules

1. State creation initializes `DeJureRegion.RegionName` from the seat city's
   current committed name.
2. `RegionName` is returned directly from read models and map modes.
3. Automatic naming, save-load repair, city assignment, capital repair,
   migration, seat selection, and seat movement never derive or overwrite the
   persisted state name.
4. A tracked native rename (`City.setName(..., pTrack: true)`) updates the
   state name only when the renamed city is the active region's locked legal
   seat.
5. Generated or system renames (`pTrack: false`) never update the state name.
6. A member city that is not the legal seat never updates the state name.
7. An explicit state-name edit in the dual rename window updates only
   `RegionName`; it does not rename the city.

## Persistence Model

`DeJureRegion.RegionName` remains the authoritative persisted state name.
Existing fields `SeatCityId`, `SeatLocked`, and `RegionNameSource` are used to
distinguish a legal seat and the origin of its name:

- `HistoricalDefault` or `LegacyPreserved` for initial and migrated values;
- `ManualSeatRename` after a tracked seat rename.

No state name is computed on read. Existing saves retain their stored name.
Missing legacy names are initialized only by the existing migration/create
path, using the current seat name as a one-time fallback.

The native `City.data.name` remains the authoritative persisted city name.
The two values are saved through their existing persistence paths, so a city
name and a state name can diverge without one being reconstructed from the
other.

## Command And UI Flow

Add a city rename command to the existing authoritative multiplayer command
catalog and router. Its payload contains the country, city, city-name field,
and state-name field. The authoritative handler:

1. resolves the live city and checks that it belongs to the requesting realm;
2. validates the city name and state name independently;
3. commits the native city rename with a tracked player flag;
4. commits the state name to the active region, when supplied;
5. rolls back or reports failure if either persistence step cannot complete;
6. invalidates city, de jure region, court, and map projections once.

The city window gets a rename button that opens a dedicated dual-field window.
The window follows the existing AW3 rename window conventions: prefilled
single-line inputs, validation status, confirm, cancel, pending state, and
authoritative command dispatch. The city-name and state-name inputs are
independent; an empty state field is allowed only when the city has no active
de jure region, while an existing region requires a non-empty normalized state
name.

The existing seat-rename Harmony postfix remains the compatibility hook for
native player renames outside the new window. It forwards `pTrack` to the
store and is the only automatic bridge from a native city rename to state-name
synchronization.

## Error Handling

Reject without mutation when the city is missing, destroyed, unauthorized,
the city name is empty, the state name is empty for an active region, or a
requested state update cannot resolve its active region. Repeating the current
values is idempotent and does not create duplicate history records.

If native city rename succeeds but state persistence fails, restore the
previous city name when the runtime object still permits it and report a
failed command. No partial successful command is published.

## Verification

Add focused rules and source guards covering:

- state creation initializes `RegionName` once;
- read and repair paths do not derive a name from the seat;
- generated city renames do not change `RegionName`;
- tracked legal-seat renames update `RegionName` and
  `RegionNameSource=ManualSeatRename` once;
- tracked non-seat renames do not change `RegionName`;
- direct state-name edits do not alter the city name;
- city-name edits do not alter the state name unless the explicit seat bridge
  is used;
- the dual rename command is registered and routed authoritatively;
- failed or unauthorized commands leave both persisted values unchanged.

Runtime verification should rename a seat city through the new window, rename
a non-seat city, reload the world, inspect the city and de jure map labels,
and verify the state name remains stable through capital and membership repair.
