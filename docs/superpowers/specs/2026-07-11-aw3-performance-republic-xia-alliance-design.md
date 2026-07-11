# AW3 Performance, Republic Titles, And Xia Alliance Naming Design

## Goal

Remove the remaining single-frame spikes caused by royal-guard maintenance, slave-army filling, and slave-catcher target searches while preserving immediate gameplay state. Add government-aware Republic titles across live UI and new history records, and give alliances founded by at least one Xia kingdom a stable Chinese name.

## Confirmed Scope

- Guard identity, army membership, profession, traits, slave-soldier state, and election results take effect immediately.
- Database persistence, chronicle writes, actor archival, and graphics rebuilds may complete within one to three seconds.
- Republic rulers display as `元首`; registered Republic successors display as `元老`.
- Republic terminology applies to all live displays and to history snapshots created while the Republic is active.
- Existing monarchy snapshots remain `国王`, `世子`, or `太子` and are not reinterpreted after a government change.
- An alliance uses Xia naming when either founding kingdom is Xia. Later membership changes do not rename it.
- The deleted legacy test projects remain deleted. A new focused verification project covers only this feature set.

## Confirmed Root Causes

### Royal-guard refresh

A new ruler can recruit and refresh up to four guards in one pass. Each identity refresh synchronously mutates actor state, checks whether a SQLite row exists, inserts or updates that row, and may schedule additional graphics and chronicle work. The persistence decision bypasses the runtime refresh limit, so several guard writes can still land in one frame.

### Royal-guard dismissal

The existing two-guard dismissal limit bounds actor count but not work cost. Each dismissed guard synchronously clears identity, traits, jobs, and army membership; upserts guard state; writes person and city chronicles; calls `clearGraphicsFully()`; and archives the actor. Two complete dismissals can therefore exceed the frame budget.

### Slave-army promotion

The original `City.makeWarrior` method changes profession, equips a weapon, and increments the warrior count. The expensive path is the mod Postfix: a promoted slave also triggers slave-state persistence, formation history, special-army maintenance, enlistment chronicles, slave-army renaming, fief-army naming, and guard stripping. The current promotion limit of two still concentrates this chain in one frame.

### Slave-catcher target scans

The profiler ID for catcher target scans combines two unrelated call sites. City assignment scans every unit in every enemy kingdom, while catcher AI asks `Finder` to enumerate an 80-tile radius. An 80-tile radius maps to a square of up to 121 map chunks, so a dense area can still cost about a full frame even after spatial-query conversion.

Profiler totals are nested and must not be added together. For example, guard refresh includes captain, batch, persistence, and runtime sub-timers.

## Considered Approaches

### Smaller batches only

Reducing every batch to one actor is simple, but one dismissal still contains database, chronicle, archive, and graphics work. It lowers frequency without removing the expensive synchronous unit of work.

### Main-thread budget queue and shared scans (selected)

Apply gameplay mutations immediately and enqueue non-gameplay side effects. Drain those tasks on the main thread under time and work-count budgets, coalesce duplicate state writes, and flush persistence before save. Replace whole-radius catcher work with shared, resumable chunk scans. This avoids Unity thread-safety problems while addressing the measured spikes.

### Background threads

Moving work off-thread would require immutable snapshots for all Unity objects and a separate SQLite connection or strict connection synchronization. The current services use live `Actor`, `Kingdom`, and `City` objects and one archive connection, so this approach adds save races and stale-object writeback risk without being necessary.

## Architecture

### Budgeted deferred work

A focused runtime work service owns queues for guard persistence, slave persistence, chronicles, actor archival, and graphics refresh. Queue entries contain stable IDs and immutable text/state snapshots rather than relying on a live object reference. Gameplay services submit work after completing immediate state changes.

The service drains work from a regular main-thread update hook. Each drain stops when either its elapsed-time budget or item-count budget is reached. State persistence for the same actor and state type is coalesced by actor ID; the most recent state wins. Chronicle events remain ordered and are not coalesced because appointment, dismissal, and enlistment are distinct historical facts.

Graphics refresh and archival use a re-resolved live actor when available. If the actor has died or disappeared, graphics work is discarded while snapshot-based persistence and history can still complete. Save hooks synchronously flush all pending persistence and chronicle work before the archive is serialized. Runtime-only queues and search state are cleared when a world unloads.

### Royal-guard flow

Guard appointment and dismissal immediately update actor data, traits, profession, job, and army membership. They then enqueue guard-state persistence, chronicle events, archival, and graphics refresh. Repeated graphics or archive requests for the same actor collapse into one pending task.

The existing recruitment and dismissal scans remain bounded, but their expensive side effects no longer execute inside the scan timer. New benchmark IDs distinguish immediate state mutation, enqueue cost, and deferred flush cost.

### Slave-army flow

During a slave-army fill pass, promotion performs only the required warrior transition, slave-soldier marker, and army attachment. The `City.makeWarrior` Postfix recognizes the active fill context and does not recursively ensure the slave army, persist each actor, record formation repeatedly, or rename the same army for every promotion.

