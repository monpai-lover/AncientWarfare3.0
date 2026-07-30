# Wartime Army Command Lifecycle Design

## Goal

Ensure every valid field army belonging to either side of an active war receives an RTS military order, including armies created or expanded after the war starts. Army map information must expose armies that are still assembling instead of hiding them because a mission has not yet been published.

## Confirmed Root Cause

`ArmyManager.newArmy` registers and schedules a new army before its roster is complete. At that point the army commonly contains only its captain, so the war director excludes it for being below the minimum operational force. Later `Actor.setArmy` calls refresh the strategic index, but roster changes do not schedule a new director generation. The army can therefore retain a native flag while never receiving an RTS mission.

War start registration already enumerates both attackers and defenders. The missing-order symptom affects either side when its armies are created or become operational after that initial planning pass.

## Command Lifecycle

1. War start and participant changes register every attacker and defender and schedule each participant kingdom.
2. Army creation, roster changes, and kingdom ownership changes refresh the strategic index and enqueue a coalesced director refresh for the affected kingdom.
3. The refresh is deferred until the current army mutation stack has completed, so the director captures the assembled roster rather than the captain-only intermediate state.
4. Multiple soldiers joining the same kingdom in one simulation slice produce one director refresh rather than one planning pass per soldier.
5. The director continues to exclude invalid, destroyed, captainless, royal-guard-only, and dedicated-garrison armies from field missions. A depleted army remains visible as assembling or replenishing until it becomes operational.
6. A wartime field army that is below its required strength requests forced reinforcement instead of remaining indefinitely in the replenishment state.
7. Every operational field army in an active war is allocated to one of that kingdom's active wars. If no attack objective is currently open, it receives a reserve or defensive order anchored to a valid friendly city instead of being omitted.
8. A periodic reconciliation remains as recovery for missed lifecycle notifications: a missionless operational wartime army causes a bounded kingdom replan.

## Forced Reinforcement

Forced reinforcement operates on real actors rather than changing an army count:

1. Select eligible adult actors from cities owned and currently controlled by the army's kingdom, starting with the anchor city and then nearby cities.
2. Preserve the existing population floor in every donor city. Enemy-occupied cities cannot provide recruits, resources, or soldiers.
3. Convert the selected actors to the appropriate wartime military role, attach them to the target `Army`, and ensure both `Actor.army` and `Army.units` agree.
4. Teleport the completed reinforcement batch to the army captain or its valid rally tile so the army does not wait for individual recruits to cross the map.
5. Complete the batch in one deferred reinforcement operation. Do not wait for vanilla one-at-a-time warrior creation.
6. Once the target has reached operational strength, clear the replenishment gate and enqueue a new war-director generation immediately.
7. Apply the same process to attackers and defenders. If the kingdom has genuinely exhausted all recruitable population above the protected floor, keep the army in an explicit manpower-shortage state instead of silently recreating empty armies.

## Presentation

Army flag information must not require both an RTS projection and mission merely to be visible.

- With a valid mission, show the existing army name, strength, commander, and localized operation.
- While the director refresh is pending or the army is below operational strength, show the same basic identity data with a localized assembling/replenishing status.
- Invalid or destroyed armies remain hidden.

The fallback is diagnostic protection only. It does not replace command assignment.

## Performance Boundaries

- Coalesce roster notifications by kingdom.
- Coalesce forced reinforcement by army so repeated director observations cannot create duplicate soldiers or duplicate teleports.
- Do not scan every actor or every army per render frame.
- Reuse `ArmyStrategicIndexService` cursors and the existing bounded war-director work queue.
- Run reconciliation on simulation/director cycles, never `MapBox.Update` presentation frames.

## Failure Handling

- If an army is mutated again before planning completes, invalidate the stale generation and retain only the latest coalesced refresh.
- If the army changes kingdom, refresh both the previous and current kingdom where available so stale missions are removed and the new owner receives a plan.
- If no valid friendly anchor exists, keep the army visible as awaiting orders and retry on the next bounded director cycle.

## Tests

Automated regression coverage will prove:

1. War participant enumeration includes both attackers and defenders.
2. Registering a new wartime army schedules a deferred kingdom refresh.
3. Expanding a captain-only army to operational strength schedules another refresh.
4. Repeated roster changes coalesce to one kingdom refresh.
5. Forced reinforcement selects real eligible actors without crossing the donor-city population floor.
6. Reinforced actors are assigned to the intended army and teleported to its captain or rally tile.
7. Repeated reinforcement requests cannot attach or teleport the same actor twice.
8. An operational army with no open attack target receives a reserve or defense mission when a friendly anchor exists.
9. A missionless but valid army still renders basic map information with an awaiting-order status.
10. Existing RTS lifecycle and performance rule suites remain green.

## Out Of Scope

This change does not redesign RTS routing, combat tactics, recruitment targets, or the global RTS scheduler switch. Actor sprite exceptions and Actor benchmark sampling are separate performance fixes.
