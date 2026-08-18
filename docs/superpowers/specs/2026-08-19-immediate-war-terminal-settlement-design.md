# Immediate War Terminal Settlement Design

## Objective

End an active negotiable war as soon as any authoritative terminal condition
is satisfied. Settlement must use AW3's existing treaty execution pipeline so
territory, subject relations, truces, persistence, and chronicles remain
consistent.

## Confirmed Rules

The terminal conditions are evaluated in this priority order:

1. Existing special-war guards retain authority. Zhulu wars and direct
   rebellion territory-transfer wars are not routed through ordinary peace.
2. A signed war score of `+100` or `-100` forces a maximum-benefit settlement
   for the winning side.
3. If exactly one side has zero active warriors, that side immediately
   surrenders. No multi-month confirmation is required. The winning side
   receives a full 100-point maximum-benefit settlement budget.
4. If one or more declared war-goal terms are legal and their persisted costs
   fit within the attacker's current positive war score, the affordable goal
   bundle is applied immediately. Defender resolve, AI acceptance, random
   rolls, proposal cooldowns, and annual settlement assessment do not apply.
5. If both sides have zero active warriors, the side favored by current war
   score receives the maximum legal benefit allowed by that score. A zero
   score produces white peace.

At 100 score and after one-sided military elimination, "maximum benefit"
means the existing bounded optimizer in `WarPeaceDefaultOfferRules`, not the
ordinary greedy default offer builder. It selects the highest legal total
value without exceeding the 100-point treaty cap or violating source-survival
rules.

## Architecture

Introduce one authoritative terminal-settlement coordinator. Existing score,
death, occupation, and war-goal mutation sites notify it immediately. The
authority cycle also examines a bounded number of active wars as a recovery
path, so a transient persistence failure or a missed event cannot leave a war
permanently active.

The coordinator only decides which terminal reason owns the war and queues
one coalesced settlement task per war. It delegates draft construction and
execution to the existing services:

- `WarPeaceSettlementService` remains the only ordinary treaty executor.
- `WarGoalSettlementRuntimeService` remains responsible for exact persisted
  war-goal bindings.
- `WarForceSpecialSettlementService`, `ZhuluPeaceGuard`, and
  `RebellionDirectTerritoryTransferService` retain their special behavior.
- `WarScoreService` remains the authoritative score source.

The existing independent decisive, force-elimination, and war-goal entry
points remain as compatibility adapters and route through the coordinator, so
they cannot race or choose conflicting outcomes.

## State And Retry Behavior

Each live war has at most one queued terminal settlement. Coalescing is keyed
by war ID. Before execution, the coordinator re-reads the war, participants,
score, force counts, goal rows, and special-war guards. If the terminal facts
changed, it discards the stale decision and evaluates again.

A failed settlement is reported with `ModClass.LogError`, including war ID,
terminal reason, attempt count, and failure reason. It is retried by the
bounded authority-cycle recovery path rather than relying on two immediate
same-frame retries. Successful or ended wars are removed from runtime state.

## Triggering And Performance

Immediate checks run after authoritative changes to:

- battle/death totals;
- city or war-goal control;
- signed war score;
- participant or active-warrior counts.

The recovery scan is cursor-based and bounded per authority cycle. It does
not scan actors, cities, or the whole map. Force elimination uses the native
war participant warrior counters for ordinary sides and the cached special
government participation count described below.

## Compatibility

The annual AI peace proposal system remains for non-terminal negotiations.
Terminal settlement bypasses its yearly assessment gate and acceptance logic.
Existing actionable treaty execution is resumed when valid; stale pending
proposals may not block a terminal settlement.

Multiplayer replicas remain read-only. Only the authoritative simulation may
queue or execute terminal settlements.

## Special Government Combat Participation

Bandit kingdoms, peasant-rebel kingdoms, and active military governorates use
an AW3 war-participation policy in addition to the terminal settlement rules.

Kings, recognized heirs, and city leaders keep their original identity,
profession, office, and succession data. During an active war they are allowed
to join the relevant army roster, receive RTS movement/combat ownership, and
count as live combatants. On war end their ordinary identity protection and
native jobs are restored. They are not converted to `Warrior` merely to enter
combat.

Bandit and peasant-rebel kingdoms additionally use the vanilla angry-civilian
combat semantics for that kingdom only. Every living, non-boat resident that
the vanilla actor system can simulate may receive hostile combat targets,
without the ordinary adult-male levy ratio or permanent profession changes.
The global `world_law_angry_civilians` switch is never changed, so ordinary
kingdoms keep the player's world-law setting.

The scoped policy is applied at the same combat-eligibility and movement/task
boundaries used by the vanilla `BaseSimObject.canAttackTarget` path. It must
not grant attack permission to unrelated friendly actors, buildings, invalid
targets, or dead actors. Existing RTS ownership arbitration remains the sole
movement owner for actors assigned to an AW3 army.

Force-elimination facts use the participation policy. Ordinary kingdoms and
military governorates use native war warrior counters plus their eligible
command actors. Bandit and rebel sides count their eligible living residents,
so civilians who are allowed to fight cannot be ignored when deciding that a
side has been eliminated. This count is maintained incrementally from actor
lifecycle and kingdom-membership changes. A missing cache is reconciled in
bounded batches over that kingdom's own unit list; no terminal check performs
a synchronous world-population scan.

## War Name Display Contract

`WarRuntimeDisplayService` is the only source for AW3 war names. The
localized war asset key is authoritative and resolves to the Chinese type
name represented by keys such as `war_name_aw_normal_war`,
`war_name_bandit_suppression_war`, or `war_name_reclaim`. The live native
`War.name` string is never preferred when it is an English/generated name
such as `Great War of ...`.

All UI, chronicle, diplomacy conversation, truce, and war-record consumers
must call this service. The negotiation window is included; it must not pass
`war.name` directly. If the asset has no usable localized key, the service
returns the localized `aw_diplomacy_unnamed_war` fallback. Names already persisted as
unstructured historical free text are not rewritten with heuristic string
replacement; new and currently resolved runtime records use the corrected
contract.

## Verification

Rules and source-guard tests cover:

- one zero-force observation causes immediate surrender;
- one-sided elimination receives a positive 100-point winner budget;
- mutual zero forces use score or white peace;
- `+100/-100` selects the correct winner and maximum-benefit mode;
- an affordable legal war-goal bundle settles without acceptance checks;
- special-war guards remain ahead of ordinary terminal rules;
- repeated notifications coalesce to one execution;
- failed execution remains eligible for bounded recovery;
- the authority fallback is bounded and performs no actor or city scan;
- special-government rulers and heirs remain identity-safe while becoming live
  combatants;
- bandit/rebel scoped angry-civilian combat does not change the global world
  law or ordinary kingdoms;
- rebel/bandit elimination counts eligible civilians;
- every war-name consumer uses the localized type service and the negotiation
  window has no direct `war.name` display path.

The full rules suite and release build must pass before deployment. Runtime
testing should cover attacker victory, defender victory, military elimination,
an affordable city goal, and a guarded special war.
