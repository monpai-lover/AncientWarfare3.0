# Disable New Skeleton Spawns

## Goal

Prevent vanilla WorldBox from creating any new skeleton actors while keeping
already existing skeletons untouched.

## Root Cause

The vanilla necromancer behavior, skeleton spell, and skeleton transformation
paths converge on `ActionLibrary.spawnSkeleton(BaseSimObject, WorldTile)`. A
single guard at that method therefore covers all known creation paths without
changing ordinary actor birth or death handling.

## Scope

- Block the shared `ActionLibrary.spawnSkeleton` call with a Harmony Prefix.
- Return `false` before the vanilla body executes, so no skeleton actor,
  effects, or follow-up relationship is created.
- Leave existing skeletons alive and unchanged.
- Do not block zombie creation, necromancer creation, or ordinary unit births.

## Failure Handling

The Prefix has no world-state side effects and returns `false` even when the
caller is a spell, behavior task, or transformation. Harmony registration
failure remains visible through the existing patch-load diagnostics.

## Verification

- Add a pure rule test for the deny-new-skeleton decision.
- Add a source guard confirming the Harmony patch targets
  `ActionLibrary.spawnSkeleton` and returns `false`.
- Run the focused and full rules suites and build the net48 mod assembly.
