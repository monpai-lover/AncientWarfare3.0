# Kingdom Atlas Vassal Display Design

## Goal

Show a vassal as an independent realm in Kingdom Atlas while inheriting its suzerain's historical map color. The vassal keeps its own territory boundary and historical country label.

## Current Problem

Atlas nodes retain only physical city owners and event-party colors. A vassal therefore renders with no historical suzerain color and is not labelled unless it is one of the two event parties. The runtime hierarchical map mode is unsuitable because it deliberately merges subject territory into the suzerain.

## Design

`KingdomAtlasNode.CityOwners` remains the physical owner map. A new node-level relation snapshot collection records the direct vassal-to-suzerain edges valid at the node's world time. A deterministic resolver follows those edges upward, with a visited set and a fixed maximum depth, to select the root suzerain's historical color. The resolver never changes the owner ID.

Visible owners are the event parties plus their historical vassals at that node. Rasterization resolves each owner's display color from the node snapshot, while boundary detection continues to compare physical owner IDs. Label generation uses the owner's own historical name and the resolved display color. Tributary contracts use the same subject-color rule as formal vassals so all subject relations are visually consistent.

Relations are read only from the persisted `VassalRelation` table. A row is valid when `START_TIME <= nodeTime` and `END_TIME < 0 || END_TIME >= nodeTime`; `ACTIVE` is not used to reject closed historical rows. If a relation is missing, invalid, or cyclic, the resolver falls back to the last valid owner in the chain, then to the owner's own historical color.

## Testing

Pure rules tests cover independent owners, direct and nested vassals, relation windows, cyclic relations, and display-owner visibility. Raster tests verify that two adjacent subject realms share a color while retaining separate boundaries and labels.
