# Family Tree Death State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make runtime and archived family trees resolve missing actors as dead after world load, persist captured deaths when async writes are disabled, and reject stale pre-death UI snapshots.

**Architecture:** Extend pure rules for stable-runtime authority and strict revision matching. Wire those rules into the family-tree overlay and detached read completion, then make the existing death authority queue use its synchronous writer whenever async queueing fails. Death callbacks remain memory-only.

**Tech Stack:** C# net48 production sources, net9 rules harness, Harmony/Unity runtime guards, PowerShell source guards.

---

### Task 1: Runtime Death Authority Rule

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/FamilyTreeLabelLayoutRulesTests.cs.txt`
- Modify: `Code/core/lineage/FamilyTreeSnapshotOverlayRules.cs`
- Modify: `Code/core/lineage/FamilyTreeSnapshotOverlayService.cs`

- [ ] **Step 1: Write the failing tests**

Add assertions that `ResolveAlive` returns false for `runtimeAuthorityReady=true` and `runtimeActorMissing=true`, while preserving the database value when authority is not ready. Keep existing pending-death and live-dead assertions.

- [ ] **Step 2: Verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Expected: compile failure because `ResolveAlive` lacks the two runtime-authority parameters.

- [ ] **Step 3: Implement the pure rule and runtime wiring**

Change the rule to:

```csharp
public static bool ResolveAlive(bool snapshotAlive,
    bool hasPendingArchive, bool pendingArchiveAlive,
    bool liveKnownDead, bool runtimeAuthorityReady,
    bool runtimeActorMissing)
{
    if (runtimeAuthorityReady && runtimeActorMissing) return false;
    bool archiveAlive = hasPendingArchive
        ? pendingArchiveAlive
        : snapshotAlive;
    return archiveAlive && !liveKnownDead;
}
```

In the service, define stable authority as `Config.game_loaded &&
!SmoothLoader.isLoading() && !AW3MultiplayerReplicaScope.IsApplying`; pass
`live == null` separately. Import the multiplayer namespace used elsewhere.

- [ ] **Step 4: Verify GREEN and commit**

Run the rules harness and commit only the rule, overlay service, and test with:

```text
fix: resolve missing family tree actors as dead
```

### Task 2: Captured Death Durability

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorDeathArchiveRulesTests.cs.txt`
- Modify: `Code/core/lineage/ActorDeathArchiveRules.cs`
- Modify: `Code/core/lineage/ActorDeathArchiveService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorDeathArchiveBacklogSourceGuard.ps1`

- [ ] **Step 1: Write failing rule and source-guard tests**

Add `ShouldAttemptSynchronousWrite(queueAccepted)` assertions: false when
accepted, true when rejected. Extend the source guard to require a call to
`WriteCapturedDeathSynchronously` from the authority `Process` path, not only
from save flush.

- [ ] **Step 2: Verify RED**

Run the rules harness. Expected: missing rule or source guard failure.

- [ ] **Step 3: Implement bounded fallback**

When `TryQueueCapturedDeath` succeeds, remove the item as today. When it fails,
call `WriteCapturedDeathSynchronously` for that same item within the existing
item/time budget. Remove it only on success; otherwise increment attempts and
retain backoff. Do not write from `EnqueueLineage`.

On successful death enqueue, advance `FamilyTreeProjectionRevision` with the
captured projection change so an open family tree invalidates before durable
commit.

- [ ] **Step 4: Verify GREEN and commit**

Run the rules harness and commit with:

```text
fix: persist queued deaths without async writer
```

### Task 3: Reject Pre-Death Async Snapshots

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/IntegratedNamingRulesTests.cs.txt`
- Modify: `Code/core/uiquery/FamilyTreeMaterializationRules.cs`
- Modify: `Code/ui/windows/FamilyTreeWindow.cs`

- [ ] **Step 1: Write failing revision tests**

Replace the old no-revision-equality contract with
`AcceptCompletedSnapshot(sameGeneration, sameWorldGeneration, sameSpec,
sameProjectionRevision)`. Assert false when only the revision differs.

- [ ] **Step 2: Verify RED**

Run the rules harness. Expected: missing strict API.

- [ ] **Step 3: Implement strict acceptance**

Require all four booleans. In `ApplyBulkSnapshot`, compare
`FamilyTreeProjectionRevision.Current` with `_bulkRequestProjectionRevision`.
On mismatch use the existing stale-completion restart path. Never stamp an old
snapshot with the completion-time revision.

- [ ] **Step 4: Verify GREEN and commit**

Run the rules harness and commit with:

```text
fix: reject stale family tree life snapshots
```

### Task 4: Death-State Verification

- [ ] Run the rules harness.
- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/AncientWarfare3.Rules.Tests/ActorDeathArchiveBacklogSourceGuard.ps1`.
- [ ] Run `git diff --check`.
- [ ] Record the known unrelated `XiaItems.cs:70-71` net48 baseline errors if the full build still fails.
