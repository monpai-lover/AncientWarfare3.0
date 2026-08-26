# AW3 Runtime Scan Removal Design

## Objective

Reduce AW3-owned steady-state CPU cost and long-running deferred-work growth without changing RTS command ownership, RTS P0 execution, transport/portal behavior, Cultiway pathfinding, or the authority scheduler core.

The governing rule is:

1. Delete an AW3 patch or service when the original game already preserves the required invariant.
2. Reuse the original lifecycle or lookup path when AW3 only needs an event notification.
3. Narrow the AW3 hook to exceptional objects and states when the feature has no original equivalent.
4. Add a cache, retry service, or replacement implementation only when the first three options cannot preserve required behavior.

## Evidence And Scope

The current investigation found the following active or plausible AW3 costs:

- `AW_ActorKingdomSafetyPatch` invokes `KingdomFounderSpeciesSafetyService.RepairLoadedKingdoms()` from every `MapBox.Update`, which copies and traverses the kingdom collection even after repair is complete.
- `AW_XiaMinimapKingPatch` traverses all kingdoms during king-marker rendering and may exhaust the collection when no eligible king is visible.
- `AW_CityBuildNullSafetyPatch` pre-scans cities, zones, and buildings before the original `CityManager.updateDirtyBuildings` performs its own dirty-building traversal.
- `AW_WarRefugeePatch` sends every `Actor.joinCity` call through refugee-state lookup, including actors that have no refugee journey.
- Actor age, nutrition, name, and attack-candidate hot methods have multiple AW3 patches whose fixed per-call cost scales with population or combat candidate count.
- Local-office vacancy handling can combine SQL queries, whole-kingdom candidate sorting, and deferred retries.
- Dynastic male-line maintenance performs work approaching kingdom count multiplied by global male title-holder count.
- An older runtime log retained 1,288 deferred items (`runtime=1231`) while reporting `last_drain=1`. The deployed build did not include pending-prefix diagnostics, so the producer distribution remains unproven.

Dead or currently uncalled authority-cycle services are not runtime causes. They may be removed only after reference search proves that they are not save, UI, multiplayer, or reflection entry points.

## Removal Decision Process

Every candidate follows the same audit:

1. Read the complete original method and its callers from the exported WorldBox source.
2. State the exact invariant the AW3 patch was intended to preserve.
3. Search git history for the bug or feature that introduced the patch.
4. Add a source-guard or rule test that captures the required invariant without requiring the patch's current structure.
5. Remove the patch in isolation and run focused tests.
6. If removal breaks a required AW3 invariant, restore only the smallest event bridge or exceptional-state branch.
7. Do not introduce a new cache or scheduler until deletion and original reuse have both been rejected with evidence.

Each deletion must be a separate commit so it can be reverted independently during save testing.

## Candidate Decisions

### Per-frame kingdom repair

Preferred outcome: delete the `MapBox.Update` hook. Retain a bounded load migration only if malformed legacy saves still require founder-species repair. New kingdom creation must rely on the original creation lifecycle plus a one-time AW3 initialization event, not a world scan.

### Dirty-building safety scan

Preferred outcome: delete the Harmony prefix entirely if the current original `CityManager.updateDirtyBuildings` safely tolerates the previously observed invalid references. If an AW3-created wall or stronghold can still introduce invalid building ownership, repair that object at creation or destruction time instead of scanning every dirty city.

### Minimap king rendering

Preferred outcome: reuse the original `QuantumSpriteLibrary.drawKings` behavior and add AW3 eligibility or appearance only for the marker currently being rendered. If the original renderer cannot expose the necessary marker, maintain an event-fed set of AW3 special rulers; never perform an independent full kingdom pass every draw.

### Refugee city transfer

Preferred outcome: ordinary `Actor.joinCity` remains the original path. Refugee settlement is finalized from an in-memory active-journey membership check or the refugee arrival task. SQLite is persistence and recovery storage, not the hot-path membership test.

### Actor age, nutrition, naming, and attack checks

Preferred outcome: delete duplicated patches and preserve one owner per original method. Non-AW3 actors and worlds without an active special state must return directly to the original result. Historical-name lookup may use an ID index only if the localized historical-name feature cannot be moved out of `Actor.getName`.

### Court vacancies and candidate catalogs

Preferred outcome: reuse the original `CityBehCheckLeader` cadence and invoke AW3 appointment only when the city uses an AW3 court template or regional office. Remove frame-based retry loops where the next meaningful state change can re-request appointment. Candidate catalogs should be invalidated by relevant membership or eligibility changes, not every unrelated kingdom assignment.

### Dynastic continuity

Preferred outcome: execute from annual kingdom maintenance but query holders already associated with that kingdom. If an existing title or lineage store can provide that association, reuse it; create a new index only if no authoritative store exists.

### Deferred runtime work

Do not raise the drain budget as the first response. First expose pending counts, enqueue counts, oldest age, and execution time by key prefix. Then delete redundant producers, replace polling retries with state-change events, and bound legitimate retries. Queue fairness changes are permitted only after producer removal demonstrates that starvation still occurs.

## Protected Behavior

The following are out of scope for deletion or replacement:

- RTS units remain P0 and retain persistent military command ownership.
- Attack, defense, retreat, return-home, boarding, sailing, landing, and temporary-boat task chains must remain operational.
- Shared portal and transport handling must not be reverted.
- Cultiway-derived pathfinding and large-step scheduling semantics are not changed by this work.
- Bandit stronghold disposal, de-jure state, courts, schools, chronicles, and multiplayer behavior must retain their externally visible results even when their polling implementation is removed.

## Verification

Use one unchanged large save for before-and-after comparison. Capture at least five minutes for each scenario:

1. Peace, normal map view, no open windows.
2. Peace at 20x with zooming, minimap, actor window, court window, and school window interactions.
3. Active land war with several RTS armies.
4. Active cross-sea war covering embark, temporary-boat sailing, landing, retreat, and return home.
5. City occupation, leader vacancy, kingdom creation/destruction, bandit destruction, and save reload.

Acceptance criteria:

- No AW3-owned per-frame traversal of all kingdoms, cities, zones, buildings, or actors remains unless a rendering API supplies no narrower source and measurements justify it.
- Deferred runtime pending count does not grow monotonically during steady peace or a completed war.
- Removed patches do not reintroduce null-reference errors or corrupt old saves.
- RTS armies do not fall into `waiting_order`, lose missions midway, attack owned cities, or lose P0 movement.
- Performance comparisons report both frame time and simulation speed; a visual FPS improvement alone is insufficient.

## Delivery Order

1. Diagnostic evidence needed to identify deferred producers.
2. Per-frame kingdom repair deletion.
3. Dirty-building prefix deletion or creation-boundary repair.
4. Minimap renderer convergence on the original path.
5. Refugee `joinCity` hot-path removal.
6. Actor hot-method patch consolidation.
7. Court vacancy and candidate retry removal.
8. Dynastic maintenance query narrowing.
9. Deferred producer deletion and, only if still necessary, queue fairness repair.
10. Same-save performance and RTS regression verification.

## Non-Goals

- No new gameplay feature is introduced.
- No broad rewrite of the authority scheduler or pathfinder is included.
- No attempt is made to optimize dead code before active runtime entry points.
- Existing user changes in the dirty worktree are not reverted or reformatted.
