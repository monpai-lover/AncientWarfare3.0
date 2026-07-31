# AI Diplomacy Proposal Chain Design

## Goal

Complete the ordinary AI proposal chain for joining wars, diplomatic
vassalization, peaceful vassal release, and unilateral alliance withdrawal.
Also support tributary internalization and suzerain-to-subject household
offers through the same proposal infrastructure.

All accepted actions must produce real world state, history, conversation,
multiplayer replication, and strategy revision updates. AI must not bypass the
proposal pipeline by directly changing vassal state.

## Root Cause

The player diplomacy menu already exposes `JoinWar`, `Vassalize`,
`EndVassal`, and `EndAlliance`, and the execution switch contains handlers for
all four. The ordinary AI candidate builders do not create these candidates.
`EndAlliance` is mapped to the asynchronous proposal enum but has no producer;
the other three types are absent from that enum. The current weak-realm
protection path in `VassalAIService.TryActiveVassal` starts a policy decision
that writes vassal state outside the proposal lifecycle.

Consequently these actions cannot pass through the normal capture, ranking,
delivery, acceptance, persistence, replay, and multiplayer paths.

## Scope

The implementation covers six proposal opportunities:

1. An ally asks another ally to join a specific active war.
2. A stronger independent realm demands diplomatic submission.
3. A threatened weaker realm asks a stronger realm for protection.
4. A tributary asks its current imperial tributary suzerain to internalize it.
5. Either side of a direct vassal relation proposes peaceful release.
6. An ally unilaterally leaves a harmful alliance and creates a five-year
   truce.

It also makes upper-to-lower `HouseholdOffering` an explicit ordinary AI
opportunity. The existing player-facing action types remain; no duplicate
proposal type or second diplomacy subsystem is introduced.

## Shared Opportunity Rules

Add a UI- and Unity-independent facts model and evaluator alongside the
existing diplomacy AI rules. It receives already captured facts and returns
an eligible direction, score inputs, stable war identity, and detail identity.
Both synchronous and read-only asynchronous candidate builders consume this
same result. Live object access, database reads, and mutations remain outside
the pure evaluator.

The evaluator must not assign a fixed priority that suppresses other
diplomacy. Each candidate enters the existing score ranking. Urgent military
help, immediate protection, costly subject maintenance, and harmful alliances
receive data-derived score adjustments.

Candidate generation remains bounded to the ordinary diplomacy contact plus a
bounded scan of that requester's active wars. The protection selector retains
the existing infrequent `VassalAIService` threat and protector scan rather than
adding an all-realm scan to every ordinary diplomacy cycle.

## Stable Candidate Identity

`PreparedAiProposal` and `AsyncDiplomacyCommitCandidate` gain `WarId`.
`AsyncDiplomacySelectionIdentity` also includes `WarId`, so the async fact
fingerprint rejects a plan if the selected war changes between capture and
commit.

Direction is stored in `DiplomacyProposalSelection.DetailId` using stable
values:

- `vassalize_demand`
- `vassalize_seek`
- `vassalize_internalize`
- `end_vassal_release`
- `end_vassal_request`

`AsyncDiplomacyProposalKind` gains `JoinWar`, `Vassalize`, and `EndVassal`.
`EndAlliance` keeps its existing value. Existing enum numeric values must not
be renumbered because async diagnostics and multiplayer payloads may contain
them.

Synchronous creation, asynchronous commit, and proposal replay must use the
prepared `WarId` and `DetailId`; they must not rediscover an arbitrary war or
infer a different direction after selection.

## Join War

The requester and responder must be members of the same alliance. The
requester must participate in the selected active war, while the responder
must be absent from both sides. Subjects, opposing subject trees, existing
enemy-side participation, and other participant conflicts make the candidate
unavailable.

When several wars are joinable, choose one deterministically using capital
danger, losing position, enemy power, and stable war ID tie-breaking. The
responder acceptance score continues to include opinion, alliance, shared
enemy, diplomacy, and relative power. A bilateral request is generated only
when expected acceptance passes.

Execution joins the responder to the requester's exact side under
`WarParticipantEntrySourceKind.AllianceCall`. If the war ended or either side
changed before execution, the proposal is cancelled with a specific reason.

## Diplomatic Vassalization

### Demand Submission

`vassalize_demand` is available only between independent, non-allied realms at
peace. `VassalService.CanSetVassal` must pass and requester power must be at
least approximately twice responder power before acceptance scoring. The
existing title, adjacency, cycle, rebel, and subject checks remain
authoritative.

On acceptance, the responder becomes an `Outer` vassal of the requester.

### Seek Protection

`vassalize_seek` preserves the current threat logic: the third-party threat is
at least 1.6 times requester power, the proposed protector is at least 1.9
times requester power, is adjacent, and has opinion of at least -25.

It may also be generated during an active defensive war when the requester is
losing badly. The candidate stores that defensive war ID. The proposed
protector cannot already be on the enemy side or have a subject-tree conflict.

Acceptance includes the enemy coalition's total war power. A protector facing
an enemy 1.2 to 1.6 times its own system war power receives a strong penalty.
Above roughly 1.6 times, rejection is normal unless excellent relations, a
clear shared enemy, and a strongly war-oriented court overcome the risk.

