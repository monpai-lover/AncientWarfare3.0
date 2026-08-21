# De Jure New City Auto-Assignment Design

## Goal

Ensure every newly founded eligible city is automatically added to an existing de jure state of its current kingdom. Prefer a directly adjacent state; otherwise use the nearest state. Do not create a new state implicitly.

## Root Cause

`DeJureRegionStore` currently builds regions only during the one-time legacy migration or through player powers. Runtime city foundation never enters `AssignCity`, so a new city remains absent from every active region.

`City.newCityEvent` is a suitable foundation hook because the city ID, kingdom, zones, and generated name already exist. However, `CityManager.newCity` only marks city status dirty after adding zones. `neighbour_zones` and `neighbours_cities_kingdom` may therefore still be stale when the postfix runs. Auto-assignment must explicitly refresh the new city's neighbour data or retry after initialization settles.

## Scope

This change applies only to newly founded cities that pass the existing de jure eligibility rules. It does not redistribute existing cities, change current ownership, create regions, revive retired regions, or include bandit strongholds.

## Assignment Flow

1. The `City.newCityEvent` postfix submits the new city ID to a bounded runtime assignment service.
2. The service validates that the city is alive, has a stable ID and living kingdom, is not a bandit stronghold, and is not already in an active de jure region.
3. The service refreshes the new city's neighbour zones and neighbour cities before candidate selection.
4. Candidate regions must be active and have at least one live, eligible member city currently owned by the new city's kingdom. `CreatedByKingdomId` is historical metadata and is not used as the current-owner test.
5. Adjacent candidates are ranked first by the number of directly adjacent member cities. A larger shared boundary wins.
6. If there is no adjacent candidate, candidates are ranked by the shortest squared tile distance from the new city to any live member city in the region.
7. Ties are resolved by distance to the current live seat city and then by ascending `RegionId`, giving deterministic results.
8. If no candidate exists, the city remains unassigned. The player may create a new state later with the existing power.
9. A successful assignment records a change reason of `city_created_auto_assign`, increments the region version and store revision, clears regional-government aggregation, and refreshes the hierarchy/de jure presentation.

## Deferred Retry

The foundation hook attempts assignment immediately. If the city or kingdom is not ready, neighbour refresh fails, or no usable city tile exists, the city ID enters a deduplicated bounded retry queue. The queue performs one later attempt after the simulation advances. It never scans every city and never retries indefinitely.

An ordinary "no candidate region exists" result is final and is not retried, because repeated attempts would be an implicit periodic world scan. Future player-created regions remain explicit player decisions.

## Ownership And History Rules

- Existing active membership is idempotent and wins over automatic selection.
- Automatic foundation handling never removes a city from an active region.
- Retired regions are not candidates and remain retired.
- Bandit stronghold cities are not candidates or assignment targets.
- A candidate region may contain cities held by several kingdoms after war, but only its live members currently owned by the new city's kingdom contribute adjacency and distance.
- The change history stores the target region, city ID, `FromRegionId = -1`, target region ID, current year, and reason `city_created_auto_assign`.

## Components

### Pure Assignment Rules

A focused rule component ranks immutable candidate facts. It contains no WorldBox objects or global state, allowing deterministic tests for adjacent selection, nearest fallback, tie-breaking, and no-candidate behavior.

### Runtime Assignment Service

The runtime service resolves WorldBox cities and regions, refreshes neighbour state, builds facts for the pure rules, commits the selected assignment through `DeJureRegionStore`, and manages the one-shot retry queue.

### Store Mutation

`DeJureRegionStore` gains an explicit automatic-assignment mutation that accepts a target region and reason. It revalidates eligibility, active target state, and absence of existing membership under the store lock before appending the city. This avoids using the player-power-specific `power_assign` reason.

### Foundation Hook And Refresh

The existing `City.newCityEvent` postfix notifies the runtime service after the current chronicle, technology, military, and map ownership notifications. A successful mutation marks the affected kingdom hierarchy dirty and refreshes de jure presentation once.

## Failure Handling

Invalid, destroyed, neutral, stronghold, already-assigned, or missing cities are ignored safely. Expected ineligible states do not emit errors. Unexpected exceptions are logged once with the city ID and do not interrupt city creation.

## Verification

Automated rule tests cover:

- one adjacent state;
- multiple adjacent states ranked by shared adjacency count;
- nearest-state fallback when no state is adjacent;
- deterministic seat-distance and region-ID tie breaks;
- exclusion of foreign-owned, retired, empty, and stronghold candidates;
- no candidate result;
- idempotent handling of an already assigned city;
- source guards proving the new-city hook, automatic history reason, one-shot retry, store revision, region version, aggregation invalidation, and map refresh are wired.

Release verification builds the mod and runs the complete rules regression suite. Manual verification creates an adjacent mainland city and an isolated same-kingdom city, then confirms both immediately display under the expected de jure state and remain correct after save/load.
