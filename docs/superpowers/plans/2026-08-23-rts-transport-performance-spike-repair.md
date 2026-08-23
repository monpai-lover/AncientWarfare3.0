# RTS Transport and Performance Spike Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make cross-island RTS armies reliably use temporary transports, stop retrying impossible routes, and remove the pathfinding/scheduler feedback loop that causes wartime performance spikes and persistent low simulation speed.

**Architecture:** Separate strategic route admission from physical dock availability. A cross-island mission first receives a validated shore or dock route; only then does the transport state provision one temporary boat and own the vanilla loading/sailing/landing task chain. Route failures are deduplicated and cooled down per army. Pathfinding and annual work remain behaviorally unchanged but receive bounded, observable work queues so a single failed army or annual event cannot monopolize a frame.

**Tech Stack:** C#/.NET Framework 4.8, Unity/WorldBox actor and `Boat` APIs, existing AW cooperative scheduler, source-guard rule tests, PowerShell deployment script.

---

## Scope and invariants

- Preserve the existing RTS land movement, captain/follower ownership, siege entry, return-home, and P0 priority behavior.
- Preserve the vanilla transport task IDs and boat animation chain: `boat_transport_go_load`, `boat_transport_go_unload`, `BehBoatTransportDoLoading`, `BehTaxiFindShipTile`, `BehTaxiEmbark`, and `BehTaxiSitInside`.
- Do not change localization files in this plan.
- Do not reset, checkout, or clean the existing dirty worktree. Before every edit, inspect the current diff in the target file and keep unrelated user changes.
- A failed route must never cause a per-pulse immediate replan. A mission may retry only after its cooldown or a topology/target revision change.

## Task 1: Add a route-admission result that can explain transport failure

**Files:**
- Modify: `Code/core/pathfinding/AWDockTransportService.cs`
- Modify: `Code/core/pathfinding/AWDockTransportRules.cs`
- Modify: `Code/core/pathfinding/AWDockRouteModels.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/AWDockTransportRouteRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/AWDockTransportRouteSourceGuardTests.cs.txt`

- [ ] **Step 1: Define the failure categories and route source contract.** Add an internal `AWDockRouteFailureReason` enum with `InvalidEndpoints`, `NoStableShore`, `NoNavigableWater`, `DifferentWaterComponents`, `NoDockOrShorePair`, and `TopologyChanged`. Extend the route resolver with a diagnostic overload:

```csharp
internal static bool TryResolveRoute(
    WorldTile pStart,
    WorldTile pTarget,
    out AWDockRouteCandidate pCandidate,
    out AWDockRouteFailureReason pReason)
```

Keep the existing two-argument overload as a compatibility wrapper that discards the reason.

- [ ] **Step 2: Test the pure rules first.** Add source-guard assertions that the compatibility wrapper calls the diagnostic overload, that `ShoreFallback` is still a valid route source, and that a failure reason is assigned on every `false` return. Add rule tests for same-island rejection, same-water-component acceptance, and different-component rejection.

- [ ] **Step 3: Run the targeted rule test.** Run `dotnet test Tests/AncientWarfare3.Rules.Tests/AWDockTransportRouteRulesTests.cs.txt` through the repository's existing rule-test harness. Expected result: the new source guard initially fails because the overload and reason assignments do not exist.

- [ ] **Step 4: Implement the smallest resolver change.** Thread a local `failureReason` through `TryResolveDockRoute` and `TryResolveShoreFallback`. Do not add a full-map scan to every call; keep topology construction in `EnsureTopology()` and return the first precise reason that explains why no candidate was produced.

- [ ] **Step 5: Run the targeted guard again.** Expected result: PASS, with no changes to existing route selection for valid dock routes.

## Task 2: Make temporary boats a real shore fallback

**Files:**
- Modify: `Code/core/pathfinding/AWDockTransportService.cs`
- Modify: `Code/core/lineage/ArmyRtsTransportService.cs`
- Modify: `Code/core/lineage/ArmyRtsTransportProductionService.cs`
- Modify: `Code/core/lineage/ArmyRtsTransportRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/TemporaryShoreTransportRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/TemporaryShoreTransportSourceGuardTests.cs.txt`

