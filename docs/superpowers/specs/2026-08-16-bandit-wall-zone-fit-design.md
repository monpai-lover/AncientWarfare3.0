# Bandit Stronghold Wall-Zone Fit Design

## Goal

Make newly created bandit stronghold territory approximately match the land enclosed by its wooden wall instead of transferring every zone whose center happens to fall inside the wall's outer bounding rectangle.

Existing saved strongholds retain their persisted `FixedZoneKeys` and current zone ownership. This change only affects strongholds planned after the update.

## Root Cause

The wall planner follows the connected city land around the civic buildings, so its shape can be irregular and coastal. The stronghold planner currently discards that geometry and computes only `minX`, `maxX`, `minY`, and `maxY` from the final wall tiles. A complete zone is then marked inside whenever its center tile lies inside that rectangle.

This center-point test transfers edge zones even when most of their tiles are outside the actual wall, causing stronghold territory to extend far beyond the visible fortification.

## Geometry Contract

`CultiwayStyleWallGeometryRules` will expose the bounded connected land used to generate the wall boundary. This enclosed-land set is calculated before gates are carved, so intentional three-tile entrances do not make the logical interior leak into exterior land.

`CultiwayStyleCityWallService` will return a detailed plan containing:

- the final placeable wall points;
- the bounded connected land enclosed by that wall plan.

The existing wall-only API remains available for callers that do not need territory geometry.

## Zone Eligibility

Each mother-city zone is represented by:

- its stable zone key;
- the number of zone tiles present in the wall plan's enclosed-land set;
- the zone's total tile count;
- neighbouring mother-city zone keys.

A zone is eligible only when its wall-enclosed tile count is strictly greater than half of its total tile count:

```text
enclosedTileCount * 2 > totalTileCount
```

Exactly 50 percent is exterior. Null or empty zones are exterior.

Starting from the city center zone, the existing breadth-first selection keeps only connected eligible zones. If the city center zone is not eligible, the nearest eligible zone becomes the seed. Disconnected eligible islands are not transferred.

The split remains valid only when at least one zone transfers to the stronghold and at least one zone remains with the mother city.

## Persistence

The selected keys continue to be stored in `PeasantRebelBanditStrongholdState.FixedZoneKeys`. Runtime acquisition restrictions remain unchanged.

No load-time recalculation or migration is introduced. Existing strongholds preserve their saved keys and territory.

## Testing

Pure rule tests will cover:

- a zone with more than 50 percent enclosed tiles is selected;
- a zone with exactly 50 percent is rejected;
- a zone with a center-like relationship but minority enclosed coverage is rejected;
- only connected majority-enclosed zones transfer;
- empty zones are rejected.

Geometry tests will verify that the exposed enclosed-land set is the same bounded connected land used to generate the wall and is unaffected by gate carving.

A source guard will require the stronghold planner to count zone tiles against the detailed wall plan and will reject the old wall `min/max` center-point classification.

After the focused tests pass, build the net48 project, deploy the source tree with a timestamped backup, launch WorldBox visibly, and confirm the mod and wall-related patches load without compilation or runtime exceptions.
