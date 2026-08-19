# Bandit Great Uprising Era Design

## Goal

When a civilization accumulates a large bandit population while suffering
long-running institutional decline or famine, the realm enters a domestic
Great Uprising Era. Every bandit kingdom originating from that realm is
converted independently into a peasant-rebel kingdom and begins its own
revolutionary war against the origin realm.

The feature must be annual, persistent, idempotent and compatible with the
existing bandit stronghold, restoration and kingdom-extinction pipelines.

## Scope

This design covers:

- measuring bandit population as a share of the origin realm population;
- tracking long-term corruption and famine conditions;
- persisting the realm-level uprising-era state;
- converting each qualifying bandit kingdom independently;
- starting or preserving the existing origin-suppression war path;
- bounded diagnostics and regression tests.

It does not merge bandit kingdoms, change ordinary bandit spawning, change
the restoration claimant route, or replace the existing bandit suppression
and stronghold disposal rules.

The initial ratio threshold is a named production constant with a default of
5 percent. It remains adjustable without changing the state machine or data
format.

## Definitions

### Origin realm

Each bandit kingdom already stores
`MANDATE_REBEL_ORIGIN_KINGDOM_ID`. The referenced live civilization is its
origin realm. A bandit with no valid live origin is not counted for this
feature.

### Populations

- **Origin population**: the sum of living people reported by the origin
  realm's current cities.
- **Bandit population**: the sum of living, non-boat actors in all bandit
  kingdoms whose stored origin resolves to that realm.
- **Bandit ratio**: `bandit population / max(1, origin population)`.

Population reads are defensive. Invalid cities, dead actors, boats and
disposed kingdoms are excluded.

### Long-term corruption

Because the project has no standalone corruption meter, corruption is
represented by existing mandate state:

- mandate value <= 30, or imperial authority <= 30; or
- the global mandate phase is Decline or Chaos.

The corruption streak must remain true for five completed realm years.

### Long-term famine

For the origin realm's current cities, sum `city.status.hungry` and the
reported city population. Famine is true when hungry population is at least
30 percent of total population. The famine streak must remain true for two
completed realm years.

## State Machine

Each civilization gets a persisted uprising record containing:

- `Active`: whether the Great Uprising Era has started;
- `StartedYear`;
- `LastEvaluatedYear`;
- `CorruptionStreakYears`;
- `FamineStreakYears`;
- `ConversionCursor` for bounded bandit conversion;
- `LastConversionYear`.

Before activation, each annual evaluation updates the two streaks and checks:

```
banditRatio >= 0.05
AND (corruptionStreak >= 5 OR famineStreak >= 2)
```

Activation is written once and emits one chronicle/diagnostic event. Once
active, the state is not cleared merely because the ratio falls; it records
that the realm has entered the era. Conversion continues until no eligible
bandit kingdoms remain.

If a new bandit kingdom appears while the era is active, it is converted on a
later annual pass. A disposed or invalid origin record is ignored and does
not reactivate the era.

## Annual Evaluation and Performance

The coordinator runs from the existing kingdom annual-work pipeline after
mandate state is available. It returns immediately for non-civilizations,
neutral kingdoms, disposed kingdoms, replicas and duplicate evaluations in
the same year.

The world bandit scan is indexed once per world year by origin kingdom ID.
Each origin evaluation then reads its already-built candidate list rather
than scanning every actor repeatedly. Conversion uses a bounded cursor and a
small per-year budget. A failed conversion is logged and skipped; the cursor
advances so one malformed kingdom cannot block the rest.

The feature must not perform whole-world actor scans more than once per year,
must not run in frame-level actor jobs, and must not mutate kingdom lists from
background workers.

## Conversion Flow

For each eligible bandit kingdom belonging to an active origin realm:

1. Resolve and validate the bandit kingdom and origin.
2. Confirm it is still on the bandit route and has not already converted.
3. Call the existing `PeasantRebelRouteService.ConvertBanditToFounding`.
4. Preserve the bandit's kingdom identity, territory, ruler, stronghold
   history and origin metadata.
5. Let the existing founding route start the origin-suppression war.
6. Record success or a bounded failure reason.

No kingdoms are merged. Conversion is independent per bandit kingdom, so one
successful revolution does not cancel other conversions.

## Safety and Compatibility

- The existing bandit stronghold release/disposal path remains authoritative.
- Restoration claimant redirects remain authoritative and are not rerouted
  through the uprising coordinator.
- A normal civilization is never converted simply because its population is
  unhappy; the bandit ratio and a long-term corruption/famine condition are
  both required.
- A bandit with no live origin is excluded rather than assigned to an
  arbitrary kingdom.
- Replica sessions only consume persisted state and never perform conversion.
- If a war already exists, the founding route's existing war-start guard is
  reused; no duplicate war is created by the uprising coordinator.

## Diagnostics

Annual diagnostics include origin ID, origin population, bandit population,
ratio, corruption streak, famine streak, active state, conversion cursor,
success count and failure count. These are sampled once per origin-year and
must not log every actor.

## Tests

Pure rule tests cover:

- ratio threshold boundaries and zero-population safety;
- corruption and famine streak activation;
- the requirement that at least one long-term condition is true;
- activation idempotence;
- origin filtering and independent conversion candidates;
- conversion cursor progress after a failure.

Source guards cover:

- annual coordinator wiring;
- use of `ConvertBanditToFounding` rather than direct class mutation;
- no direct kingdom-list mutation from the annual evaluator;
- replica and duplicate-year gates.

Acceptance testing verifies that a realm crossing the threshold converts all
of its existing bandit kingdoms over bounded annual passes, while unrelated
bandits and ordinary bandit suppression remain unchanged.
