# Bandit, Mandate Border, and Runtime Stability Design

## Scope

This change set closes four failures observed in the v1.2.6 runtime and
adds one connected bandit-progression rule:

1. A bandit stronghold can settle from an actor-death callback before the capture reaches 100%, invalidating actor snapshots still owned by the cooperative runner.
2. Mandate frontier walls can cover watch-tower footprints and block military movement. They also remain at obsolete borders after territory changes.
3. The persistent simulation worker pool can accept a stale wake-up as participation in a newer operation, decrementing the newer operation's completion count without executing its scheduled items.
4. A prepared native military path can become stale before serial commit and enter vanilla `RegionPathFinder.getGlobalPath` with invalid region state.
5. A surviving bandit stronghold applies pressure to one origin city at a time, annexes it after fifty world years, and becomes a founding rebel only after reaching half of the origin kingdom's current Mandate strength.

These changes share one release and verification pass, but keep separate
state, rules, and tests.

## Bandit Stronghold Settlement

An enemy killing the final resident records the hostile killer but does not settle the stronghold. Enemy capture must reach 100%. The `City.finishCapture` interception then suppresses vanilla capture and peace negotiation and queues one coalesced critical-runtime settlement. The deferred action re-resolves the kingdom, city, and persisted state before calling `CompleteFall`.

Population reaching zero without a hostile killer, including starvation, queues the same deferred settlement immediately. Runtime restore also queues settlement instead of mutating city and kingdom collections while enumerating them.

`CompleteFall` remains the single idempotent authority operation. It records both kingdom and city chronicles, restores zones to the mother city, removes stronghold towers, restores stronghold walls, removes the stronghold city, and leaves no atlas territory-change node.

If suppression removes the bandit's final city, the same deferred fall
authority clears any remaining stronghold state, wall, tower, raid, and
loyalty-pressure projection. It does not negotiate or leave a cityless
bandit kingdom with a detached stronghold.

## Bandit Pressure, Encroachment, and Revolution

An active bandit stronghold maintains exactly one pressure target. The
mother city is the first target. The persisted stronghold state stores the
target city id, pressure in the inclusive range `0..300`, and the last world
year applied. Each elapsed world year adds six points, so uninterrupted
pressure reaches 300 after exactly fifty world years. Save restoration may
catch up elapsed years once, but never applies the same year twice.

Only the current target receives the dynamic loyalty contribution
`aw_bandit_pressure`, worth `-25`. The contribution is calculated from the
active persisted state rather than by mutating a cached city loyalty value.
Changing or clearing the target therefore removes the old city's penalty
immediately, and stronghold fall or creation rollback restores loyalty
without a compensating write.

At 300 pressure, a coalesced authority action re-resolves the bandit,
origin, target, and state. A valid target must still be a live city owned by
the origin kingdom. The action temporarily whitelists that city through the
existing bandit territory gate and uses the normal city ownership-transfer
API. It then clears that target's pressure and compares current realm
strength. Ownership transfer is a real territory change and may use the
normal city and kingdom history/atlas behavior; only stronghold destruction
remains excluded from atlas territory nodes.

Strength uses the same Mandate realm-strength calculation already used by
the rebel route. The transition is deterministic:

- If `banditStrength * 2 >= originStrength`, the bandit converts through the existing founding-rebel transition, changes to the peasant-rebel government, and starts the existing revolution war against the origin.
- If the origin is destroyed or has no cities, the bandit converts immediately.
- Otherwise the bandit remains in bandit government and selects one next target from live origin cities directly adjacent to bandit territory. Lowest current loyalty wins, with city id as a stable tie-breaker. The next target starts at zero pressure.
- If no eligible adjacent origin city exists, the bandit remains idle in bandit government and retries selection on later kingdom years. It never jumps across borders or creates an enclave.

If a target is destroyed or changes owner before reaching 300, its pressure
and loyalty contribution are cleared and a new eligible adjacent origin
city is selected. Already annexed cities do not return automatically when a
later target becomes invalid or the stronghold is suppressed.

The previous age/turmoil/random conversion path no longer converts an
active bandit early. Active bandits enter the founding-rebel route only
through the 300-pressure annexation strength check, or immediately when the
origin kingdom ceases to be a viable state.

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
- one pressure target, `-25` loyalty projection, six pressure per unique year, the exact fifty-year threshold, stale target reselection, lowest-loyalty adjacency selection, normal annexation, the 50% Mandate-strength boundary, deterministic rebel conversion, and final-city cleanup;
- per-city manifest replacement, no refresh before decision activation, local affected-city selection, tower-footprint exclusion, tower preservation, and obsolete wall restoration;
- stale and duplicate worker wake-ups not joining a newer generation plus alternating synchronous/asynchronous stress runs;
- stale prepared path fingerprints, invalid region rejection, valid native commits, and conversion of only the expected vanilla global-path null failure;
- focused rule suites, the full rules harness, Release build, deployment source parity, and an in-game smoke test covering capture, border transfer, tower passage, RTS movement, and both reported exception signatures.
