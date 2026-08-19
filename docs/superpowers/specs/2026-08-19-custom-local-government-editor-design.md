# Custom Local Government Editor Design

## Goal

Separate the central custom-court workflow from city local-government
editing, provide useful civil and military default local governments, and
select the appropriate automatic template from each city's current role.
The change must preserve player-authored templates and the existing canvas
editing interaction.

## Entry Points And Navigation

- Opening the editor from a kingdom court continues to show the existing
  `Custom Court Workflow` title and starts in central-court mode.
- A city local-government view adds a visible `Custom Local Government`
  toolbar button.
- The city button opens the shared workflow window in local-government mode,
  using the template currently resolved for that city.
- The local entry uses the `Custom Local Government Workflow` title. Central
  controls and the central/local context selector are hidden or locked so the
  window cannot accidentally become a central-court editor.
- Local mode retains template selection, create, duplicate, delete,
  replacement, default-role, import, export, save, and apply controls. This is
  how one kingdom maintains multiple reusable local-government types.
- Applying from a city entry returns to that same city's local-government
  view. Applying from the kingdom entry returns to the kingdom court.
- The editor stores an explicit entry context containing kingdom ID, optional
  city ID, and central/local mode. Opening a different source replaces stale
  context.

## Default Local Governments

Every newly created custom court contains both default templates.

### Civil Template: Minzhou

The `minzhou` template is the `CivilDefault` and contains:

| Office | Grade | Role | Default effect |
|---|---:|---|---|
| Governor | 10 | Administrative head | City civil order +5 flat |
| Chief Clerk | 20 | General administration | City civil order +3 flat |
| Household Officer | 30 | Population and taxation | City tax income +8 percent |
| Granary Officer | 30 | Food and granaries | City food production +10 percent |

The governor manages the other three offices. The supporting offices are
laid out on one subordinate row and have distinct stable IDs.

Chinese display names are `州牧`, `长史`, `司户`, and `司仓`. English and
Traditional Chinese localizations are provided in the court locale file.

### Military Template: Junfu

The `junfu` template is the `MilitaryDefault` and contains:

| Office | Grade | Role | Default effect |
|---|---:|---|---|
| Commander | 10 | Military head | Kingdom/army morale +5 flat |
| Chief Clerk | 20 | Military administration | City civil order +3 flat |
| Marshal | 20 | Troop command | Kingdom/army morale +3 flat |
| Staff Officer | 30 | Operational planning | Court influence +3 flat |

The commander manages the other three offices. The commander and marshal are
military-capable and prefer the military school. The supporting offices are
laid out on one subordinate row and have distinct stable IDs.

Chinese display names are `都督`, `长史`, `司马`, and `参军`. English and
Traditional Chinese localizations are provided in the court locale file.

Effects use only the existing custom-office effect model and valid scopes.
An effect applies only while its office has an incumbent, preserving the
existing appointment and vacancy semantics.

## Automatic Template Resolution

Manual city bindings always win. When a city has no valid manual binding:

1. Use the military default if the city has a live foreign land neighbour.
2. Otherwise use the military default if the city's current economy role is
   `FrontierMilitary`.
3. Otherwise use the civil default.
4. If a preferred default is missing, use the first valid template as the
   existing resolver does.

The foreign-border check excludes the owner, dead kingdoms, and water-only
contact. Diplomatic relations do not remove frontier status: a live allied,
vassal, or tributary realm on the other side is still foreign territory. The
check must reuse an existing correct land-border helper where possible rather
than scanning the world.

Automatic resolution is evaluated when the local template is requested, so a
city that becomes interior and no longer has the `FrontierMilitary` role
returns to the civil template. The resolved ID may be cached on city data, but
the manual flag remains false. Player-selected templates remain stable across
border and economy-role changes.

The current realm-wide use of `MilitaryGovernorateStore` as the
`militaryCity` condition is removed. A military-governorate realm does not make
every city a military city.

## Existing Save Migration

The custom local-government schema advances by one version. Normalization and
instance import both run the same migration.

- If `minzhou` or `junfu` exactly matches the old generated structure, meaning
  one generated `<template>_governor`, the old generated name, grade, slot,
  empty requirements/effects/edges, and original center layout, it is replaced
  by the corresponding four-office template.
- Any difference in name, office properties, requirements, effects, edges, or
  layout marks the template as player-authored and it is left untouched.
- If `minzhou` or `junfu` is missing, add only the missing default template.
- Never rename or overwrite other templates.
- Preserve city manual bindings and remap no city unless a deleted or invalid
  binding already falls through existing resolution rules.
- Migration is idempotent: normalizing an already upgraded snapshot produces
  no further changes.

## Toolbar Layout And Scrollbar

- Keep the current window width, window size contract, canvas position,
  workspace size, node card dimensions, and canvas drag behavior.
- The toolbar viewport top remains at its approved coordinate.
- Its visible height subtracts the positive top offset and a bottom inset from
  the root viewport height, preventing its bottom edge from leaving the
  window.
- Attach a permanently visible vertical `Scrollbar` to the toolbar
  `ScrollRect`, following the existing court-window scrollbar construction.
- Place the narrow track at the toolbar's right edge and reserve width so it
  does not cover button text.
- Support mouse-wheel scrolling and direct thumb dragging. Use clamped
  movement and keep the toolbar's current 80 percent scale.
- Reset normalized scroll position to the top when opening a different editor
  context; preserve it during ordinary refreshes and node edits.

## Localization

Add complete Simplified Chinese, English, and Traditional Chinese rows for:

- custom local-government button;
- local workflow window title, including the window framework's ` Title`
  compatibility key;
- both template names;
- all eight default office display names;
- any status or tooltip introduced by the contextual entry.

No editor-facing label may rely solely on an English fallback.

## Testing And Verification

- Pure rule tests cover manual override, foreign border, military economy role,
  ordinary interior city, return from border to interior, and missing preferred
  defaults.
- Migration tests cover pristine civil upgrade, pristine military upgrade,
  missing-template creation, modified-template preservation, and idempotence.
- Source guards cover central and local entry methods, contextual titles,
  return navigation, corrected viewport-height calculation, permanent vertical
  scrollbar binding, unchanged canvas coordinate, and both four-office
  presets.
- Run focused rules, the full rules suite, production build, and diff checks.
- Deploy to the WorldBox Mods folder, launch a visible game window, and inspect
  the fresh `Player.log` for compile, Harmony, localization, and AW3 exceptions.
- Runtime visual checks verify the full toolbar can be reached, the scrollbar
  can be dragged, central and local titles differ, a city opens its resolved
  template, and canvas interaction is unchanged.
