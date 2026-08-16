# Peaceful Warrior Citizen Job Design

## Goal

Prevent ordinary warriors from remaining idle and starving after military work ends by assigning them an actor job whose literal ID is `citizen`.

## Runtime Design

- Register `ActorJob` ID `citizen` if it is not already present.
- The job contains the original `make_decision` task so WorldBox decisions continue to select movement, eating, and other valid peaceful tasks.
- Keep the actor's warrior profession, army membership, citizen job field, appearance, and military history unchanged.
- Assign `citizen` only to living ordinary warriors with a valid city when they have no active RTS mission, no return order, no military emergency, no combat state, and no city attack order.
- Never replace jobs owned by royal guards, special armies, temporary levies, wartime garrisons, slave vanguards, transports, or other active military systems.
- War and RTS lifecycle services remain authoritative and may replace `citizen` when military activity begins.

## Integration

- Centralize eligibility and the job ID in standing-army rules/content.
- Make peaceful job refresh assign `citizen` after releasing obsolete military or patrol behavior.
- Use the same refresh path after an RTS return completes so actors do not fall back to an inert state.
- If the job asset is unavailable, fall back to the original next-job selection rather than leaving the AI job null.

## Tests

- A peaceful ordinary warrior selects `citizen`.
- Active mission, active return, military emergency, combat, attack order, or special-army state rejects `citizen`.
- The registered `citizen` job contains `make_decision`.
- Return completion invokes the centralized peaceful refresh path.
- Existing RTS and wartime job ownership tests remain green.
