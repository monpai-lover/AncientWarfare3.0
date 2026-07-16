# AW3 Nameplate Hot-Path Performance Design

**Date:** 2026-07-16

**Status:** Approved

## Goal

Remove AW3 database, reflection, component lookup, duplicate text generation, and
whole-world city scanning from the per-frame nameplate path while preserving the
current visual output and updating state on the next rendered frame.

## Scope

This slice covers:

- kingdom title suffixes;
- mandate, rebel, and pseudo-mandate nameplate markers;
- vassal suzerain flags;
- school-map city nameplates;
- regression tests that keep database work out of nameplate rendering.

This slice does not change:

- vanilla nameplate overlap suppression;
- vanilla building simulation jobs;
- building or tower behavior;
- the unrelated `missing text: ??????` report;
- old-save compatibility.

## Required Behavior

1. A vassal relation change is visible on the next rendered frame.
2. A title, republic, rebel, or mandate state change is visible on the next
   rendered frame.
3. Kingdom nameplates retain the same suffix and population text as today.
4. Mandate markers continue replacing the kingdom species icon and do not add a
   second special icon.
5. Vassal nameplates continue displaying the direct suzerain flag immediately
   before the kingdom name.
6. School map mode continues displaying at most 100 visible city plates with the
   dominant school's name, color, and icon.
7. Rendering a nameplate must not execute SQLite commands.
8. Rendering a stable nameplate must not perform `GetComponent`, reflective field
   discovery, hierarchy reordering, or flag sprite reloads every frame.

## Architecture

### Runtime Vassal Projection

`Kingdom.data[LineageKeys.VASSAL_SUZERAIN_ID]` is the runtime authority for the
current direct suzerain. A missing value and an explicit `-1` both mean that the
kingdom is independent. `VassalService.GetSuzerainId()` reads this projection and
does not fall back to `VassalRelation` SQLite history.

`SetVassal`, `EndVassal`, absorption, kingdom destruction, and reparenting already
write the runtime projection in the same operation that changes the durable
relation. Those event paths remain responsible for consistency. Historical SQL
queries remain available for reports and repair operations, but never sit beneath
the normal runtime getter.

This intentionally drops recovery for unpublished old saves whose relation exists
only in the archive database.

### Native Text Generation

The title suffix is injected through a prefix on
`NameplateText.getStringForNameplate(string, int)`. The prefix checks whether the
nameplate's current `nano_object` is a full kingdom plate and changes only the
method's local name argument.

The original method then creates the final `name + suffix + population` string and
the original `showTextKingdom()` calls `setText()` once. The existing
`showTextKingdom` postfix that recalculates population and writes a second string
is removed.

No kingdom or metadata name is temporarily mutated.

### Native Marker Loading

A prefix on `NameplateText.showSpecies(string)` replaces the species-icon path
with `MandateMapMarkerService.GetMarkerIcon(kingdom)` when the current plate is a
kingdom and a marker exists. Marker availability is cached by icon path after its
first sprite lookup; the prefix replaces the original path only when the marker
sprite exists. The original method then performs its normal cached sprite lookup
and sets its own visibility state.

`MandateMapMarkerService` keeps marker selection only. Its reflective access to
`_icon_species`, `_show_icon_species`, `_icon_special`, and
`_show_icon_special` is removed. `NameplateText.resetElements()` already disables
the special icon at the beginning of every prepare cycle, so an explicit
per-frame clearing pass is unnecessary.

### Vassal Flag Lifetime

Every newly created `NameplateText` receives one
`VassalNameplateSuzerainFlag` component from a postfix on
`NameplateText.newNameplate()`. The component registers itself in a bounded static
lookup keyed by the `NameplateText` instance and unregisters on destruction.

The component creates its two child UI objects lazily, only when first used by a
vassal. It resolves the private name `Text` reference once during initialization,
and places the flag before that text once when the child UI is created.

On every prepare cycle, `Hide(nameplate)` uses the lookup and only deactivates the
root. It does not call `GetComponent` and does not erase the cached suzerain ID.
On `showTextKingdom`, `Apply` reads the current direct suzerain and:

