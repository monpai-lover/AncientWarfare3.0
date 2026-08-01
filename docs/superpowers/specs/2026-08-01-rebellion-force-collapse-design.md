# Rebellion Force Collapse Design

## Goal

End a direct-transfer rebellion when the rebel side has no remaining wartime soldiers and no reserve manpower, while immediately recalculating reserve manpower whenever a city changes hands in that rebellion.

## Scope

- Apply only to active wars whose authoritative `WarAsset.rebellion` flag is true.
- Treat the main attacker as the rebel realm and the main defender as the loyalist realm, matching WorldBox rebellion creation.
- Do not change ordinary wars, Zhulu wars, succession-specific settlement rules, or player peace negotiations.
- Keep rebellion city capture as immediate ownership transfer without ordinary peace terms.

## Captured-City Reserve Refresh

- Run only after a direct rebellion capture has completed and the city has its final new owner.
- The existing city-owner callback first removes the city and its members from the previous owner's reserve index.
- Rebuild the captured city's eligible-actor index for the new owner in one bounded complete pass based on the city's current resident count.
- Reconcile the new reserve membership against the new owner's conscription law even while wartime pools are frozen.
- The refresh must use existing `TemporaryLevyService.CanRegisterReserve` eligibility and must not invent manpower or copy the old owner's cached pool.
- After refresh, the realm-level reserve count must immediately include the captured city.

## Rebel Collapse Rule

- A rebellion collapses only when all of these facts are true:
  - the war is valid, active, and marked as a rebellion;
  - the rebel main attacker is valid and still participates in the war;
  - the rebel side has zero living wartime soldiers according to the war's attacker-warrior count;
  - the rebel realm's refreshed reserve pool count is zero.
- A missing or unreadable military count fails closed and does not surrender the rebels.
- Army object count is not used because empty flags and regrouping armies do not represent actual fighting strength.
- Loyalist force collapse does not trigger this rule.

## Runtime Flow

- Add a pure rule that evaluates detached collapse facts.
- Add a dedicated authority-side `RebellionCollapseSettlementService` that queues a coalesced deferred check by war ID.
- Queue the check from the common war-score settlement-check boundary so the last combat death can trigger it.
- Queue it again after a direct rebellion city capture, after the captured-city reserve refresh.
- Re-resolve the war and recompute all facts inside the deferred callback to prevent stale combat or capture state from ending a recovered rebellion.
- When the rule still passes, call `World.world.wars.endWar(war, WarWinner.Defenders)`. Existing `AW_WarPatch` end-war hooks perform cleanup, persistence, reserve unfreezing, history, and diplomatic consequences.
- Multiplayer replicas never queue or execute authority settlement.

## Verification

- Zero rebel soldiers plus zero rebel reserves collapses a rebellion.
- Any rebel soldier prevents collapse.
- Any available reserve prevents collapse.
- Non-rebellion and ended wars never collapse through this service.
- Missing military facts fail closed.
- Direct capture refresh removes old membership, indexes only current eligible residents for the new owner, and respects the new owner's reserve percentage.
- The direct-capture postfix refreshes the city before queueing collapse evaluation.
- Combat settlement checks also queue collapse evaluation.
- Run the complete standalone rules project and focused PowerShell source guards. Do not compile the mod DLL.

