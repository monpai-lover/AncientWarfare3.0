# Enclosed Non-Land Zone Repair Design

## Goal

Extend the existing enclosed unowned Zone repair so a fully enclosed
non-land component is assigned to a nearby city under the same ownership
rules as an enclosed land gap.

## Behaviour

- A connected unowned component may contain land, water, or other non-land
  Zones.
- Every owned boundary Zone must belong to a live city in the same kingdom.
- Components touching the world edge remain unowned.
- Components larger than the existing 64-Zone traversal budget remain
  unowned.
- Mixed-kingdom enclosures remain unowned.
- The target city remains the city sharing the most boundary sides with the
  component. Distance to the component centre and city id remain the existing
  deterministic tie-breakers.
- Ownership continues to be applied through `City.addZone`.

## Implementation

Remove only the `containsGroundlessZone` rejection from
`EnclosedUnownedZoneRules.SelectComponentTargetCity` and its runtime call.
Keep world-edge detection, component traversal, ownership validation,
authority-cycle budgets, load sweep, and ownership-change hooks unchanged.

## Verification

- Add a rule test proving a groundless enclosed component selects a target
  city.
- Retain tests proving world-edge, mixed-kingdom, invalid-boundary, and
  over-budget components are rejected.
- Update the source guard so runtime repair no longer rejects a component for
  being groundless.
- Run the focused rules project, source guard, full rule suite, and Debug and
  Release builds before deployment.
