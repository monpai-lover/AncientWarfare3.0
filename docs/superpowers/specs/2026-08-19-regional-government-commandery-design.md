# Regional Government and Two-Level City Administration Design

## Status

Approved design for implementation planning. This feature adds a runtime-only
regional administrative aggregation, a dynamic regional-government node in the
custom central court, and a two-level city administration map mode. It also
includes the confirmed court-window regressions and the nine-rank appointment
rules required by the new hierarchy.

## Goals and Non-Goals

The hierarchy is:

```text
Central court
  -> regional administrative layer (郡/道/路/etc.)
    -> regional governor (郡守/刺史/etc.; the seat city's 州牧兼任)
      -> multiple local-government offices
        -> each city's 州牧 and local officers
```

The regional layer is a read-only aggregation of current cities. It is not a
country, vassal, military governorate, territory owner, city, or persisted
office. It has no independent diplomacy, war, food, loyalty, succession, or
territorial settlement behavior.

## Runtime Aggregation

`RegionalGovernmentAggregationService` produces a read model from the current
valid cities of one kingdom:

- region display name and governor display title;
- seat city ID and its current leader Actor ID;
- member city IDs and local-government read models;
- deterministic ordering and geometry anchors for court and map labels.

The service uses `City.neighbours_cities_kingdom` for the adjacency graph and
the existing `DevelopmentMapModeService.GetCityScore` for development. It does
not duplicate or persist a development formula.

Cities are ordered by descending development, then population, then stable city
ID. The highest-ranked unassigned city becomes a seat and absorbs unassigned,
directly adjacent cities until the preferred region size of five is reached.
When more than four adjacent cities are available, members use the same
development, population, and stable-ID ordering. No non-adjacent city is pulled
into a region merely to reach a minimum size.
Remaining cities repeat the process, so one- and two-city kingdoms still get a
valid region. A region never crosses a kingdom boundary. The result is rebuilt
when read and may be short-lived cached for one UI/map refresh only.

The seat city's current leader is the regional governor projection and also
remains the city's 州牧. If the city leader changes, the projection changes
without appointment or persistence migration. A region name is derived from the
seat city name after removing common administrative suffixes (`州`, `府`, `城`)
and appending the configured region title.

## Custom Central Court

Central JSON contains at most one special node with stable semantic ID
`regional_government_layer`. The node stores localized display names and
management edges, not appointments:

```json
{
  "regional_government_layer": {
    "region_title_zh": "郡",
    "region_title_en": "Commandery",
    "governor_title_zh": "郡守",
    "governor_title_en": "Regional Governor",
    "management_edges": ["central_office_id"]
  }
}
```

Players can rename the administrative unit (`郡`, `道`, `路`, `省`) and its
governor (`郡守`, `刺史`, `观察使`, `总督`). The names are realm-wide for the
template and are exported/imported with the central JSON. Local JSON never
copies these fields.

The editor renders the node with the existing office-card language, a dynamic
virtual marker, and no grade, vacancy count, appointment effect, or delete
action. It can connect to central offices. Old documents without the node are
normalized by adding the default node. At runtime the node expands to one
projection per computed region. Each projection shows the region, seat city,
兼任州牧 identity, and member local-government cards; every projection links
its local cards to the same regional node.

If a legacy/custom template omits the node, the runtime uses a default link
position so local governments remain visible.

## Local Government View and Editor

Every local-government view displays a non-deletable upper projection above
its existing local template:

```text
临淄郡 · 郡守张某
          |
       州牧 / 都督
```

The projection resolves the current region title, region name, governor title,
and governor Actor. It is displayed even when the same Actor is also the local
州牧. In the editor it is a virtual, dashed node; it automatically connects to
all local offices without an incoming management edge. Imported local templates
therefore cannot disconnect a city from its regional superior.

## Nine-Rank Appointment Integration

The existing nine-rank and official-career state remains authoritative.

- Before the nine-rank institution is unlocked, existing candidate and career
  rules remain unchanged.
- After unlock, the lowest local-government seats are the formal entry path and
  may start at ninth/secondary-ninth grade.
- Higher local offices require progressively stronger grade and service history.
- 州牧 remains a high local office.
- High grade, local service, and valid evaluation history constrain appointment
  or promotion to 州牧. The regional governor is not a separately gated office:
  any valid current leader of the seat city is automatically projected as the
  regional governor, including compatible existing-save leaders.
- The same Actor can be rendered as both regional governor and seat-city 州牧;
  the two identities are not collapsed.

## City Administration Map Mode

Only the existing city-administration mode of the hierarchical map mode is
changed. Kingdom mode retains its current state and behavior.

City administration has an independent two-level navigation state:

```text
Region layer: 临淄郡, 河东郡
  click region
City layer: 临淄州, 即墨州, 安邑州
  click city -> existing city inspection
```

The region layer aggregates all member city zones and reuses existing
hierarchical-map label geometry, fonts, zoom, hit testing, pooling, and cache
invalidation. The city layer renders only member cities. Clicking outside the
focused region or using the existing return action goes back one level. Kingdom
mode never consumes or mutates this state.

Runtime label keys include kingdom ID and seat city ID, so regrouping cannot
reuse stale labels. City ownership, zone, and kingdom changes reuse existing
`MarkCityDirty`, ownership invalidation, and label-cache invalidation hooks.

## Confirmed UI Regressions

Implementation also fixes these existing regressions:

1. Local court summaries invoke the same `KingdomFlagBuilder.Build` path as the
   national summary. Unresolved sprites disable the invalid image instead of
   leaving a white placeholder.
2. The 民州/军府 selector resolves both built-in templates and custom snapshot
   templates. Built-in templates remain selectable and persistable.
3. The approved central custom-court internal component offset is +20 px; local
   mode layout remains unchanged.

## Error Handling and Compatibility

Invalid or missing city/kingdom references are skipped. A region with no valid
seat is omitted from presentation. Malformed JSON receives default regional
names and the dynamic node is retained. Runtime failures are logged through the
existing error path without breaking the court or map window.

## Verification

Tests must cover deterministic grouping, adjacency, seat selection, one/two-city
kingdoms, city changes, dual identity rendering, JSON normalization and
round-trip localization, two-level map navigation, kingdom-mode isolation,
nine-rank entry/qualification, flag rendering, and built-in local-template
selection. Release build and deployed Mod smoke tests must verify no missing
localization or resource errors.
