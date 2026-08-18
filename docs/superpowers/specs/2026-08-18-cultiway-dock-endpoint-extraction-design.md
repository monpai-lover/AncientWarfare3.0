# Cultiway Dock Endpoint Extraction Design

**Date:** 2026-08-18

**Scope:** Extract only the independent dock/path-worker reliability fixes from `feature/cultiway-pathfinding-upgrade` into the current `master` baseline. The source branch remains retained for later audit.

## Problem

Dock routes currently identify water bodies through the legacy island id. That id can be stale or unavailable while the traversal snapshot is being built, so a valid route may be rejected or an invalid route may be accepted after terrain connectivity changes. The path workers also use timed semaphore polling, which can accumulate wake debt and burn simulation/runtime budget while no work is available.

## Design

### Dock water identity

`AWDockEndpoint` stores both the traversal snapshot water component and the legacy island id. A small pure rule helper resolves the snapshot component first and uses the legacy value only before a traversal snapshot is available. Route matching rejects endpoints whose resolved component is invalid and only accepts matching resolved components.

`AWDockTransportService` resolves the component at registration time and stores the fallback value. It tracks the traversal generation used by the registry. Before route lookup, a changed generation causes the existing world refresh path to rebuild endpoint registrations, preventing stale topology from surviving terrain edits. `Clear` resets the generation marker.

### Path worker wake-up

`AWPathFinder.WorkerLoop` waits on `_queueSignal` without a timeout, checks the stop flag after wake-up, and then dequeues one scheduled work item. Priority selection, diagnostics, exception handling, and shutdown behavior remain unchanged. This removes timed polling and semaphore wake debt without changing path results.

### Boundaries preserved

This extraction does not include the source branch's async authority, UI, RTS war lifecycle, scheduler, path-session, or broad Cultiway migration changes. Existing transport ownership, task assignment, route generation, and native handoff logic in `master` remain authoritative.

## Verification

Add focused rule tests for component precedence, legacy fallback, matching/mismatching/invalid components, generation refresh wiring, and the blocking worker wait. Add a source guard that ensures the timed wait and unrelated Cultiway portal/train types are not introduced. Run the complete `AncientWarfare3.Rules.Tests` project before committing.

## Rollback

The extraction is isolated in a single integration commit. Reverting that commit restores the exact pre-extraction `master` behavior without modifying the retained source branch.
