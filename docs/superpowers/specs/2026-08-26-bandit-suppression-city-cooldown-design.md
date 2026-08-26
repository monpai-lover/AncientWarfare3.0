# Bandit Suppression City Cooldown Design

## Goal

After a bandit stronghold is suppressed or annihilated, its restored mother
city cannot automatically produce another bandit stronghold for 50 in-game
years. Player use of the bandit stronghold god power bypasses this cooldown.

## Scope

The cooldown applies to every automatic route that creates or converts into a
bandit stronghold, including annual low-loyalty spawning, mass uprisings,
Guiyi routing, and government route transitions. It does not apply to the
manual god power.

Amnesty and voluntary conversion from a bandit government to an ordinary
government do not create the cooldown. Only a completed suppression or
annihilation settlement does.

## Persistence

Store an absolute expiry year on the restored mother city's data. The value is
set to `currentYear + 50` only after stronghold fall cleanup has completed
successfully. City data already participates in save persistence, so no
separate world scan or global ledger is required.

The cooldown is active while `currentYear < expiryYear`. Automatic bandit
creation becomes legal when `currentYear >= expiryYear`.

## Creation Gate

Introduce a pure rule that evaluates current year, expiry year, and an explicit
manual-bypass flag. Apply it at the shared stronghold planning boundary so all
automatic callers inherit the same behavior.

The direct creation path accepts an optional bypass argument. Existing
automatic callers use the default `false`; the god power passes `true`. When the
gate rejects creation, the operation returns a dedicated localization key.

## Settlement Hook

Record the cooldown in the successful `CompleteFall` path after residents,
zones, walls, and towers have been restored and the completed state has been
persisted. Use the existing suppression-settlement flag to distinguish hostile
suppression from ordinary government cleanup.

This covers city capture, population annihilation, and leaderless suppression
without duplicating writes in each trigger path.

## Failure Handling

If stronghold fall fails before completion, do not start the cooldown. If the
city or its data is unavailable, preserve the existing failure result instead
of creating detached cooldown state.

The manual god power remains usable during cooldown. Automatic callers receive
the existing boolean failure contract plus the new localized reason.

## Tests

Add rule tests proving:

- automatic creation is blocked through year 49;
- automatic creation is allowed at the exact 50-year boundary;
- manual god power bypasses an active cooldown;
- missing or expired cooldowns allow creation;
- only suppression completion writes the cooldown;
- the god power passes the bypass while automatic callers do not.

Run the focused bandit spawn/stronghold tests and the full project build before
completion.
