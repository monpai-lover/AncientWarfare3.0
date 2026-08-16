# Bandit, Mandate Border, and Runtime Stability Design

## Scope

This change set closes four failures observed in the v1.2.6 runtime:

1. A bandit stronghold can settle from an actor-death callback before the capture reaches 100%, invalidating actor snapshots still owned by the cooperative runner.
2. Mandate frontier walls can cover watch-tower footprints and block military movement. They also remain at obsolete borders after territory changes.
3. The persistent simulation worker pool can accept a stale wake-up as participation in a newer operation, decrementing the newer operation's completion count without executing its scheduled items.
4. A prepared native military path can become stale before serial commit and enter vanilla `RegionPathFinder.getGlobalPath` with invalid region state.

The four fixes share one release and verification pass, but keep separate state, rules, and tests.

## Bandit Stronghold Settlement

An enemy killing the final resident records the hostile killer but does not settle the stronghold. Enemy capture must reach 100%. The `City.finishCapture` interception then suppresses vanilla capture and peace negotiation and queues one coalesced critical-runtime settlement. The deferred action re-resolves the kingdom, city, and persisted state before calling `CompleteFall`.

Population reaching zero without a hostile killer, including starvation, queues the same deferred settlement immediately. Runtime restore also queues settlement instead of mutating city and kingdom collections while enumerating them.

`CompleteFall` remains the single idempotent authority operation. It records both kingdom and city chronicles, restores zones to the mother city, removes stronghold towers, restores stronghold walls, removes the stronghold city, and leaves no atlas territory-change node.

## Mandate Frontier Wall Lifecycle

Executing the `mandate_border_defense` decision writes a persistent activation flag on that kingdom. War start and territory events may refresh walls only when this flag is present; they never activate the feature implicitly.

Each wall-owning city has a persisted manifest containing the wall tile coordinates and the original top-tile id for each coordinate. Refresh is local:

- A city ownership or zone ownership change queues the changed city, its previous city where applicable, and their direct neighboring cities.
- Work is coalesced by city id and runs at a safe authority boundary.
- The refresh restores only wall tiles recorded in that city's manifest. It never removes towers and never scans or removes unrelated `wall_order` tiles.
- If the city is still an eligible frontier city, the service computes its current frontier, removes every watch-tower footprint from the planned wall points, places the new wall, and writes a replacement manifest.
- If the city is no longer an eligible frontier city, it only restores its old manifest and clears it.

The tower asset or race does not matter. Any live building with `asset.type == "type_watch_tower"` reserves its complete footprint. A tower is the passage: no additional artificial road or three-tile gap is carved. Existing towers survive every wall refresh.

The decision builds towers before the wall refresh so a newly constructed frontier tower is included in the same refresh. Guard and tower caps retain their current behavior. Wall refresh is no longer limited by the three-city border-army selection; only changed eligible frontier cities are rebuilt after activation.

Legacy v1.2.6 walls have no ownership metadata and cannot be distinguished safely from unrelated `wall_order` tiles. The first execution after upgrade adopts only newly computed/placed wall points into manifests. It does not perform a destructive global legacy-wall sweep.

## Simulation Worker Dispatch

Each worker receives an atomic assigned-generation token in addition to its `AutoResetEvent`. `StartOperation` assigns the new generation before signaling a worker. `WorkerLoop` consumes the assignment exactly once and participates only when the consumed generation matches the active generation. A stale or duplicate wake-up with no assignment performs no work and does not decrement `_remainingParticipants`.

The existing operation generation, action exception propagation, synchronous main-thread participation, and coordinator assistance remain unchanged. Diagnostics add the active generation and remaining participant count when completion is inconsistent.

## Prepared Native Path Commit

Prepared serial path work captures an actor-state fingerprint: actor id, current tile id, target tile id, local-path cursor, and whether a global path existed. At commit, AW3 revalidates actor liveness, batch membership, start/target tiles, start/target regions, and the fingerprint. A stale item is discarded and leaves the actor for a later valid movement pass; it does not call `Actor.updatePathMovement`.

For a valid native military path, the call remains serial. Before vanilla `Actor.goTo` is allowed, the prefix rejects a missing start tile, target tile, tile type, or region and clears the unusable path state. As defense in depth, the `RegionPathFinder.getGlobalPath` finalizer converts only recognized invalid-region `NullReferenceException` failures to `NotFound`, clears `last_globalPath`, records a rate-limited diagnostic, and lets unrelated exceptions propagate.

## Error Handling and Multiplayer

All world mutations run only when `PeasantRebelRouteRules.CanMutateAuthority` or the equivalent multiplayer authority guard allows them. Replica application does not schedule wall or stronghold authority work.

Persisted wall state is schema-versioned JSON. Corrupt manifests are logged and ignored without scanning the map. Deferred actions always re-resolve ids and return when their objects no longer exist. Wall restoration changes a tile only when it still contains the wall type placed by this system; player edits made afterward are preserved.

## Verification

Tests cover:

- hostile zero population waiting for 100% capture, environmental zero population queueing settlement, and capture settlement being coalesced;
- per-city manifest replacement, no refresh before decision activation, local affected-city selection, tower-footprint exclusion, tower preservation, and obsolete wall restoration;
- stale and duplicate worker wake-ups not joining a newer generation plus alternating synchronous/asynchronous stress runs;
- stale prepared path fingerprints, invalid region rejection, valid native commits, and conversion of only the expected vanilla global-path null failure;
- focused rule suites, the full rules harness, Release build, deployment source parity, and an in-game smoke test covering capture, border transfer, tower passage, RTS movement, and both reported exception signatures.
