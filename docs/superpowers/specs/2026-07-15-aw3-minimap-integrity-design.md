# AW3 Minimap Integrity Design

## Scope

AW3 owns two actor minimap overlays: the registered heir marker and the
historical-figure marker. Mandate icons are kingdom nameplate decorations,
school/technology views are map modes, and building `mini_0` sprites stay under
the vanilla building renderer.

## Authoritative Identity

- A kingdom's `KINGDOM_HEIR_ID` is authoritative for that kingdom's legal heir.
- `Actor.data[IS_HEIR]` is derived presentation state. Clearing one kingdom's
  registration must not clear it while another live kingdom still registers the
  same actor.
- `FigureStateStore` is authoritative for historical figures. The `figure` and
  `first` traits are gameplay traits and must not independently grant the
  historical-figure minimap marker.

## Rendering

- The heir draw pass reads the stored actor without recalculating succession.
- A stored, living actor may render even if a stale global `IS_HEIR` flag was
  cleared; the per-kingdom registration is the display source.
- The same actor is rendered at most once per pass if transiently registered by
  multiple kingdoms.
- The marker color and hover anchor use the actor's current kingdom, falling
  back to the legal kingdom only when the actor has no current kingdom.
- Existing visibility, magnet, king/leader, cityless-kingdom, option-toggle and
  three-new-sprites-per-frame limits remain unchanged.
- The historical-figure pass continues to replace the vanilla favorite star in
  one pass, but only for an actor registered in `FigureStateStore`; all ordinary
  favorites keep the vanilla star.

## State Transitions

When a kingdom replaces or clears its heir, the service counts other live
kingdom registrations for the old actor. It clears `IS_HEIR` only when that
count is zero. This scan runs only on succession mutations, never in the
per-frame renderer.

Destroyed and cityless kingdoms do not keep a derived heir flag alive. Their
existing extinction path remains responsible for archiving the former-heir
title and clearing the kingdom registration.

## Verification

- Pure rule tests cover last-registration clearing and duplicate marker
  reservation.
- Historical minimap rules reject a trait lookalike that lacks a FigureState
  registration.
- Source guards require the authoritative lookups and forbid the old trait-only
  and global-flag-only gates.
- Debug and Release builds, the complete rule executable, source guards and
  `git diff --check` must pass.
- The installed mod is synchronized without `Tests` or `docs`; its runtime
  database hash must remain unchanged, and WorldBox must log successful Harmony
  installation for both minimap patches without AW3 runtime errors.

