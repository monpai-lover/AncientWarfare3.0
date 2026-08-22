# Vanilla AI Lookup and Cosmetic Budget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce civilian ambient AI scan and action pressure at high speed while preserving all RTS, military, transport, and path ownership behavior.

**Architecture:** Extend the existing idle throttle with a bounded process-wide budget per cosmetic kind. A successful prefix reserves one slot for the current vanilla action and releases it at task completion, cancellation, actor disposal, or world clear; exhausted budgets return `BehResult.Stop`. Add a separate Harmony patch that replaces only expensive vanilla lookup methods with existing AW indexes and returns `true` whenever an index cannot answer safely.

**Tech Stack:** C#/.NET Framework 4.8, Harmony, Unity/WorldBox publicized assemblies, existing AW performance indexes and source-guard rule tests.

---

### Task 1: Add pure budget rules and tests

**Files:**
- Modify: `Code/core/performance/AWIdleBehaviourThrottleRules.cs`
- Create: `Code/core/performance/AWIdleBehaviourBudget.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/IdleBehaviourThrottleGateTests.cs.txt`

- [ ] **Step 1: Write failing tests** for budgets `Socialize=32`, `EmoteSearch=16`, `Sleep=0` (sleep remains cooldown-only), per-kind isolation, and exhaustion returning false without waiting state.
- [ ] **Step 2: Run** `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --idle-throttle-gate` and verify the new assertions fail.
- [ ] **Step 3: Implement** immutable budget constants, kind mapping, and an atomic reservation/release helper with `TryAcquire`, `Release`, `Clear`, and peak/rejection counters.
- [ ] **Step 4: Re-run** the focused test and verify it passes.
- [ ] **Step 5: Commit** `git add Code/core/performance Tests/AncientWarfare3.Rules.Tests/IdleBehaviourThrottleGateTests.cs.txt && git commit -m "feat: add bounded cosmetic behaviour budgets"`.

### Task 2: Wire budget lifecycle without touching movement

**Files:**
- Modify: `Code/core/performance/AWIdleBehaviourThrottleService.cs`
- Modify: `Code/patch/AW_IdleBehaviourThrottlePatch.cs`
- Modify: `Code/core/performance/AWIdleBehaviourThrottleDiagnostics.cs`
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/IdleBehaviourThrottleGateTests.cs.txt`

- [ ] **Step 1: Add failing lifecycle/source-guard tests** for acquire on eligible cosmetic prefix, release on `setTaskBehFinished`, actor disposal, and `MapBox.clearWorld`; assert no references to `Actor.goTo`, `AWPathMovementBridge`, `ArmyRtsControllerService`, or transport methods.
- [ ] **Step 2: Implement** `TryBegin` so cooldown and budget both gate only eligible civilians. Budget exhaustion sets `BehResult.Stop` and returns `false`; it never queues or changes movement ownership. Add task-finished release keyed by actor and kind, with clear-world reset.
- [ ] **Step 3: Keep military exclusions fail-open** by reusing `WartimeMilitaryTaskGate`/`ArmyMilitaryMovementPriorityIndex` checks and existing actor/boat/army/king predicates.
- [ ] **Step 4: Add bounded diagnostics** for allowed, deferred, budget-rejected, active, and peak counts without per-attempt allocation.
- [ ] **Step 5: Run** focused tests, `git diff --check`, and `dotnet build AncientWarfare3.csproj --no-restore`; commit the lifecycle change.

### Task 3: Port Cultiway vanilla lookup optimization

**Files:**
- Create: `Code/patch/AW_VanillaLookupOptimizationPatch.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/VanillaLookupOptimizationSourceGuardTests.cs.txt`

- [ ] **Step 1: Add source guards** requiring `HarmonyPriority(Priority.Last)`, explicit original fallbacks (`return true`), and use of `AWChunkWindowIndex`, `AWFreeTileSearchIndex`, and `AWNearbyStatusTargetIndex`.
- [ ] **Step 2: Port only these methods from `Cultiway-Reborn-perf/Source/Patch/PatchOptimizeVanilla.cs`: `BehFindBuilding.execute`, `Toolbox.getBuildingsTypeFromChunk`, `BehFindMeatSource.getClosestMeatActor`, `BehFindTargetForHunter.execute`, `Finder.findTileInChunk` (free tile only), `BehFindLover.execute`, and `BehTryToSocialize.execute`.
- [ ] **Step 3: Preserve vanilla behavior** for null/invalid state, non-free tile requests, failed index lookups, and all unknown task paths. Keep random ordering semantics where the port relies on chunk lists.
- [ ] **Step 4: Verify** the patch compiles and source guards pass; commit separately from the budget changes.

### Task 4: Regression verification and deployment

**Files:**
- Modify only test/source-guard files if verification exposes a concrete defect.

- [ ] **Step 1: Run** `dotnet build AncientWarfare3.csproj --no-restore`.
- [ ] **Step 2: Run** the focused idle and lookup rule tests plus `git diff --check`.
- [ ] **Step 3: Compare protected RTS/path/transport files with `git diff` and confirm they are unchanged by this feature.
- [ ] **Step 4: Run** `./deploy-local.ps1` to deploy the latest source to the WorldBox Mods directory.
- [ ] **Step 5: Record** expected log counters for social/emote budget rejection and confirm no new RTS movement, embark, landing, or return-home errors.

### Explicitly excluded

- Timer range parallelization or shared `HashSet` changes.
- Enabling actor presentation snapshots.
- Changes to `AWPathMovementBridge`, `Actor.goTo`, P0 scheduling, RTS mission selection, ship spawning, embark, unload, or return-home tasks.
