# Civil-Service Transition And Sibling Succession

## Purpose

Opening the civil-service examination must not make the current pool of
unappointed civil-officer candidates disappear. Agnatic primogeniture must
also prefer a ruler's full brothers when the ruler has no valid descendants.

## Civil-Service Transition

The existing candidate list is computed from live actor eligibility rather
than stored as a separate queue. Once the examination technology is complete,
the formal-appointment qualification gate filters those actors out. No
candidate row is intentionally deleted, but the visible and AI candidate pools
become empty of the pre-existing people.

When `aw_tech_civil_service_examination` completes, AW3 will snapshot the
then-current, unappointed civil-officer candidates using the pre-examination
eligibility rules. Each selected actor receives a durable transition marker
containing the host kingdom id. The marker is a one-time legacy credential:

- It is valid only for the marked kingdom and only while the actor remains a
  normal, live, otherwise eligible civilian candidate.
- It permits one formal civil appointment without a new examination
  qualification.
- A successful formal civil appointment consumes the marker.
- Death, invalidity, foreign transfer, or an ordinary candidate-rule failure
  removes the actor from the live candidate pool without creating a
  replacement marker.
- Candidates created after the snapshot still require the normal
  examination qualification.

For existing saves where the technology was completed before this fix, a
one-time, versioned backfill takes the same snapshot on first authority cycle.
It is intentionally limited to the migration pass and never repeats for a
kingdom.

The formal candidate index and all civil appointment paths will merge marked
legacy candidates with examination-qualified candidates. The same final
appointment gate validates the marker, so a UI list, AI appointment, or
vacancy fill cannot disagree about eligibility.

## Full-Brother Succession

The present direct-descendant selection remains the first and unchanged branch
of agnatic primogeniture. If at least one valid direct descendant exists, no
brother is considered.

When no valid direct descendant exists, evaluate the late ruler's full
brothers before the existing collateral-family fallback. A full brother must:

- share both recorded parents with the late ruler;
- be male, alive, adult, and eligible under the existing heir exclusion rules;
- belong to the ruling house under the existing dynasty ownership rules.

Eligible full brothers are ordered by age from eldest to youngest, with stable
actor id as the final deterministic tie-breaker. If none are valid, the
current collateral successor selection runs unchanged.

## Failure Handling

Snapshot and migration failures leave current candidate state unchanged and
are retried only while the one-time migration has not committed. Invalid actor
records are skipped individually. The succession branch never returns an
invalid sibling; it delegates to the existing fallback when no full brother is
eligible.

## Verification

Add focused rule and source tests covering:

- legacy candidates remain appointable after the examination technology is
  enabled, while new unqualified candidates do not;
- the marker is kingdom-scoped, consumed on first successful appointment, and
  migration runs only once;
- the formal candidate index includes both qualified and marked legacy actors
  without duplicates;
- valid descendants always outrank brothers;
- with no descendants, the eldest eligible full brother outranks younger full
  brothers and all collateral relatives;
- half-brothers, dead brothers, and ineligible brothers fall through to the
  established fallback correctly.