On acceptance, the requester becomes the responder's `Outer` vassal and the
new suzerain joins the requester's defensive side. Execution revalidates both
operations. If vassal creation succeeds but war entry fails, it compensates by
ending only the newly created relation with a dedicated failure reason.

`VassalAIService.TryActiveVassal` calls this proposal entry instead of starting
`aw_decision_seek_suzerain`. Rejection uses the existing proposal cooldown and
does not alter relations or war participation.

### Tributary Internalization

`vassalize_internalize` is available only when the requester is a tributary of
the responder and the responder is imperial tier. It cannot target another
empire.

Acceptance converts the current relation as follows:

- current Mandate empire: `VassalContractTierRules.Inner`
- other imperial realm: `VassalContractTierRules.Outer`

Add a dedicated transactional conversion in `VassalService`. It closes the
active tributary row and inserts the replacement vassal row in one database
transaction, then updates counters, projections, map state, history, and
strategy revisions after commit. At no point may two active relations or no
active relation be persisted because of a partial database write.

## Peaceful End Vassalage

`end_vassal_release` is sent by the suzerain to a direct vassal when autonomy,
soft-cap pressure, maintenance burden, and low strategic value favor release.

`end_vassal_request` is sent by a direct vassal to its suzerain when autonomy,
relation age, relative power, and peaceful court preference favor negotiated
independence. It is unavailable during an independence war.

Both are bilateral proposals with direction-specific acceptance. Rejection
only creates the normal diplomacy rejection cooldown; it does not start a war.
Execution resolves the subject from the persisted direction and verifies that
the same direct relation still exists.

## Unilateral End Alliance and Truce

`EndAlliance` becomes unilateral in `DiplomacyProposalRules.IsUnilateral`.
Ordinary AI creates it when opinion has seriously deteriorated or the alliance
has become a sustained strategic liability. It is ranked from the actual
liability and is not gated by responder acceptance.

Execution makes the requester leave and registers a bilateral truce for
`DiplomacyProposalRules.BrokenPactTruceYears`, currently five years. The truce
uses the existing treaty persistence and war-declaration blocker.

The operation is retry-safe. Recovery considers it complete only when the
requester is outside the alliance and the five-year truce exists. If alliance
departure succeeded but the truce write did not, replay must finish the truce
instead of accepting a half-applied proposal. Truce registration is
idempotently keyed to the withdrawal proposal.

## Upper-to-Lower Household Offering

A direct vassal or tributary suzerain may proactively offer an eligible woman
from its own noble or ruling lineage to the lower realm's current ruler.
Existing age, relationship, marriage, residence, slavery, protected-role, and
household-capacity validation remains authoritative.

The offer kind is deterministic:

- no current principal wife: offer `PrincipalWife`
- otherwise, if capacity remains: offer `Consort`

The existing `HouseholdOffering` proposal, actor IDs, acceptance, migration,
biography, and history path are reused. The suzerain direction is made an
explicit candidate source so it does not depend on random contact ordering.

## Commit-Time Validation and Compatibility

Before commit, recapture the same requester, responder, type, kind, direction,
war ID, and selected actors. Cancel without mutation when any of these changed:

- selected war ended or participant sides changed;
- alliance or direct subject relation no longer exists;
- a realm joined a conflicting alliance, war side, or subject tree;
- `CanSetVassal` no longer passes;
- the tributary no longer belongs to the same tributary suzerain;
- an offered actor or receiving ruler is stale or ineligible.

Legacy `Vassalize` proposals with an empty direction keep the current
requester-demands-responder behavior. Legacy `EndVassal` proposals infer the
subject from the live direct relation as they do now. Player-created join-war
proposals continue to use their selected war ID. No database schema migration
is required.

## Error Reporting

New failures use specific stable reason IDs for stale war identity, protector
war conflict, protector risk rejection, internalization target mismatch,
transactional relation conversion failure, and alliance-truce persistence
failure. Conversation localization must expose these reasons instead of the
generic unavailable fallback.

## Tests

Follow RED-to-GREEN development with isolated rule slices and existing test
entry points. Cover:

1. Each new ordinary AI candidate and its rejection boundaries.
2. Synchronous and read-only candidate parity.
3. Async enum mapping without renumbering existing values.
4. War ID and direction in async identity and fingerprints.
5. Multiple joinable wars retaining the selected war through commit.
6. Demand submission and protection request using opposite vassal directions.
7. Emergency protection acceptance penalties and automatic defensive entry.
8. Compensation when emergency war entry fails.
9. Tributary internalization restricted to its current imperial suzerain.
10. Mandate internalization producing `Inner`; other empires producing
    `Outer`; exactly one active relation remains.
11. Both peaceful end-vassal directions and stale relation cancellation.
12. Unilateral alliance exit, five-year truce, retry after partial execution,
    and war blocking during that truce.
13. Upper-to-lower principal-wife and consort offers selecting the correct
    actor and receiving ruler.
14. Legacy directionless proposal compatibility.
15. `VassalAIService` no longer directly starts the seek-suzerain decision.

Finally run the existing diplomacy and rule tests, source guards, production
build, and focused multiplayer/async slices affected by identity changes.

## Non-Goals

- Replacing the complete diplomacy AI with action-provider classes.
- Changing forced-vassal war title restrictions or war-goal preferences.
- Adding a new household action type or changing household candidate age.
- Allowing a tributary to internalize under any realm other than its current
  imperial tributary suzerain.
