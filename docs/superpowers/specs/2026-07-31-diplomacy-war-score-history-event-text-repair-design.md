# Diplomacy War Score and History Event Text Repair

## Problem

Older saves can contain `NULL` values in the two reserve-exhaustion columns of
`WarScoreSnapshot`. `WarScorePersistence.ReadSnapshot` converts those values
directly to integers, so constructing the runtime war-score service fails. The
diplomacy window then disables war negotiation and falls back to the generic
`unavailable` message even though the realms share an active war and the save
contains its score row.

Royal-marriage history has two display gaps. The event-label switch does not
map `royal_marriage`, so the raw identifier is shown. Simplified Chinese and
Traditional Chinese history content stores only `A与B` / `A與B`, leaving the
sentence incomplete. Existing save rows must be repaired at display time;
future rows should be written completely.

## Design

### War-score compatibility

- During war-score schema initialization, normalize legacy `NULL` reserve
  exhaustion values to zero.
- Read nullable numeric compatibility columns with an explicit zero fallback so
  a partially migrated save cannot disable the complete war-score runtime.
- When negotiation context cannot obtain a score snapshot, return the stable
  `war_score_unavailable` reason instead of the generic `unavailable` reason.
- Preserve all existing score values, war participants, occupations, and
  negotiation eligibility rules.

### History text completeness

- Map `royal_marriage` to the existing localized event title
  `aw_hist_event_royal_marriage`.
- Add a localized sentence suffix: `缔结婚盟`, an empty English suffix because
  the English middle fragment already contains the verb, and `締結婚盟`.
- Future royal-marriage records append the suffix when written.
- History rendering normalizes legacy royal-marriage rows by appending the
  suffix only when it is absent. This affects the list row and tooltip content
  without rewriting archived database rows.
- Keep the shared tooltip prefab and dimensions unchanged; the defect is
  incomplete content and a raw event identifier, not missing layout height.

## Verification

- A SQLite regression test creates a legacy nullable war-score schema and
  verifies that an active score can be loaded with both reserve-exhaustion
  values normalized to zero.
- History rule tests verify the localized royal-marriage label and legacy
  content completion without duplicate suffixes.
- A source guard verifies that negotiation snapshot failure exposes
  `war_score_unavailable`.
- Run the focused war-score and history tests, diplomacy integration guards,
  and the Release `net48` build before deployment.

## Deployment

Deploy only the changed war-score persistence, negotiation controller,
history-display, marriage-history, localization, and test-backed support files.
Do not replace unrelated installed files or force-close a running WorldBox
process.