- [ ] **Step 1: Add an explicit shore-route resolver.** Add `TryResolveEmergencyShoreRoute(start, target, out candidate, out reason)` to `AWDockTransportService`. It must choose the nearest stable land tile adjacent to a boat-safe ocean tile on each island, require the same computed `WaterComponent`, and create an `AWTransportRouteSource.ShoreFallback` candidate with endpoint IDs of `0`.

- [ ] **Step 2: Keep docks preferred.** Update `TryResolveRoute` ordering to remain: live dock portal, registered shore fallback, emergency shore fallback. A valid dock route must never be replaced by a farther shore route.

- [ ] **Step 3: Admit the transport state before provisioning.** In `ArmyRtsTransportService.TryHandleActor`, when the actor and target are on different islands and the normal resolver fails with a shore-compatible reason, call the emergency shore resolver, create the `TransportState`, and set its stage to `AssembleAtEntry`. Do not call `TryProvisionAtRoute` from the route resolver itself.

- [ ] **Step 4: Provision exactly one temporary boat per voyage.** In `ArmyRtsTransportProductionService.TryProvisionAtRoute`, keep the existing dock build path for portal routes. For `ShoreFallback` routes, create `actor_asset_id_transport` directly on the candidate's pickup ocean tile, bind it to the kingdom, register it in `TemporaryBoatIds` and `OwnedTransportBoats`, then assign `boat_transport_go_load`.

- [ ] **Step 5: Preserve the vanilla task chain.** Add source guards requiring the temporary boat to receive `boat_transport_go_load`, and requiring the voyage state to advance through `Boarding`, `Sailing`, and `Landing` before `CompleteVoyage`. The guard must reject any implementation that teleports members or directly changes their tile across islands.

- [ ] **Step 6: Add failure-safe cleanup.** If boat creation returns null, the component is missing, or the boat dies before boarding, mark the voyage failed, clear the transport ownership index, and let the mission-level cooldown handle retry. Never destroy a boat with live passengers until emergency disembark has completed.

- [ ] **Step 7: Run transport rule tests.** Verify: no-port cross-island route produces a shore candidate; one temporary boat is created; repeated `TryHandleActor` calls do not create a second boat; invalid water components fail without mutating the army task.

