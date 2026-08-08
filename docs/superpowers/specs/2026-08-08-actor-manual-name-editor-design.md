# Actor Manual Name Editor Design

## Goal

Replace the vanilla actor name input with a culture-aware two-field editor so player-authored given names and 姓/氏 identities survive runtime projection, save/load, language refresh, and lineage promotion.

## Scope

This change applies only to manual actor renaming from the actor/unit window. It does not change generated names, kingdom names, clan names edited through existing dedicated windows, or historical canonical figures.

The editor has two fields:

- Xia naming: `姓/氏` followed by `名`.
- Non-Xia naming: `名` followed by `姓`.

Changing the given-name field changes only the selected actor. Changing the 姓/氏 field creates or updates a branch rooted at that actor and synchronizes the actor's patrilineal descendants. Ancestors, siblings, collateral branches, and the existing parent branch remain unchanged.

## Current Root Cause

Vanilla `UnitWindow.onNameChange` writes `actor.data.custom_name = true` and `actor.setName(text)`, which persists the visible name but does not update AW3's structured identity slots. AW3's actor name projection can subsequently select `aw_native_name` or `aw_chinese_name` and overwrite `data.name`. The read-restore migration also applies its stored identity to live actor data. Lineage recomposition can overwrite custom names because `LineageService.ApplyDisplayName` does not guard its entry point with the authored-name contract.

## Architecture

### `ActorManualNameRules`

Pure rules for parsing, normalizing, and composing the two-field identity. It receives a naming mode and two input values, returning a validated manual-name request. It never touches Unity objects, world collections, SQLite, or UI.

Parsing uses structured lineage fields first. For legacy actors with missing structured fields, it uses the current display name only as a one-time fallback. The editor never repeatedly splits a generated display string after structured fields have been established.

### `ActorManualRenameService`

The single write boundary for player-authored actor names. It:

1. Validates and normalizes the given name and 姓/氏.
2. Resolves whether the actor uses Xia naming.
3. Writes `custom_name`, `data.name`, `display_name`, `aw_native_name`, and `aw_chinese_name` to the same composed manual display name. Both language slots intentionally contain the player-authored result so language switching cannot restore an older generated value.
4. Writes `aw_naming_given_name`, `LineageKeys.GIVEN_NAME`, and the appropriate family fields.
5. For a surname change, creates a new branch identity when the actor already participates in a traceable lineage, then applies the new identity to the actor and its patrilineal descendants. A commoner without traceable lineage receives the new personal family fields without automatic noble admission.
6. Archives changed live actors and enqueues localized identity persistence through the existing bounded write queue.
7. Refreshes vanilla ruler/founder references through the existing update methods after all identity writes are complete.

The service must be idempotent. Submitting the same two fields twice produces no additional branch or duplicate archive transition.

### `AW_ActorManualNamePatch`

Harmony integration around the vanilla `UnitWindow` name editor. It removes the vanilla single-field listener, reuses the existing `NameInput` visual style for the first field, creates one matching second field, adds localized labels, and binds both fields to the service. The patch must restore and rebind listeners when the selected actor changes and must not create duplicate fields or listeners.

The field order and labels are selected on each window refresh from the actor's naming mode. Existing window dimensions and input scaling are retained; only the single input row is split into two stable fields.

### Authored-name protection

`custom_name` remains the authoritative marker. Every path that projects a live actor name must obey this rule:

- A player-authored actor returns its stored manual display name.
- AW3 must not call `ProjectStored` in a way that replaces a custom actor's manual slots with older database identity.
- `ApplyDisplayName` may normalize structured fields only when its resulting display equals the manual identity; otherwise it exits without changing `data.name`.

Historical canonical figures retain their existing canonical-name behavior and cannot be manually rewritten through this editor.

## Descendant Synchronization

The existing bounded patrilineal traversal is reused for descendant selection. No world-wide actor scan is introduced. Each selected descendant receives the new family/氏 fields and a recomposed display name while preserving its own given name unless the actor is the edited root.

For a traceable noble actor, the branch record is forked from the current branch with the edited actor as founder. The fork is persisted before descendant projection, so a later read-restore or promotion resolves the new branch identity rather than the old parent branch. Existing branch history remains immutable.

If branch creation or a required archive write fails, the service reports failure and does not partially claim the operation as successful. Already queued writes remain bounded and retryable according to the existing persistence queue contract.

## Save/Load Behavior

Manual rename writes the vanilla fields and AW3 identity fields before the save boundary. The save patch therefore exports the latest actor name and lineage archive state without requiring a separate DLL or external file.

During restore, a `custom_name` actor is reconciled from live saved actor data first. Older AW3 database identity is used only to fill missing non-authoritative components; it must never replace a non-empty manual display name. The migration must enqueue the corrected identity so the repair is durable on the next save.

## Error Handling

- Empty given names are rejected.
- Empty 姓/氏 is allowed only when the existing actor has no family identity; it produces a single-name actor.
- Whitespace is trimmed and repeated internal whitespace is normalized using existing name rules.
- Dead or missing actors are ignored by the UI and rejected by the service.
- Duplicate listener/field installation is treated as an idempotent refresh, not as a second rename operation.

## Testing

Add rule tests for:

1. Xia field ordering and display composition.
2. Non-Xia field ordering and display composition.
3. Given-name-only edits leaving family identity unchanged.
4. 姓/氏 edits selecting only the actor's patrilineal descendants.
5. Repeated submission not creating another branch.

Add source/integration tests for:

1. The vanilla single name listener being replaced by the two-field editor.
2. Manual commit writing both localized slots and `custom_name`.
3. Restore preferring the saved manual display over stale database identity.
4. `getName` and lineage projection preserving a manual actor name.
5. The full manual rename -> archive -> restore round trip.

## Acceptance Criteria

- A player can edit 姓/氏 and 名 separately in the actor window.
- The displayed order is correct for Xia and non-Xia naming.
- After save/load, the actor has exactly the manually submitted name, not its previous generated or localized name.
- After promotion, kingdom succession, and language refresh, the manual name remains unchanged.
- A changed 姓/氏 is visible on the actor's patrilineal descendants and does not rename siblings or ancestors.
- No duplicate input fields, listeners, archive rows, or lineage branches are created by reopening the unit window or resubmitting unchanged values.
- Existing unrelated court and lineage changes in the dirty worktree are preserved.
