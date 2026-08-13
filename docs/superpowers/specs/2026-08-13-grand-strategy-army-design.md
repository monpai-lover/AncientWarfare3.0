# Grand Strategy Army Design

Date: 2026-08-13
Status: Approved

## 1. Goal

Add a new mutually exclusive army mode named `Grand Strategy Army`. It replaces ordinary WorldBox field combat with CK3-style strategic movement, persistent round-based field battles, abstract sieges, and numeric casualties. No ordinary soldier actor attacks another actor in this mode.

The mode applies to every kingdom. The player may directly command only armies belonging to the currently selected kingdom; other kingdoms use strategic AI.

## 2. Runtime Modes

The game has three exclusive army runtime modes:

1. Vanilla army behavior.
2. Existing `Army RTS` behavior.
3. New `Grand Strategy Army` behavior.

The settings UI must prevent `Army RTS` and `Grand Strategy Army` from being enabled together. Mode changes take effect on restart or world load. Runtime switching inside a loaded world is out of scope because it would require translating live actor armies into a different authority model.

## 3. Authority Model

`GrandStrategyArmy` is a standalone data entity. It does not inherit from the native `Army` type and does not require a captain or soldier actors.

Authority is split into four layers:

- Kingdom layer: manpower accounts, finances, military technology, unit-type training, equipment capability, war exhaustion, and available generals.
- Army layer: numeric troop composition, position, route, morale, organization, supply, current training, equipment generation, assigned generals, and task.
- Battle layer: participating armies, terrain, frontage, reserves, phases, rounds, rolls, reinforcement timing, losses, and commander events.
- Presentation layer: banners, animated dummy soldiers, arrows, routes, battle buttons, and UI windows. Presentation objects never own simulation state.

Peace has no active numeric field armies. Wars raise temporary levy armies. When their war obligations end, armies disband and surviving manpower returns to the kingdom accounts.

## 4. Manpower And Troops

Manpower is a kingdom-wide resource and is not owned by cities. Cities influence population growth, revenue, technology diffusion, and manpower recovery, but armies are not anchored to cities.

The manpower ledger distinguishes:

- Available manpower.
- Raised manpower.
- Wounded manpower recovering over time.
- Dispersed manpower returning after battle.
- Permanent battle deaths.

Every transfer must conserve the ledger total except for explicit recruitment growth and permanent deaths.

The first release includes infantry, spearmen, archers, cavalry, and engineers. Military technology controls unlocks, quality, recruitment proportions, training ceilings, organization, siege capability, and supply efficiency.

Training uses two layers:

- Kingdom training for each troop type, improved by policy, technology, spending, and peacetime general activity.
- Current army training, inherited on raising, diluted by low-quality reinforcement, and improved by campaign experience.

An army records its equipment generation when raised. Reinforcements may gradually update equipment; an army never upgrades instantly during a battle.

## 5. Generals And Royal Guards

Ordinary WorldBox warrior actors are treated as generals or general candidates, not one-for-one soldiers. An army may contain multiple real general actors and may move and fight without any general.

Army positions include commander, vanguard, left wing, right wing, rear guard, and siege officer. The commander is replaced automatically when incapacitated, captured, or killed. With no eligible general, the army continues with no command bonus.

Assigned general actors follow the numeric army position. They do not plan routes, seek targets, attack, or receive ordinary combat damage. Commander injury, capture, and death come only from abstract battle events.

Royal guards remain a native special force made entirely of real actors and continue protecting the king. They use native combat against assassins, rebels, animals, and other real actors. When a numeric army reaches them, they stop native engagement and contribute a special guard force to the abstract battle.

## 6. Raising And Organization

When a kingdom enters a war, the system chooses initial rally points from its capital, war goals, border distance, safety, and supply conditions. Manpower is converted into troop types according to technology and military organization.

The system creates several temporary armies based on total manpower, supply limits, front count, and available generals. Players may split and merge armies and redistribute troops, supply, and general assignments. Merging requires co-location; split armies begin at the same position.

AI may split and combine armies according to enemy strength, fronts, supply, and siege demand.

There are no standing numeric armies in the first release.

## 7. Army-Level Movement

