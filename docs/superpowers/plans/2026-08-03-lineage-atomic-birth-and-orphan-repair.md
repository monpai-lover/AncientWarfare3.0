# Lineage Atomic Birth And Orphan Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist every newborn's minimum archive and both parent edges atomically, then repair legacy orphan edges without inventing unavailable identity data.

**Architecture:** Capture an immutable minimum actor snapshot on the main thread, enqueue a per-child state envelope, and execute the archive upsert plus both deterministic edges in one SQLite transaction. Before the asynchronous writer starts during restore, run an idempotent versioned migration that resolves live actors and creates explicit unresolved placeholders for unrecoverable dead actors.

**Tech Stack:** C#, System.Data.SQLite, AW3 historical asynchronous writer, WorldBox actor snapshots, `AncientWarfare3.Rules.Tests` SQLite tests.

---

### Task 1: Atomic birth SQL persistence

**Files:**
- Create: `Code/core/lineage/LineageBirthArchiveModels.cs`
- Create: `Code/core/lineage/LineageBirthArchivePersistence.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/LineageBirthArchivePersistenceSqlTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write RED tests**

Create in-memory `ActorArchive` and `FamilyEdge` tables. Add `OrdinaryBirthCommitsArchiveAndTwoEdges`, `SecondEdgeFailureRollsBackEverything`, and `RepeatedBirthIsIdempotent`. The success assertion is:

```csharp
using SQLiteTransaction tx = db.BeginTransaction();
LineageBirthArchivePersistence.Execute(db, tx, write);
tx.Commit();
Equal(1L, Scalar(db, "SELECT COUNT(*) FROM ActorArchive WHERE ID=100"),
    "birth writes one child archive");
Equal(2L, Scalar(db, "SELECT COUNT(*) FROM FamilyEdge WHERE CHILD_ID=100"),
    "birth writes both parent edges");
```

For rollback, install an abort trigger on slot 2, roll back after the expected SQLite exception, and assert both counts are zero. Link the files and add `--lineage-atomic-birth-slice` plus the default-suite call.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --lineage-atomic-birth-slice
```

Expected: compile failure because the model and persistence type do not exist.

- [ ] **Step 3: Implement the minimum model and persistence**

`LineageBirthArchiveWrite` owns a detached `ActorArchiveTableItem`, parent slot ids, and created time. `Execute` must use only the caller's transaction:

```csharp
UpsertActorArchive(pDb, pTransaction, pWrite.Child);
bool first = UpsertParentEdge(pDb, pTransaction, pWrite.Child.id,
    pWrite.ParentSlot1, 1, pWrite.Child.lineage_id, pWrite.CreatedTime);
bool second = UpsertParentEdge(pDb, pTransaction, pWrite.Child.id,
    pWrite.ParentSlot2, 2, pWrite.Child.lineage_id, pWrite.CreatedTime);
return new LineageBirthArchiveOutcome(pWrite.Child.id, true, first, second);
```

Use parameterized upserts. Edge ids remain `child*10+slot`. Do not commit inside `Execute`.

- [ ] **Step 4: Run GREEN and full suite**

