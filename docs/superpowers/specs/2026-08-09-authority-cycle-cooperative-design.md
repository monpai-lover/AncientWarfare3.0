# AW3 Authority Cycle Cooperative Scheduling Design

## Goal

Eliminate recurring 100-170 ms main-thread stalls caused by running all AW3
authority services as one indivisible large-scheduler phase.

## Evidence

The runtime log records `authority_cycle` stalls of 168.12 ms, 107.91 ms and
46.13 ms while annual succession itself is only 1.14 ms. The current
`AWAuthorityCycleService.ProcessCycle` invokes more than thirty independent
services sequentially inside one `AWFramePriorityGovernor.RunPhase` call.

## Design

Large-scheduler execution becomes a persistent cooperative state machine. A
new cycle token initializes the authority stage cursor once. Each call executes
exactly one named authority service and advances the cursor. The simulation
runner asks the frame governor for budget again before executing the next
service, so a depleted render frame returns immediately and resumes from the
same cursor on a later frame.

The native scheduler retains its existing complete-cycle execution path.
Cultiway-derived Actor, building, maintenance, worker-pool and pathfinding
boundaries are unchanged.

Every cooperative authority stage receives a stable diagnostic phase name.
The existing cycle gate remains authoritative, preventing duplicate work for
the same logical token. Reset and fault paths clear the cursor so stale work
cannot cross world generations.

## Correctness

- Services preserve their existing order.
- A logical authority cycle completes every service exactly once.
- A cycle that is paused, loading, initializing or running as a replica is
  skipped exactly as before.
- RTS scheduling receives the original cycle token and paused state.
- Native scheduling continues to use the existing synchronous method.

## Verification

- A source guard proves the large runner exposes per-service phase names and
  advances only after the cooperative authority cursor completes.
- Existing Cultiway scheduler non-regression guards remain green.
- The rules project and production project compile with zero errors.
- Runtime diagnostics must report `aw3.authority.<service>` instead of one
  opaque `aw3.authority` block, with no recurring 100 ms aggregate phase.
