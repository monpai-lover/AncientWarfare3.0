# Actor Runtime Performance Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Restore valid AW3 runtime performance by removing the remaining Actor/RTS hot paths, deploying a complete source tree, and verifying the approved autosave at 20x.

**Architecture:** Preserve vanilla Actor scheduling and RTS responsiveness. Replace presentation-bound global Actor filtering with bounded event-driven repair, clean per-Actor runtime caches at lifecycle boundaries, coalesce expensive death and RTS facts work behind existing queues/revisions, and validate a complete source-only deployment before accepting runtime measurements.

**Tech Stack:** C# source mod, Harmony, WorldBox/Unity APIs, PowerShell source guards, .NET rules tests.

---

### Task 1: Remove Global Actor Safety Scans

**Files:**
- Modify: `Tests/ActorKingdomSafetyRuntimeSourceGuard.ps1`
- Modify: `Code/core/lineage/ActorKingdomSafetyService.cs`
- Modify: `Code/patch/AW_ActorKingdomSafetyPatch.cs`

- [ ] Add a failing source guard rejecting `FilterRuntimeActors`, `RestoreRuntimeActors`, and `ActorListIsolationState` from render/zone hooks.
- [ ] Require load, enemy-check, `addUnit`, and conquest boundaries to queue only the supplied invalid Actor.
- [ ] Remove `UnitLayer.UpdateDirty` and `SimObjectsZones.checkUnits` global-list isolation hooks.
- [ ] Preserve bounded repair draining and world-reset cleanup.
- [ ] Run focused source guards and the complete rules suite.

### Task 2: Bound Actor Runtime Cache Cleanup

**Files:**
- Create or modify focused rule/source-guard tests under `Tests/`
- Modify Actor idle, presentation, path, and kingdom-safety lifecycle services identified by the audit
- Modify their existing disposal/reset Harmony hooks

- [ ] Add failing tests proving actor disposal removes only that Actor's entries and world reset clears all generations.
- [ ] Replace dictionary-wide per-disposal scans with direct keyed removal or bounded reverse indexes.
- [ ] Ensure stale world generations cannot retain live Actor references.
- [ ] Run focused and full verification.

### Task 3: Reduce Death-Chain Main-Thread Spikes

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorDeathArchiveRulesTests.cs.txt`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/patch/AW_HistoricalSchoolPatch.cs`
- Modify: `Code/core/lineage/ActorDeathArchiveService.cs`
- Reuse existing bounded/deferred historical write services

- [ ] Add failing rules/source guards for immutable, idempotent death work and bounded retries.
- [ ] Keep immediate live-world removal, succession, military, office, and school indexes synchronous.
- [ ] Move only persistence/archive transformations and SQLite work off the death call stack.
- [ ] Add save-drain and world-generation gates.
- [ ] Verify duplicate submission, reset, and save behavior.

### Task 4: Gate RTS Facts And Roster Work By Revision

**Files:**
- Add focused RTS rules/source-guard tests
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/CityAttackZoneService.cs`
- Modify relevant mission/roster/shared-route index services

- [ ] Add failing tests for city hostile-fact dirty revisions, roster cursors, and shared-route revisions.
- [ ] Cache tile scans until city military occupancy changes.
- [ ] Avoid restarting member scans when unrelated roster state is unchanged.
- [ ] Preserve immediate target, combat, transport, and replenishment reactions.
- [ ] Run the 10-city/20-army rule and simulation guards.

### Task 5: Bound Follower Recovery And Path Resubmission

**Files:**
- Modify focused path reuse tests and guards
- Modify: `Code/core/lineage/AWArmyMarchService.cs`
- Modify: `Code/core/lineage/ArmyStallWatchdogService.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`

- [ ] Add failing tests for same-target cooldown and active-request reuse.
- [ ] Prevent repeated follower `goTo` submissions while the leader route and terrain revision are unchanged.
- [ ] Preserve terminal recovery, transport transitions, and continuous movement.
- [ ] Verify no original/AW3 dual movement ownership.

### Task 6: Verify Complete Source Deployment

**Files:**
- Create: `Tests/VerifySourceDeployment.ps1`
- Update deployment documentation/evidence

- [ ] Add a verifier that compares every production source/config/localization relative path and SHA256.
- [ ] Exclude `.git`, `.worktrees`, `Tests`, `docs`, `bin`, `obj`, logs, temporary files, and DLLs.
- [ ] Stop WorldBox and mirror the complete production source tree to `D:/SteamLibrary/steamapps/common/WorldBox/Mods/AncientWarfare3.0`.
- [ ] Prove `DiplomaticWarAvailabilityRules.cs` and all referenced production types are present and hash-identical.
- [ ] Launch once and reject the run if AW3 or its multiplayer child mod fails source compilation.

### Task 7: Run The Approved 20x Recovery Benchmark

**Files:**
- Modify: `docs/performance/2026-08-02-actor-runtime-baseline.md`

- [ ] Auto-load `C:/Users/24908/AppData/LocalLow/mkarpenko/WorldBox/autosaves/1785772934` through `AW3_BENCHMARK_LOAD_PATH`.
- [ ] Confirm AW3 loaded, the requested save loaded, and game speed reached 20x.
- [ ] Warm up for two minutes and sample for ten minutes.
- [ ] Record average FPS, P95/P99 frame time, Actor stages, RTS controller stages, path submissions, deaths, GC, population, and active armies.
- [ ] Require average FPS at least 20 for the first recovery gate; otherwise preserve evidence and return to the first failing hotspot with a new RED test.
- [ ] Commit exact measurements and deployed source hashes only after a valid run.
