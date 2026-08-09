# King Death Succession Performance Root-Cause Design

## Problem Statement

Large saves visibly stall when a ruler dies. Runtime diagnostics already isolate
the dominant AW3 cost from native death, pathfinding, and ordinary Actor death:

- `king_heir_prepare` reached 349.797 ms in one sampled interval.
- `king_civil_service` reached 35.420 ms in another interval.
- Ordinary AW3 death work was generally 1-10 ms.

The current death prefix synchronously selects or re-stores the heir, prepares a
succession dispute, and later performs a civil-service SQLite transaction. The
same dead ruler can also enter preparation again through
`KingdomBehCheckKing.execute`. This combines duplicate work, N+1 lineage reads,
realm scans, live-object mutation, and SQLite commits in a frame that is already
executing native death cleanup.

## Goals

1. Keep the native `KingdomBehCheckKing` workflow as the sole controller that
   clears a dead king, obtains a candidate, moves the candidate to the capital,
   and calls `Kingdom.setKing`.
2. Make the AW3 portion of `Actor.die` constant-time for a king: capture IDs and
   immutable facts only, with no SQLite and no global collection scans.
3. Preserve Xia, Xiaized, republic, military-acclaim, civil-acclaim, collateral,
   underage, and succession-dispute behavior.
4. Prevent one dead ruler from being prepared twice.
5. Move candidate and dispute computation into stable-reign incremental work,
   then consume a revision-validated snapshot when the king dies.
6. Move ruler-death persistence to the existing historical database worker.
7. Keep all Unity and WorldBox object access on the authority/main thread.

## Non-Goals

- Do not change RTS, pathfinding, large-step scheduling, annual policy rules, or
  war settlement.
- Do not make Actor, Kingdom, City, Clan, Army, or Unity calls from a worker.
- Do not replace special AW3 succession laws with native royal-clan semantics.
- Do not change native succession delay, profession changes, new-king logging,
  or `Kingdom.setKing` behavior for a realm whose capital and royal clan state
  are valid. Damaged-realm recovery is defined explicitly below.

## Confirmed Current Flow

```text
Actor.die Prefix
  -> HeirService.PrepareSuccessionBeforeKingDeath
     -> remember legitimate line
     -> select candidate when stored heir is invalid
     -> clear old heir flag by scanning kingdoms
     -> store candidate and repeat lineage/archive projections
     -> SuccessionDisputeService.Prepare
        -> resolve three inheritance factions
        -> query family relationships repeatedly
        -> scan cities/generals/officers
        -> synchronously write dispute rows
  -> native Actor death
Actor.die Postfix
  -> synchronous civil-service lookup/update transaction

Later KingdomBehCheckKing.execute Prefix
  -> HeirService.PrepareSuccessionBeforeKingDeath again
  -> native KingdomBehCheckKing.execute
     -> clearKingData
     -> SuccessionTool candidate hook
     -> native move-to-capital logic
     -> native Kingdom.setKing
```

The native installation path is already the correct integration boundary. AW3
should provide a ready candidate and post-install projections, not execute a
second succession controller inside `Actor.die`.

## Chosen Architecture

### 1. Constant-Time Death Capture

`KingSuccessionPreparationService.CaptureDeath` records one pending context per
kingdom. The context contains scalar IDs, inheritance mode, selection revision,
and the cached candidate ID. It never retains Actor or Kingdom references.

The key is `(worldGeneration, kingdomId, predecessorActorId)`. Repeated capture
of the same key is a no-op. A different predecessor replaces only stale state
after validating that the installed king has changed.

The death prefix copies legitimate lineage and Shi IDs directly from
`Actor.data`. Missing archive-derived identity is marked dirty for later repair;
it is not queried synchronously.

### 2. Original-Flow Succession Gate

`AW_MandateSuccessionPatch.Execute_Prefix` becomes the only managed succession
gate. For an alive king it returns to native immediately. For a dead managed
king it asks the preparation service whether a revision-valid candidate is
ready:

- Ready: publish the selected candidate into kingdom data and return `true`, so
  native `KingdomBehCheckKing.execute` performs the installation.
- Not ready: queue or prioritize preparation, return `false` with
  `BehResult.Continue`, and allow the native kingdom behavior to retry later.

Unmanaged kingdoms continue through `SuccessionTool` with no AW3 candidate
override. Managed kingdoms still use AW3 candidate law, but all installation
remains native.

### 3. Live Succession Relationship Index

`SuccessionRelationshipIndex` is a main-thread runtime index containing:

- father by living Actor ID;
- living children by father ID;
- living Actor IDs by lineage ID;
- living Actor IDs by Shi ID.

