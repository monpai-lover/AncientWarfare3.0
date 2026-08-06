# RTS War Doctrines Design

## Goal

Give the player one authoritative RTS war-resolution setting while fixing the
mission-integrity bug that can make an army attack its own city. The setting
must behave identically under Native and AW3/Large scheduling and must not
change the type or participant roster of any war.

This design does not repair Zhulu declaration or settlement logic. In
particular, Zhulu remains a one-versus-one war and must never be rewritten as
`totalwar`; its separate repair belongs to the other active task.

## Setting

Add one restart-required `SELECT` setting:

`AW3_ARMY_RTS_WAR_RESOLUTION_MODE`

The persisted values are:

- `Standard`: retain the current strategic movement, vanilla tactical handoff,
  casualty withdrawal, replenishment, and renewed attack lifecycle.
- `LastStand`: retain preparation, recruitment, assembly, and strategic march,
  but disable every automatic withdrawal path.
- `AbstractDecisive`: resolve a fully assembled city battle as a remote
  numerical-card duel before route generation; armies do not march or fight on
  the map. `Army.countUnits()` is the base card value, the commanding
  general's published strength is a bounded buff, and one attacker may face
  multiple defender armies. The larger adjusted value wins; close values use
  the deterministic weighted roll described below.

Unknown or missing values normalize to `Standard`. The selected value is read
once during runtime initialization so changing it cannot partially mutate an
active war.

## Shared Mission Integrity

Introduce one pure mission-target validator used by every mission producer and
consumer. Validation is mission-specific:

- `Attack` and `Assault` require an `OpenAttack` enemy city in the bound war.
- `Defend` requires an `OpenDefense` city owned by the army's kingdom.
- `Retreat` requires a safe city owned by the army's kingdom.
- `FrontHold` requires a controlled front-line city.

The validator is applied when the director proposes a mission, a player command
is accepted, a replenishment operation restores `PreviousOffensiveMission`, a
save is restored, the controller rechecks a running mission, and an abstract
battle is prepared. A city becoming friendly invalidates an offensive mission
immediately.

Invalidation clears the mission, route, installed-path state, target indexes,
vanilla tactical targets, and any stale restoration pointer as one operation.
The director is then notified to plan again. It must never reinterpret an
invalid offensive mission as a defensive mission, because that would hide the
source of the bad assignment.

## Standard Doctrine

`Standard` preserves the approved wartime lifecycle:

1. Vanilla preparation recruitment fills the army cap while filtering all
   heirs and current civil authorities, including kings, city leaders, and
   officials, out of ordinary conscription.
2. RTS assembles and moves the army from its own city toward the current enemy
   target city.
3. Entering the valid target city's zone releases the whole army to vanilla
   tactical AI. Enemy discovery is not required for handoff.
4. The army strength at the start of vanilla combat is recorded. At or below
   20 percent of that strength, RTS reacquires the army and returns it to a safe
   friendly city for synthetic replenishment.
5. At full replenishment, the prior offensive mission is restored only after
   shared mission validation. Otherwise the director selects a new target.
6. If the enemy is defeated without triggering withdrawal, vanilla remains in
   control until local combat is over, after which RTS selects the next valid
   strategic objective.

Synthetic levy actors are not real population. They remain excluded from civil
identity and are cleaned up by their existing lifecycle rules.

## Last Stand Doctrine

`LastStand` keeps preparation recruitment, army-cap filling, assembly, and RTS
strategic movement. RTS owns only the segment from a friendly city to the
current enemy target city zone. On entry, it clears AW3 movement ownership and
hands all members to vanilla tactical AI.

The doctrine disables all automatic withdrawal sources, including casualty
threshold, organization or supply checks, minimum-strength checks,
replenishment/regroup stalls, and the watchdog's final retreat fallback. It
does not disable a player-issued retreat command. A stale or newly friendly
attack target still fails mission validation; Last Stand is not permission to
attack friendly territory.

