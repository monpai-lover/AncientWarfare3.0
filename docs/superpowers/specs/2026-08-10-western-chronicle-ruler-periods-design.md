# Western Chronicle Ruler Periods

## Goal

Western kingdoms must show their actual political ruler periods and political
institution changes in the kingdom chronicle. A republic leader is a ruler,
not an empty "no king" interval, but must not be presented as a hereditary
monarch or create a regnal era.

## Design

1. Add a ruler-transition history event distinct from monarchical
   `rule_change`. It records the actor that takes a western/republic leadership
   position and starts a ruler period in the history projection.
2. Extend the history timeline reader so both monarchical accessions and ruler
   transitions begin a populated ruler period. The existing `has_king` storage
   flag remains for compatibility; presentation determines the title and
   chronology from the event/government snapshot, so republics do not display
   a royal accession or era name.
3. Route western political leader selection through the transition writer at
   the authority boundary. The writer is actor-and-kingdom idempotent and
   cannot append an event while the same leader already owns the open period.
4. Record western institution migration whenever the canonical institution
   changes, not only when a Xia court-tier rank increases. The event remains a
   normal chronicle entry and does not create a false ruler transition.
5. On a safe periodic reconciliation, recover only a missing current ruler
   projection for a living western/republic leader. It must reuse persisted
   reign/history data when present and never scan or rewrite prior history.

## Boundaries

- Do not change succession, Cultiway scheduler, pathfinding, or dynasty
  persistence.
- Do not create year names, posthumous titles, or royal accession-book entries
  for a republic leader.
- Existing monarchy `RULE_CHANGE` behavior and Xia dynasty rendering remain
  unchanged.

## Verification

- A pure timeline test proves a western ruler transition opens a ruler period.
- A source/runtime rule test proves western political refresh invokes the
  dedicated writer and migration records a non-upgrade reform.
- The full rules suite and release build must pass with no new warnings.
