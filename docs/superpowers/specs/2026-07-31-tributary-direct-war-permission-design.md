# Tributary Direct War Permission Design

## Goal

Treat loose tributaries as independent realms for war-declaration permissions. An
independent realm may declare war directly on a tributary. A tributary relationship
alone does not cause the tributary's suzerain to join that war.

## Scope

- Formal vassals remain protected from direct external declarations; attackers must
  resolve the formal suzerain relationship through the existing rules.
- Formal vassals still cannot independently declare external wars.
- Independence wars and internal-vassal restrictions remain unchanged.
- Loose tributaries may be selected as ordinary war targets and may use ordinary war
  decisions as attackers.
- Existing alliance, truce, active-war, claim, core, and no-CB checks still apply.
- The change does not add automatic military participation for a tributary suzerain.

## Implementation Boundary

War-permission callers must distinguish a formal suzerain from a tributary suzerain.
`VassalService.GetSuzerain` defines formal-vassal status for these checks;
`GetDiplomaticSuzerain` remains available where both formal and tributary relations
are intentionally relevant.

## Verification

Rule tests must prove all four cases:

1. An independent realm may target a tributary with an ordinary war decision.
2. A tributary may use an ordinary war decision against an independent realm.
3. An independent realm still cannot directly target a formal vassal.
4. A formal vassal still cannot independently declare an external war.

SAVE10 provides the regression scenario: kingdom 1 (Provence) must expose a war
target against kingdom 4 (Avalon), whose only subject relation is a tier-3 tributary
relationship with kingdom 11 (Wei).
