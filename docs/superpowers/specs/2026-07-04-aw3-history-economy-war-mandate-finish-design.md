# AW3 History Economy War Mandate Finish Design

## Context

AW3 already has the first working versions of city technology spread, policy points,
war claims, cores, restoration war, vassals, generals, fiefs, rebellion risk,
ordinary posthumous titles, and the Mandate of Heaven shell. The remaining work
should finish the missing gameplay loops without replacing those systems.

This design keeps the current architecture: durable SQLite table items, small
services under `Code/core`, existing `HistoryWriter` records, and WorldBox event
patches only at clear game boundaries. It does not add a dedicated general/fief
management UI in this pass.

## Goals

- Add city tax and city economy specialization as a real simulation layer.
- Finish war reason selection, restoration claimant selection, automatic peace
  results, and history records.
- Expand general/fief rebellion behavior through kingdom-state risk instead of
  adding a heavy management UI.
- Add Mandate-only temple names and double-character posthumous titles.
- Move new event text toward localization keys and avoid new hard-coded text
  that can become garbled in source files.

## Non-Goals

- Do not build a dedicated general/fief window or fief map mode now.
- Do not rewrite existing war, policy, vassal, or mandate systems.
- Do not replace the ordinary single-character posthumous title system.
- Do not implement a fully manual EU4-style peace negotiation UI in the first
  pass; add data and automatic settlement first, then leave UI expansion
  straightforward.

## Current State

`CityTechService` already records city-level adoption and spread of national
technology. `KingdomPolicyService` currently grants policy and tech points from
king stats, city count, and unit count, but there is no city tax or economy
specialization state.

The war chain already has `WarClaim`, `KingdomCore`, `WarProject`, `WarGoal`,
`WarDecisionService`, AI fabrication, core/claim map modes, and a target window.
The current target window aggregates mostly by target kingdom and picks best
target cities automatically.

`RoyalClaimService` stores hosted fallen-dynasty claims and can restore a
kingdom as a vassal when a restoration war succeeds. It still needs explicit
claimant and target-city selection.

`GeneralService`, `FiefService`, and `GeneralRebellionService` already appoint
generals, grant fiefs, record merit, and trigger direct rebellion or fief
independence. The risk model is still mostly personal and needs stronger
kingdom-state inputs and more result branches.

`PosthumousTitleService` evaluates ordinary rulers. The code intentionally keeps
temple names such as Tai Zu, Gao Zu, Shi Zu, and Lie Zu for the mandate layer.
`MandateService`, `MandateEvent`, and `MandateDynastyWindow` exist, so the
mandate title layer should attach to them.

## Approach Options

### Recommended: Event-Driven Completion

Finish each missing loop through focused services and history records. Reuse
existing windows and tables where possible. This keeps behavior testable and
limits UI churn.

### Not Recommended: Management UI Expansion

Building full management windows for generals, fiefs, taxes, peace, and
mandate actions would expose more controls but would slow down core behavior
work. AW3 needs believable AI simulation first.

### Future Direction: Grand-Strategy Depth

After the current systems are stable, AW3 can add treaty windows, fief map
modes, subject contracts, and manual peace deals. This design leaves storage
and events compatible with that future.

## City Economy And Tax

Add `CityEconomyState` and `CityEconomyService`. Each kingdom year, every valid
city receives an economy role and contribution snapshot. Non-capital cities
abstractly remit taxes and resources to the kingdom rather than directly
rewriting every WorldBox resource.

City roles:

- Capital/Admin: high political contribution and tax efficiency.
- Agrarian/Granary: food, population support, and stability.
- Market/Trade: wealth and tax contribution.
- Frontier/Military: manpower, border defense, and army contribution.
- Workshop/Craft: technology spread and production contribution.
- Occupied/Unrest: reduced tax and higher rebellion pressure.

Inputs:

- Capital status and distance from capital.
- Population and buildings.
- City technology adoption.
- Border/frontier status.
- Slavery and slave population.
- Occupation resentment and non-core status.
- Existing city leader and fief state.

Outputs:

- Political point contribution.
- Tech point contribution.
- Tax/wealth contribution snapshot.
- Manpower contribution snapshot.
- Food/stability contribution snapshot.
- Economy role used by history tooltip and future UI.

History rules must avoid spam. Record only first role assignment, role change,
major tax remittance, tax collapse, occupation tax failure, and major economy
specialization milestones. Kingdom history only gets high-level economic events.
City history gets detailed local records.

## War Reasons, Restoration, And Peace

Keep the current flow:

`fabrication/project -> normal decision slot -> declaration -> WarGoal -> result`

Extend the target data written into the decision slot:

- Exact goal type.
- Target kingdom id.
- Target city id.
- Source core id.
- Source claim id.
- Restoration claim id.
- Claimant actor id.
- Reason key and localized label.

The war target window becomes a two-level selector:

