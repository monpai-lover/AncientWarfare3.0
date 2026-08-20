# De Jure State Planning and Occupation-Stable Administration Design

## Status

Approved design for implementation planning. This feature introduces a save-
scoped, player-controlled de jure state registry. A county in the terminology
of this mod is the existing `city` unit. A state is a group of counties/cities.
The country remains the top level:

```text
country
  -> state
    -> county (city)
```

The existing hierarchical map mode is out of scope for UI changes. It will
continue to use its existing screens and interactions while consuming the
stable state data through its current read path.

## Goals

- Keep a state's legal/de jure membership stable when cities change hands.
- Allow a player to create a new de jure state with a divine power.
- Allow a player to move a city from one de jure state to another with the
  same divine-power entry and a two-click map operation.
- Allow a state to span multiple current countries after occupation or
  partition.
- Keep de jure planning separate from actual control, war settlement, and
  territory-change history.
- Reuse the current court regional folders and `CourtActorNodeView` cards.
- Prevent expanded regional court cards from overlapping and hiding school
  information.
- Preserve custom court and local-government templates without storing world-
  specific city IDs in shareable JSON.

## Non-goals

- No new map-mode screen, zoom model, or map interaction.
- No automatic de jure redrawing after conquest, peace, succession, country
  extinction, bandit mutation, or temporary occupation.
- No AI-driven de jure editing in the first implementation.
- No new diplomatic, war-score, or territorial-settlement mechanics solely
  for this feature.

## Terminology and Invariants

### Terminology

- **County / commandery (`city`)**: the lowest administrative unit already
  represented by a WorldBox city. It is not a new runtime entity.
- **De jure state**: a save-persisted grouping of one or more city IDs. It has
  a legal name and a legal capital city.
- **Actual control**: the current `city.kingdom` relationship. It is mutable
  through the existing war and settlement systems.
- **Administrative projection**: the view of a de jure state filtered through
  the cities controlled by one current kingdom.

### Invariants

1. A city belongs to at most one active de jure state at a time.
2. An active de jure state has at least one city.
3. The legal capital must be an active member of its state.
4. A de jure state may span multiple actual kingdoms.
5. Changing `city.kingdom` never changes de jure membership.
6. Destroying a kingdom never deletes de jure state records.
7. Only an explicit operation through the de jure planning divine power may
   change de jure membership.
8. Historical state records are retired, not physically deleted.
9. De jure events are not territory-change events.

## Save-Scoped Data Model

The implementation adds a save-level `DeJureAdministrationStore` owned by the
world-save integration already used by the mod. It is not stored on `Kingdom`
and is not exported with custom court templates.

```text
DeJureAdministrationStore
  SchemaVersion
  NextRegionId
  StoreRevision
  Regions[]
  ChangeHistory[]
  OrphanedRecords[]

DeJureRegion
  RegionId
  RegionName
  SeatCityId
  MemberCityIds[]
  CreatedYear
  CreatedByKind
  CreatedByKingdomId
  Version
  Active

DeJureRegionChange
  ChangeId
  RegionId
  CityId
  FromRegionId
  ToRegionId
  Reason
  Year
  ActorId
  Version
```

`RegionId` is stable within a save and is never reused. `CreatedByKingdomId`
is historical metadata only; it is not an ownership lock. `StoreRevision`
increments on every successful planning transaction and is part of all read
cache keys.

### Indexes

The store maintains derived indexes rebuilt after load and updated on each
transaction:

```text
CityId -> RegionId
RegionId -> DeJureRegion
RegionId -> sorted member CityIds
```

The indexes are not authoritative save data. If an index is corrupt, it is
rebuilt from the region records without changing the legal model.

## Initial Migration

For a save without a de jure store:

1. Read the current valid cities.
2. Run the existing `RegionalGovernmentAggregationService` once.
3. Convert each result into a de jure state.
4. Create a single-city state for every city not returned by aggregation.
5. Validate unique membership and legal capitals.
6. Persist the migrated store and a migration marker.

Migration is one-time. Once a city has an explicit de jure record, future
occupation or aggregation calls cannot replace it. The old aggregation service
remains available only for migration and for proposing an initial grouping for
newly discovered, unassigned cities.

## Player Operations

### One divine-power entry with two modes

De jure editing uses one GodPower button and the existing multi-toggle/cycle
option pattern. It does not create a planning window, target picker, or naming
dialog. The button cycles between:

```text
Create de jure state
Assign county to de jure state
```

Changing mode, cancelling the power, closing the power tab, or changing worlds
clears every pending click selection.

### Mode 1: create de jure state

1. Select `Create de jure state`.
2. Click any existing city.
3. Remove the city from its previous state.
4. Create a new state with the clicked city as its only member and legal
   capital.
5. Generate the state name from the existing capital-city regional naming
   rules and the active court state-level title; do not open a naming window.
6. Retire the previous state if it has no remaining members.
7. Commit the transaction and invalidate affected caches.

