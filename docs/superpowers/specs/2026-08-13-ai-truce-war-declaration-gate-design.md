# AI Truce War Declaration Gate Design

## Problem

AI kingdoms can issue or execute a diplomatic war declaration while an
accepted truce is active. The ordinary target-selection and declaration issue
paths already query `DiplomacyProposalService.HasActiveWarBlocker`, but an
issued declaration executes through `TryStartNotifiedWarWithResult` with its
casus belli marked as locked. `WarDecisionService.StartWar` currently treats
that lock as permission to skip every mutable diplomacy check, including the
truce and non-aggression gate.

War-end reconciliation also clears pending declarations only for the main
directed war pair. Coalition settlement writes truces for every attacker and
defender pair, so a declaration owned by another participant pair can survive
the war end and later execute.

Mandate-taking and mandate-conquest diplomatic goals are represented as system
wars. The current pact rule exempts every system war, allowing these ordinary
external declarations to ignore an active truce.

## Required Behavior

- An active truce or non-aggression pact blocks a new diplomatic declaration.
- The same treaty gate is checked again at the instant an issued declaration
  attempts to start its war.
- Locking a declaration preserves its selected goal, target, timing, casus
  belli, and no-CB authorization. It does not lock treaty eligibility.
- Mandate-taking and mandate-conquest declarations obey truces and
  non-aggression pacts.
- Independence wars retain their existing treaty exemption.
- Genuine internal wars and rebellions that use the direct system-war path
  retain their existing exemption and do not enter the diplomatic declaration
  ledger.
- Successfully registering a truce cancels pending declarations in both
  directions for that kingdom pair and closes their associated preparation
  notices.
- Coalition settlement applies this cancellation to every attacker/defender
  pair for which a truce is registered.
- A declaration cancelled by a newly active treaty records
  `active_war_blocker` as its cancellation reason.

## Design

### Separate Declaration Locking From Treaty Validation

`WarDecisionService.StartWar` will no longer place the active treaty check
behind `ShouldRevalidateMutableEligibility`. The existing lock continues to
control mutable mandate-phase, vassal, alliance, casus-belli, and no-CB checks,
preserving the behavior introduced to keep issued declarations stable.

The treaty check becomes an always-current gate for every external war except
independence wars. The gate must not use `pSystemWar` as a blanket exemption,
because diplomatic mandate declarations currently carry that flag. Instead,
the exemption is based on the war route: independence remains exempt, while a
notified external declaration is not.

Direct calls to `TryStartSystemWar` remain unchanged. Those calls represent
rebellions, civil restoration, coups, and similar internal conflicts and do not
use `TryStartNotifiedWarWithResult`.

### Cancel Declarations When A Truce Is Registered

After `RegisterTrucePair` successfully inserts an accepted truce row, it will
cancel pending diplomatic declarations for that exact pair in both directions.
The cancellation uses the declaration service rather than editing kingdom data
directly so that the ledger lifecycle, compatibility projection, mobilization
notice, and last cancellation reason remain consistent.

The helper will be idempotent. If no declaration exists, it performs no state
change. Existing accepted truce rows are also treated as authoritative: when
registration finds an adequate row already present, it still reconciles pending
declarations before returning success. This covers save recovery and partial
runtime state left by older versions.

Coalition registration already iterates every opposing participant pair. Pair
reconciliation therefore belongs inside `RegisterTrucePair`, ensuring ordinary,
separate, legacy, and coalition settlement routes share the same behavior.

### Execution-Time Defense

`ProcessPendingRecord` may optionally reject an active blocker before notice
readiness work, providing an early cancellation path. The authoritative final
gate remains in `WarDecisionService.StartWar`, immediately before the engine
war start. This defense is required for restored saves, race-like annual
ordering, and any caller that reaches execution without passing through the
normal yearly precheck.

## Failure Handling

- Treaty rejection returns `active_war_blocker` from execution.
- The declaration ledger marks the record `cancelled` and stores that reason.
- `WarNoticeService.OnDiplomaticDeclarationClearing` closes mobilization and
  deployment state through the existing termination path.
- Failure to insert a truce keeps the current behavior: no declaration is
  cancelled because no treaty became authoritative.
- An exception during declaration reconciliation is logged but must not roll
  back an already committed truce. The execution-time gate remains the final
  protection.

## Testing

Rules and source-guard tests will cover:

1. A locked diplomatic declaration still requires live treaty validation.
2. An unlocked immediate war retains live treaty validation.
3. Mandate diplomatic declarations are blocked by an active treaty.
4. Independence wars remain exempt.
5. Direct internal system-war behavior is unchanged.
6. Registering a truce reconciles pending declarations in both directions.
7. Existing adequate truce rows also trigger reconciliation.
8. Coalition registration reaches every attacker/defender pair.
9. Cancellation uses `active_war_blocker` and closes the notice through the
   normal declaration termination path.

The focused rule suite, war/peace integration tests, source guards, and the
`net48` project build must all pass before deployment.

## Scope

This change does not redesign war target selection, declaration timing,
mobilization readiness, peace negotiation scoring, or RTS army behavior. It is
limited to enforcing accepted treaty state across diplomatic declaration
creation, persistence, cancellation, and execution.
