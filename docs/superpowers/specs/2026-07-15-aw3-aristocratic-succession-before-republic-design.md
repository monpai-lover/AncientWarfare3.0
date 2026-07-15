# AW3 Aristocratic Succession Before Republic Design

## Goal

Prevent wars from turning monarchies into republics merely because the current
royal agnatic line has no eligible male. A surviving domestic noble house must
take the throne and found a new dynasty before republican election is allowed.

## Confirmed Root Cause

The runtime archive records two direct republic transitions during long wars.
At world time `3142.5598`, Xu had no eligible male in the old Bie royal line,
but still had several living houses: Yun had 11 members and 7 males, Yan had 8
members and 5 males, and Kong had 4 members and 3 males. The current vacancy
service skipped the house layer, wrote `ClassRepublic`, and then elected Kong
Qiu by personal attributes. Yu had already followed the same path at world time
`2761.7598`, after which every later ruler remained an elected republican
leader.

War is an amplifier rather than the state-transition cause: repeated combat
deaths exhaust royal candidates and reach this incorrect fallback more often.

## Succession Order

Managed succession uses this strict order:

1. The registered direct or collateral agnatic heir selected by `HeirService`.
2. The strongest eligible noble house currently established in the kingdom.
3. A republican election only when no eligible noble house has an adult living
   male and the existing republican entry requirements are otherwise met.

A temporary `timer_new_king` vacancy still defers all fallback decisions.
Existing republics continue their elective succession and are not converted
back to monarchy by this feature.

## Noble House Selection

A candidate must belong to a live visible `Clan`, participate in the AW lineage
system, be a living adult male of the kingdom, and be neither enslaved nor an
existing king.

Houses are ranked deterministically by:

1. vanilla clan renown, descending;
2. active office holders in this kingdom, descending;
3. living members in this kingdom, descending;
4. eligible adult males in this kingdom, descending;
5. the best eligible ruler score, descending;
6. clan ID, ascending.

The eligible clan chief is selected first. If the chief is absent, foreign,
underage, female, dead, enslaved, or already a king, the house selects its best
eligible male by the existing governing score and deterministic tie breakers.

## Integration

`AristocraticSuccessionService` performs one bounded scan of the vacant
kingdom's units, groups live candidates by their existing clan, and returns the
winning ruler. It does not create a clan for a commoner during selection.

The vacancy path marks `SuccessionMode.CLAN_FALLBACK` and returns the selected
ruler without changing `POLICY_CLASS_STATE`. Vanilla `setKing()` then installs
the ruler and royal clan, while the existing promotion, lineage, dynasty,
chronicle, guard-transfer, and capital-recall hooks complete an ordinary new
dynasty accession.

If no house candidate exists, the existing republic ranking runs unchanged.
No old-save migration or scan is added.

## Verification

Pure rule tests cover house precedence over republic, vacancy deferral,
eligibility, house ranking, chief preference, and deterministic ties. Source
guards require the live vacancy service to consult the aristocratic layer before
calling `SetRepublic`. Full verification includes rule tests, source guards,
Debug and Release builds, deployment with `.runtime` preserved, and an in-game
war/succession smoke test.
