# Succession Split Opinion And War-Goal Design

## Goal

Make the two live realms created by one succession dispute recognize each
other as irreconcilable rival courts. The original diplomacy opinion panel
must show a visible `-100` succession-split modifier, and AI realms should
prefer reunification annexation while the existing free claim remains valid.

## Scope

- The modifier starts when the rival kingdom has materialized and both courts
  still own at least one live city.
- It applies during the initial succession war and during a permanent split.
- It remains `-100` for as long as the same materialized split exists.
- It disappears when the dispute closes, reunification completes, one court is
  destroyed, or the pair is not the original/rival pair of the same dispute.
- The existing three-generation limit for the free `reunify_succession`
  casus belli remains unchanged.
- After that limit expires, the `-100` opinion remains, but ordinary claim and
  core rules govern later wars.

## Architecture

### Pair Rule

Add a pure succession-dispute rule that decides whether two distinct kingdom
IDs are the original and rival courts of one materialized dispute. It receives
the dispute status, original/rival IDs, and both live-city counts. A companion
rule returns `-100` for an opposed pair and `0` otherwise.

The runtime service exposes a read-only pair query backed by the existing hot
succession-dispute projection. It does not create a second persistent relation
record and performs no world mutation.

### Visible Opinion Modifier

Register one WorldBox `OpinionAsset` with the other AW3 opinion assets. Its
callback reads the succession pair query and returns the pure rule's value.
The same localized label is used for the negative result so the original
opinion breakdown displays `宗统分裂 -100`.

Using the native opinion asset keeps the vanilla relationship panel, AW3
diplomacy summaries, and AI opinion reads on the same value. It also makes the
modifier disappear immediately when the authoritative dispute projection is
closed, without a duplicated modifier row or cleanup race.

### Bounded AI Preference

Extend the war-goal context with an `OpposedSuccessionBranches` fact. When it
is true, `reunify_succession` receives a bounded strategic bonus. The bonus is
large enough to beat nearby vassal, tributary, and ordinary-claim scores, but
it is not an eligibility override or an absolute priority. A materially more
valuable legal goal may still win.

The existing reunification eligibility check remains authoritative. Therefore
the bonus cannot create a free claim after the three-generation boundary and
cannot bypass an active war, truce, invalid kingdom, or other declaration
gate. Successful reunification continues to use the existing whole-realm
settlement path rather than city-by-city transfer.

## Localization

Add `opinion_aw_succession_split` to every locale column used by the opinion
asset. Simplified Chinese is `宗统分裂`; English is `Dynastic split`; Traditional
Chinese is `宗統分裂`.

## Failure And Performance Behavior

- Missing, stale, or non-materialized dispute data returns `0`, never `-100`.
- The opinion callback uses the existing in-memory dispute projection and does
  not write SQLite data.
- No per-frame scan, kingdom-wide actor scan, or new annual loop is added.
- The AI bonus is evaluated only for an already constructed target option.

## Tests

Add focused tests proving:

1. Original and rival courts receive `-100` in active and permanent states.
2. Same-court, unrelated, closed, zero-city, and missing-rival cases return `0`.
3. A split context makes reunification win against nearby indirect-rule goals.
4. The preference remains bounded, so a materially stronger legal goal can win.
5. The native opinion asset and localization key are registered.
6. Existing three-generation reunification-claim tests remain unchanged and
   pass.

## Non-Goals

- No permanent `DiplomaticRelationModifier` database row.
- No extension of the three-generation free-claim period.
- No hard-coded forced-war target or bypass of declaration gates.
- No changes to succession city allocation, war scoring, or peace settlement.
