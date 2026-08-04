# Army Membership Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconcile every stable army roster to its intended kingdom after city or actor transfers, in both Native and Large modes, without breaking legitimate whole-army reassignment.

**Architecture:** A pure ownership rule decides keep/defer/release. Transfer callbacks enqueue army objects, and a bounded main-thread service evaluates final ownership after the transfer call stack ends. New membership and dirty rebuilds feed the same invariant.

**Tech Stack:** C# net48 production sources, Harmony patches, existing AW authority-cycle scheduler, net9 rules harness.

---

### Task 1: Ownership Decision Rule

**Files:**
- Create: `Code/core/lineage/ArmyMembershipOwnershipRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyMembershipOwnershipRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing tests**

Define `ArmyMembershipOwnershipDecision` values `Keep`, `Defer`, and `Release`.
Test same kingdom keep, mismatch release, missing owner defer, loading/replica
defer, missing actor kingdom release, and transfer-completed matches keep.

- [ ] **Step 2: Verify RED**

Run the rules harness. Expected: missing rule types.

- [ ] **Step 3: Implement minimal pure rule**

Implement `Decide(runtimeStable, intendedKingdomId, actorKingdomId)` with no
Unity dependencies: unstable or unknown owner defers; equal non-negative IDs
keep; all other stable values release.

- [ ] **Step 4: Verify GREEN and commit**

Run the rules harness and commit with:

```text
fix: define stable army ownership invariant
```

### Task 2: Deferred Reconciliation Service

**Files:**
- Create: `Code/core/lineage/ArmyMembershipReconciliationService.cs`
- Modify: `Code/patch/AW_DeferredRuntimeWorkPatch.cs`
- Modify: `Code/patch/AW_SlaveryPatch.cs`
- Modify: `Code/patch/AW_ArmySafetyPatch.cs`

- [ ] **Step 1: Write a failing source guard**

Create `Tests/AncientWarfare3.Rules.Tests/ArmyMembershipReconciliationSourceGuard.ps1` requiring actor and city transfer enqueue calls, authority-cycle processing, two-sided `removeFromArmy`/fallback cleanup, RTS/deployment/levy cleanup, captain disposal scope, and strategic index invalidation.

- [ ] **Step 2: Verify RED**

Run the source guard. Expected: reconciliation service or wiring is missing.

- [ ] **Step 3: Implement the queue and cleanup**

Use a `Queue<Army>` plus deduplication by live `Army` reference/data ID. Process
a bounded number after `Config.game_loaded && !SmoothLoader.isLoading()` and
outside replica application. Resolve owner through
`AWArmyService.GetIntendedKingdom`, snapshot `pArmy.units`, and apply the pure
rule to each actor's final kingdom.

For `Release`, remove the actor from the old army inside
`ArmyCaptainDisposalScope`, clear captain if needed, then release RTS,
deployment, temporary levy, wartime garrison, and mandate phase state. Notify
`ArmyStrategicIndexService.OnArmyRosterChanged` once per changed army.

Actor `setKingdom` postfix enqueues the actor's old/current army. City
`setKingdom` prefix/postfix retains and enqueues the anchor army. Deferred
processing naturally waits until whole-army reassignment finishes.

- [ ] **Step 4: Verify GREEN and commit**

Run the source guard and rules harness, then commit with:

```text
fix: reconcile army members after kingdom transfer
```

### Task 3: Prevention And Native/Large Rebuild Defense

**Files:**
- Modify: `Code/core/lineage/AWArmyService.cs`
- Modify: `Code/patch/AW_DirtyMetaActorIndexPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyMembershipReconciliationSourceGuard.ps1`

- [ ] **Step 1: Extend the source guard to fail**

Require `AddToArmy` to consult the ownership rule when owner and actor kingdom
are stable, and require the `ArmyManager.updateDirtyUnits` postfix to enqueue
armies after either Native or Large rebuild execution.

- [ ] **Step 2: Verify RED**

Run the source guard. Expected: missing prevention/rebuild wiring.

- [ ] **Step 3: Implement prevention and rebuild enqueue**

In `AddToArmy`, reject only a stable known mismatch; allow unknown ownership to
defer. Add an ArmyManager postfix that snapshots/enqueues the current world
armies after the existing prefix path. Harmony postfix runs whether Large skips
the original or Native executes it, so both modes converge through one service.

- [ ] **Step 4: Verify GREEN and commit**

Run the source guard and rules harness, then commit with:

```text
fix: enforce army ownership across rebuild modes
```

### Task 4: Army Verification

- [ ] Run the rules harness.
- [ ] Run the reconciliation source guard.
- [ ] Run the RTS adversarial simulation used by the repository.
- [ ] Run `git diff --check`.
- [ ] Record the known unrelated net48 baseline errors separately.