It is rebuilt incrementally after world load and maintained by birth, death,
lineage assignment, and world-clear hooks. Candidate resolution uses the index
instead of `LineageQuery.GetChildIds`, `GetFatherId`,
`GetLivingLineageMemberIds`, and `GetLivingShiMemberIds` on the hot path.

Only living Actors can be installed, so archived dead members are not required
in this runtime index. While the index is rebuilding, managed succession waits
instead of falling back to a synchronous database/tree scan.

### 4. Revisioned Stable-Reign Snapshot

`SuccessionPreparationCache` stores one immutable snapshot per managed kingdom.
It includes:

- king ID and kingdom ID;
- selected candidate ID and succession mode;
- effective inheritance law;
- relation, law, court, general, city-leader, and city-ownership revisions;
- alternate claimant and support totals;
- selected dispute-support city IDs.

Birth/death, heir changes, inheritance-law changes, court/general changes, city
leader changes, and city ownership changes mark only the affected kingdom
dirty. `AWAuthorityCycleService` processes at most one dirty kingdom per cycle.
Candidate gathering uses the live index; no SQL runs during snapshot building.

At death, a matching snapshot is consumed in O(1). A stale or absent snapshot
causes a short native succession delay while it is rebuilt. Correctness wins
over synchronously scanning the world.

### 5. Idempotent Heir Storage

`HeirService.StoreHeirSelection` receives a no-op gate based on candidate ID,
mode, reference-king ID, and dirty revision. An unchanged, already-signed
selection does not:

- scan every kingdom to clear a global heir flag;
- release/recall the same Actor;
- recreate lineage or school data;
- archive the same Actor;
- refresh medical targets or family-tree projections.

After a new king is installed, the reference-king ID changes, so normal
`RefreshHeir` still runs and selects/signs the next generation correctly.

### 6. Asynchronous Succession-Dispute Persistence

The stable snapshot already contains claimant and city-support facts.
`SuccessionDisputeService` converts those facts into an immutable database
envelope and submits it through `HistoricalWriteService.TryEnqueueCustom`.

The custom envelope allocates the dispute ID and inserts dispute/city rows in
one worker transaction. Its completion callback publishes the runtime snapshot
only if the same world, kingdom, predecessor, and successor are still current.
Stale completions are discarded.

If the database worker is unavailable, runtime succession continues from the
in-memory snapshot. Persistence remains pending and is flushed during the
existing paused save barrier; active gameplay does not perform a synchronous
fallback.

### 7. Indexed Civil-Service Ruler-Death Handling

`CivilServiceExamService.RebuildRuntime` builds an in-memory mapping from
kingdom ID to the one session awaiting player ranking. Session transitions keep
the mapping current.

Ruler death first checks this map. The common no-session case returns without
SQLite. The positive case updates the in-memory due set and submits a custom
compare-and-set envelope to the historical worker. A covering partial index is
added for old saves and worker-side verification.

### 8. Diagnostics

Before behavior changes, diagnostics split `king_heir_prepare` into context,
candidate, heir-store, dispute lookup, faction support, city support, and
persistence stages. `Kingdom.setKing` prefix/postfix work receives separate
identity, lineage, heir-refresh, dispute, and chronicle stages.

Diagnostics remain allocation-free unless runtime diagnostics are enabled.
They are retained until final acceptance so an apparent improvement cannot
hide a new post-death spike one or two frames later.

### 9. Damaged-Realm Accession Recovery

Managed succession repairs invalid realm identity before returning control to
native `KingdomBehCheckKing`:

- A dangling `royal_clan_id` is normalized to `-1`; native code must never
  dereference a missing or destroyed clan.
- The leaders branch returns a non-null published AW3 candidate. A missing
  royal clan must not discard a prepared heir or leave the realm permanently
  kingless.
- A foreign actor becomes a citizen when selected as the registered heir, not
  when the old ruler dies. With a valid capital the heir moves there
  immediately. Without a capital the heir joins a living city owned by the
  destination realm as a temporary home.
- A domestic heir may succeed from the heir's current living home city while
  the realm has no capital. The absence of a capital cannot create a state in
  which native `setKing` succeeds but AW3 skips succession finalization.
- When a valid capital later exists, the installed king moves to it and the
  actor's home projection is refreshed. This relocation is idempotent and must
  not repeat branch creation, dispute persistence, heir clearing, chronicles,
  or multiplayer installation notification.
- If no owned living city exists, native kingship may still complete and home
  relocation remains deferred. Missing location data cannot pause simulation
  or veto the ruler.
