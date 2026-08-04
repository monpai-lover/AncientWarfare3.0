# Army Membership Ownership Root Fix Design

## Goal

Eliminate persistent foreign ordinary soldiers from a kingdom's army while
preserving legitimate whole-army transfers. Native and Large scheduling modes
must converge on the same roster.

## Root Cause

Standing-army maintenance merges members from multiple home cities. When one
city changes kingdom, vanilla updates resident `Actor.kingdom` values but does
not remove their `Actor.army` backlinks. If the anchor city changes kingdom,
the inverse mismatch affects members from the other cities. Native and Large
dirty rebuilds both trust `actor.army`, so they reinsert the stale mixed roster.

## Design

### Stable Ownership Rule

The authoritative owner is `AWArmyService.GetIntendedKingdom()`. In stable
runtime state, an ordinary member remains only when its kingdom equals the
intended owner. Unknown owner, world loading, and replica application defer the
decision rather than destructively clearing membership.

### Deferred Reconciliation

`Actor.setKingdom` and `City.setKingdom` callbacks enqueue affected army IDs in
a deduplicated reconciliation queue. They do not mutate rosters inside the
Harmony callback. The main-thread authority cycle scans queued armies in
bounded batches after the current transfer stack has completed.

Foreign ordinary members leave both sides of the relationship, release RTS
deployment and temporary military state, and return to a valid non-army job.
Foreign captains use the existing captain disposal scope. Roster and strategic
indexes are invalidated after reconciliation.

### Legitimate Whole-Army Transfer

Bulk ownership changes, including feudatory Jingnan reassignment, run inside an
explicit `ArmyOwnershipTransferScope`. Checks are deferred while the scope is
open. Closing the outermost scope enqueues the army for final reconciliation:
members that now match the new owner remain, and any residual mismatch leaves.
No permanent exception exists for captives or feudatory troops.

### Prevention And Rebuild Defense

`AWArmyService.AddToArmy` rejects a new stable-state cross-kingdom membership.
The Native and Large dirty rebuild integration both enqueue rebuilt army IDs to
the same reconciliation service. Large-mode classification remains read-only;
all mutation happens later on the main thread.

## Failure Handling

- Unknown owner or active load/replica state requeues without mutation.
- Queue insertion is idempotent.
- Roster changes during a batch restart or safely continue the cursor.
- Empty armies are handed to existing army cleanup.
- Cleanup releases actor and army backlinks together so rebuild cannot restore
  a one-sided stale membership.

## Tests

- Same-kingdom members remain.
- Capturing a donor city removes its transferred residents from the old army.
- Capturing an anchor city removes old-kingdom members from the transferred
  army.
- Unknown owner and load/replica state defer.
- Transfer scope preserves a fully transferred army and removes residual
  mismatches after close.
- Captain and ordinary-member cleanup use their appropriate paths.
- Duplicate events enqueue once; batching does not skip changed rosters.
- Native and Large rebuilds call the same reconciliation service.
- Save/reload does not restore foreign membership.

## Non-Goals

- Supporting permanent foreign mercenaries.
- Mutating actors from Large-mode worker classification.
- Replacing vanilla kingdom transfer semantics.
