# Disable Army Supply Design

## Goal

Temporarily disable the RTS Army supply mechanic because it can trap armies
in logistics-driven retreat and regroup loops. Keep organization, casualties,
rallying, replenishment, movement, and combat behavior active.

## Runtime Semantics

- Add one authoritative rule that reports whether Army supply simulation is
  enabled. It is disabled for this release.
- When disabled, every RTS consumer receives an effective supply value of
  100, regardless of a previously persisted or runtime value.
- Supply does not drain during march, pursuit, assault, or isolation.
- Supply corridors and supply connectivity do not affect state transitions,
  director force estimates, pursuit eligibility, retreat, or regroup.
- Organization remains active. Casualties and captain loss may reduce it;
  regrouping, nearby support, and uninterrupted marching may restore it.
- Regroup completion still requires sufficient organization and operational
  force, but not a supply threshold.

## Compatibility

Existing supply fields, indexes, mission APIs, and save data remain intact.
No save migration or schema change is introduced. Re-enabling the feature in
a later release requires changing the single authoritative rule rather than
reconstructing removed code.

On runtime rebuild, stale low supply values are harmless because all consumers
use the normalized effective value while the feature is disabled.

## Implementation Boundary

The switch belongs in pure Army logistics rules. Runtime services normalize
supply at the points where it is updated or exposed. State-transition helpers
must explicitly ignore supply gates when the feature is disabled so unit tests
cover behavior without relying on WorldBox runtime objects.

No setting button is added in this change. This is a temporary release-wide
disable, not a player-selectable simulation mode.

## Verification

- A failing rule test first demonstrates that disabled supply normalizes zero
  supply to 100 and allows an otherwise-ready Army to finish regrouping.
- Existing organization tests continue to prove casualty loss and regroup
  recovery behavior.
- Army RTS rule suites and adversarial simulations must pass.
- The main mod project must build with zero errors.
- Deployment copies only changed source files; no DLL is deployed.
