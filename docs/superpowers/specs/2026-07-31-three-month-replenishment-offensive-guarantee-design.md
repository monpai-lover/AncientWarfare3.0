# Three-Month Replenishment and Offensive Guarantee Design

## Goal

Make wartime reinforcement bounded, actor-backed, and visible. An ordinary
army may replenish for at most three game months. If its approved shortage is
filled earlier, it departs immediately. If a realm still has enough ordinary
troops to field a viable force, at least one ordinary army must retain an
attack mission.

Add a fourth auxiliary-law section, Conscription Law, that controls how many
eligible adult civilians are registered in persistent city reserve pools.

## Scope

This design changes:

- city reserve-pool capacity and reconciliation;
- preparation and formal-war recruitment ordering;
- per-army replenishment timing and persistence;
- weak ordinary-army consolidation and offensive continuity;
- auxiliary-law UI, AI selection, localization, and history;
- army map information for shortage and reserve supply.

It does not redesign RTS target selection, special-army recruitment, combat,
or the existing donor-city population floor.

## Conscription Law

Add `CourtConscriptionLaw` and `CourtAuxiliaryLawKind.Conscription` with four
values:

| Law | Reserve share |
| --- | ---: |
| Limited | 30 percent |
| Standard | 50 percent |
| Expanded | 70 percent |
| Full mobilization | 100 percent |

`Standard` is the default for existing saves and new realms.

The reserve share is applied to the city's eligible, living, adult,
non-soldier civilians after existing identity, culture, realm-control, and
population-protection gates. Reserve registration does not change an actor's
profession. Registered actors remain civilians and can work and reproduce
until actually enlisted.

Capacity is `floor(eligible civilian count * reserve share)`. The service
keeps the actor-ID set deterministic. When capacity shrinks, it removes only
uncommitted reserve registrations; it never demobilizes active soldiers.

### Law lifecycle

- Peacetime maintenance gradually reconciles each city to the current law.
- Preparation uses the existing accelerated indexing budget.
- Immediately before formal war starts, the service performs one final,
  bounded reconciliation over eligible adult non-soldiers.
- The final reconciliation runs before reserve freeze and before temporary
  levy conversion.
- Enlisted actors are removed from reserve membership atomically.
- Raising the law during war explicitly indexes additional eligible
  civilians even though ordinary frozen-pool maintenance remains disabled.
- Lowering the law during war removes only uncommitted reserve registrations.
- At war end, temporary soldiers demobilize through the existing actor-safe
  return path and may re-enter the pool up to the currently active capacity.

Full mobilization means registering 100 percent of eligible civilians. It
does not immediately turn the whole city population into soldiers.

### AI selection

AI evaluation uses the same auxiliary-law cost, cooldown, and improvement
rules as the other sections. It combines the dominant court school with the
existing court-direction snapshot:

- strong livelihood or peace direction, and agrarian, Daoist, or medical
  dominance, prefer Limited;
- no decisive direction prefers Standard;
- military or legalist dominance, or high war/aggression direction, prefers
  Expanded;
- Full mobilization is reserved for an active existential defense, capital
  threat, or severe military disadvantage, with military/legalist courts
  weighting it most strongly.

The AI can change the law during war. Full mobilization is an emergency
choice, not a permanently superior tier.

## Preparation Recruitment

The current preparation method completes without recruiting. Replace that
contract. During preparation, approved deployment targets may consume real
actors from city reserve pools. Consumption is bounded by each target army's
approved establishment shortage. Actors removed for preparation enlistment
cannot remain registered in a reserve pool.

The old source guard that forbids preparation conversion must be replaced
with guards requiring reserve-backed, approved-target conversion and
forbidding unrestricted resident scans.

## Per-Army Replenishment Operation

The first transition of an ordinary army into replenishment creates one
durable operation with:

- army and kingdom identity;
- preferred source city;
- immutable approved shortage;
- enlisted count;
- absolute game-month start and deadline;
- schema version.

The deadline is three game months after the start. Re-entering replenishment
does not reset the start, move the deadline, or enlarge the approved shortage.
The operation can end earlier whenever the live shortage reaches zero,
including when another valid recruitment path filled it.

The state is stored on persistent army data alongside the existing durable
RTS mission intent. Save/load restores the same absolute window. Pausing does
not advance it.

