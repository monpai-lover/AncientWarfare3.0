# Court Candidate Performance and Civilianization Design

## Scope

This slice extends the existing manual central-office appointment feature. It must:

- exclude actors for whom the original game reports `Actor.isAdult() == false`;
- keep every eligible actor reachable in a kingdom with roughly one thousand actors;
- avoid a single-frame population scan, sort, UI-node, or portrait spike;
- remove military identity after a committed appointment to a civil central office;
- preserve military identity for `marshal` and `bingbu` appointments.

Existing saves do not require compatibility handling.

## Candidate Eligibility

`CourtManualCandidateFacts` gains an `Adult` fact. `CanListCandidate` requires it in
addition to the existing alive, domestic, male, non-slave, sane, non-king,
non-asylum, office-free, and affiliation checks. Both automatic court filling and
manual appointment use this shared gate, so minors cannot enter through another
appointment path. The final click path re-evaluates the same live actor facts.

## Incremental Query

Opening the window takes a cheap snapshot of actor IDs only. It does not calculate
stats, read school membership, or create portraits during that snapshot.

While the window is active, it processes at most 32 actor IDs per frame and also
stops when its 1 ms work budget is exhausted. Cheap live-state checks run before
affiliation, school, and stat reads. Eligible candidate view models are collected,
then sorted once by the existing deterministic score and actor-ID tie breaker.

The result is paged at 48 candidates. All eligible candidates remain reachable.
The active page is appended at no more than four rows per frame, bounding portrait
creation. Previous and next rows change pages without rescanning the kingdom.

Opening another office, changing kingdoms, or refreshing the window replaces the
scan generation, so stale work cannot populate a newer selection.

## Military Identity Transition

The database appointment remains the authority. Runtime military cleanup starts
only after `OfficialCareerService` or the atomic replacement transaction reports a
committed appointment.

For a central office other than `marshal` or `bingbu`, cleanup performs bounded,
actor-local operations only:

1. end an active general appointment;
2. dismiss an active royal guard;
3. remove the actor from the temporary levy index;
4. clear the border-guard marker;
5. call original `Actor.stopBeingWarrior()` and remove an emptied special army.

Military central offices retain all military state. Non-central layers are not
changed by this slice.

## Failure and Staleness

Candidate rows resolve the live actor before drawing or clicking. A dead, moved,
underage, newly appointed, or otherwise invalid actor is rejected by the commit
gate even if an older row remains visible. A persistence failure leaves the actor's
military identity untouched.

## Verification

Rules tests cover the adult gate, military/civil office classification, pagination,
and per-frame bounds. Source guards assert that the synchronous full-list window
path is removed and military cleanup occurs after the commit gate. Debug and Release
rebuilds verify integration against the game assemblies.
