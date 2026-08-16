# RTS Post-Return Taxi Release Design

## Problem

An RTS warrior can complete its return, receive the literal `citizen`
actor job, and still remain immobile at the coast. Runtime evidence shows
that the selected actor receives `check_warrior_transport` immediately
after return. The original behaviour creates a `TaxiRequest` whenever the
warrior's city still has a cross-island attack-zone order.

The stale city order survives the AW3 RTS mission lifecycle. Because the
actor keeps the Warrior profession, `make_decision` continues to evaluate
the original Warrior decision set even while the actor job is `citizen`.
The taxi request then owns the embarkation target and prevents ordinary
peace movement from progressing.

Return release also processes the captain twice when the captain is already
present in `army.units`, causing a second job and task reset.

## Design

### Deduplicate return release

`ArmyRtsControllerService.ReleaseAfterReturnActors` will release each actor
ID once. It will process the unit collection first and process the captain
only when the captain ID was not already observed.

### Validate and clear only stale attack orders

Before assigning the peaceful `citizen` job, the return-completion path will
inspect the actor's city attack target using the same validity conditions as
the original `CityBehCheckAttackZone` cleanup:

- the target city must exist and be alive;
- the source city must still have warriors;
- the target kingdom must still be an enemy of the source kingdom;
- the target city must remain reachable from the source city.

If any condition is false, both `target_attack_city` and
`target_attack_zone` are cleared. A still-valid hostile attack order is
preserved so active cross-island war and landing behaviour remain intact.

### Release stale taxi ownership

When the actor is entering the confirmed post-return peaceful path, any
existing original `TaxiRequest` for that actor is cancelled before citizen
decision selection. This prevents a request created by the completed
mission from continuing to own the actor after the city order is removed.

The cleanup is limited to ordinary, valid Warriors accepted by the existing
confirmed-return citizen eligibility rule. Special armies, royal guards,
synthetic levies, wartime garrisons, and actors still owned by a live RTS
mission keep their existing behaviour.

## Verification

- Rule tests cover actor-ID deduplication.
- Rule tests cover stale versus valid attack-order cleanup eligibility.
- Source-contract tests require taxi cancellation before citizen decision
  selection.
- The focused peacetime citizen test suite and project rebuild must pass.
- Runtime verification must show one `post_return_citizen` entry per actor,
  no surviving `check_warrior_transport` task for a non-hostile stale target,
  and normal movement after return.
