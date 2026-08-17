# Custom Court Office Settings Design

Status: approved for inline implementation on 2026-08-17.

## Goal

Add a settings button to the upper-left corner of every custom court office
card. The button opens a court-styled settings window where the player can edit
the office's base attributes, appointment requirements, and five preset
functional effects. Saved effects must be serialized in the existing JSON
template and must affect live gameplay when the office has a living incumbent.

## UI Design

The existing custom court editor keeps its current dimensions and canvas
layout. Each `CourtWorkflowVacancyCard` receives an 18 by 18 settings button in
the upper-left corner. The existing selection badge remains at the top center
and the delete button remains at the upper-right corner.

The settings button uses the existing court button styling, a settings icon
with a text fallback, and an `AW_RawTooltip`. Clicking it opens
`CustomCourtOfficeSettingsWindow`.

The settings window follows the existing draggable court-window design. It has
two tabs:

1. **Base and appointment**
   - localized office name
   - office layer
   - grade from 1 to 100
   - slots from 1 to 32
   - military-capable toggle
   - preferred school
   - minimum official rank
   - required school
   - required trait ID
   - prerequisite office selected from the current template

2. **Functional effects**
   - tax income
   - food production
   - army morale
   - civil order
   - court influence

Each effect occupies one fixed row and cannot be duplicated. A row contains an
enabled toggle, valid scope selector, mode selector, bounded numeric input, and
a localized tooltip. Enum selectors cycle through localized values and only
offer values accepted by `CustomCourtEffectRules`.

The window edits a normalized clone of the selected office. **Confirm** validates
and copies the clone back into the editor template, refreshes the card, and
returns to the editor. **Cancel**, the close button, or Escape discards the
clone.

## Data And Validation

No template schema change is required. `CustomCourtOffice` already contains
`Layer`, `Grade`, `Slots`, `MilitaryCapable`,
`PreferredSchoolId`, `Requirements`, and `Effects`.

A focused pure helper owns office cloning, fixed effect-row normalization,
enum cycling, numeric parsing, and validation. Validation uses
`CustomCourtTemplateRules.ValidateOffice`; invalid input remains in the
settings window with a localized error and never mutates the editor template.

The five effect IDs are unique. Disabled rows are omitted from
`CustomCourtOffice.Effects`. Enabled rows are written in stable effect-ID
order so JSON export remains deterministic.

## Effect Semantics

Effects only apply while the custom office has a living active incumbent whose
runtime court office matches the persisted officer row. Vacant, dead, removed,
or mismatched incumbents contribute nothing.

Effect modes compose in this order:

```text
result = (baseValue + additiveFlat)
       * (1 + additivePercent / 100)
       * multiplicativeFactor
```

The default multiplicative factor is 1. Values are clamped by the existing
template bounds. This replaces the current incomplete aggregation where flat
and percentage values are merged and multiplication starts from zero.

Scopes are routing metadata:

- tax income and food production route to city economy
- civil order routes to city unrest/order
- army morale routes to AW3 abstract battle strength
- court influence routes to the incumbent's court-school influence weight
- kingdom scope applies to all applicable consumers in that kingdom

## Runtime Integration

A new runtime facade reads the active custom snapshot, indexes active officers,
verifies living incumbents, and returns immutable effect modifiers. Consumers
request only the modifier they need.

- **Tax income:** modifies each city's AW3 tax contribution.
- **Food production:** modifies each city's AW3 food-stability contribution.
- **Civil order:** modifies `100 - unrestRisk`, then converts the result back
  to unrest risk.
- **Army morale:** modifies participant unit strength in
  `ArmyAbstractBattleRules`. It does not alter vanilla actor damage because
  the vanilla RTS combat path has no morale statistic.
- **Court influence:** modifies the school influence weight contributed by the
  incumbent of the configured office.

With no active custom court, no living incumbent, or no configured effect, all
modifiers are identity values and existing behavior is unchanged.

## Error Handling

- Invalid numeric text shows a localized validation error.
- Invalid layer, scope, mode, school, office, or effect combinations cannot be
  confirmed.
- Missing referenced offices are normalized to no prerequisite.
- Missing traits remain visible as IDs but fail candidate matching through the
  existing requirement path.
- Runtime effect reads catch stale actor/kingdom data and return identity
  modifiers rather than throwing during annual updates or battle resolution.

## Verification

Pure tests cover clone isolation, fixed-row uniqueness, mode composition,
bounds, vacancy behavior, and identity fallback. Source guards cover the card
settings entry, two-tab window, localization, and editor refresh callback.
Integration-focused rules cover tax/food/order transformations, abstract battle
morale strength, and office-holder court influence. The final gate is the full
Release build, source deployment, DLL deployment, and hash verification.

