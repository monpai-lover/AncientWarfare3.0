# County Renaming Design

## Goal

Allow the player to rename a virtual county from either the county map layer or
the local government window. Both entry points use the same command, validation,
persistence, and refresh path.

The feature changes only the virtual county record. It does not rename the
containing city, the de jure region, or the county's persisted historical
commandery assignment.

## Entry Points

### County map layer

When the hierarchy map is focused on a city's county layer, selecting a county
shows the existing county information strip. A pencil icon beside the county
name opens the county rename dialog.

Selecting a county continues to perform selection and highlighting only. The
player must press the pencil icon to rename it, preventing accidental edits
while navigating the map.

### Local government window

Each county magistrate card displays a pencil icon beside its county title. The
button remains available when the magistrate office is vacant because the
county record exists independently of its incumbent.

Both buttons resolve the same `CountyId` and open the same dialog.

## Rename Dialog

The dialog contains:

- the current county name;
- a single-line text field prefilled with the current name;
- confirm and cancel commands;
- a `Restore Historical Name` command.

Confirming an input without the `县` suffix appends the suffix. Existing suffixes
are not duplicated. Whitespace-only input is rejected. The normalized name must
not duplicate another active county name in the same de jure region. The current
county is excluded from the duplicate check.

The dialog reports validation failures without closing. Cancel closes the dialog
without changing data.

`Restore Historical Name` clears the manual-name state and immediately asks the
existing county naming service to regenerate this county from its persisted
historical commandery. It does not reassign the county to another historical
commandery.

## Data And Command Flow

The rename operation is keyed by `CountyId`, not by displayed name or map zone.
The shared command handler performs these steps:

1. Resolve an active `CountyRecord` by `CountyId`.
2. Normalize and validate the requested name.
3. Check uniqueness across active counties in the same de jure region.
4. Set `Name` and `ManualName=true`, then persist through
   `CountyAdministrationStore`.
5. Publish a county-administration revision and invalidate affected UI/map
   projections.

The restore operation follows the same command path but clears `ManualName` and
regenerates the automatic name before persistence.

The operation must be represented in the multiplayer command catalog so host
and clients apply the same authoritative rename.

## Refresh Behavior

A successful rename immediately refreshes:

- the selected county nameplate and information strip;
- county-layer map labels and tooltips;
- the county magistrate card in the local government window;
- open appointment and office-history views that display the county name.

Refresh is revision-driven. It must not scan all actors or all counties every
frame. Only the affected county, containing city, and de jure region are marked
dirty.

## Persistence And Compatibility

Manual county names continue to use the existing `CountyRecord.Name` and
`CountyRecord.ManualName` fields. Existing save-sidecar serialization therefore
remains compatible and requires no new schema field.

Automatic reconciliation, repartition repair, and old-save migration preserve
records with `ManualName=true`. If a manually named county is retired because a
city shrinks, its record and history remain inactive according to the existing
retirement behavior.

## Error Handling

The command is rejected with a localized reason when:

- the county no longer exists or is inactive;
- the input is empty after normalization;
- another active county in the same de jure region already uses the name;
- the caller cannot resolve the relevant city or de jure region;
- persistence fails.

The window remains usable after validation errors. A stale map or government
card closes the dialog or refreshes its target rather than renaming another
county.

## Localization

All visible text is provided through CSV localization, including:

- rename county;
- new county name;
- restore historical name;
- empty-name error;
- duplicate-name error;
- inactive-county error;
- rename success and restore success.

The pencil icon uses the project's existing icon library or an existing rename
sprite. No new raster asset is required.

## Tests

Rules tests cover:

- suffix normalization;
- empty-name rejection;
- same-region duplicate rejection;
- the same name being allowed in different de jure regions;
- manual names surviving reconciliation and save/reload;
- restoration clearing `ManualName` and retaining the historical commandery;
- inactive or stale county IDs being rejected;
- both UI entry points opening the shared dialog with the same `CountyId`;
- successful commands invalidating the county map and local court projections;
- multiplayer command registration and authoritative dispatch.
