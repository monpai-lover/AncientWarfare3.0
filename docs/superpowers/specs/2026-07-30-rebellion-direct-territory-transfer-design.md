# Rebellion Direct Territory Transfer Design

## Goal

All AW3 internal wars whose `WarTypeAsset.rebellion` flag is true use direct,
two-way territorial transfer instead of frozen occupation and ordinary peace
negotiation.

- A rebel realm that captures an old-regime city immediately owns that city.
- The old regime that recaptures a rebel city immediately owns that city.
- The actual capturing kingdom receives the city. Vassal or coalition capture
  redirection must not substitute a suzerain or war leader in this path.
- Ordinary interstate wars retain frozen occupation and negotiated cession.

## Scope

The rule is based on the authoritative war-type `rebellion` flag, not a loose
name match. It therefore covers the currently registered internal-war types:

- `tianmingrebel`
- `general_rebellion_war`
- `fief_independence_war`
- `jingnan_war`
- `succession_dispute_war`
- `coup_restoration_war`

New war types opt in only by registering `rebellion = true`.

Hosted restoration wars, ordinary independence wars, vassal wars, Mandate
wars between established states, and normal interstate wars do not enter this
path unless their authoritative war asset is explicitly marked as rebellion.

## Direct-Capture Authority

Introduce a pure rule and a bounded runtime resolver.

The pure rule accepts a transfer only when all of these facts are true:

1. the city and proposed capturing kingdom are valid and different;
2. there is an active, non-ended war containing both the current city owner
   and the proposed capturing kingdom on opposing sides;
3. that exact war's authoritative asset has `rebellion = true`.

The runtime resolver returns the matching rebellion war. It must not infer
authority merely because either kingdom has a rebel trait, government class,
Mandate marker, old parent relation, or involvement in another rebellion.
This prevents a third-party war from turning an ordinary conquest into an
instant transfer.

The lookup reuses the existing bounded active-war/index path where possible.
It must not scan all historical wars or every kingdom.

## Capture Pipeline

`AW_CityOccupationAccelerationPatch.FinishCapture_Prefix` checks the direct
rebellion resolver before vassal capture redirection and before frozen
occupation is recorded.

When direct rebellion authority exists:

1. retain the actual `pNewKingdom` supplied by the capturing military actor;
2. do not call `VassalCaptureService.ResolveCaptureRecipient`;
3. do not call `TryFreezeCityOccupation` or create a pending frozen capture;
4. do not queue a non-territorial settlement;
5. allow the original `City.finishCapture` transfer to run immediately.

`JoinCapturedCity_Prefix` applies the same authority check so the nested
`joinAnotherKingdom(..., pCaptured: true)` call cannot redirect the city to a
suzerain after the outer prefix approved a direct rebel transfer.

The finish-capture Harmony state carries the old owner, actual capturing
kingdom, and matched rebellion war ID from prefix to postfix. The nested join
guard may revalidate the exact active war, but the postfix must use the
captured war ID rather than rediscovering a war after ownership has changed.
No process-wide "currently capturing" flag is allowed because nested or
back-to-back captures could leak authority between cities.

The existing capture postfix remains responsible for garrison cleanup, city
owner change notifications, war-director retargeting, population protection,
and other normal ownership callbacks. The new path does not duplicate these
effects.

If stale frozen-control state for the same city and matching rebellion war is
present from an older build or interrupted capture, the direct-transfer path
clears that state through the existing war-score cleanup API after ownership
changes. It must not clear control rows belonging to unrelated simultaneous
wars.

## Peace And War Completion

An active direct-transfer rebellion war cannot create or submit an ordinary
`WarPeaceSettlementProposal`:

- the player negotiation action is unavailable with a stable reason;
- AI peace, surrender, enforce-demands, decisive-score, and exhaustion
  settlement paths skip the war;
- no war-score-100 treaty is forced for this category.

The authoritative rejection reason is
`rebellion_uses_direct_territory_transfer`. It is returned before a proposal
is persisted. Any old pending ordinary settlement for the same still-active
rebellion war is cancelled during settlement recovery or execution with that
same reason, so loading an older save cannot apply a stale treaty.

This block belongs in the authoritative settlement context/creation path;
UI and AI filters are secondary guards only.

Dedicated internal-war completion remains authoritative. Jingnan, succession
dispute, coup restoration, Mandate rebel, and other rebellion services may end
their wars when their own victory conditions are met. Otherwise the native
war lifecycle ends the war when one side no longer has a viable realm. This
feature does not invent a new generic peace or victory condition.

War score may continue to exist for display or dedicated logic, but it cannot
freeze cities or invoke ordinary peace settlement for these wars.

## Failure Handling

- If no exact active rebellion war is found, fall back to the existing AW3
  frozen-occupation pipeline without changing ownership.
- If the original direct transfer throws or fails to commit, keep the native
  failure behavior and do not fabricate a frozen occupation afterward.
- A missing or invalid war asset is treated as a normal war, never as a
  rebellion.
- World unload clears any temporary resolver cache or scoped capture state.

## Testing

Add focused tests proving:

1. rebel attacker capture selects direct transfer;
2. old-regime defender recapture selects direct transfer;
3. every `rebellion=true` asset follows the rule without hard-coded IDs;
4. a normal war between the same kingdoms remains frozen;
5. involvement in an unrelated rebellion cannot authorize direct transfer;
6. the actual capturing kingdom is retained instead of a suzerain recipient;
7. no frozen-control or pending-capture call runs on the direct path;
8. ordinary player and AI peace proposal creation is rejected for an active
   direct-transfer rebellion war;
9. dedicated rebellion completion services remain reachable;
10. source and installed builds compile with zero warnings and errors.

## Acceptance

In each covered internal-war type, capture a city in both directions. The city
must change owner immediately to the actual capturing kingdom, must never
appear as a frozen occupation or selectable peace cession, and the ordinary
war-negotiation window must not open. A simultaneous ordinary war involving
one of the same kingdoms must continue using frozen occupation.
