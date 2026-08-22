# Manual Local Chief And De Jure Seat Rename Design

## Goal

Make the local-government root office and the native city leader one
authoritative appointment, and keep a de jure region name derived from its
seat city's current name.

The player-visible outcomes are:

- appointing a commandery chief or military governor from the court
  appointment window immediately makes that actor the target city's native
  city leader;
- the appointed actor moves into the target city;
- a failed appointment restores the former city leader and the candidate's
  former city;
- appointing a subordinate local or central office never changes a city
  leader;
- renaming a de jure seat city from `X` to `Y` immediately renames its active
  region to `Y州`.

## Scope

The appointment change applies only when all of these facts are true:

- the command targets `CourtOfficeLayer.City`;
- the command has a live target city owned by the target kingdom;
- the office ID equals `CourtService.ResolveCityOffice(kingdom, city)`.

All central offices and non-root local offices retain their existing manual
appointment behavior. Automated vacancy selection and intercity circulation
retain their current candidate policy.

The rename change applies only to an active de jure region whose
`SeatCityId` is the renamed city. Member-city renames do not rename the
region. This design does not introduce a separate manual region-name editor.

## Authoritative Local Chief Appointment

`CourtService.TryManualAppointment` remains the authoritative command entry
for single-player and multiplayer. It detects the root-office scope after the
existing target, stale-incumbent, candidate, law and prerequisite checks.
Root appointments delegate to a dedicated local-chief appointment operation;
the UI does not mutate the city directly.

The operation captures:

- the target city's current leader;
- the candidate's current city and profession-relevant runtime state;
- the active root-office career row, including a stale row that differs from
  the current city leader.

Inside `GovernorRotationRuntimeScope`, the operation moves the candidate to
the target city and calls the native `City.setLeader(candidate, true)` method.
It verifies that the native assignment actually accepted the candidate,
because gender, heir, royal-guard and other authority gates may reject it.

The existing official-career persistence path then replaces or creates the
root-office appointment. A stale root-office row is closed as replaced, while
the former native leader's applicable governor projection is closed once.
The new career row records the target city and resolved root office. Successful
commit publishes the runtime officer projection, calls
`CityGovernorPlacementService.OnCommittedAssignment`, requests immediate city
bureau reconciliation and invalidates regional-government presentation.

This is one command outcome: success is not returned until both native city
leadership and the root career agree on the same actor.

## Appointment Rollback

Any rejection or persistence failure after tentative runtime placement runs
inside the same governor-rotation scope and restores:

- the former live city leader with `pNew: false`;
- the candidate to the candidate's former city, or to the former no-city
  state;
- the candidate's pre-command runtime officer projection;
- all uncommitted career changes.

Rollback does not create leader chronicles, duplicate noble promotion events
or duplicate career-end rows. If a former leader cannot be restored because
it died or changed kingdom during the operation, the target city is left
vacant and the existing vacancy repair is requested. The command reports
failure rather than publishing a mismatched root office.

## De Jure Seat Rename Synchronization

The existing `NanoObject.setName` patch already captures the old and committed
city names. After a city rename is committed, it calls a de jure store method
with the city and committed name.

Under the de jure store lock, the method finds the active region whose
`SeatCityId` equals the city ID. It derives the new region name through
`RegionalGovernmentRules.RegionName(committedCityName, "州")`. If the derived
name differs, it updates `RegionName`, increments the region version and store
revision, and appends a `DeJureRegionRenamedFromSeat` history record.

After releasing the store mutation, the change clears the regional-government
aggregation cache and invalidates de jure map presentation. Empty names,
disposed cities, inactive regions and non-seat cities are ignored. Repeating
the same name is idempotent and writes no history row.

## Persistence And Compatibility

The existing de jure JSON schema already persists `RegionName`; no schema
change is required. The next normal save publishes the synchronized name.
Old saves are compatible and begin synchronizing on their next seat-city
rename.

Existing career tables and command payloads already contain the kingdom,
office layer, office ID, city ID and actor IDs required for authoritative root
appointment. No multiplayer protocol change is required.

## Verification

Rules and source-guard coverage must prove:

- only the resolved city root office selects the authoritative leader path;
- a successful root appointment leaves `city.leader`, the active career row
  and runtime officer projection on the same actor;
- root replacement moves the candidate into the target city;
- persistence failure restores the former leader and candidate city;
- a rejected native leader assignment cannot commit a career row;
- subordinate local and central appointments do not call `City.setLeader`;
- a seat-city rename produces `new city name + 州`, increments revisions and
  records `DeJureRegionRenamedFromSeat` once;
- non-seat, inactive and unchanged city names do not mutate the de jure store;
- cache invalidation occurs after a successful region rename.

Runtime verification should replace a populated commandery chief, inspect the
city window and both court-history views, then rename the de jure seat and
inspect the de jure map and regional court card before and after saving and
reloading.
