# Zhulu Vanilla Total War Design

## Goal

Keep AW3 responsible only for Zhulu eligibility, AI target selection, declaration, naming, and presentation. After declaration, a Zhulu war uses WorldBox's native `total_war` lifecycle and native city ownership changes.

## Runtime Boundary

- Register `zhulu_war` with `WarTypeAsset.total_war = true`.
- Do not create or persist an AW3 `zhulu_annexation` war goal after declaration.
- Do not redirect captured cities to an AW3-selected principal.
- Do not freeze capture at 100 percent or route it through AW3 peace settlement.
- Do not block native `War.removeFromWar` or `WarManager.endWar`.
- Do not rebuild or drain the old deferred Zhulu settlement queue.
- AW3 score, exhaustion, goal, negotiation, and decisive-settlement entry points must skip active Zhulu wars.
- Existing Zhulu age, declaration eligibility, target scoring, name, and icon remain.

## Zero-force Fallback

The only post-declaration AW3 intervention is a bounded liveness fallback using the war's own attacker and defender warrior counts.

- Both sides at zero: end the live war with `WarWinner.Peace`; transfer no city.
- Attackers alone at zero: transfer all attacker-principal cities to the defender principal, then end with `WarWinner.Defenders` if the native transfer did not already end the war.
- Defenders alone at zero: transfer all defender-principal cities to the attacker principal, then end with `WarWinner.Attackers` if the native transfer did not already end the war.
- Snapshot cities before transfer. After every transfer, re-check whether the war and both principals still exist. Never call `endWar` twice.

This fallback does not create a peace proposal, war score, war goal, occupation freeze, or deferred extinction settlement.

## AI Gate

AI may select or issue a Zhulu declaration only while the current world age is exactly `age_zhulu`. Player-facing/manual eligibility remains governed by the existing declaration rules.

## Verification

- Pure rules test all zero-force outcomes and the AI age gate.
- Source guards prove the old Zhulu goal, capture redirect, peace guard, `removeFromWar` interception, `endWar` deferral, and restore registrations have no production entry.
- Focused Zhulu tests and the complete rules suite must pass.