- keeps the existing sprites when the suzerain ID is unchanged;
- reloads the flag only when the suzerain ID changes;
- hides the root for independent, invalid, or mini nameplates.

This preserves next-frame correctness while removing stable-frame work.

### School Map Visibility

`DrawSchoolNameplates` collects candidate cities from
`World.world.zone_camera.getVisibleZones()` instead of scanning
`World.world.cities`. A reusable city-ID set removes duplicates because one city
can own several visible zones. The existing center-in-camera check remains the
final visibility condition.

The reusable candidate list is sorted by city ID before the 100-plate cap is
applied. This matches the creation-order behavior of the existing world-city
iteration and prevents the displayed set from changing when visible-zone order
changes.

For each candidate, the method obtains one `CitySchoolSnapshot`. A new overload of
`AWMapModeMetaLibrary.GetSchoolIdentityMetaForCity` accepts that snapshot so the
same object supplies both the meta identity and the icon definition. The 100-plate
limit remains unchanged.

At maximum zoom-out, visible zones may cover the world, so complexity can still
approach the world size. The new path is never asymptotically worse and avoids
scanning off-camera cities at normal zoom levels.

## Data Flow

```text
relation/title/mandate event
        |
        v
kingdom.data runtime projection
        |
        v (next NameplateManager.update)
original NameplateText generation
        |
        +-- title prefix changes local name argument
        +-- marker prefix changes local sprite path
        +-- vassal component applies cached flag state
        v
original active/layout/overlap checks
```

School map mode follows:

```text
visible zones -> distinct cities -> one cached snapshot per city
              -> school meta + icon -> original nameplate layout
```

## Error Handling

- Null, destroyed, neutral, and non-civilized kingdoms produce no AW3 marker or
  vassal flag.
- Missing marker sprites leave the original species icon intact.
- A missing name-text reflection binding prevents only the optional vassal flag;
  it does not prevent the kingdom plate from rendering.
- A missing school snapshot queues the existing deferred rebuild and skips that
  city's plate for the current frame.
- Runtime getters do not query SQL as an error-recovery fallback.

## Tests

### Rule Tests

- Stable suzerain IDs do not request a flag reload.
- Changed suzerain IDs request exactly one reload.
- Independent and invalid relations do not show a flag.
- Marker path selection remains correct for mandate, rebel, pseudo, and ordinary
  kingdoms.
- School snapshot reuse preserves the existing dominant-school identity rules.

### Source Guards

- `GetSuzerainId` must not call `ReadActiveSuzerainId`.
- The nameplate prepare/hide path must not call `GetComponent`.
- Mandate nameplate rendering must not contain `FieldInfo.GetValue` or
  `FieldInfo.SetValue`.
- Kingdom title rendering must not use a `showTextKingdom` postfix that calls
  `getPopulationPeople` or `setText`.
- School nameplate rendering must use visible zones and a single snapshot local.

### Build And Runtime Verification

1. Run the focused rules executable and source guards.
2. Build Debug and Release configurations.
3. Deploy without deleting the installed `.runtime` directory.
4. Load a world containing independent states, vassals, Xia titles, a mandate
   state, rebels, and school influence.
5. Verify normal, selected, mini, mandate-map, vassal-map, and school-map plates.
6. Change a vassal relation and mandate state and verify next-frame updates.
7. Use the vanilla `Benchmark Nameplates` panel to compare `set_nameplates` and
   total nameplate time at the same camera position and simulation state.
8. Confirm `Player.log` contains no post-load AW3 exceptions.

## Acceptance Criteria

- No AW3 SQLite command is reachable from the normal nameplate render path.
- Stable-frame vassal rendering performs no `GetComponent`, no hierarchy move,
  and no flag reload.
- Mandate marker rendering uses no reflection.
- Kingdom title text is generated and assigned once per plate preparation.
- School nameplates inspect only visible-zone cities and read one snapshot per
  candidate city.
- All listed visual states remain correct and update on the next frame.
- Focused tests, source guards, and Debug/Release builds pass.
