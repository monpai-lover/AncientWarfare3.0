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
- Every supporter card uses pink rarity, minister role, civil-official subtype,
  Xia actor defaults, and the `supporters` collection id.
- Empty or malformed names are skipped. Stable card ids are derived from the
  normalized display name so existing inventory entries survive rank changes.
- Supporter cards use the standard fallback portrait until dedicated art exists.

## Draw Rules

- The crate retains the existing shared gold grand-prize pool.
- A pink result selects uniformly from the distinct supporter cards. Rank,
  donation amount, CSV order, and duplicate record count do not affect weight.
- Supporter cards are excluded from historical period crates because their
  collection id is explicit and outside all period ids.
- The crate is available as a minister crate and does not expose an empty
  monarch variant.

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
- Draw tests prove every pink supporter occupies exactly one uniform slot while
  the shared gold pool remains available.
- Crate/catalog tests prove all current distinct supporter names are present,
  pink, minister-role cards and excluded from period crates.
- Build, source guards, local deployment, and source/deployed hash comparison
  complete the release check.
