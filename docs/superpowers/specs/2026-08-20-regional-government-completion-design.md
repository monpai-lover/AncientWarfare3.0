# Regional Government Completion Design

## Status

Approved completion design for the regional-government branch. It supersedes
the name-composition details in the 2026-08-19 commandery design while keeping
its runtime aggregation, court projection, editor, and map-mode architecture.

## Administrative Names

Place names and administrative-level titles are separate values.

- A WorldBox `CityName` is already the complete lowest-level place name. It is
  displayed unchanged and never receives an appended `州`, `府`, `城`, or other
  suffix.
- A region derives its place-name stem from the seat city's current name by
  removing a recognized legacy administrative suffix only when present. The
  runtime does not append the regional-level title to this stem.
- The central custom-court template stores three independently localized
  titles: regional level (`郡`, `道`, `路`), regional governor (`郡守`, `观察使`,
  `总督`), and lowest city level (`州`, or another player-selected title).
- Court presentation may display separated labels such as `临淄 · 郡` and
  `即墨 · 州`. The separator makes the administrative title metadata rather
  than part of the place name. Map labels retain the actual place name and may
  expose the level title as secondary presentation or tooltip text.
- Chinese and English values have separate inputs. Editing one language never
  overwrites the other language.
- Legacy JSON receives `州/Prefecture` as the default lowest-level title.

No independent commandery-name word bank is introduced. Region identity stays
stable and understandable by deriving it from the seat city, while the player
controls the administrative terminology.

## Court Presentation

Every local-government view retains a non-deletable regional-superior actor
projection above its local offices. The same projection is visible in local
template editing as a read-only dashed card so the relationship cannot be
hidden or disconnected.

Central court presentation groups city-government cards by computed region.
Each regional projection displays the separated region name and level title,
the governor title, seat city, and member-city count. Its management links to
member local governments use the existing court link renderer.

Changing the seat city's leader invalidates the regional cache immediately so
the regional-governor portrait and actor action always resolve the current city
leader. Removing a city, transferring ownership, changing zones, changing a
template, and resetting the world invalidate the same bounded cache.

## City Administration Map Mode

The city-administration mode remains independent from kingdom hierarchy mode.
Its root level displays computed regions; selecting one displays only its
member cities. Member-city clicks use the existing city inspection path.

While a region is focused, clicking a mapped city outside that region returns
to the region level instead of doing nothing. Clicking unmapped terrain and the
existing back action have the same one-level return behavior. City labels use
the original `CityName`; region labels keep the region name and administrative
level visually separated.

## Officials Become Nobles

Only a committed formal appointment grants permanent noble identity.

- `pActing == false` and a successfully committed career appointment are the
  authority boundary.
- Acting appointments do not grant noble identity. Promotion from acting to a
  formal office grants it once the formal transaction commits.
- The existing lineage admission service remains the only noble-identity
  writer. It creates or admits the actor's lineage as appropriate, sets current
  noble status and the noble trait, and remains idempotent.
- Leaving office does not revoke noble identity.
- Appointment does not automatically grant a hereditary noble rank, fief, or
  virtual title. Those systems retain their existing grant rules.
- Existing saves are repaired through bounded active-officer restoration or
  annual reconciliation; no full-world actor scan is introduced.

## Verification And Integration

Tests cover separated place/title composition, schema migration and bilingual
round trips, formal-versus-acting ennoblement, cache invalidation after leader
replacement, member counts, and cross-region map clicks. Source guards cover
runtime aggregation and regional-template persistence in addition to the
existing court, editor, map, localization, and nine-rank guards.

All focused regional-government, custom-court, civil-service, and office-history
suites must pass, followed by a zero-warning Release build. The completed
branch is then merged into `master`, deployed to the WorldBox Mods directory,
hash-checked, and smoke-tested for court hierarchy, editor labels, map drill
down, leader replacement, localization, and log errors.
