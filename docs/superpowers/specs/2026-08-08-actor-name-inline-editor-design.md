# Actor Name Inline Editor Design

## Goal

Keep the vanilla full actor name presentation at rest. Expand the same name
control into the existing split family/given editor only while the player is
actively editing.

## Interaction

- Display state uses the original `NameInput` size and position and shows the
  actor's complete projected name.
- Clicking the original name control enters editing state and divides the same
  rectangle into two fields.
- Xia order is `family/clan | given`; non-Xia order is `given | family/clan`.
- Moving focus between the two fields keeps editing state active.
- When both fields lose focus, Enter ends editing, or the window closes, commit
  both fields once and restore display state.
- A validation failure keeps the editor expanded and focuses the required
  given-name field.

## Architecture

`ActorManualNameEditorRules` is a pure two-state transition rule tested by the
rules project. `AW_ActorManualNamePatch` owns Unity layout and focus tracking,
using a small pointer trigger attached to the original input field. The naming
service remains the only authority that commits structured identity fields.

## Safety

The patch removes the cloned vanilla end-edit callbacks so a partial field
cannot be interpreted as a complete actor name. It commits only after leaving
the split editor, preserving field-to-field navigation and the existing custom
name persistence behavior.

## Verification

Run the focused manual-name rules, localized-name persistence tests, actor-name
source guard, and the main project build. Deploy only changed source files and
verify workspace/deployment hashes.