Vanilla military decisions are blocked only for actors currently owned by RTS.
After tactical handoff, global Harmony safety patches must allow vanilla army
combat decisions again. Native and AW3/Large scheduling both use this same
ownership predicate.

## Abstract Decisive Doctrine

### Battle formation

The director first completes normal preparation, recruitment, and complete army
grouping. Before route creation or controller activation, the authoritative
host groups battle candidates by `(warId, targetCityId)`.

Attackers are valid assembled armies targeting that enemy city. Defenders are
the city's canonical army, garrison, and special defenders, deduplicated by
actor ID. Every army and actor may be reserved by only one battlefield. Stable
sorting by target city ID and army ID determines reservation order.

No battle is resolved for `0/0`. If only one side has soldiers, that side wins.
If one side has at least 1.25 times the other's count, the stronger side wins
directly. Otherwise the result is a deterministic weighted roll using
`sideCount / totalCount`.

The roll seed combines war ID, target city ID, sorted participant army IDs, and
a persisted resolution sequence. Neither `UnityEngine.Random` nor
`System.Random` is allowed. The host rolls once and replicates the persisted
outcome; clients never recompute it.

### Territory and demobilization

An attacking victory transfers the target city to the valid attacking receiver.
A defending victory transfers only the primary attacker's owning city to the
defender. The primary attacker is the participating attacking army with the
largest battle count, breaking ties by army ID.

No loser is demobilized until the city transfer succeeds. A failed transfer
leaves both sides unchanged and retries or aborts safely. After a successful
transfer, only losing armies reserved for this battlefield are processed:

- real losing soldiers are removed by the controlled battle-demobilization
  path (the configured self-sacrifice outcome); they must not be left in the
  losing army or counted as living military actors;
- kings, heirs, city leaders, and officials retain valid civil authority but
  are protected from ordinary preparation recruitment and retain civil
  authority if a corrupted save places them in a losing roster;
- synthetic levy actors are deleted instead of becoming permanent residents;
- winning armies remain intact.

Persist the transaction phases `Prepared`, `Transferred`, `Demobilizing`, and
`Complete`. Each phase is idempotent, save-safe, and multiplayer-safe. Outcome
projection is also idempotent so reconnecting clients cannot duplicate city
transfer, demobilization, or notices.

## Runtime Boundaries

The doctrine gate lives in the shared rules/controller layer reached by both
scheduling modes. The abstract resolver runs after director grouping and before
route/controller work. The target validator is called at all ingress and
restore boundaries rather than being duplicated in each scheduling mode.

War membership, war type, diplomatic declaration, settlement, occupation, and
participant-roster rules remain authoritative outside this feature. The
doctrine may resolve a battle for Zhulu, rebellion, restoration, or ordinary
war, but it may not convert one war type into another or add participants.

## Diagnostics and Failure Handling

Diagnostics record doctrine, validation decision, war ID, army ID, source and
target city IDs, handoff/reacquisition reason, battle counts, deterministic
seed identity, transaction phase, and retry result. Logging is sampled and
must not scan the world on the Actor hot path.

Corrupt setting values use `Standard`. Missing cities, wars, kingdoms, armies,
or transfer receivers cause bounded cancellation and replanning, never a
friendly-city attack or partial demobilization.

## Verification

Tests must cover:

- mission validation at director, player command, replenishment, restore,
  runtime recheck, and abstract-resolution boundaries;
- a formerly enemy city becoming friendly before route install and during
  replenishment restoration;
- Standard 20-percent withdrawal, safe replenishment, and validated return;
- Last Stand tactical handoff plus every disabled automatic retreat source and
  the still-valid player retreat path;
- Native and AW3/Large parity;
- deterministic abstract count outcomes, 1.25 threshold, zero-side cases,
  reservation deduplication, stable primary attacker, transfer-before-retire,
  protected civil identities, synthetic deletion, transaction replay, and
  multiplayer host authority;
- ordinary, Zhulu, rebellion, and restoration wars without war-type mutation;
- full rules suite, net48 mod build, RTS adversarial simulation, source guards,
  and focused deployment-diff inspection.