## Task 3: Stop the cross-island failure/replan loop

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/ArmyStallWatchdogService.cs`
- Modify: `Code/core/lineage/ArmyRtsTransportService.cs`
- Modify: `Code/core/lineage/ArmyRtsModels.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportRetryRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportRetrySourceGuardTests.cs.txt`

- [ ] **Step 1: Add per-army retry state.** Add to the runtime mission state: `RouteFailureFingerprint`, `RouteFailureCount`, `NextRouteAttemptAt`, `LastRouteFailureReason`, and `TransportAttemptActive`. The fingerprint must include army ID, target city ID, target tile ID, topology revision, and same-island status.

- [ ] **Step 2: Gate route advancement.** In `AdvanceRoute`, return without submitting another path request when the fingerprint is unchanged and `Time.realtimeSinceStartupAsDouble < NextRouteAttemptAt`. Use a 3-second initial cooldown and exponential backoff capped at 30 seconds; reset the backoff only after a route is accepted, a topology revision changes, or the target city changes.

- [ ] **Step 3: Route failures must enter transport recovery once.** Change `TryBeginCrossIslandTransportAfterRouteFailure` to attempt the emergency shore route before asking the watchdog to rebuild the same land route. If transport admission succeeds, call `Controllers.Requeue` once and set `TransportAttemptActive`; if it fails, retain the cooldown rather than reasserting the captain every logical pulse.

- [ ] **Step 4: Make watchdog sampling observe cooldowns.** In `ArmyStallWatchdogService.OnRouteFailed`, do not invoke `ReassertCommand` or `RebuildRoute` while an unchanged cross-island fingerprint is cooling down. A watchdog sample may still record diagnostics, but must not mutate actor tasks during the cooldown.

- [ ] **Step 5: Add route-failure metrics at the strategic failure site.** Before `LogStrategicRouteFailure` returns, call `ArmyRtsBenchmark.RecordRoute(ArmyRtsRouteLifecycle.Failed)` exactly once for that attempt. Keep `ArmyRouteProvider` lifecycle metrics unchanged so provider failures are not double-counted.

- [ ] **Step 6: Test loop prevention.** Add rules asserting that 100 calls with the same fingerprint produce one attempt during cooldown, a topology revision permits one new attempt, and a successful boat admission resets the backoff.

## Task 4: Bound and deduplicate pathfinding work without breaking RTS movement

**Files:**
- Modify: `Code/core/pathfinding/ArmyRouteProvider.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/core/pathfinding/AWStreamingPathGenerator.cs`
- Modify: `Code/core/performance/ArmyRtsExecutionBudgetRules.cs`
- Modify: `Code/core/performance/ArmyRtsSchedulingService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/RtsPathRequestDeduplicationRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/RtsPathBudgetSourceGuardTests.cs.txt`

- [ ] **Step 1: Define request identity.** Use `(actorId, startTileId, targetTileId, movementProfile, topologyRevision, worldGeneration)` as the path request key. A request with the same key must reuse the pending/running result instead of generating a second path.

- [ ] **Step 2: Protect operational requests.** Add separate per-logical-pass budgets for operational RTS requests and ambient actor requests. Operational requests may be admitted until their bounded budget is reached; ambient requests must yield when the path queue or expanded-node budget is high. Never drop an already accepted RTS captain/follower route.

- [ ] **Step 3: Prevent follower storms.** In `AWPathMovementBridge`, do not resubmit a follower path merely because its native target is unchanged and the prior request is pending. Poll the existing owned path state using the existing lifecycle rules.

- [ ] **Step 4: Bound generator expansion.** In `AWStreamingPathGenerator`, stop a single request at the configured expansion budget and return a retryable pending result. Do not restart the same request from node zero in the same logical pass.

- [ ] **Step 5: Preserve land RTS tests.** Add source guards that the captain and follower request paths retain `AWPathWorkClass.Operational`, that route ownership is not cleared while a request is pending, and that `BehGoToTileTarget` remains the native land movement target.

- [ ] **Step 6: Verify counters.** In a controlled war log, require `path_reused` to increase when a key is repeated, `path_ambient_queue_high` to stop growing without bound, and operational routes to complete without `army_rts_no_progress_ms` increasing solely from duplicate submissions.

## Task 5: Separate annual-event and actor-preparation spikes from RTS work

**Files:**
- Modify: `Code/core/policy/KingdomAnnualWorkService.cs`
- Modify: `Code/core/lineage/NobleRemarriageService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/AnnualNobleRemarriageBudgetRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/AnnualNobleRemarriageSourceGuardTests.cs.txt`

- [ ] **Step 1: Keep annual semantics but persist a bounded cursor.** Split `NobleRemarriageService.OnKingdomYear` into candidate discovery and a bounded application step. Persist the unresolved candidate cursor on the kingdom so only the configured number of subjects/candidates are processed per annual slice.

- [ ] **Step 2: Prevent annual work from running in the same burst as a large RTS pulse.** Have `AWAuthorityCycleService` defer the annual noble-remarriage slice when the current cycle reports an over-budget RTS or pathfinding pulse; process it at the next authority cycle without changing the in-game year or marriage eligibility.

- [ ] **Step 3: Bound actor preparation churn.** In `AWCooperativeActorPostRunner`, keep the existing cosmetic/idle throttles and add a cap for newly dirtied actor preparation work. Do not suppress military P0 actor preparation or actors currently owned by a transport voyage.

- [ ] **Step 4: Test behavior preservation.** Assert that all eligible subjects eventually process, no subject is processed twice for the same annual key, and transport-owned actors bypass cosmetic throttling.

## Task 6: Make performance governor recover after a transient spike

**Files:**
- Modify: `Code/core/performance/AWFramePriorityGovernor.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Code/core/performance/AWFrameSchedulerRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/FramePriorityRecoveryRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/FramePriorityRecoverySourceGuardTests.cs.txt`

- [ ] **Step 1: Separate diagnostic max from scheduling estimate.** Keep `LongestPhase` as a diagnostic sample, but do not use its lifetime maximum as a scheduling estimate. Use the existing exponentially weighted `PhaseEstimates` for admission decisions and expose a rolling P95/last-window peak for diagnostics.

- [ ] **Step 2: Add a rolling peak reset.** Rotate a 120-frame peak window. When no phase exceeds the frame budget for one recovery window, decay the peak and clear the displayed phase name if it is no longer present. Do not reset `PhaseEstimates` for active phases.

- [ ] **Step 3: Recover actual speed.** In `AWCooperativeSimulationRunner.UpdateActualSpeed`, reset the rate window after a long host stall and calculate the next window from completed logical seconds only. Verify that a transient 748ms pulse does not hold `ActualSpeed` at `0.65x` after two clean recovery windows.

- [ ] **Step 4: Test the governor in isolation.** Add rule tests for: one transient spike, two clean windows, speed recovery; sustained over-budget work, continued throttling; and paused world, no simulated-speed accumulation.

## Task 7: Improve diagnostics so the next reproduction identifies the exact stage

**Files:**
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `Code/core/performance/ArmyRtsBenchmark.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/ArmyRtsTransportService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/RtsTransportDiagnosticsSourceGuardTests.cs.txt`

- [ ] **Step 1: Emit one structured transport state line per state transition.** Include army ID, route source, failure reason, topology revision, boat ID, stage, member census, and next retry time. Do not log the same pending state every frame; use the existing phase diagnostic gate.

- [ ] **Step 2: Add a transport counter group.** Track `route_candidate_failures`, `emergency_shore_routes`, `temporary_boats_created`, `temporary_boats_destroyed`, `boarding_timeouts`, `sailing_timeouts`, `landing_timeouts`, and `cooldown_suppressed_replans`.

- [ ] **Step 3: Add path pressure fields.** Emit per interval: unique path keys, duplicate requests suppressed, operational requests, ambient requests, expanded-node budget hits, and maximum queue depth. Keep the existing `path_generated` and `path_expanded_nodes` fields for backward comparison.

- [ ] **Step 4: Add source guards.** Require direct strategic transport failures to increment the route-failed counter and require cooldown suppression to be visible in diagnostics. This prevents a future “routes_failed=0” false negative.

## Task 8: Integration verification and source deployment

**Files:**
- Modify only the files listed in Tasks 1-7.
- Test: all new `Tests/AncientWarfare3.Rules.Tests/*Transport*`, `*Path*`, `*FramePriority*`, and `*Annual*` guards.

- [ ] **Step 1: Run source guards before building.** Run `.un_relevant_guards.ps1`. Expected result: `ALL PASS`.

- [ ] **Step 2: Build the mod.** Run `dotnet build AncientWarfare3.csproj`. Expected result: `0 warnings, 0 errors` or a documented pre-existing warning with no new compiler diagnostics.

- [ ] **Step 3: Deploy source, not only the DLL.** Run `.deploy-local.ps1` and verify the deployed `Code` tree contains the updated transport, pathfinding, scheduler, and diagnostics sources.

- [ ] **Step 4: Run four in-game scenarios.**
  - Same-island RTS march: movement completes with no transport state.
  - Cross-island attack with no ports: a temporary boat appears, loads, sails, unloads, and the army resumes the attack.
  - Cross-island attack with ports: the existing dock route remains preferred.
  - War end/return voyage: the army boards a temporary boat, returns, lands, and the boat is removed after passengers leave.

- [ ] **Step 5: Compare the new log against the captured baseline.** Acceptance criteria:
  - No repeated identical `transport_route_unavailable` line more often than the configured cooldown.
  - `temporary_boats_created > 0` for the no-port scenario.
  - `path_reused` increases for repeated actor-target requests.
  - `army_rts_no_progress_ms` stops increasing for the recovered army.
  - No single `aw3.rts.logical_pulse` or `vanilla.actors.post.b3.prepare.batch.0` spike repeats continuously after the voyage is admitted.
  - `ActualSpeed` returns toward the requested speed after two clean recovery windows.

- [ ] **Step 6: Commit the repair as separate focused commits.** Use one commit for transport admission/lifecycle, one for retry/path budgets, one for annual/governor recovery, and one for diagnostics/tests. Do not include unrelated dirty worktree files.

## Plan self-review

- Transport failure is handled before boat provisioning, so the original “temporary boat exists but is never called” gap is covered by Tasks 1-2.
- Repeated cross-island retries are covered by Task 3, including watchdog and benchmark under-reporting.
- Pathfinding pressure and duplicate work are covered by Task 4 without changing RTS land ownership.
- The annual marriage spike and actor preparation spike are isolated in Task 5 rather than incorrectly attributed to transport.
- Persistent low speed and stale longest-phase output are covered by Task 6.
- Every new behavior has a source guard or rule-test task, and deployment verification is explicit in Task 8.