- First level: target kingdom row.
- Second level: target details, including core cities, claim cities,
  restoration claimants, and valid war buttons.

AI follows the same model: choose a target kingdom, choose a specific goal and
city/claimant, then queue `aw_decision_declare_war`.

Automatic peace settlement should resolve:

- Core reclaim: transfer selected target city if attackers win.
- Claim war: transfer selected claimed city if attackers win.
- Force vassal: set defender as vassal if attackers win.
- Independence: remove suzerain if attacker wins; keep or punish if attacker
  loses.
- Restoration: restore the selected old kingdom and make it the attacker's
  vassal if attackers win.
- No-CB: apply stronger penalty and record the result.
- White peace: record unresolved goal and keep territorial state unchanged.

History records:

- Declaration reason with attacker, defender, target city, and claimant.
- War goal creation.
- Goal enforced or failed.
- Peace result and concrete terms.
- City history for transferred/restored target cities.
- Person biography for restoration claimants and major rulers.

## General, Fief, And Advanced Rebellion

Do not create a dedicated general/fief management window in this pass. Generals
are a small military-aristocracy layer and should surface through biography,
city history, kingdom crisis events, and war results.

Add a kingdom crisis score to rebellion calculations. Inputs:

- Weak king stats, especially diplomacy, stewardship, and warfare.
- Child ruler, very old ruler, missing heir, or unstable succession.
- Recent war defeat, long war, or capital loss.
- Many non-core or occupied cities.
- Low tax state, unrest, or occupation resentment.
- Low mandate, low legitimacy, or failing prestige if mandate exists.
- Disloyal vassals or many subject states.
- Lack of royal guard.

Personal general risk remains:

- Loyalty and ambition.
- Personal troop power share.
- Fief ownership and fief population share.
- Military merit and unrewarded merit.
- Relation to the king's lineage or shi.
- Border fief and neighboring strong kingdoms.

Possible outcomes:

- Palace coup: high military power near the capital and weak ruler.
- Fief independence: strong fief, weak center, non-capital base.
- Direct military rebellion: high personal power without a stable fief.
- Defection to a strong neighbor: border fief and hostile/strong neighbor.
- Restoration support: a claimant exists and old cores are held by a target.

History placement:

- Personal biography records all important general/fief events.
- City history records fief grants, fief revocation, city-based rebellion,
  occupation, and defection.
- Kingdom history records only major rebellion, coup, independence, defection,
  and restoration events.

## Mandate Temple Names And Double Titles

Keep ordinary kingdoms on the existing single-character posthumous path. Add a
Mandate-only title layer that runs when a mandate ruler's reign ends.

Mandate rulers may receive:

- Temple name: Tai Zu, Gao Zu, Shi Zu, Lie Zu, Tai Zong, Shi Zong, Ren Zong,
  Xuan Zong, and similar mandate-only names.
- Double-character posthumous title: examples include Wen Wu, Zhao Lie, Xuan
  De, Xiao Wu, Ming De, or negative pairs for disastrous reigns.

Rules:

- Tai Zu: founder of a new mandate dynasty.
- Gao Zu: low-origin unifier or restorer.
- Shi Zu: refounder after collapse, foreign occupation, or major restoration.
- Lie Zu: conquest-heavy founder.
- Tai Zong/Shi Zong: second or later ruler who consolidates, expands, or reforms.
- Negative title pairs: mandate collapse, civil war, major loss of core,
  rebellion failure, or foreign pseudo-dynasty disaster.

Storage should either extend `PosthumousTitle` with mandate-specific fields or
add a small `MandateRulerTitle` table linked to actor id, reign id, and mandate
period id. The latter is cleaner because ordinary posthumous title rows stay
unchanged.

The mandate window should display history as:

`dynasty period -> ruler reign -> era/year-name segments -> mandate title events`

The mandate history must include title decisions as explicit events.

## Localization And Encoding

New event strings should be represented by locale keys with formatted variables
where practical. Hard-coded Chinese strings in new code should be minimized to
prevent the existing source-encoding garbling problem from spreading.

New or updated locale files should cover:

- City economy roles and tax events.
- War target labels and peace terms.
- Restoration claimant text.
- General rebellion outcomes.
- Mandate temple names, double titles, and mandate title event text.

## Testing Strategy

Add small rule tests where possible instead of relying only on game boot:

- City economy role selection and contribution math.
- War target decision data rules.
- Peace settlement rule selection.
- Restoration claimant selection.
- General rebellion branch selection.
- Mandate temple-name and double-title scoring.

Full build command:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet build
```

Existing F-drive-only validation still applies. Do not require D-drive DLL
deployment verification for this work.

## Implementation Order

1. City economy and tax foundation.
2. War target detail selection and decision data.
3. Automatic peace settlement and richer history.
4. General rebellion advanced branch selection.
5. Mandate-only temple names and double-character titles.
6. Localization, README/Roadmap updates, and build verification.
