# AW3 Policy Node Lock Design

Date: 2026-07-09

## Purpose

Players need a way to manually lock any single AW3 policy node for a specific kingdom. A locked node must not be selected by the player, auto-selected by AI, started from a queue, or continue progressing if it is already active.

This is a per-kingdom control. It is not a global mod setting, and it is not a policy repeal system.

## Scope

The lock applies to all `KingdomPolicyDef` nodes:

- Technology nodes.
- Social policy nodes.
- Repeatable normal decisions.
- Targeted decisions such as war declaration, claim fabrication, and vassal absorption.
- The dedicated core fabrication slot, because `aw_decision_fabricate_core` no longer uses only the normal decision slot.

Completed non-repeatable technology and social policies keep their existing effects. Locking a completed node only prevents future selection paths from treating it as runnable; it does not roll back slavery, title systems, Xiaization, enfeoffment, or any other already-applied effect.

## Data Model

Add a kingdom data key:

- `LineageKeys.POLICY_LOCKED_NODES = "aw_policy_locked_nodes"`

The value is a semicolon-separated set of node ids:

```text
aw_policy_slave_army;aw_decision_claim_mandate;aw_tech_city_defense
```

This follows the existing completed-node storage style and avoids a new table for the first version.

`KingdomPolicySnapshot` should include the raw locked-node string so the archive/debug snapshot can show and preserve the state consistently.

## Service API

Add a small service layer or focused methods in `KingdomPolicyService`:

- `IsNodeLocked(Kingdom kingdom, string nodeId)`
- `SetNodeLocked(Kingdom kingdom, string nodeId, bool locked)`
- `ToggleNodeLocked(Kingdom kingdom, string nodeId)`
- `GetLockedNodesRaw(Kingdom kingdom)`
- `CleanLockedNodeSideEffects(Kingdom kingdom, string nodeId)`

`SetNodeLocked(..., true)` must immediately clean active work for that node:

- If the locked node is the current tech, clear `TECH_CURRENT` and `TECH_PROGRESS`.
- If the locked node is the current social policy, clear `POLICY_CURRENT` and `POLICY_PROGRESS`.
- If the locked node is the current decision, clear `DECISION_CURRENT`, `DECISION_PROGRESS`, and decision target metadata.
- Remove matching entries from `DECISION_QUEUE`.
- If the locked node is `aw_decision_fabricate_core`, clear the dedicated core fabrication current slot and queue.

Unlocking only removes the id from the set. It does not restore canceled progress.

## Rule Integration

The lock check must sit below the existing whole-policy-system enable switch and above normal availability.

`GetStatus()` should return `Locked` for a locked node unless it is completed. UI tooltip will distinguish "locked by player" from "missing requirements".

Every start path must reject locked nodes:

- `StartResearch()`
- `ForceStartResearch()`
- `StartDecisionWithTarget()`
- `StartFabricationDecision()`
- `StartWarDecision()`
- `StartCoreFabrication()`
- `StartNextQueuedDecisionIfEmpty()`
- `StartNextQueuedCoreFabrication()`

`KingdomPolicyAI.PickDecision()` and `PickResearch()` must filter out locked nodes before scoring.

The dedicated core fabrication auto-start in `TryStartCoreFabrication()` must do nothing while `aw_decision_fabricate_core` is locked.

## UI Behavior

In `KingdomPolicyWindow`, each node gets a compact lock toggle in the node corner.

Unlocked state:

- Node behaves normally.
- Lock button tooltip uses the localized meaning "Lock this node: player and AI cannot select it."

Locked state:

- Node body is greyed out and non-startable.
- Lock button uses a clear locked color.
- Tooltip shows:
  - Node description.
  - Cost.
  - Status meaning "Locked by player".
  - Missing requirements, if any.
  - Existing target/progress text only if relevant before cancellation.

Clicking the small lock button toggles the lock and refreshes the window. The main node click remains reserved for research/force switch, so locking does not conflict with node selection.

The decision panel should also show lock state for repeatable decisions. The core fabrication sidebar should show the localized meaning "Core fabrication is locked" and refuse to add new core projects when the core decision is locked.

## Localization

Add Chinese, English, and Traditional Chinese localization keys:

- `aw_policy_node_locked`
- `aw_policy_node_locked_by_player`
- `aw_policy_lock_node`
- `aw_policy_unlock_node`
- `aw_policy_lock_node_desc`
- `aw_policy_unlock_node_desc`
- `aw_policy_locked_count`
- `aw_policy_core_fabrication_locked`

## Testing

Add pure rule tests before implementation:

- A locked tech cannot be started manually.
- A locked social policy cannot be selected by AI.
- A locked repeatable decision is skipped by the decision queue.
- Locking the current tech clears current tech and progress.
- Locking the current decision clears target metadata.
- Locking `aw_decision_fabricate_core` clears the dedicated core fabrication slot and queue.
- Completed non-repeatable nodes are not rolled back by lock state.

The existing `WarFabricationRuleTests` project is a suitable place for the first rule coverage if no narrower policy test project exists.

## Non-Goals

- Do not add global policy bans in this version.
- Do not add a policy repeal or completed-effect rollback system.
- Do not change prerequisite graphs.
- Do not change AI scoring except filtering locked nodes.
- Do not change existing whole-kingdom policy enable/AI-enable behavior.

## Acceptance Criteria

- Player can lock or unlock any visible policy, technology, or decision node from the policy window.
- Locked nodes are visually distinct and have a clear tooltip.
- AI never starts a locked node.
- Manual start and force-switch paths reject locked nodes.
- Existing queued locked decisions do not execute.
- Dedicated core fabrication obeys the same lock.
- Unlocking a node makes it available again once normal requirements are met.