Each numeric army submits one pathfinding request. The start position comes from the army data, not a captain actor. The route follows real WorldBox terrain and selects the lowest total strategic cost.

Cost includes:

- Terrain speed and passability.
- Roads, forests, mountains, rivers, and crossings.
- Hostile territory and supply risk.
- Embarkation, naval movement, and landing time.
- Supply consumption and route danger.

Sea travel is automatic CK3-style transport. The army changes to a fleet projection after reaching a valid coast, pays embarkation time and resources, travels at sea, then lands. No real WorldBox transport boat is created.

The army interpolates continuously along its route. Its banner, animation projection, and assigned generals follow the same position. Movement locks while engaged, besieging under restricted actions, or performing forced retreat.

A routed army automatically paths to the nearest safe territory and rejects new orders until retreat completes.

## 8. Player Commands

Existing RTS command concepts are reused through an adapter, but commands modify `GrandStrategyArmy` state rather than native armies or captains.

Supported commands include movement, rally, pursuit, siege, follow, merge, split, retreat, and disband when legal.

The player changes a destination by dragging the army banner or route endpoint. During drag, the UI shows a preview route, arrival estimate, supply cost, terrain penalties, and unreachable feedback. Releasing on a legal destination submits a new pathfinding request.

## 9. Field Battles

Enemy armies entering engagement range create a persistent battle instance and stop ordinary movement. Later arrivals register as reinforcements and enter on the next round.

Battle phases are:

1. Engagement: resolve attacker, defender, terrain, crossings, weather, supply, frontage, and initial morale.
2. Main battle: resolve one round per approximately one game month.
3. Rout: begin when morale reaches zero or legal withdrawal is ordered.
4. Pursuit: cavalry and relevant technology convert dispersed troops into deaths or prisoners.
5. Completion: assign war score, retreat routes, manpower accounts, and the battle report.

An even battle should normally last three to twelve rounds.

Each side receives a visible deterministic roll from 0 to 10. The seed uses world seed, war ID, battle ID, and round number, so save loading cannot reroll and multiplayer clients cannot diverge.

Round strength considers:

- Engaged troop composition.
- Military technology and equipment generation.
- Kingdom and army training.
- Morale, organization, and supply.
- Commander and subordinate assignments.
- Terrain, crossings, weather, and frontage.
- Deterministic roll and temporary tactics.

Terrain defines combat frontage. Troops beyond frontage enter reserve and replace front-line losses. Large armies therefore cannot apply their entire strength in narrow terrain.

After the minimum engagement period, the player may order withdrawal. Withdrawal causes pursuit losses and a locked retreat route.

## 10. Casualties And Commander Events

Numeric losses are classified as permanent deaths, wounded, dispersed, and prisoners. Wounded and dispersed manpower return through different recovery schedules. Pursuit converts part of dispersed manpower into permanent deaths. Ordinary prisoners are abstract resources and war-score facts, not actors.

At battle phase boundaries, each assigned general receives a risk check using side losses, rout state, assignment, prowess, age, health, traits, and the deterministic roll. Outcomes are safe, wounded, severely wounded, captured, or killed. These are the only battle events that directly affect real assigned actors.

## 11. Abstract Sieges

Reaching a hostile city does not invoke native capture progression. Enemy field armies must first be defeated or displaced. With no blocking field force, the army creates a persistent siege instance.

Defensive strength uses city level, buildings, population, local and royal guard strength, terrain, defensive policy, military technology, and occupation resistance. Siege power uses engineers, equipment generation, siege-general skill, total manpower, supply, military technology, and deterministic events.

One siege round occurs per approximately one game month. It changes defense, attacker losses, defender losses, and progress.

The player may choose steady siege or assault. Assault advances faster but causes substantially higher troop and general risk.

Enemy relief pauses the siege and creates a field battle. If the besiegers lose, the siege ends. A completed siege calls the existing temporary occupation and war-score services. It does not create a second occupation model and does not immediately transfer legal city ownership.

Existing occupied-city supply, goal control, peace settlement, return, and cession behavior remains authoritative.

## 12. Map Presentation

