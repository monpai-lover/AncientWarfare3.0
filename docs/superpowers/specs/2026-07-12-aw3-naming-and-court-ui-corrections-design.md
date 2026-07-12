# AW3 Naming And Court UI Corrections Design

## Scope

This increment closes five runtime-facing gaps without changing old-save compatibility:

1. An official without a Shi has a 20 percent chance to receive an attested
   office-derived Shi, but only when the exact Shi is historically documented.
2. A Xia alliance place-name form uses the founding leader kingdom's real capital
   city instead of a fixed historical meeting place.
3. The AW3 power tab exposes separate Hundred Schools map and overview buttons.
4. The court canvas separates central officers from potentially numerous local
   governors and does not draw hierarchy links across that boundary.
5. The kingdom detail name input shows `kingdom.data.name` and never leaves the
   prefab/localization placeholder `NAME` visible.

## Historical Official Shi

The whitelist is deliberately narrow. It retains only office-derived forms whose
surname and office relationship are directly attested and whose game office is a
semantic match:

| Game office | Granted Shi | Historical basis |
| --- | --- | --- |
| Censor | `史` | Hereditary scribal/史 office; the Zhou grand historian Shi Yi line is recorded as taking the office name |
| Marshal | `司马` | Zhou military office and attested compound surname |
| Justice | `司寇` | Zhou judicial office and attested compound surname |
| Steward | `司徒` | Zhou civil/land office and attested compound surname |
| Imperial Astrologer | `太史` | Attested office and compound surname, including the Taishi Ci line |

`相`, `仓`, `尉`, `医`, and `将` are removed from the office mapping. The
characters may exist as surnames, but the current game-office-to-Shi relationship
is not strong enough for the strict historical gate. Those offices use the normal
Shi word library for all rolls. Existing Shi is always preserved.

## Xia Alliance Names

The Chinese Name route remains authoritative when that optional dependency is
loaded. AW3 supplies an `aw_xia_alliance` parameter getter that first calls the
Chinese Name default alliance getter, then adds `meeting_city` from the first
founder kingdom's live capital. It falls back to that kingdom's first valid city.

The generator has only contextual forms:

- `$meeting_city$之盟` when a real city is available.
- `$k1_short$$k2_short$之盟` as a non-place fallback.
- Non-place Spring-and-Autumn forms from a reduced word library.

Fixed place names such as `葵丘` and `践土` are removed. After generation, AW3
checks active alliances for an exact duplicate. It tries the alternate contextual
form, then appends a stable Chinese numeral suffix based on the alliance ID. This
check runs only at alliance creation and does not scan units or cities globally.
Custom player names remain untouched.

## Tab And School Entry Points

The AW3 lineage tab adds two adjacent buttons to the lineage group:

- `百家学派地图`: the existing school map-mode toggle power.
- `百家学派总览`: a simple button that opens `SchoolWindow` in school-list mode.

Both reuse registered WorldBox/AW3 sprites and existing map/window services. No
second school window or map-mode implementation is introduced.

## Court Sections

The central court keeps the existing rank pyramid for the king, heir, central
officers, specialists, and generals. Governors form a separate local section below
it. The local section uses a bounded number of columns and wraps into additional
rows, so a large late-game realm grows vertically instead of creating an extremely
wide row.

A labeled divider (`中央官场` above and `地方官署` at the boundary) is rendered
inside the zoomable/pannable canvas. Orthogonal links are generated only among
central ranks; no line connects generals to governors. Divider objects are reused
on refresh and reset with the canvas transform.

## Kingdom Detail Name

The data source remains `Kingdom.data.name`. A display-only postfix on the kingdom
top-information refresh rebinds the existing `NameInput` after the original window
has loaded its prefab/localization components. It does not rename the kingdom,
change `custom_name`, track a past name, or call a name generator. A diagnostic is
logged only when the visible field was the placeholder or differed from the data.

## Verification

- Pure rule tests cover the exact historical whitelist, 20 percent boundaries,
  meeting-city fallback, duplicate resolution, local-section wrapping, and the
  absence of cross-section links.
- Source gates verify both school buttons, the Chinese Name parameter getter,
  Harmony ordering, and the display-only kingdom-name patch.
- Release and debug builds must complete with zero warnings and zero errors.
- Runtime verification must confirm the Chinese route log, unique real-city
  alliance names, both tab buttons, the central/local divider, correct link layout,
  school list/map behavior, continuous occupation progress, and the real kingdom
  name in the detail window.