At the end of the fill pass, the service queues one state write and one enlistment history event per newly enlisted actor, while formation recording and army naming run once for the completed batch. Deferred state writes are coalesced if the same actor changes again before flush.

### Shared resumable catcher scans

Target-search state is keyed by kingdom, island, and origin chunk. Cities and catchers in the same region reuse a validated result or the same in-progress scan. The scan preserves the exact 80-tile radius and all current eligibility rules: hostility, island, adulthood, life state, health threshold, slavery eligibility, and important-person restrictions.

Instead of enumerating up to 121 chunks in one call, the scan stores a chunk cursor and current nearest candidate. A main-thread budget advances it across frames and pauses at the time or unit-count limit. A valid nearby candidate may finish the scan early; a no-target result is published only after the full radius is covered.

City catcher assignment no longer scans all enemy kingdoms. It reads a shared result or submits a regional scan, then assigns the job once a valid target is available. Catcher AI reuses the shared target and submits or joins a scan when no valid result exists. Positive cache entries are short-lived; negative entries live longer. Every cache read revalidates actor existence, hostility, island, and distance.

Profiler IDs separate city gating, scan submission, incremental scan work, cache hits, immediate state work, queue insertion, and deferred flushing.

## Republic Terminology

A centralized government-title rule resolves ruler and successor labels with this precedence:

1. Republic
2. Mandate government
3. Ordinary monarchy

For a Republic, the ruler label key resolves to `元首` and the registered elective successor label key resolves to `元老`. Live social titles use `国名 元首` and `国名 元老`. Ordinary monarchies continue to use their existing title character, `世子`, or `太子`.

The rule is used by the kingdom-window avatar labels, original stats rows, city-window ruler rows, live family-tree titles, actor archive snapshots, and history role snapshots. Republic history stores distinct role values such as `republic_head` and `republic_elder`; the history window localizes those values without inspecting the kingdom's current government. Existing `king`, `heir_shizi`, and `heir_taizi` rows remain unchanged.

New localization keys provide Simplified Chinese, English, and Traditional Chinese strings. Chinese uses `元首` and `元老`; English uses `Head of State` and `Elder`.

## Xia Alliance Naming

`XiaNameSets` gains an alliance generator with classical roots and suffixes, producing names such as `诸夏盟`, `九州会盟`, `河洛同盟`, and `王畿盟誓`.

An `Alliance.addFounders` Postfix checks the two explicit founding kingdoms. If either is Xia, it replaces the original generated name with a Xia alliance name after both founders have joined but before `AllianceManager.newAlliance` writes the creation world log. If neither founder is Xia, the original name is untouched. The name is assigned only at creation and is not recomputed when members join or leave.

## Error Handling

- A deferred item re-resolves live objects by ID and uses its immutable snapshot when the live object no longer exists.
- One failed item does not stop the queue. Transient database-unavailable failures receive a bounded retry; permanent failures are logged and removed.
- Graphics-only work is safely discarded when the world or actor is unavailable.
- Save flushing ignores the frame budget for persistence and chronicle correctness, but does not attempt invalid graphics work.
- Search entries are invalidated on world clear and revalidated on every read, so stale targets cannot be returned.
- Queue coalescing never removes ordered chronicle events.

## Verification Strategy

The deleted legacy test projects are not restored. One new focused executable test project covers pure rules for:

- deferred-work coalescing, ordering, retry, and budget-stop decisions;
- chunk-cursor progression, complete-scan detection, cache reuse, and invalidation;
- Republic ruler/successor keys, precedence, social titles, and historical role snapshots;
- Xia alliance eligibility when either, both, or neither founder is Xia;
- fill-context suppression of repeated persistence, formation, and rename work.

Tests are written and observed failing before production changes. Focused verification then runs after each component, followed by `dotnet build`. In-game acceptance uses the profiler at 60 FPS, where the effective frame budget is approximately 10.83 ms. Guard refresh, guard dismissal, slave-army fill, and catcher incremental scan rolling averages must remain below 100 percent, and no single frame may flush several actors' expensive side effects.

## Success Criteria

1. Guard and slave gameplay state remains immediate.
2. Guard persistence, chronicles, archival, and graphics work are spread over frames and finish within approximately one to three seconds.
3. Slave-army promotion does not recursively repeat persistence, formation, ensure, and rename operations per actor.
4. No catcher call enumerates the complete 80-tile region or every enemy kingdom in one frame.
5. Profiler labels identify the exact stage responsible for remaining cost.
6. All live Republic ruler and successor displays show `元首` and `元老`.
7. New Republic history snapshots retain Republic terminology after later government changes, while old monarchy snapshots remain unchanged.
8. Alliances with at least one Xia founder receive a stable Chinese name before their creation log is emitted.
9. Non-Xia alliance naming and ordinary monarchy terminology remain unchanged.
10. The focused verification project and the mod build pass without restoring or staging the deleted legacy tests.
