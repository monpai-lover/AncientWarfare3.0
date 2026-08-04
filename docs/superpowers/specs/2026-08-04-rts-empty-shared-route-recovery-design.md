# RTS Empty Shared Route Recovery Design

## Problem

RTS armies can retain an `Installed` shared-route status after the actor has
lost its actual local path. Runtime evidence shows the contradictory state
`Installed`, zero local path nodes, no movement, and no local target. The
watchdog currently reasserts the same task or rebuilds the strategic route,
but it does not reliably clear every actor-side movement owner before the task
restarts. Under global path backlog this leaves armies visibly stuck in march,
retreat, or idle tasks.

RTS levy diagnostics also emit one long line for every processed work item.
Thousands of lines are retained by the Unity/NML log UI and can crash native
UI mesh generation.

## Chosen Design

### Shared-route truth

Treat an installed revision as reusable only while the actor is currently
following a non-empty local path, or has reached the recorded endpoint. A
matching historical revision without either fact is stale and must be
reinstalled.

Expose this decision as a pure rule so the contradictory runtime state has a
direct regression test.

### Recovery sequence

When the stall watchdog reasserts a captain command or requests a route
replan, clear actor-side shared-route metadata, cancel AW path ownership,
clear local path and tile targets, and only then restart the expected RTS task.
The strategic mission and target remain intact. Existing escalation remains:
reassert command, rebuild route, alternate endpoint, then target handoff or
retreat.

The recovery path must be idempotent and must not interrupt active combat or
transport ownership outside the watchdog's existing recovery gates.

### Diagnostic throttling

Keep RTS diagnostics useful without logging every levy work item. Emit the
first observation for a recovery plan, material outcome changes, and a bounded
periodic sample. Suppress identical zero-progress batches between samples.
Request diagnostics use the same bounded sampling principle. Reset sampling
state when diagnostics are disabled or the world runtime is cleared.

## Tests

1. A matching installed revision with an empty, non-following local path is
   not reusable and requires installation again.
2. A matching revision that is actively following a local path is reusable.
3. A matching revision at its endpoint is treated as arrived, not reinstalled.
4. Repeated identical zero-progress levy batches are suppressed inside the
   sampling interval; first, changed, and periodic observations are emitted.
5. Existing rules test suite and full mod build pass.

## Non-Goals

- Replacing the shared-route architecture.
- Changing retreat strength or logistics thresholds.
- Increasing global pathfinding worker counts.
- Disabling RTS diagnostics entirely.
