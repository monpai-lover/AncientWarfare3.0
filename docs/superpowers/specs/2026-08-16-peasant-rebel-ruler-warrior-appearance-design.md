# Peasant Rebel Ruler Warrior Appearance Design

## Goal

Give the ruler and designated heir of every active peasant-rebel realm the
same warrior visual presentation already used by military governorates. This
applies to both the founding-rebel route and the bandit route.

## Scope

- An active peasant-rebel kingdom's current king uses the race's native
  warrior skin and warrior portrait presentation.
- The actor identified by `LineageKeys.KINGDOM_HEIR_ID` in that same kingdom
  uses the same presentation.
- Ordinary rebel residents retain their normal visual roles.
- Leaving the peasant-rebel system restores vanilla presentation.
- The feature changes presentation only. It does not change `isWarrior()`,
  army membership, jobs, equipment, traits, combat eligibility, or persisted
  actor identity.

## Architecture

Add a dedicated `IActorVisualRoleProvider` for peasant-rebel realms and
register it with the existing `ActorVisualRoleResolver`. The provider checks
the live actor, its current kingdom, `MandateRebelService.IsRebelKingdom`, the
king actor ID, and the stored heir actor ID. A matching king or heir returns
`ActorVisualRole.Warrior`; every other case returns
`ActorVisualRole.Default`.

The existing `AW_ActorVisualRolePatch` remains the only texture and portrait
override. Consequently, warrior assets are resolved through the actor's own
race, subspecies, mutation skin, equipment, and advanced texture catalogue in
exactly the same way as military-governorate rulers.

## Invalidation

Existing heir registration already calls `clearGraphicsFully` for outgoing
and incoming heirs, and succession refreshes the new king. Rebel-route entry
and exit must additionally invalidate the current king and designated heir so
cached sprites cannot survive a government transition.

Invalidation is best-effort and must not interrupt route mutation or actor
disposal. Provider failures remain isolated by `ActorVisualRoleResolver`.

## Verification

- Pure rules cover active/inactive rebel state, king, heir, ordinary member,
  dead actor, and cross-kingdom stale heir IDs.
- A source guard requires provider registration, use of the rebel marker and
  stored heir ID, warrior-role output, and route-transition invalidation.
- The full detached rules suite, affected guards, and net48 production build
  must pass before deployment and release packaging.