Run the slice and then the Rules.Tests project without arguments. Both must pass.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/LineageBirthArchiveModels.cs Code/core/lineage/LineageBirthArchivePersistence.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: persist lineage births atomically"
```

### Task 2: Async state envelope and failure cleanup

**Files:**
- Create: `Code/core/lineage/LineageBirthArchiveAsyncWrite.cs`
- Modify: `Code/core/db/HistoricalWriteWorker.cs`
- Modify: `Code/core/db/HistoricalWriteService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/LineageBirthArchivePersistenceSqlTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/HistoricalWriteRulesTests.cs.txt`

- [ ] **Step 1: Write RED tests**

Assert the envelope key is `lineage-birth:v1:child:100`, its kind is `State`, and its custom execution returns the typed outcome. Add a fake-sink worker test where a terminal batch failure produces a failure completion for every in-flight sequence; add a source assertion that `TryEnqueueCustom` accepts a failure callback.

- [ ] **Step 2: Run RED**

Expected: the envelope is absent and terminal worker failure exits without a completion.

- [ ] **Step 3: Implement envelope and failure completion**

`LineageBirthArchiveEnvelope` implements `IHistoricalCustomWriteEnvelope` and delegates to Task 1 persistence. Extend `HistoricalWriteCompletion` with committed/error state. Before terminal worker exit, enqueue a failure completion for the current batch. `HistoricalWriteService` stores separate success/failure callbacks and removes both after invoking exactly one. Existing overloads forward a null failure callback.

- [ ] **Step 4: Run GREEN and full suite**

Verify commit calls only success, terminal failure calls only failure, and all existing historical writer tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/LineageBirthArchiveAsyncWrite.cs Code/core/db/HistoricalWriteWorker.cs Code/core/db/HistoricalWriteService.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "fix: report failed historical write completions"
```

### Task 3: Route all birth paths through one service

**Files:**
- Create: `Code/core/lineage/LineageBirthArchiveService.cs`
- Modify: `Code/core/lineage/LineageArchiveWriter.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/core/lineage/WesternLineageParentEdgeService.cs`
- Modify: `Tests/WesternLineageParentEdgeSourceGuard.ps1`

- [ ] **Step 1: Write RED source guards**

Require Xia/monkey full birth, mixed ancestry, and Western/orc lightweight birth to call `LineageBirthArchiveService.TryRecord`. Reject the old `RecordFamilyEdges` then `ArchiveActor` sequence and direct lightweight edge-only persistence. Require one per-child state key and one post-commit `FamilyStructure` revision.

- [ ] **Step 2: Run RED**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/WesternLineageParentEdgeSourceGuard.ps1
```

Expected: current full and lightweight paths persist edges separately.

- [ ] **Step 3: Extract relationship snapshot capture**

Move actor-to-archive capture into an internal reusable method. Relationship capture bypasses noble/clan admission but stores only real actor fields. Standalone full archive calls retain the existing admission gate.

- [ ] **Step 4: Implement the service**

Capture live data before enqueue. Call `TryEnqueueCustom` using the Task 2 envelope. Publish `ActorArchivePendingStore` and projection pending only after queue acceptance. Success completes pending and advances `FamilyStructure` once; failure removes pending state. Queue rejection uses `FlushForSynchronousFallback`, then Task 1 persistence inside one synchronous transaction.

- [ ] **Step 5: Replace all direct birth writes**

Update `OnActorBornWithParents`, owned-edge `OnMixedAncestryBorn`, and `WesternLineageParentEdgeService`. `OnActorBorn` remains name initialization. Allow the later baby-name patch to refresh an archive already created by the relationship path even when the child still has no lineage/clan.

- [ ] **Step 6: Run GREEN and full suite**

Run the source guard, targeted slice, and full Rules.Tests.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/lineage Tests/WesternLineageParentEdgeSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests
git commit -m "fix: route lineage births through atomic archive"
```

### Task 4: Versioned legacy orphan repair

