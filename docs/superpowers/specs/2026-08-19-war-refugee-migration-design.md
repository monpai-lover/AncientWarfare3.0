# War Refugee Migration Design

## Goal

Make civilians flee cities directly threatened by war, first toward safe
domestic cities and then toward safe foreign cities. Migration should be
visible when practical, preserve households where possible, remain bounded
during large wars, and support voluntary return or permanent settlement after
the danger passes.

## Scope

This feature covers civilian eligibility, city threat snapshots, household
batching, destination selection, host acceptance, physical and abstract
journeys, persistence, arrival, cultural assimilation, return evaluation and
permanent settlement.

It does not move kings, heirs, officials, generals, active warriors or royal
guards; command military units; alter war goals; create a general peacetime
economic-migration system; or reserve P0 military scheduling capacity.

## Architecture

The monthly data flow is:

```
city threat snapshot
  -> departure quota
  -> household batches
  -> safe destination ranking and acceptance
  -> journey record
  -> physical or timed abstract journey
  -> arrival registration
  -> post-war return/settlement evaluation
```

The implementation has four bounded components:

- `WarRefugeeRules`: pure threat, quota, household, destination, acceptance,
  return and settlement decisions;
- `WarRefugeeThreatService`: a monthly snapshot assembled from existing war
  goals, RTS city threat facts, siege/combat state, recent transfer state and
  famine data;
- `WarRefugeeJourneyService`: household journey ownership, physical travel,
  path-failure recovery, abstract arrival and return journeys;
- `WarRefugeePersistence`: durable journey, origin and decision records.

Monthly scheduling uses the existing bounded authority-work queue and drains
small city and household batches over multiple frames. It never runs as an
unbounded actor update and never takes P0 military work.

## Threat and Departure Rules

War participation alone does not cause migration. A safe rear city receives
refugees but does not emit them.

Monthly civilian departure ranges are:

- enemy army nearby or city selected as an active attack objective: 1 to 3
  percent;
- siege, combat inside the city or active ownership transfer: 5 to 10
  percent;
- direct war threat combined with famine: up to 15 percent.

The exact percentage inside a range is deterministic from threat severity,
not global random state. Quotas are clamped by eligible population, the
monthly world/city budgets and a minimum population floor. A city is never
emptied in one update.

Threat facts are captured once per month and reuse existing military caches.
The migration system must not rescan every army for every resident.

## Migrant Eligibility and Households

Only live civilian residents are eligible. Excluded actors are:

- king and heir;
- central or local officials;
- generals;
- active warriors and members of an army;
- royal guards;
- actors already owned by another active migration journey.

Selection first builds households from spouses and dependent children. A
household is selected as one unit when the quota and destination capacity can
hold it. When that is impossible, the rules may split it into the smallest
viable subgroups or individual migrants. An excluded family member remains
behind and does not invalidate the eligible remainder.

Every physical household has one live civilian leader. A leader's death or
invalid state promotes another eligible adult; when none exists, the journey
uses abstract completion or is safely cancelled according to the remaining
members and destination.

## Destination Safety and Ranking

A receiving city must be live and must satisfy all of these conditions:

- it is not an active war goal or attack objective;
- no hostile army is nearby and no combat/siege is active in the city;
- it is not in famine;
- it has food, housing and population capacity for the proposed batch;
- its kingdom is not an enemy of the origin kingdom.

Eligible destinations are ranked in this order:

1. safe cities of the origin kingdom;
2. safe cities in allied, suzerain, tributary or otherwise protected partner
   kingdoms;
3. safe neutral foreign cities.

Distance, spare capacity, food security and diplomatic relation break ties.
Foreign hosts evaluate the batch using relations, food and capacity. A
refusal advances to the next candidate. Enemy kingdoms are never candidates.

Destination capacity is reserved when a journey starts and released on
arrival, cancellation or rerouting. This prevents many monthly batches from
overbooking the same city.

## Journey Model

For a reachable land destination, the household leader receives a refugee
travel task. Other members use the existing follower movement pattern and do
not request independent full routes. Members that lose cohesion are
periodically collected by the journey state rather than generating new route
searches every frame.

Cross-sea journeys and land routes that remain unreachable beyond a bounded
retry window become timed abstract journeys. Travel duration is derived from
distance and cannot complete in the departure frame. This path guarantees
progress without adding a second civilian naval transport system.

If the destination becomes unsafe, full or invalid, the journey reranks
destinations. If no safe destination exists, migrants remain associated with
their origin and retry later; they are not deleted or assigned to an enemy.

