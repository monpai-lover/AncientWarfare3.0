# RTS Unified Mobilization And Chaos Gate Design

## Scope

This change replaces the split pre-war recruitment and wartime replenishment
rules with one city-owned mobilization ledger. It also prevents a world that
has never established a Mandate realm from entering the Chaos phase and
starting Zhulu wars. The school-death persistence fix is audited and retained
as a data-model correction.

## Required Behavior

1. A diplomatic war notice is a military emergency. During its waiting period,
   every participant reconciles its eligible city reserves once per in-game
   month and assigns those actors to that city's ordinary army.
2. A newly created army receives a non-zero target strength derived from its
   source city's population, conscription law, protected civilian floor, and
   existing military population. A `Create` disposition must recruit just as a
   `Replenish` disposition does.
3. Reserve ownership remains city-local. An ordinary army may consume only its
   source city's ledger; the implementation must not silently borrow actors
   from another city.
4. A city may own at most one ordinary army. Royal guards, temporary slave
   vanguards, and other explicitly special armies are exempt. The war director
   changes the ordinary army's attack, defense, and relief mission instead of
   creating another ordinary army for the same city.
5. An army may leave preparation at 80 percent of target strength. Recruitment
   continues toward 100 percent while the notice or war remains active.
6. The same ledger and consumption contract serves both notice preparation and
   active-war casualty replacement. Phase-specific code may change scheduling,
   but must not impose contradictory eligibility or frozen-state gates.
7. An army cannot remain in `Replenish` forever. It exits when full, when its
   city has no usable reserve after a complete reconciliation, or when its
   bounded wait expires. Reaching 80 percent releases deployment even while
   later reinforcement continues.
8. Save/load restores the city ledger when possible and deterministically
   rebuilds it from living eligible residents when persisted membership is
   absent, stale, or from an older save.
9. Chaos is legal only after a Mandate period has existed. Forced transitions
   must obey the same history gate as annual evaluation. A loaded `Chaos` phase
   with no Mandate history is repaired to `Golden`.
10. Zhulu remains available during a legitimate post-Mandate Chaos phase and in
   the explicit Zhulu world age. A normal world with no Mandate history cannot
   start Zhulu wars merely because stale phase data says `Chaos`.
11. Existing active Zhulu wars continue to settle normally; the new gate
    controls only declarations.

## Architecture

### Mobilization Phase

Introduce a pure mobilization phase contract with four states: `Peace`,
`Notice`, `War`, and `Inactive`. The phase is derived from active notices and
formal wars. Pool maintenance and consumption accept this phase rather than
testing `Frozen` independently.

- `Peace`: index eligible residents and maintain the law-limited ledger.
- `Notice`: perform a full monthly reconciliation, create or repair the city's
  army, consume city-local reserves, and deploy at 80 percent readiness.
- `War`: keep the ledger's ownership frozen to the source city while admitting
  newly eligible residents through bounded monthly reconciliation; consume it
  for casualty replacement.
- `Inactive`: clear invalid operations and do no recruitment.

`Frozen` may remain as persisted compatibility data, but it cannot be the
authority for whether recruitment is legal. The derived phase is authoritative.

### City Mobilization Ledger

`CityReservePoolService` remains the owner of eligible and selected actor IDs.
It exposes one consumption operation taking the derived phase, source city,
target army, and requested count. Before reporting exhaustion, it completes a
bounded full reconciliation of that source city. Count queries distinguish
`indexed`, `available`, and `confirmed exhausted`; an uninitialized runtime
state is not treated as a confirmed zero.

The ledger capacity remains based on eligible residents and the active
conscription law. Population protection and protected identities remain in the
existing eligibility rules. No cross-city fallback is introduced.

`AW_ARMY_CITY_ID` is the stable ownership key. At runtime and after load, an
index enforces one live ordinary army per city. If legacy data contains several
ordinary armies with the same source city, the service selects a canonical army
using stable captain continuity and army ID, merges the other members into it,
then disposes only the empty duplicate formations. It never resolves a
duplicate by assigning it another city's reserve. A source city under foreign
control cannot replenish its army; reclaiming the city restores access.

