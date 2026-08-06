# Virtual Title Roster Portrait Layout

## Scope

Adjust the Kingdom virtual noble title holder window to remain readable at a
40 pixel narrower default width while adding the same live actor portrait used
by the court window.

## Design

- Change roster width constraints from 620/600/920 to 580/560/880 pixels.
- Reserve a 40 pixel portrait slot at the left of each live holder row.
- Move the ceremonial title and actor name from x=28 to x=68.
- Keep the existing actor-window navigation on the identity text.
- Instantiate the official `UiUnitAvatarElement` prefab obtained through
  `FamilyTreeNodeView.GetAvatarPrefab()` and bind the row actor with
  `avatar.show(actor)`.
- Keep virtual-title editing controls in a compact right column so the input,
  edit, and delete controls remain inside the narrowed row.
- The roster continues to include only live actors, so no random actor or
  fabricated portrait can appear.

## Failure Handling

If the official avatar prefab cannot be resolved, the row remains usable and
the existing title, name, input, and navigation controls remain visible.

## Verification

The source guard will assert the width values, portrait prefab path, live actor
binding, shifted text positions, and compact control positions. The full rules
test runner and the AncientWarfare3 project build must then pass.
