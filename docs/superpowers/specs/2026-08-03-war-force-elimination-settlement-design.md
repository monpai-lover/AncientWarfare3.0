# War Force Elimination Settlement Design

## Goal

End wars cleanly when a whole belligerent side has exhausted both its active
ordinary army and its AW3 reserve manpower. The decision must include every
current participant on that side and must work for ordinary wars, rebellions,
Zhulu wars, and war types that normally block negotiated peace.

## Source Of Truth

Use WorldBox's existing aggregate APIs for vanilla facts:

- `War.countAttackersWarriors()` for all active-warrior actors belonging to
  current attacking participants.
- `War.countDefendersWarriors()` for all active-warrior actors belonging to
  current defending participants.
- `Kingdom.countTotalWarriors()` only when a per-kingdom breakdown is needed.
- `CityReservePoolService.CountAvailable(kingdom)` for the AW3-only reserve
  value that vanilla cannot know.

Do not scan `ArmyFieldIndexService`, Army groups, or individual actors to
recompute active side strength. Vanilla already counts every actor whose city
profession is `UnitProfession.Warrior`; the `War` methods already aggregate all
current participants. This also includes warriors that temporarily have a
broken or missing Army/Group association.

For one side:

```text
military potential = vanilla active warriors + available AW3 reserves
```

Reserve aggregation iterates the side's current participant kingdoms exactly
once and clamps overflow to `int.MaxValue`. A kingdom that is not a current
participant is not counted merely because it is an ally or vassal.

## Monthly Decision

A dedicated elimination-settlement service runs from the existing monthly war
settlement path. It evaluates each live war before ordinary peace guards,
war-goal completion, and war-exhaustion settlement.

Each side must report zero potential on two consecutive monthly evaluations
before it is confirmed exhausted. A non-zero result immediately clears that
side's zero streak. This prevents a temporary reserve rebuild or mobilization
transition from ending the war.

The service keeps only a small runtime streak record keyed by war ID. Records
are removed when the war ends, the world resets, or the save changes. Streaks
are deliberately not persisted because confirmation lasts at most one month
and recalculates from authoritative state after loading.

## Settlement Outcomes

### One Side Is Exhausted

The exhausted side surrenders. The other side receives the maximum victory
outcome supported by that war type, regardless of whether ordinary peace is
blocked.

- Ordinary clause-based wars use the existing maximum-benefit settlement
  builder with full surrender authority.
- Zhulu and other total wars use their dedicated decisive-victory adapter.
- Rebellion/direct-transfer wars use their dedicated territory-transfer
  adapter.

The adapters are selected from the war's actual type. Failure in one adapter
must not fall through to an incompatible ordinary settlement. It is logged
once and retried at the next monthly evaluation while the confirmed exhausted
state remains true.

### Both Sides Are Exhausted

Use the current signed war score:

- Positive score: attackers are the beneficiary.
- Negative score: defenders are the beneficiary.
- Zero score: white peace.

For non-zero score, the beneficiary receives the maximum set of compatible
clauses affordable by the absolute current score. This is not treated as a
100-score surrender. The rule applies to every war type. Special-war adapters
must translate the same beneficiary and score into that type's valid result;
for example, a Zhulu-style total war transfers the largest score-affordable
continuous territory block instead of granting an automatic full annexation.
No special war may discard the current score or silently enter the ordinary
peace path.

## Ordering And Safety

The new check has priority over:

1. `ZhuluPeaceGuard.BlocksOrdinarySettlement`;
2. `RebellionDirectTerritoryTransferService.BlocksOrdinarySettlement`;
3. war-goal settlement;
4. war-exhaustion settlement.

It does not run for ended wars, wars without valid participants on either
side, multiplayer replicas, paused authority state, loading, or world reset.
The host/authoritative simulation alone mutates settlement state.

If a participant kingdom is destroyed between counting and settlement, the
settlement path revalidates the live war and participant lists. The monthly
service performs no actor-level scans and allocates no per-frame collections.

## Vanilla Statistics Reuse Audit

AW3 should prefer the following existing APIs wherever the required meaning is
identical:

| Scope | Vanilla API | Meaning |
| --- | --- | --- |
| Army | `Army.countUnits()` | Members in one Army |
| City | `City.countWarriors()` | Warrior-profession actors |
| City | `City.getUnitsTotal()` | Total city units |
| City | `City.countBuildings()` | City buildings |
| City | `City.countBuildingsOfID(id)` | Buildings of one ID |
| City | `City.countFoodTotal()` / `getTotalFood()` | Food totals |
| Kingdom | `Kingdom.countTotalWarriors()` | Warriors in all cities |
| Kingdom | `Kingdom.countCities()` | Cities |
| Kingdom | `Kingdom.countBuildings()` | Buildings |
| Kingdom | `Kingdom.getPopulationTotal()` | Total population |
| Kingdom | `Kingdom.countBoats()` | Boats |
| Kingdom | `Kingdom.countTotalFood()` | Food |
| Alliance | `Alliance.countWarriors()` | Allied active warriors |
| Alliance | `Alliance.countPopulation()` | Allied population |
| Alliance | `Alliance.countCities()` | Allied cities |
| War | `countAttackersWarriors()` / `countDefendersWarriors()` | Current side warriors |
| War | `countAttackersPopulation()` / `countDefendersPopulation()` | Current side population |
| War | `countAttackersCities()` / `countDefendersCities()` | Current side cities |
| War | `countAttackersMoney()` / `countDefendersMoney()` | Current side money |

Thin exception-safe wrappers around these calls are acceptable. Reimplementing
their aggregation by iterating cities, armies, or actors is not acceptable
unless AW3 intentionally needs a different semantic subset. Examples that
must remain AW3-owned are available reserves, ordinary-army-only counts that
exclude royal guards, occupied-territory control, synthetic levies, titles,
and war-goal state.

The implementation plan will first replace the duplicate active-military scan
used by `WartimeMilitaryPotentialService`. Other duplicate statistics found by
the audit will be recorded as follow-up candidates and changed only when their
semantics are proven identical, avoiding an unrelated broad refactor.

## Tests

Rule tests cover:

- vanilla active warriors plus reserves for every participant on a side;
- an ally or vassal with remaining potential preventing side exhaustion;
- non-participating allies and vassals being excluded;
- a one-month zero reading not ending a war;
- a non-zero recovery clearing the confirmation streak;
- attacker-only and defender-only exhaustion;
- both sides exhausted at positive, negative, and zero scores;
- ordinary, Zhulu, rebellion, and ordinary-peace-blocked war routing;
- cleanup on war end and world reset;
- integer overflow clamping.

Integration verification builds the rules project, runs the focused settlement
tests, runs the full rules suite, and verifies a source-only deployment. No DLL
is deployed.
