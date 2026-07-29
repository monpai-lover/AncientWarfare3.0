# Ruler Household And Consort Diplomacy Design

## Goal

Add a durable ruler-household system to AW3. The court window gains a harem entry, the player can inspect the current ruler's principal wife and consorts, and diplomacy can offer a young noblewoman directly to a foreign ruler as either principal wife or consort. This direct household offering remains distinct from royal-house marriage between non-ruling members of two royal lineages.

## Reference And Selected Architecture

EmpireCraft keeps the vanilla mutual `lover` relation for a principal spouse and stores additional concubines in mod-owned clan data. AW3 adopts the separation but not EmpireCraft's one-sided `lover` assignment. A principal wife remains a real mutual WorldBox spouse; every consort is stored in a new AW3 SQLite relationship table and must not overwrite either actor's vanilla spouse link.

This is required because AW3's remarriage, ten-month pregnancy, biography, family tree, and inheritance systems assume that `lover` is unique and mutual. Consort pregnancy therefore resolves the father through the AW3 relationship table and feeds the real mother and father to the existing birth and lineage pipeline.

## Relationship Model

Each durable household relationship records:

- relationship ID;
- ruler actor ID;
- partner actor ID;
- source and recipient kingdom IDs;
- relationship kind (`principal_wife` or `consort`);
- rank code;
- start year and world time;
- end time and status;
- source diplomacy proposal ID.

Only one active principal-wife record may exist for a ruler. Active consort capacity is bounded by the recipient realm's title tier:

- Mandate or empire: 8 consorts;
- kingdom: 4 consorts;
- lower title tiers: 2 consorts.

The principal wife does not consume consort capacity. A consort may appear in only one active ruler household. Runtime actor keys may cache the active ruler ID for pregnancy routing, but SQLite is authoritative and load repair must rebuild a missing cache rather than inventing a relationship.

## Titles And Display

Display titles derive from the current ruler's realm tier:

- Mandate or empire: `皇后` for the principal wife and `嫔` for a consort;
- kingdom: `王后` and `侧妃`;
- lower title tiers: `正妻` and `侧室`.

The table reserves a rank code so later work can add promotion and demotion without replacing the schema. This version does not add palace factions, rivalry, favor, or rank progression.

## Court And Harem UI

The court summary toolbar gains a `后宫` button beside the existing examination and kingdom navigation commands. It opens a resizable harem window using the same default and minimum dimensions and the same visual language as the court window.

The harem window shows:

- the current ruler's portrait, personal name, ceremonial title, and realm;
- the principal wife in a dedicated section;
- a scrollable consort list;
- for every partner: portrait, personal name, household title, age, origin realm, clan or lineage, entry year, living-child count, and current status;
- `返回官场` and `返回国家` commands.

Clicking a living portrait opens the actor inspection view. Monarchies with no principal wife or consorts show an explicit empty state. Republics do not expose the harem entry.

## Royal-House Marriage

`宗室婚盟` remains a bilateral marriage between members of two royal houses. Both reigning rulers are removed from the candidate pools, including cases where the ruler would otherwise classify as direct royal kin. Direct children, heirs, and collateral royal kin remain eligible subject to the existing adult, breeding-age, unmarried, realm, and close-kin checks.

The accepted pair becomes a real mutual WorldBox spouse pair. This action retains the existing royal-marriage history and diplomatic relation modifier. If either realm has no eligible non-ruling royal candidate, the action reports a localized unavailable reason.

## Direct Household Offering

Diplomacy gains a separate `提供后妃` action. Its selection window fixes the foreign ruler on the right and lists eligible young noblewomen from the requester's realm on the left. A segmented control selects `正妻` or `侧室`.

An eligible offered woman must be alive, adult, within breeding age, female, resident in the requester realm, of recognized noble or royal lineage, free of slavery and incompatible protected roles, unrelated to the recipient ruler, and not already married or assigned to a ruler household.

Additional rules are:

- `正妻` is unavailable when the recipient ruler already has a living mutual spouse or an active principal-wife record.
- `侧室` is unavailable when the recipient has reached the tier capacity.
- Acceptance moves the woman to the recipient capital while preserving her birth lineage and origin-realm record.
- Rejection does not move or mutate the candidate.
- Providing a principal wife or consort is not a royal-house marriage and never creates an alliance or the royal-marriage modifier.
- A principal-wife offering grants a moderate relation improvement; a consort offering grants a smaller improvement.
- AI may offer a household partner only when a valid vacancy exists, the candidate remains eligible, the expected acceptance passes, and no equivalent pending proposal exists.

All player and AI commits pass through the AW3 command and authority path so a multiplayer client cannot mutate a household locally.

## Pregnancy And Children

The principal wife continues through the existing mutual-spouse pregnancy path. An active consort is eligible for real ten-month pregnancy with her recorded ruler while both actors are alive, of breeding age, in a valid monarchy, and in the same recipient realm. The runtime must process a bounded number of household pregnancy candidates per kingdom cycle and must not scan all world actors or all historical household rows.

Delivery calls the existing birth pipeline with the actual mother and ruler as parents. The child receives normal parent IDs, lineage archiving, naming, biography, noble continuity, and living-son index updates. The relationship kind at conception is recorded as the child's birth legitimacy:

- child of the principal wife: legitimate (`嫡`);
- child of a consort: concubine-born (`庶`).

Under primogeniture, legitimate sons rank ahead of concubine-born sons. A concubine-born child remains a royal-lineage member and a valid fallback or faction-backed succession candidate. Military acclamation and civil-official enthronement may still select a concubine-born claimant when their own rules prevail.

## Lifecycle And Repair

A household relationship closes when the partner dies, the ruler dies, the ruler permanently leaves the throne, either actor becomes invalid, or the principal mutual spouse link is replaced. A former ruler's consorts are not transferred to the next ruler. Living widows remain in their current city unless another existing system lawfully moves them.

Annual bounded maintenance closes stale rows, repairs actor cache keys from active database rows, and archives both actors before history is written. Save and load use the existing reflected table creation and incremental column migration. Active-row indexes cover ruler, partner, recipient realm, and source proposal lookups.

## History And Localization

Accepted offerings write person biographies for the ruler and offered woman and kingdom history for both realms. Death or relationship closure writes no duplicate marriage event. UI labels, failure reasons, proposal text, AI messages, history templates, household titles, and empty states are provided in Simplified Chinese and English using AW3 localization keys.

## Testing

Pure rule tests cover:

- tier capacities `8 / 4 / 2`;
- title selection by realm tier;
- exclusion of both reigning rulers from royal-marriage candidates;
- principal-wife and consort candidate eligibility;
- spouse vacancy and consort capacity gates;
- principal-wife versus consort diplomatic effects;
- legitimate children ranking before concubine-born children while both remain eligible;
- lifecycle closure and cache-repair decisions;
- bounded pregnancy scheduling.

Persistence and source-guard tests cover schema fields and indexes, authority-routed commands, proposal serialization, no one-sided `lover` assignment for consorts, use of real parents at delivery, court and diplomacy window registration, navigation buttons, and complete localization. The final regression run includes the full rules suite and the AW3 build.

## Out Of Scope

This version does not add palace factions, favor, jealousy, rank promotion, forced divorce, repudiation, regency by empresses, dowager politics, consort-specific traits, or a general multi-spouse system for ordinary actors.