### Preparation Coordinator

`TemporaryLevyService.ProcessPreparationMonth` becomes an idempotent monthly
coordinator. For each controlled city it:

1. reconciles the city ledger;
2. asks `StandingArmyService` to return or create the city army;
3. computes target strength for both `Create` and `Replenish` dispositions;
4. consumes and enlists city-local reserve actors;
5. records readiness independently from full-strength completion;
6. continues until every city is ready, exhausted, or invalid.

Missing preferred-frontier data is not a global recruitment blocker. It affects
deployment destination only; recruitment proceeds in the source city.

### Wartime Replenishment

`ArmyReplenishmentOperationService` uses the same ledger operation. An
operation can exist when the mobilization phase is `Notice` or `War`, not only
when the old pool flag is frozen. Its completion reason is explicit: full,
ready-and-released, exhausted, deadline, invalid army, or ended emergency.
Readiness release and continued replenishment are separate decisions so an army
does not stay home solely because it is between 80 and 100 percent.

### Chaos And Zhulu Gate

`MandatePhaseRules` gains pure rules for whether Chaos may be entered and how a
loaded phase is normalized. `MandatePhaseService.ForceChaos` reads the current
Mandate report and refuses the transition when no historical period exists.
Loading performs the same normalization and persists the repair.

`ZhuluEligibilityFacts` carries `HasMandateHistory`. `ZhuluWarRules.CanStart`
accepts ordinary-world Chaos only when that flag is true. The explicit Zhulu
age override remains valid. Both AI target selection and diplomatic completion
revalidate through `ZhuluWarService.CanDeclare`, so there is one declaration
gate.

## School Death Persistence Root Fix

A school membership's identity is the immutable tuple:

- membership ID;
- actor ID;
- school ID;
- source type and source ID;
- teacher actor ID;
- city ID;
- generation;
- start year.

Reputation, standing, and loyalty expiry are mutable projections. They may be
updated by promotion or runtime work after a death request captures its
membership snapshot and therefore cannot participate in death-transaction
identity matching. `HistoricalSchoolStore` uses the immutable tuple before
atomically closing membership, affiliation, and historical-master rows. End
state, active state, end reason, and update time remain transaction outcome
checks. A mismatch in any immutable field or duplicate active row remains a
hard conflict and continues to block save rather than corrupting history.

## Failure Handling

- A missing runtime pool triggers reconciliation, not immediate exhaustion.
- A city that cannot create an army stays pending only for the current bounded
  work cycle; diagnostics record its disposition and reason.
- Rejected actor candidates are restored to the same city ledger when still
  eligible.
- Invalid armies and ended notices/wars clear their replenishment operations.
- Save preparation serializes the reconciled ledger; load repair never invents
  actors and only indexes living eligible residents.
- Illegal pre-Mandate Chaos is normalized once and logged with a bounded
  diagnostic.

## Verification

Pure rule tests must cover:

- `Create` and `Replenish` both receive a target and recruit;
- one city cannot create a second ordinary army, and legacy duplicates merge
  into one stable canonical army;
- attack, defense, and relief assignments reuse the same city army;
- notice and war phases both allow city-local consumption;
- peace and inactive phases reject consumption;
- 79 percent remains in preparation, 80 percent releases deployment, and 100
  percent completes replenishment;
- missing/uninitialized pool is not confirmed exhaustion;
- a complete empty reconciliation exits replenishment;
- no Mandate history rejects forced Chaos and ordinary-world Zhulu;
- historical post-collapse Chaos permits Zhulu;
- explicit Zhulu age remains permitted;
- an illegal loaded Chaos phase normalizes to Golden;
- mutable school projection changes do not alter membership identity, while an
  immutable-field difference still conflicts.

The adversarial RTS simulation must include at least ten cities and twenty
armies and exercise notice preparation, deployment, casualties, replenishment,
exhaustion, and war completion. Source guards verify that all declaration paths
use the shared Zhulu gate. Release compilation and the focused rules suites
must pass before source-only deployment; no DLL is deployed.
