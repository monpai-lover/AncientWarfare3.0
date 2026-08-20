# Map Mode Internal City Selection Design

## Goal

When the player clicks a city in the school or Shi-lineage map mode, show the
existing composition bottom bar without creating the vanilla selected-meta
shortcut tab. Ordinary city selection outside these map modes must remain
unchanged.

## Root Cause

Both map modes currently write the clicked city into
`SelectedMetas.selected_city` and call `SelectedObjects.setNanoObject(city)`.
The latter activates WorldBox's selected-meta UI and creates the white shortcut
tab seen in the bottom navigation. Both bottom-bar controllers then depend on
that global selected-object state, so the unwanted tab cannot be hidden without
also breaking their current ownership checks.

## Design

Each map-mode service owns its selected city in a private field and exposes a
read-only accessor for its own bottom-bar controller. `SelectCity` validates the
city, clears selected units, stores the city in this private state, requests the
relevant snapshot, and shows the existing composition bar. It does not call
`SelectedObjects.setNanoObject` and does not publish the city through
`SelectedMetas.selected_city`.

The school and Shi bottom-bar controllers read the selected city from their
own service. Their frame validation checks that the map mode remains active and
that the owned city remains alive. They no longer require a native selected
nano object. Their existing custom `PowersTab` and composition elements remain
unchanged.

The map-mode `MetaTypeAsset` selection callbacks also use the owning service's
selected-city accessor. This preserves delayed `PowersTab` initialization
without routing through vanilla selected-meta state.

## Lifecycle

- Selecting another city replaces only that map mode's owned city.
- Leaving a map mode hides its composition bar and clears its owned city.
- Resetting the world clears both owned selections and bottom bars.
- Opening ordinary city UI outside these modes continues through vanilla
  `MetaTypeLibrary.city` and retains the normal selected-meta shortcut tab.
- No code forcibly hides or edits vanilla tabs.

## Compatibility

The change does not alter map coloring, influence snapshots, focus commands,
genealogy/school window navigation, or ordinary city selection. It preserves
the current authority and deferred-frame behavior.

## Verification

Source guards will require both services to own their selected city and forbid
`SelectedObjects.setNanoObject` in their selection methods. Guards will also
require both bottom-bar controllers to consume the service-owned selection and
not depend on `SelectedObjects.getSelectedNanoObject`. Existing map-mode rule
tests and the main project build must continue to pass.
