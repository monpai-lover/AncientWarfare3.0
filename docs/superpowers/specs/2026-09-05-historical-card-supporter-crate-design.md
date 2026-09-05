# Historical Card Supporter Crate Design

## Scope

Add one easter-egg crate named `赞助者` to the historical figure card window.
Its headline is `赞助，你也可以进入游戏`. Every distinct person in
`supporters.csv` appears as one deployable card.

## Card Data

- `supporters.csv` remains the source of truth; no second handwritten roster is
  maintained in the card catalogue.
- Repeated names are merged case-insensitively into one card. Their monetary
  records and contribution descriptions are combined for the card biography.
- Every supporter card uses minister role, civil-official subtype, Xia actor
  defaults, and the `supporters` collection id.
- Empty or malformed names are skipped. Stable card ids are derived from the
  normalized display name so existing inventory entries survive rank changes.
- Supporter cards use the standard fallback portrait until dedicated art exists.

## Draw Rules

- The crate retains the existing shared gold grand-prize pool.
- Rarity is rolled against the complete fixed 10,000-point distribution before
  a card is selected. Card counts never alter gold, red, pink, purple, or blue
  odds. If a selected local rarity is empty, it falls toward the next lower
  available local rarity instead of renormalizing the distribution.
- Within the selected rarity, every card has equal probability. Adding cards to
  one rarity never changes another rarity's probability.
- Distinct supporters are ordered by their aggregated numeric donation amount
  plus the separately recorded non-monetary contribution weight, then by their
  earliest leaderboard rank and normalized name. The weight recognizes actual
  technical and art assistance without presenting it as donated money. With
  the current 20-person roster, the top 2 are red, the next 3 pink, the next 5
  purple, and the remaining 10 blue. Duplicate rows contribute to one total.
- Supporter cards are excluded from historical period crates because their
  collection id is explicit and outside all period ids.
- The crate is available as a minister crate and does not expose an empty
  monarch variant.

## Author Portrait

- The provided portrait is assigned to the `mengpai` gold card at
  `ui/historical_cards/mengpai`.
- The source portrait is downscaled proportionally to 128 by 190 pixels for the
  game resource. Existing fixed portrait rectangles and `preserveAspect` remain
  authoritative in crate cards, the opening track, inventory, and details, so
  the image cannot cover card text or resize the card.

## Deployment

Supporter cards use the existing minister deployment path. Deployment into a
city keeps the country's name unchanged and adds the actor to the official
candidate pool with the normal historical-card minister preference.

## UI And Localization

- Add a crate card, localized name, headline/description, and a dedicated crate
  image path with a safe fallback when the image is absent.
- Selecting `赞助者` automatically uses the minister category so the crate is
  never presented as empty.
- Card details identify the source collection as `赞助者` rather than the raw
  `supporters` id.

## Verification

- Parser tests cover malformed rows, duplicate-name merging, stable ids, and
  biography aggregation.
- Draw tests prove fixed rarity thresholds, deterministic fallback for missing
  local rarities, equal selection within a rarity, and the shared gold pool.
- Crate/catalog tests prove all current distinct supporter names are present,
  use the 2/3/5/10 rarity split, remain minister-role cards, and are excluded
  from period crates.
- Build, source guards, local deployment, and source/deployed hash comparison
  complete the release check.