**Files:**
- Modify: `Code/core/db/ActorArchiveTableItem.cs`
- Create: `Code/core/db/LineageFamilyArchiveMigration.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/LineageFamilyArchiveMigrationSqlTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write RED migration tests**

Seed one orphan resolvable by a supplied dictionary and one unavailable orphan. Assert repair yields zero orphan edges, `resolved` for the real row, `unresolved_legacy` for the placeholder, and migration version 1. Re-run and assert idempotence. Inject an abort and assert rows/version both roll back.

- [ ] **Step 2: Run RED**

Expected: resolution column and migration class are absent.

- [ ] **Step 3: Add explicit resolution state**

Add `archive_resolution = "resolved"` to `ActorArchiveTableItem` and archive reader/writer column mappings. Placeholders have empty identity/portrait fields, known ids/edges, `is_alive=0`, and `archive_resolution="unresolved_legacy"`; they do not invent species, skin, title, clan, or personal name.

- [ ] **Step 4: Implement atomic versioned repair**

Create a migration-state table keyed by `lineage-family-archive`. In one transaction, left-join orphan child ids, use `Func<long,ActorArchiveTableItem>` for verified live snapshots, otherwise insert placeholders, verify orphan count is zero, write version 1, and commit. Roll back everything on any error.

- [ ] **Step 5: Run GREEN and full suite**

Run the migration slice and full Rules.Tests.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/db/ActorArchiveTableItem.cs Code/core/db/LineageFamilyArchiveMigration.cs Code/core/lineage/LineageArchiveWriter.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "fix: repair legacy lineage orphan edges"
```

### Task 5: Restore lifecycle and placeholder display

**Files:**
- Create: `Code/core/lineage/LineageFamilyArchiveMigrationService.cs`
- Modify: `Code/core/db/LineageArchiveManager.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Code/core/lineage/LineageBulkQuery.cs`
- Modify: `Code/ui/windows/FamilyTreeWindow.cs`
- Modify: `Locales/aw3_family_tree.csv`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AW3RuntimeRestorePipelineTests.cs.txt`

- [ ] **Step 1: Write RED lifecycle/source tests**

Require stage order `localized_name_projection`, `lineage_family_archive_migration`, `western_lineage_migration`; require migration before `AWAsyncWorldLifecycle.StartWorld`; require bulk query to keep unresolved rows and UI to use localized placeholder text.

- [ ] **Step 2: Run RED**

Expected: stage and placeholder handling are absent.

- [ ] **Step 3: Implement World adapter and lifecycle**

Resolve only valid living actors through the shared snapshot capture. Fresh DBs mark migration current; loaded DBs ensure the new table/column at version 0. Run repair synchronously after localized names and before Western migration while the restored DB is exclusive and the historical writer has not started. Log one aggregate scanned/resolved/placeholder/failure summary.

- [ ] **Step 4: Preserve placeholder navigation**

Bulk materialization keeps unresolved rows and known edges. Family tree displays a localized “资料缺失的后代” label and generic unavailable portrait, without guessing a race or Xia portrait.

- [ ] **Step 5: Run GREEN and full suite**

Run restore tests, migration slice, family-tree guards, and full Rules.Tests.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/lineage/LineageFamilyArchiveMigrationService.cs Code/core/db/LineageArchiveManager.cs Code/core/multiplayer/AW3RuntimeRestorePipeline.cs Code/core/lineage/LineageBulkQuery.cs Code/ui/windows/FamilyTreeWindow.cs Locales/aw3_family_tree.csv Tests
git commit -m "fix: migrate and display unresolved lineage descendants"
```

### Task 6: Final verification

**Files:**
- Verify only

- [ ] **Step 1: Run targeted and full tests**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --lineage-atomic-birth-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --lineage-family-migration-slice
powershell -ExecutionPolicy Bypass -File Tests/WesternLineageParentEdgeSourceGuard.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: all pass.

- [ ] **Step 2: Validate a copy of the reported database**

Run repair against a copied save database, then execute:

```sql
PRAGMA quick_check;
SELECT COUNT(*) FROM FamilyEdge e LEFT JOIN ActorArchive a
ON a.ID=e.CHILD_ID WHERE a.ID IS NULL;
SELECT ARCHIVE_RESOLUTION,COUNT(*) FROM ActorArchive
GROUP BY ARCHIVE_RESOLUTION;
```

Expected: `quick_check=ok`, orphan count `0`, and unrecoverable dead children appear only under `unresolved_legacy`.

- [ ] **Step 3: Check the complete diff**

```powershell
git diff --check ba6a413..HEAD
```

Expected: no whitespace errors.
