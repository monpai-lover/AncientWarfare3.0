# Shi Lineage Mapmode Design

## Goal

Add a family-power mapmode that mirrors the historical-school mapmode while
showing which Shi branch currently has influence in each city. Selecting a
city shows its ranked local Shi branches and weighted influence shares.
Selecting any displayed branch opens that branch's existing full genealogy.

## Scope

This feature covers the mapmode option, city colors, nameplates, tooltips,
city selection bottom bar, focused-branch mode, local influence snapshots and
the genealogy navigation entry. It does not create a second genealogy UI,
change Shi membership, or change lineage ownership rules.

## Data Model

Each live city has a runtime `CityShiInfluenceSnapshot` containing:

- city ID and snapshot generation;
- weighted totals by Shi branch ID;
- dominant branch ID;
- stable ordered top branches;
- total weight and percentage helpers.

The data source is the current city's live residents, grouped by `SHI_ID` and
resolved through existing `ShiBranch` records. A branch is a Shi branch, not
just a surname, so same-surname branches remain separate.

Weights are mutually exclusive per actor and use the highest applicable role:

| Resident role | Weight |
| --- | ---: |
| ordinary member | 1 |
| noble | 2 |
| local or in-city central official | 4 |
| heir | 6 |
| city leader | 8 |
| king | 10 |

The dominant branch is selected by total weight. Stable ties compare highest
single-member weight, living-member count, branch creation time and Shi ID.
Percentages use branch weight divided by total city Shi weight. Cities with no
valid Shi members have a neutral result.

## Runtime Index and Invalidation

The snapshot service uses the existing resident/lineage runtime indexes and
does not query the full archive database from map drawing code. It maintains a
bounded dirty queue and a small demand queue for selected or hovered cities.

Snapshots are invalidated when a resident is born, dies, changes city,
changes Shi branch, gains or loses noble status, changes office, becomes or
ceases to be king/heir/city leader, or when city ownership changes. World load,
world reset and multiplayer restore clear the runtime cache and rebuild it on
demand. Map activation requests bounded city refresh work, matching the
school mapmode's frame budget.

## Mapmode Presentation

Add a separate mapmode power and map metadata entry. Its interaction and
visual layout mirror `SchoolMapModeService`:

- overview mode colors a city by its dominant branch using a stable branch
  color;
- city nameplates show the dominant branch and its share;
- tooltips show the dominant branch and the top three branches with shares;
- clicking a city opens the matching bottom bar with city identity,
  dominant branch and the top-three composition;
- selecting a branch enters focus mode, coloring cities by that branch's share
  and using a neutral color where it is absent;
- no-branch cities display a localized no-Shi-influence result;
- a one-branch city displays 100 percent.

Colors are deterministic from Shi ID and cached by color key. The mapmode
must dirty the zone/map cache through the same throttled path as the school
mapmode and must clear focus, selection and bottom-bar state when disabled or
when another mapmode takes over.

## Genealogy Navigation

Every branch row in the selected-city bottom bar, including the dominant
branch and each top-three entry, is a clickable command. The command calls:

```csharp
FamilyTreeWindow.OpenBigTree(shiId)
```

No new genealogy window or alternate branch identifier is introduced. Invalid
or missing Shi IDs render as non-clickable rows and never close the mapmode or
throw. The existing family-tree window remains responsible for historical and
deceased-node rendering.

## Performance and Failure Handling

- No world-wide actor scan occurs during map drawing.
- Snapshot rebuilds are bounded per frame and selected-city demand has
  priority over background refresh.
- A missing archive branch or invalid actor is ignored for that contribution;
  one malformed resident cannot block the city snapshot.
- Stale snapshots are marked neutral or demand-rebuilt rather than throwing.
- Multiplayer replicas consume authoritative resident/lineage state and do
  not create divergent branch colors or snapshots.

## Tests

Pure rule tests cover role-weight precedence, dominant-branch ties, percentage
boundaries, no-branch cities and stable branch ordering.

Source/integration guards cover map registration, metadata selection,
nameplates, tooltip top-three output, focus colors, dirty-queue processing,
city invalidation and bottom-bar branch rows calling
`FamilyTreeWindow.OpenBigTree(shiId)`. Runtime smoke coverage verifies that a
selected branch opens the genealogy and that invalid IDs are safely ignored.