The primary world projection is a clickable army banner that follows the numeric army position. It reuses the visual style and assets of native `ArmyMetaBanners`, `ArmySelectedMetaBanners`, and army UI where compatible.

The banner displays army name, total strength, morale or organization, commander name, and kingdom color.

Presentation varies with zoom:

- Far: banner only.
- Medium: banner, route, animated directional arrows, and arrival estimate.
- Near: smaller banner, army flag, dummy marching animation, and real assigned generals.

Dummy soldiers are pooled visual objects with no AI, collision, combat, or authoritative save state.

During battle, opposing banners stop on either side of the battlefield and a clickable battle button appears between them. During siege, a clickable siege button and progress appear beside the city.

## 13. Windows And Reports

Clicking an army banner opens the army window and selects the army for commands. The window shows troop composition and quality, morale, organization, supply, training, technology, equipment, commanders, current task, route, and arrival estimate. It provides split, merge, retreat, disband, commander assignment, and assault actions when legal.

The live battle window shows both sides, front line and reserves, rolls and modifiers, troop-type losses, morale and organization, reinforcements, and commander events.

The siege window shows defenses, engineers, supply, progress, round modifiers, losses, and assault controls.

Completed battles and sieges become immutable reports. They remain accessible from war and history windows and retain every round and final result.

## 14. Persistence And Transactions

Persistence covers kingdom manpower and training, active armies, compositions, positions, routes, equipment, commander assignments, battle instances and rounds, reinforcement events, commander events, casualties, and siege instances.

Army, battle, and siege IDs are stable. Round resolution, casualty transfer, occupation, and war-score changes use staged idempotent transactions. Loading during a round must not duplicate losses, occupation, or score.

World load rebuilds indexes and presentation objects, resumes paths, battles, and sieges, and synchronizes assigned generals. If a war ended while state remained, recovery disbands its armies and reconciles manpower exactly once.

## 15. Multiplayer

The host owns all army, movement, battle, siege, and casualty state. Clients submit authorized commands and receive snapshots and committed round results. Deterministic rolls support verification but clients never independently commit simulation outcomes.

## 16. Compatibility Boundaries

Grand strategy mode intercepts ordinary wartime army creation, native soldier combat, native army strategic AI, and native city capture for covered wars.

It does not replace royal guard protection, small real-actor conflicts, animals, disasters, god powers, or unrelated actor damage. Explicit classification must decide whether an event is native, grand-strategy, or royal-guard hybrid before any attack or capture action proceeds.

Existing `Army RTS` behavior must remain unchanged when its mode is selected.

## 17. Delivery Phases

1. Mode selection, data model, IDs, and kingdom manpower ledger.
2. War raising, automatic army organization, army-level pathfinding, sea travel, and world projection.
3. Selection, draggable movement commands, army window, split, and merge.
4. Persistent field battles, frontage, troop types, commander events, retreat, and reports.
5. Abstract sieges integrated with existing temporary occupation.
6. Strategic AI, multiplayer replication, save recovery, performance tuning, and compatibility hardening.

Each phase must be independently testable and must not enable incomplete later phases in production mode.

## 18. Verification Strategy

Pure rule tests cover manpower conservation, raising, army organization, route cost, troop matchups, frontage, deterministic rolls, casualties, retreat, and siege progress.

Transaction tests cover interrupted saves, duplicate rounds, occupation idempotency, war-end reconciliation, and recovery pools.

Integration tests verify native combat suppression, royal guard exceptions, general synchronization, reinforcement, dragging orders, temporary occupation, and mode isolation.

Performance tests verify one route per army, bounded route scheduling, pooled presentation objects, and frame-budgeted battle and siege processing under many simultaneous wars.

Game acceptance covers the complete path: declaration, raising, command, land and sea movement, reinforcement, battle, retreat, siege, occupation, peace, disbanding, saving, and loading.

## 19. Explicit Non-Goals For The First Release

- Permanent standing numeric armies.
- Real soldier actors representing numeric casualties.
- Native transport ships for numeric armies.
- Runtime conversion between army modes in a loaded world.
- A second city occupation model.
- Dragging arbitrary intermediate route nodes. The approved interaction drags the banner or route destination and recalculates the full optimal path.