Creation does not alter `city.kingdom`, borders, diplomacy, or war state. A new
state may begin with one city and may be created on a city controlled by any
kingdom.

### Mode 2: assign county to de jure state

This mode is a two-click state machine:

```text
AwaitCapital -> AwaitCounty -> AwaitCounty ... -> Cancelled
```

1. The first click must be the legal capital city of an active state. It locks
   that state as the assignment target.
2. The second click selects the city to move into the locked target state.
3. A successful assignment keeps the same target locked, allowing repeated
   second clicks to add more cities without clicking the capital again.
4. Clicking another valid legal capital replaces the locked target.
5. Clicking the currently locked capital does not move it; it confirms/switches
   the target and provides selection feedback.
6. Cancelling the power clears the locked target.

The power displays native cursor/toast feedback for `select a legal capital`,
`target state selected`, `county assigned`, and validation failures. It never
opens an additional planning window. Selecting a non-capital on the first click
does nothing and explains that a legal capital must be selected first.

### City window

The city window may display read-only de jure information already needed by
the court/map read model: current state, legal capital, actual controller, and
control share. It contains no create, assignment, capital-change, or target-
selection controls.

### Capital rules

- A new state always uses its clicked city as capital.
- If the capital is occupied, it remains the legal capital and is marked
  occupied; it is not silently replaced.
- If the capital city is destroyed, the state remains active with a missing
  capital marker. The state cannot be selected as an assignment target until a
  future dedicated capital-repair operation exists or a new state is created.
- When an assignment moves the capital city of the source state, the source
  state deterministically promotes its highest-development remaining member;
  population and stable city ID are tie-breakers. This automatic repair is part
  of the same atomic transaction and is recorded as `DeJureSeatChanged`.

### Planning constraints

- A city may be moved even when controlled by another kingdom.
- States do not have to be geographically contiguous; non-contiguous states
  show a warning but remain valid.
- A target must be active and must contain at least one city after the move.
- A historical/retired state cannot receive new members.
- A city cannot be assigned twice in the same transaction.
- Every click operation is atomic. There is no confirmation window; failed
  validation leaves both saved membership and the locked target unchanged.

## Occupation and Country Views

The legal and actual relationships are deliberately separate:

```text
legal:  city -> DeJureRegionId
actual: city -> city.kingdom
```

If kingdom B occupies part of a state historically administered by kingdom A:

- the city keeps its `DeJureRegionId`;
- A sees the complete state with a lost-member count;
- B sees the same state ID with only B-controlled members in its projection;
- B may administer its controlled members with its own court/local templates;
- neither view creates a duplicate legal state;
- the legal capital remains unchanged even when not controlled.

The current country is the owner of an administrative projection, not the
owner of the legal state record. A legal state with no cities controlled by a
country does not appear in that country's court projection.

### Shared state label and control tooltip

The same legal state name is rendered for every controlled portion of the
state. A city controlled by A and a city controlled by B both display the
same `RegionName`; the label is never replaced with a country-specific name.

The existing hierarchical-map tooltip is extended with control data without
adding a new map screen or interaction:

```text
De jure state: Yongzhou
Legal members: 6 counties
A control: 3 counties (50%)
B control: 3 counties (50%)
Capital: controlled by A
```

The denominator is the total number of active legal members in the state. Each
current controlling kingdom is listed with its count and percentage, ordered
by count and then stable kingdom ID. When the map view is already scoped to a
kingdom, that kingdom's line is emphasized while the other controlling
kingdoms remain visible. Tooltip data is read from the saved legal membership
and current `city.kingdom` values; it never changes state names, colors, or
membership.

## Court Integration

### Read model

Add a `DeJureRegionReadModelService` with country and city projections. It
groups the current kingdom's cities by the saved `DeJureRegionId`, rather than
rebuilding groups from adjacency:

```text
RegionId
RegionName
RegionTitle
GovernorTitle
SeatCityId
ControlledMemberCityIds
TotalMemberCount
ControlledMemberCount
IsSeatControlled
HasForeignDeJureMembers
```

The existing `RegionalGovernmentAggregationService` remains for migration and
unassigned-city suggestions. It is not allowed to overwrite persisted legal
membership.

### Governor and local offices

- The governor projection belongs to the current controlling kingdom's court.
- Only controlled cities produce local-government officer cards.
- Foreign de jure members contribute counts/status but never duplicate local
  officer cards.
- If the legal capital is outside the current kingdom, the region shows
  `capital not controlled` and chooses a controlled member as a temporary
  administrative anchor for layout only.
- The temporary anchor never changes `SeatCityId`.
- Existing civilian, military, and custom local-government templates continue
  to apply to the actual controlling kingdom.
- Custom central-court JSON changes titles, offices, management edges, and
  layout only; it does not contain city membership or region IDs.

### Card reuse and duplicate prevention

The regional folder continues to use the existing `CourtActorNodeView`. City
officers are excluded from the central duplicate pool and are rendered once
under the relevant regional projection. No second miniature card type is
introduced.

## Expanded Regional Folder Layout Repair