Before arrival, actors remain registered to their origin city. At arrival,
the existing `joinCity`/kingdom transition is used so housing, food,
reproduction and normal jobs function in the host city. The migration record
preserves historical origin kingdom and city independently of current runtime
citizenship.

## Persistence

Each journey stores:

- operation and household/batch ID;
- origin kingdom and city IDs;
- destination kingdom and city IDs;
- member actor IDs and current leader ID;
- departure month/time and expected abstract-arrival month;
- physical/abstract/arrived/returning/settled/cancelled state;
- retry count and last progress time;
- capacity reservation;
- return eligibility and decision state.
- origin culture ID, host-culture exposure years and last assimilation year.

Actor-level ownership prevents the same resident from joining two active
journeys. State transitions and arrival writes are idempotent. Load recovery
revalidates actors and cities, reconstructs reservations, reroutes invalid
destinations and never duplicates an actor.

## Assimilation in Non-Xia Kingdoms

A foreign host is treated as non-Xia when it is neither a native Xia kingdom
nor a kingdom at `XiaizationService.LevelXiaizedDynasty`. Partial adoption of
Xia rites or institutions does not disable local assimilation. The culture
adopted is the live host city's local culture.

Adult refugees retain their original culture for the first five completed
years of residence. Beginning in year six, one bounded annual evaluation
applies a deterministic assimilation chance that increases with residence
duration, local marriage, locally born children and permanent employment or
office. The progression is tuned so most continuously resident households
assimilate after roughly 15 to 20 years. It does not consume global random
state and cannot run more than once per actor-year.

Children born while their refugee household lives in the host city receive
the host city's culture at birth, including children of mixed-culture
parents. Their parents' origin kingdom, city and culture remain available in
the family/refugee archive.

Assimilation changes the actor's normal runtime culture through the existing
culture-assignment path. It does not replace naming, genealogy or origin
history. A culturally assimilated refugee who later returns home keeps the
adopted culture; return does not automatically reverse assimilation. A host
that becomes a native or fully Xiaized kingdom stops new non-Xia assimilation
evaluations, but already completed culture changes remain.

## Return or Permanent Settlement

A refugee household becomes eligible for a return decision only when its
origin city exists and has remained continuously safe for one year. A renewed
threat resets the safety period. Return is voluntary and evaluated per
household; a previously split individual decides independently.

Return preference increases with:

- safety, food, housing and prosperity in the origin city;
- surviving relatives and family ties there;
- short residence in the host city.

Permanent-settlement preference increases with:

- greater safety and prosperity in the host city;
- years spent in the host city;
- marriage, children, office or established livelihood in the host city;
- destruction, renewed danger or lack of capacity in the origin city.

A household that chooses return enters the same journey pipeline with origin
and destination reversed and is admitted in monthly batches. A household
that chooses to stay becomes a normal permanent resident and releases the
active refugee status. Historical origin remains archived. No return decision
is offered while the origin is unsafe.

## Performance and Priority

- Threat facts are built once per month.
- City, household and arrival processing have named per-cycle and per-month
  limits.
- One household leader owns pathfinding; followers do not compute duplicate
  routes.
- Candidate-city indexes are reused across households and invalidated by war,
  famine, ownership and capacity changes.
- Refugee work runs below military P0 and cannot delay RTS movement or naval
  landing work.
- Diagnostics aggregate counts per city/month and never log per-frame actor
  movement.

## Compatibility and Failure Handling

- Old saves have no active refugee records and require no migration.
- Existing actor city/kingdom transitions remain authoritative on arrival.
- School-affiliated actors that are otherwise eligible pass through the
  existing historical-school residence guards and cancellation callbacks.
- Failed persistence leaves the current city registration intact and retries;
  it never partially commits half a household.
- Multiplayer replicas consume persisted journey state and do not perform
  independent threat selection or destination decisions.

## Tests

Pure rule tests cover:

- no migration for a rear city merely because its kingdom is at war;
- all threat percentage boundaries and the minimum population floor;
- role exclusions and household-first selection with fallback splitting;
- domestic/partner/neutral ranking and absolute enemy exclusion;
- safety, food, housing and capacity gates;
- deterministic host acceptance and overbooking prevention;
- leader death, path failure, cross-sea timing and destination invalidation;
- exactly-once arrival and load recovery;
- one-year safety gate, voluntary return and permanent settlement decisions.
- five-year assimilation grace, increasing annual assimilation tendency,
  host-born child culture and no automatic cultural reversal after return.

Integration and performance tests cover monthly bounded work, reuse of threat
facts, absence of duplicate full household paths, actor city/kingdom
registration on arrival, origin-history retention, and a large multi-kingdom
war without starvation of military P0 work.