### Progressive conversion

On authority simulation cycles, calculate the cumulative number that should
have been enlisted by the current point in the three-month window. Consume
only the difference between that allowance and the operation's enlisted
count. This spreads conversion across the window instead of scanning or
converting an entire city in one frame.

At the final deadline settlement, consume every still-valid indexed reserve
actor needed for the approved shortage. If sufficient eligible actor IDs
exist, the army must be filled to that shortage no later than the deadline.
The operation may remain short only when the indexed supply is genuinely
insufficient or candidates became invalid.

Donor order is deterministic: preferred home city first, then the nearest
controlled cities, then city ID. Consumption never exceeds the immutable
approved shortage and never drains unrelated reserve capacity merely because
it is available.

Every consumed actor is converted through the existing temporary-soldier
path, atomically removed from reserve membership, attached to the target
army, and teleported through the existing safe reinforcement-arrival path.

Invalid, dead, migrated, already enlisted, or enemy-occupied-city actors are
discarded from reserve candidacy and replaced by the next valid indexed ID.
They do not increment the operation's enlisted count.

## Deadline and Offensive Continuity

At completion or deadline:

1. An ordinary army at or above
   `ArmyLogisticsRules.MinimumOperationalForce` immediately resumes its saved
   attack mission, even if below ideal establishment.
2. A weaker secondary ordinary army merges into the realm's deterministic
   primary assault army.
3. The primary is the largest viable ordinary army with an attack assignment,
   breaking ties by stable army ID.
4. If no attack assignment survives, the war director assigns the best valid
   enemy-city objective and queues replanning.
5. If total ordinary forces are at least the minimum operational force, the
   realm must retain at least one viable attacking army.
6. If total ordinary forces are below the minimum, the director does not send
   a one-actor army.

Royal guards, slave vanguards, dedicated garrisons, restoration forces, and
all other special armies are excluded from ordinary-army merging and from the
offensive guarantee candidate set.

Army disappearance, war completion, or kingdom ownership change terminates
the operation. Actors not yet consumed remain in their city reserve pools.

## Presentation

Army map information displays separate facts:

- current shortage: target establishment minus living formation members;
- reserve supply: `CityReservePoolService.CountAvailable(kingdom)`.

The UI must not label reserve supply as the army shortage or imply that every
available reserve actor is approved for the current operation. Add localized
labels and descriptions for all four conscription-law values, the law section,
the two army-map values, and law-change history.

## Performance and Authority

All authoritative recruitment work remains on simulation/authority cycles,
not presentation frames. Routine cycles use bounded city, actor, army, and
conversion budgets. The final pre-war reconciliation and deadline catch-up
operate only on indexed actor IDs and do not introduce an unrestricted full
population scan.

The existing client replica gate remains authoritative: clients display
replicated state but do not enlist actors or change laws locally.

## Persistence and Recovery

New law values use kingdom data with `Standard` as the missing-key fallback.
Per-army operation fields use versioned army-data keys. Restore validates the
army, kingdom, war, deadline, approved shortage, and recruited count before
resuming.

Recovery rules are deterministic:

- clamp recruited count to `[0, approved shortage]`;
- never extend an expired deadline during restore;
- never increase an approved shortage during restore;
- close orphaned operations without consuming actors;
- retain the last valid mission intent for post-replenishment departure;
- remove stale reserve membership through the existing validation path.

## Verification

Add pure rule tests for:

- 30/50/70/100 percent capacities and the Standard default;
- AI scoring for livelihood, neutral, military, and existential-defense cases;
- immutable approved shortage and deadline;
- proportional progress, early completion, deadline catch-up, and exhausted
  supply;
- weak ordinary-army merging and special-army exclusions;
- minimum-force offensive continuity and one-actor suppression.

Add persistence and integration coverage for:

- save/load in each month of an active operation;
- final reserve reconciliation before freeze and levy conversion;
- preparation recruitment consuming only registered actors;
- wartime law increases and decreases;
- war-end demobilization and capacity reconciliation;
- map information showing shortage and supply separately;
- multiplayer replica read-only behavior.

Run the focused rule suites, reserve and RTS source guards, save/load tests,
the RTS adversarial simulation, the full PowerShell test suite, and the
`.NET Framework 4.8` build before deployment.
