# School Map Bottom Bar And Capital Safety Design

## Scope

This change fixes the school MapMode selection UI, school influence labels, the missing heir-urge localization, and unsafe capital candidates. It does not change save compatibility, the existing school influence calculation, or the existing AW3 capital score and cooldown.

## School MapMode Selection

- School MapMode no longer treats a school identity object as a kingdom or unit.
- Clicking a city zone selects the real `City` and opens the vanilla `selected_city` power tab.
- School nameplates remain visually school-based, but their clickable object is the real city.
- The city tab receives an `element_school_composition` section while School MapMode is active.
- The section displays the city name, dominant school icon, and every non-zero school ordered by influence.
- Each school entry shows its icon, localized name, raw influence, and percentage. A details action opens the existing school city window.
- When School MapMode is inactive, the added section hides and the vanilla city tab behaves normally.

## Influence Label Rendering

- `SchoolInfluenceBar` keeps its existing colored fill.
- Its text uses a nested, override-sorting Canvas so the label cannot be covered by the fill or a parent Canvas.
- The visible label format is `school name  score  percentage`.
- School icon and text remain present together so color is never the only identifier.

## Localization

- Add `trait_aw_heir_urge` as `求嗣` / `Heir Seeking` / `求嗣`.
- Add `trait_aw_heir_urge_info` explaining that a ruler without a living male heir gains a large fertility bonus until a son is born.
- Add the school bottom-bar labels to the existing school locale file.

## Capital Candidate Safety

- Preserve the current AW3 capital candidate requirements, score formula, improvement threshold, peace requirement, policy flow, and AI cooldown.
- Add a hard candidate condition: a city touching any living foreign-owned neighboring city is not eligible to become the new capital.
- Neutral, destroyed, or ownerless neighboring cities do not count as foreign borders.
- The current capital is not forcibly moved merely because it later touches a foreign city. A move still requires another eligible city that passes the existing AW3 score threshold.
- The same candidate predicate is shared by policy execution and AI evaluation to prevent disagreement.

## Validation

- A rule test covers domestic-only neighbors, foreign neighbors, neutral/ownerless neighbors, and existing candidate requirements.
- Source integration tests verify School MapMode uses real city selection and no longer configures the school asset as `selected_kingdom`.
- UI integration tests verify the composition element includes school icons, localized names, scores, percentages, and the details action.
- Locale checks verify all new keys are unique and complete.
- Debug and Release builds, existing focused rule suites, startup compilation, Harmony patching, and the live Player log must be clean before handoff.
