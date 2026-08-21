# Direct De Jure Removal Power Design

## Goal

Provide one map power that removes de jure assignments directly from the
hierarchical map without changing real city ownership, territory, officers,
or government state.

## Interaction

The existing `aw_de_jure_region_retire` power remains the single entry point.
Selecting it opens and keeps the hierarchical de jure city map active.

The operation is context-sensitive and completes on one click:

- Clicking a de jure regional capital retires that entire de jure region.
- Clicking an ordinary member city removes only that city's membership from
  its current de jure region.
- Clicking a city with no de jure assignment does nothing and shows a concise
  localized notice.
- Clicking a bandit stronghold does nothing. Bandit strongholds remain outside
  the de jure system.

The old two-click "select then confirm" retirement interaction is removed.
Players can use the existing assignment powers later to place an unassigned
city into a region or create a new region.

## Domain Changes

`DeJureRegionStore` gains an explicit city-level mutation that removes one
ordinary member city from its region. The mutation must reject a regional
capital so that whole-region retirement always goes through `RetireState`.

The city-level mutation updates the saved de jure data and records a
`DeJureCityUnassigned` history reason. Whole-region removal keeps the existing
`DeJureRegionRetired` history reason. Neither path transfers city ownership,
zones, residents, officials, or buildings.

If removing an ordinary member leaves only the regional capital, the region
continues to exist. Empty-shell repair remains an initialization concern and
is not run as part of this power.

## Power Service Flow

`DeJureRegionPowerService.RetireMode` resolves the clicked city and its current
de jure state once, then chooses exactly one mutation:

1. Reject invalid tiles, bandit strongholds, and unassigned cities.
2. If the city is the state's capital, call `RetireState`.
3. Otherwise call the new city-level unassignment mutation.
4. On success, clear transient power selection state and invalidate the
   hierarchical map data and labels immediately.
5. Keep the de jure map mode selected so the player sees the result and can
   continue removing assignments.

Failures use localized player notices and `ModClass.LogError` only for actual
unexpected errors. Expected invalid clicks do not emit warning-level logs.

## Localization

All visible text is added to the unified CSV localization file. Text covers:

- removal of one city's de jure assignment;
- retirement of an entire de jure region;
- a city having no de jure assignment;
- a regional capital requiring whole-region retirement;
- bandit strongholds being ineligible.

Existing supported language columns receive valid fallback text so the editor
does not expose raw localization keys.

## Testing

Targeted tests cover these behaviors:

- a capital click selects whole-region retirement in one click;
- an ordinary member click selects city-only unassignment in one click;
- city-only unassignment removes the member but preserves the state and its
  capital;
- a capital cannot enter the city-only mutation;
- unassigned cities and bandit strongholds do not mutate saved state;
- successful mutations request an immediate hierarchical map refresh;
- source guards confirm the power remains registered, visible, localized, and
  bound to the hierarchical map mode.

The relevant rules test suite and project compilation must pass before the
change is deployed. Deployment copies only the validated source/resources to
the installed mod and must not include unrelated dirty workspace files.

## Non-Goals

- No city ownership or territorial transfer.
- No automatic reassignment after removal.
- No changes to de jure initialization or player-authored valid regions.
- No new window and no second power button.
- No changes to RTS movement, transport, warfare, or court appointments.
