# Live City Zone Retention Design

## Problem

WorldBox marks a city for abandoned-zone validation whenever one of its
zones is removed. On the next city-zone update, `CityZoneAbandon.check` can
remove every zone that is no longer connected to a civic building and can
discard all but the largest connected component. A single explicit border
change can therefore cause a large, unintended territory rollback.

The existing AW3 empty-city patch only suppresses
`CityBehBorderShrink.execute`. It does not suppress the later
`CityZoneAbandon` cascade.

## Required Behavior

- A live city's zones must not be removed by automatic abandoned-zone
  cleanup.
- Explicit ownership changes remain valid. Border stealing, city-to-city
  transfer, war settlement, player scissors, and similar code may still
  remove or transfer the specific zones they target.
- City destruction remains valid and may clear the destroyed city's zones.
- The Xia technology zone allowance continues to limit future claims only;
  it does not reclaim zones a city already owns.

## Design

Add a pure rule that decides whether automatic abandoned-zone cleanup is
suppressed. The rule returns true only for a valid, non-destroyed city that
still owns at least one zone.

Expose the rule through `EmptyCitySurvivalService` and add a Harmony prefix
for `CityZoneAbandon.check`. When suppression applies, the prefix skips the
original cleanup method. It does not patch `City.removeZone`, `City.addZone`,
`City.destroyCity`, or the city manager's destruction entry point.

This keeps the intervention at the automatic rollback source and preserves
all explicit territory mutations.

## Failure Handling

Runtime object inspection is guarded. If city state cannot be evaluated, AW3
allows the original method to run rather than blocking engine maintenance for
an unknown object.

## Tests

- A valid live city with zones suppresses abandoned-zone cleanup.
- A destroyed, invalid, or zoneless city does not suppress cleanup.
- A source guard confirms the Harmony patch targets `CityZoneAbandon.check`.
- A source guard confirms AW3 does not patch `City.removeZone` or
  `City.destroyCity` for this behavior.
- Existing empty-city survival tests remain green.

## Non-Goals

- Reassigning disconnected zones to a different city.
- Restoring zones already removed in an existing save.
- Changing zone growth limits or claim rates.
- Preventing explicit razing, conquest, settlement, or player edits.
