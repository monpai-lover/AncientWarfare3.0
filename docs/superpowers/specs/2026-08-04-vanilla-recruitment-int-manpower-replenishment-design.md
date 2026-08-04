# Vanilla Recruitment and Integer Manpower Replenishment Design

## Goal

Remove AW3's proactive temporary-levy recruitment system, restore WorldBox's
native army recruitment, and retain only AW3 wartime replenishment.

## Recruitment Ownership

- WorldBox owns ordinary enlistment, army creation, and peacetime recruitment.
- AW3 no longer runs annual levy recruitment, monthly preparation recruitment,
  emergency levy recovery, or actor-based reserve selection.
- War preparation may still stage existing armies, but it cannot convert
  civilians into soldiers or enqueue levy recruitment work.

## Manpower Pool

Each city has only integer wartime manpower state:

- available capacity is derived when the city enters a formal war;
- consumed manpower is deducted atomically when replenishment is approved;
- the pool stores no actor ids, eligible-actor sets, cursors, or profession
  tracking state;
- ending the relevant war clears the wartime capacity and consumption state.

Actor adulthood, death, profession, city, and kingdom transitions do not
maintain reserve membership.

## Replenishment

AW3 replenishment remains bounded and army-specific. It reserves at most the
requested integer manpower, creates exactly that many soldier actors when
creation succeeds, assigns them to the target army, and releases any reserved
amount that failed to materialize. No real civilian actor is converted.

Generated replenishment soldiers keep the existing synthetic-history
suppression and source-city ledger metadata so their creation and removal do
not pollute genealogy or double-count city population.

## Save Compatibility

Old actor reserve membership and temporary levy state are ignored. Runtime
restore clears legacy levy queues and safely removes legacy temporary flags.
Existing integer wartime reserve snapshots may be read, but new snapshots must
not serialize actor-id membership.

## Performance Requirements

- `aw3_month_preparation_levy` no longer runs.
- annual war work does not call `TemporaryLevyService.OnKingdomYear`.
- war notice/emergency changes do not enqueue `levy_*` deferred work.
- city reserve authority processing performs no population-wide actor scan.
- replenishment remains bounded by its existing per-cycle army and spawn
  limits.

## Verification

Automated source guards and rules tests prove that annual/monthly levy entry
points are disconnected, the reserve pool is integer-only, replenishment still
reserves manpower and creates soldiers, and legacy actor reserve hooks are not
active. The full rules suite must remain green. The main mod DLL is not built.
