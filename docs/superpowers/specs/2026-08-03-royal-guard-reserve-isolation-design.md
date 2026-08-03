# Royal Guard Reserve Isolation Design

## Goal

Royal guards never use the city reserve pool or synthetic wartime levies.
They are replenished only by `RoyalGuardService` from living eligible actors.

## Runtime Rules

- A royal-guard Army is ineligible for `ArmyReplenishmentOperationService`.
- When an Army becomes or is recognized as a royal-guard Army, any persisted
  ordinary-army replenishment operation and RTS replenishment latch are cleared.
- The RTS controller does not calculate, request, or wait for reserve-pool
  replenishment for royal guards.
- `RoyalGuardService` remains the sole owner of guard recruitment. It does not
  create synthetic actors and retains its existing candidate eligibility,
  recruitment budget, peace/emergency gates, and lifetime-service rules.

## Presentation

Army map information for royal guards omits both replenishment shortage and
available reserve supply. The guard's name, commander, member count, and current
guard task remain visible.

## Safety

- Ordinary city armies retain the current notice and wartime reserve behavior.
- Royal guards already serving in a guard Army are not dismissed or converted.
- Old saves with stale ordinary-army replenishment metadata on a guard Army are
  repaired lazily on the authoritative RTS cycle.
- Multiplayer replicas consume the host projection and never initiate local
  guard replenishment.

## Verification

- Pure rules tests reject reserve-pool display and replenishment for royal
  guards while preserving ordinary armies.
- A regression test covers cleanup of a stale guard replenishment operation.
- The focused RTS, reserve-pool, royal-guard, and replenishment suites pass.
- The main project and NML runtime compile without errors.
- In game, an understrength royal guard shows no reserve-pool fields and gains
  members only when `RoyalGuardService` appoints real candidates.