The current screenshot shows expanded commandery cards overlapping because the
rendered card width is larger than the fixed horizontal slot. This is a
required prerequisite for the court integration phase.

The folder layout must:

- allocate each `CourtActorNodeView` its own fixed grid cell;
- derive cell dimensions from the actual scaled card bounds;
- wrap cards by available content width instead of using negative offsets;
- use a deterministic maximum column count (three columns for the current
  wide window) and create additional rows as needed;
- size the folder content from row count and measured row height;
- place following regional folders from the previous folder's measured bottom;
- keep school icon, office name, and action buttons inside the cell;
- reuse the existing card instance/type and avoid duplicate card creation;
- use a content scroll area when expanded content exceeds the window;
- rebuild management-line endpoints only after the final layout pass.

Acceptance examples:

- two cards are fully separated;
- three cards render as `3 x 1`;
- six cards render as `3 x 2`;
- multiple expanded folders never overlap;
- repeated expand/collapse and window reopening produce stable positions.

## Existing Hierarchical Map Mode

No map-mode UI or interaction is added. The existing hierarchy map continues to
use its current navigation and rendering. Its data provider must resolve state
membership through the saved de jure store so that occupation does not cause a
new grouping, but the visible controls and screen layout remain unchanged. The
existing tooltip is the only presentation surface extended: it shows the
shared legal state name plus controlled counts and percentages for all current
kingdoms.

## History and Territory Separation

Planning changes use independent events:

```text
DeJureRegionCreated
DeJureCityTransferred
DeJureSeatChanged
DeJureRegionRenamed
DeJureRegionRetired
```

These events are available to the atlas/chronicle readers but are not territory
change nodes. They do not modify war score, peace settlement, diplomacy, or
actual kingdom borders. Existing conquest and peace events remain the source
of actual-control history.

## Persistence and Error Recovery

Save integration follows the existing world-save lifecycle:

- load restores the store before court/map read models are built;
- successful player operations increment `StoreRevision` and mark world data
  dirty;
- normal world saves persist the store;
- a save failure does not clear the in-memory store;
- a parse failure falls back to the last valid snapshot;
- malformed records are retained in `OrphanedRecords` instead of silently
  discarded;
- structured failures use `LogError`.

Load validation covers duplicate IDs, duplicate city membership, missing cities,
invalid capitals, empty states, invalid history references, and ID-counter
collisions. Repairs preserve history and never silently redraw a legal state.

## Performance and Cache Invalidation

- Maintain a save-level `CityId -> RegionId` index.
- Cache court projections by `KingdomId + StoreRevision`.
- A planning transaction invalidates only the source and target states and the
  affected kingdoms' court projections.
- A city conquest invalidates only the old and new controlling kingdoms.
- No full-map legal regrouping runs when opening a court window.
- Existing dynamic aggregation cache remains limited to migration/candidates.

## Test Matrix

### Rules

- one city cannot belong to two active states;
- new state starts with one capital city;
- moving a city updates both states atomically;
- capital cannot be assigned outside its state;
- an empty state becomes historical;
- each planning operation creates the correct independent event.

### Occupation

- one city of a state changes kingdom;
- both countries resolve the same legal state ID;
- the original country reports loss;
- the occupying country reports foreign legal members;
- occupation does not alter membership or capital.
- both sides render the same legal state name;
- the tooltip reports each controller's count and percentage against the full
  legal membership.

### Save/load

- legacy save migrates once;
- reload does not duplicate states;
- extinct kingdoms do not remove states;
- succession does not redraw states;
- malformed records do not crash the world;
- save failure preserves the in-memory model.

### Court/UI

- city officers appear once;
- foreign members do not create duplicate cards;
- missing legal capital is displayed without silent migration;
- custom court templates change presentation only;
- two, three, and six expanded cards never overlap;
- multiple folders and repeated expand/collapse remain stable.

## Implementation Sequence

1. Add the save model, serializer, schema version, migration, validation, and
   indexes.
2. Add the de jure read model and replace persisted-region reads while keeping
   legacy aggregation for migration/candidates.
3. Add the two-mode divine-power state machine, native feedback, atomic
   creation/assignment, and history; keep the city window read-only.
4. Apply court integration, foreign-member status, cache invalidation, and
   custom-template compatibility.
5. Repair the expanded regional folder grid layout before final UI validation.
6. Add rule/save/court regression tests, build Release, deploy to Mods, and
   inspect logs for stale full-map regrouping or duplicate-card errors.

## Acceptance Criteria

- The player can create a legal state with the divine power.
- The player can select a legal capital and then assign multiple cities to its
  state through repeated map clicks.
- No de jure planning or naming window is introduced.
- A legal state remains stable through occupation, extinction, succession, and
  save reload.
- The same legal state is visible from multiple country projections without
  duplicated legal records.
- Legal changes never appear as territory changes.
- Existing hierarchical map UI remains unchanged.
- Existing court, custom court, local-government, civilian-state, and military-
  government features remain functional.
- Expanded regional court cards show all school information without overlap.
