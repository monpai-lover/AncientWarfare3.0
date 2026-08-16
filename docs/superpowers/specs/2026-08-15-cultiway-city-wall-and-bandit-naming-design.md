# Cultiway-Style City Walls And Bandit Naming Correction

## Goal

Replace the incomplete border-zone wall placement used by bandit governments
and the Mandate border-defense decision with one shared Cultiway-style city
wall creator. Correct rebel and bandit realm names so they use a dedicated
Chinese outlaw-name-root library and swap the visible `义军` / `贼` suffix
without stacking suffixes.

## Confirmed User Decisions

- Follow the current `Cultiway-Reborn-master` city-wall behavior rather than
  the earlier national-border outline.
- Every city receives its own wall.
- Keep Cultiway-style road and dock gates.
- Bandits and the Mandate border-defense decision must call the same wall
  creation tool.
- Bandit names use a dedicated Chinese root library. The persisted root is
  combined with either `义军` or `贼`.

## Reference And Attribution

The wall geometry is adapted from:

- `Cultiway-Reborn-master/Source/Content/WallShapeHelper.cs`
- the wall placement portion of
  `Cultiway-Reborn-master/Source/Content/Plots.cs`

Those files are under Cultiway-Reborn's MIT-licensed `Source` directory. The
implementation must update `THIRD_PARTY_NOTICES.md` and the packaged notice
collection to cover the adapted wall code. AW3 will own an adapted copy under
its namespace and will not acquire a runtime dependency on Cultiway.

## Shared Wall Tool

Add a focused shared `CultiwayStyleCityWallService` with an entry point that
accepts:

- the owning `City`;
- the original `TopTileType` to place;
- wall width;
- whether road and dock gates are carved.

The service returns the complete set of wall coordinates and the count of
tiles whose wall type changed. Callers own their policy-specific persistence
and repair state.

For each city the service performs the same geometry stages as current
Cultiway master:

1. Compute the building bounds while ignoring remote windmills, mines, and
   crop buildings farther than 16 tiles from the hall, bonfire, or city
   center.
2. Clamp the building half-width and half-height to 3 through 60, then add a
   three-tile inner-wall margin.
3. Collect non-water tiles from the city's zones and retain only the
   four-directionally connected component containing, or nearest to, the city
   center.
4. Intersect that connected land with the wall bounds and flood the exterior.
5. Peel the requested number of outer boundary layers.
6. Seal diagonal-only joins so units cannot pass through corner gaps.
7. Prefer gates near city roads, add directional fallback gates, and carve
   dock passages near actual dock buildings. Gate width remains three tiles.
8. Ensure passable exterior land can reach the city core; if necessary, carve
   the minimum wall crossing into a three-tile passage.
9. Place the requested original wall asset through
   `WorldTile.setTopTileType`. Do not use `MapAction.terraformTop`, remove
   buildings, or mutate city zones.

The tool accepts roads, buildings, and existing top tiles on wall coordinates,
matching Cultiway's direct placement behavior. It rejects tiles outside the
city, water, mountains, and summits at final placement.

## Bandit Integration

On successful formal bandit entry, iterate every valid currently owned city
and call the shared tool with:

- `TopTileLibrary.wall_wild`;
- width `1`;
- road and dock gates enabled.

Union and sort all returned coordinates, then persist them through the
existing `MANDATE_REBEL_BANDIT_WALLS` field. The annual bounded repair loop
continues to use those fixed recorded coordinates. It restores only missing
`wall_wild` tiles and does not recompute geometry, expand walls with later city
growth, remove old walls, or replay entry behavior during save restoration.

Disconnected cities therefore receive independent walls, matching Cultiway's
per-city model. The previous scan of `City.border_zones` is removed.

## Mandate Border-Defense Integration

Keep the existing selection of at most three Mandate-system border cities,
border guards, border armies, and watchtower construction. Replace only
`BuildBorderWalls` with the shared tool, passing:

- `TopTileLibrary.wall_order`;
- width `2`;
- road and dock gates enabled.

The former 24/40 segment wall caps and every-ninth-tile gap rule are removed
from wall creation because they necessarily produce incomplete rings. One
decision or Mandate-war response completes the full wall for every selected
border city. History reports the actual number of wall tiles changed. Existing
watchtower caps and placement remain unchanged and separate from the shared
wall tool.

## Chinese Rebel And Bandit Names

Add a UTF-8 word library dedicated to outlaw name roots. It contains roots,
not complete realm names, for example `赤眉`, `黄巾`, `绿林`, `黑风`, `白莲`,
`飞虎`, and `青龙`. Load it through AW3's existing integrated naming resource
pipeline and select deterministically from kingdom ID, founder ID, and creation
year.

Before composing a route name, normalize the stored root by repeatedly
removing trailing `义军` and `贼`, then trim whitespace. A valid root must be a
member of the dedicated library and contain Chinese characters.

For a new manual or generated peasant rebel, persist one root from the library
before applying a route name. Existing saves keep a valid library root. An
empty, non-Chinese, or non-library legacy root is replaced the next time the
government enters the rebel or bandit transition path. Once repaired, the
root remains stable across later transitions.

Visible names are always:

- founding rebel: `<root>义军`;
- formal bandit: `<root>贼`.

Switching governments changes only the suffix. It cannot produce forms such
as `<root>义军贼` or `<root>贼义军`.

## Authority, Failure, And Save Behavior

- Multiplayer authority checks occur before wall, name, route, policy, or
  persistence mutations.
- Missing wall assets, missing city bounds, empty connected land, or missing
  name-library data fail closed and log one concise warning.
- Bandit entry preflights all required data before ending wars or changing the
  government class.
- Save restoration reads persisted route, name root, wall coordinates, and
  territory whitelist without rebuilding walls or rerolling valid names.
- The shared wall tool has no static per-world cache. A computation context is
  scoped to one city invocation so world reloads cannot retain stale tiles.

## Tests And Acceptance

Detached wall-geometry tests cover:

- connected city land and exclusion of detached zone islands;
- one- and two-layer closed boundaries;
- diagonal-gap sealing;
- road-priority and fallback gates;
- dock passages;
- minimum core-reachability passage;
- building, road, and existing-top-tile coordinates remaining eligible;
- water, mountain, summit, and foreign-city coordinates being rejected.

Naming tests cover deterministic root selection, Chinese/library membership,
legacy Latin-root replacement, suffix stripping, stable repeated transitions,
and exact `<root>义军` / `<root>贼` composition.

Runtime source guards require both bandit and Mandate wall paths to call the
shared tool, forbid their old `border_zones` wall loops, preserve original
`wall_wild`, `wall_order`, and `setTopTileType` use, and forbid copying
`MapAction.terraformTop` behavior.

Acceptance in WorldBox:

1. A multi-city bandit realm displays a separate Cultiway-style wooden wall
   around every city, including diagonal closure and visible road/dock gates.
2. A Mandate border-defense decision completes double-layer stone walls around
   each selected border city in one execution while retaining existing border
   guards and towers.
3. A manual ordinary-to-rebel-to-bandit sequence produces a Chinese
   `<root>义军`, then the same `<root>贼`, and switching back restores the same
   `<root>义军`.
4. Reloading a save does not reroll names, duplicate walls, end wars, or replay
   bandit entry.
