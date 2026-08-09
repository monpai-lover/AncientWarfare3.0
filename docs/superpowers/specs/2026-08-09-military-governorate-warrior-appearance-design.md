# Military Governorate Warrior Appearance Design

## Goal

Military governorate rulers and designated successors use warrior visuals in
the world and every live-actor UI while retaining their real king and heir
identities. Direct military governorates also appear in the suzerain court.

## Scope

- Add a reusable internal visual-role switcher for actor skins.
- Register military governorate ruler and successor rules as its first client.
- Apply the resolved visual role to body texture, special head selection and
  live UI avatars.
- Add direct governorate rulers and successors to the suzerain court military
  section.
- Refresh actor graphics when governorate presentation ownership changes.

The feature does not change actor professions, jobs, AI, army membership,
kingdom ownership, succession authority or saved institutional identity. It
does not add player-facing skin controls.

## Visual Role Switcher

`ActorVisualRoleResolver` exposes the roles `Default`, `Civilian`, `Warrior`,
`Leader` and `King`. Feature providers register during mod initialization.
After initialization, resolution uses an immutable ordered provider array and
does not allocate or lock per actor.

Providers receive an actor and either decline or return a visual role. The
first accepted result wins. `Default` preserves vanilla presentation. Future
systems can reuse the resolver without patching each portrait or map renderer.

The military governorate provider returns `Warrior` when all of the following
hold:

- the actor is live and belongs to a live military governorate kingdom;
- the actor is that kingdom's current king, or its projected designated
  successor ID matches the actor;
- the governorate projection is active.

Resolution reads only actor and kingdom runtime projections. It performs no
SQLite query and no world or kingdom scan.

## Rendering Integration

The resolver is applied at shared presentation boundaries:

1. Actor body texture selection resolves the same warrior skin path vanilla
   uses for that actor's asset, subspecies and mutation skin.
2. Actor head selection suppresses the king head for an overridden ruler and
   uses the warrior special head only when vanilla warrior presentation would
   show it; otherwise it uses the actor's ordinary sex/head identity.
3. `ActorAvatarData` uses the resolved role so live avatars in the court,
   person window and other UI surfaces agree with the map.

The implementation must not patch `Actor.isKing()`, `Actor.isWarrior()` or
change `profession_asset`. Gameplay code continues to observe the real role.

When a governorate is created, ended, restored, changes ruler or changes
successor, affected live actors have their graphics cache invalidated through
the existing actor graphics API. Old and new role holders therefore refresh
without a reload.

## Suzerain Court Projection

`CourtReadModelService` adds nodes for direct vassals whose subject kind is
`MilitaryGovernorate` and whose active snapshot is valid.

- The live subject king is shown as `MilitaryGovernorateGovernor`.
- The live designated successor is shown as
  `MilitaryGovernorateSuccessor` when present.
- Both nodes use military-section ranks and stable ordering by subject kingdom
  and role.
- The seat city and command identity are included in the node labels and
  tooltip data.
- Clicking a node retains the existing actor-window action.

Actors are validated against the subject kingdom rather than the suzerain's
ordinary court-affiliation rule. Dead, destroyed, stale, non-member and
non-direct records are omitted. The read path iterates only the suzerain's
indexed direct vassals and does not scan all kingdoms.

## Failure Handling

- Missing texture data declines the override and preserves vanilla rendering.
- Missing or stale governorate projections do not create court nodes.
- Provider exceptions are isolated so one future provider cannot break actor
  rendering; resolution falls through to the next provider or vanilla.
- Lifecycle invalidation is idempotent and accepts missing or dead actors.

## Verification

Rules tests cover provider priority and default fallback. Source and rule tests
cover:

- ordinary kings and heirs keep vanilla visuals;
- active governorate rulers and successors resolve to `Warrior`;
- ended governorates stop overriding their former holders;
- warrior body and head selection preserve subspecies and mutation behavior;
- live UI avatars consume the same resolved role;
- graphics caches are invalidated on every governorate role transition;
- a suzerain court includes valid direct governor and successor nodes only;
- production and Cultiway scheduler non-regression builds remain clean.