- After native `setKing`, a king without a clan uses the native
  `world.clans.newClan(king, true)` plus `kingdom.trySetRoyalClan()` sequence.
  Clan creation failure is non-fatal and cannot roll back kingship.

## Cultiway Perf Scheduler Non-Regression Boundary

The already-ported `Cultiway-Reborn-perf` large-step scheduler is a protected
baseline for this work. Succession preparation is domain maintenance owned by
AW3; it is not a new simulation stage and must not be inserted into Cultiway's
fixed-step runner, batch cursor, parallel Actor jobs, presentation clock, save
barrier, pause transition, or world-clear choreography.

The only scheduler integration is one bounded call from
`AWAuthorityCycleService.ProcessCycle`, after the existing authority token gate
has accepted the cycle. `SuccessionPreparationService.ProcessAuthorityCycle(1)`
therefore runs at most once for a cooperative logical-cycle token and at most
once for a native-cycle token. It must return immediately when its dirty queue
is empty and must never wait on a worker, database, lock, or full-world rebuild.

The following scheduler-port files are read-only for this implementation:

- `Code/patch/AW_FramePrioritySchedulerPatch.cs`
- `Code/core/performance/AWCooperativeSimulationRunner.cs`
- `Code/core/performance/AWCooperativeBatchRunner.cs`
- `Code/core/performance/AWCooperativeActorParallelJobRunner.cs`
- `Code/core/performance/AWFrameSchedulerRules.cs`
- `Code/core/performance/AWSimulationStepContext.cs`

The implementation branch records their current SHA-256 values in a source
guard before behavior changes. Final verification compares those values and
the protected lifecycle invariants against `F:/WorldBox New
Mod/Cultiway-Reborn-perf`: frame-begin read boundaries, one admitted logical
cycle at a time, fixed-step context restoration, presentation completion,
save-time drain, abort-before-clear, and post-world-creation reset. A hash
change is a hard failure for this plan; a future scheduler upgrade must be a
separate reviewed change with its own Cultiway baseline.

The new succession indexes and caches contain only main-thread WorldBox object
lookups and scalar/immutable records. Worker envelopes contain IDs, primitive
values, and SQL parameters only. Completion callbacks are drained through the
existing AW authority completion path and revision-check before publishing;
they do not create another scheduler or independently advance simulation.

## Failure Handling

- Duplicate death capture returns the existing context.
- A stale preparation snapshot is never installed; native succession waits.
- A candidate that dies or changes kingdom between snapshot and native lookup
  invalidates the snapshot and queues a rebuild.
- Database worker failure does not block native succession. The pending runtime
  item remains observable and is retried or flushed at save.
- A completion for another world generation is ignored.
- World clear resets contexts, indexes, revisions, pending persistence, and
  diagnostic identities.
- Multiplayer replicas do not select candidates or write succession state;
  they continue to apply authority results through the existing facade.

## Performance Acceptance

Use the same large save and diagnostics configuration that produced the
349.797 ms spike.

- `Actor.die` king-specific AW3 work: target <= 2 ms, hard ceiling 5 ms.
- No `king_heir_prepare`, civil-service, or AW3 database stage above 5 ms on the
  death frame.
- No post-death AW3 frame above 16.7 ms in the next 120 frames.
- Zero synchronous SQLite commands from `Actor.die`,
  `KingdomBehCheckKing.execute` prefixes, and `Kingdom.setKing` hooks.
- Cached valid-heir succession performs zero candidate scans and zero all-world
  kingdom/Actor scans.
- Cold or stale candidate preparation examines at most the indexed living
  lineage members and does not enumerate `World.world.units`.
- Native and large-step scheduler modes produce the same successor, dispute,
  civil-service, chronicle, mandate, and multiplayer outcomes.
- All six protected scheduler-port files retain their pre-implementation
  SHA-256 values and Cultiway lifecycle source guards pass.

## Test Matrix

1. Managed monarchy with a valid registered heir.
2. Registered heir dies before the king.
3. King has no direct son and uses a collateral heir.
4. Military-acclaim and civil-acclaim successions.
5. Republic leader death.
6. Multi-city realm with a valid succession dispute.
7. Single-city realm where no dispute can form.
8. No legal successor and native chaos/fallback behavior.
9. Heir is a royal guard, general, official, or foreign-returning royal.
10. Multiplayer authority and replica succession.
11. Save immediately after death and reload before dispute materialization.
12. Native scheduler and large-step scheduler on the same deterministic save.

## Delivery Order

Implement diagnostics first, then duplicate/no-op removal, then the live index
and stable snapshot, then asynchronous dispute and civil-service persistence.
Each layer has a focused rollback point and must demonstrate its own performance
change before the next layer is enabled.
