# Nine-Rank Vacancy Fallback Design

## Status

Approved design for implementation planning. The nine-rank system remains the
primary appointment rule, but it must not leave a court structurally unfilled
when otherwise valid officials are available.

## Goal

Appointments use two passes:

1. The strict pass applies the existing rank, service-history, evaluation, and
   qualification rules and selects the best qualified candidate.
2. If and only if the office is still vacant because the strict pass found no
   candidate, the vacancy fallback selects the best otherwise-valid candidate
   and grants that candidate the rank required by the office.

This preserves the institutional value of the nine-rank system while treating
staffing the court as the higher operational priority.

## Eligibility Boundaries

The fallback may bypass institutional progression gates that can prevent a
vacancy from being filled:

- current rank below the office requirement;
- insufficient service at the expected office grade;
- missing or insufficient evaluation history;
- an examination or civil-service qualification that is insufficient for the
  strict appointment tier.

The fallback never bypasses basic actor and office validity:

- the actor must exist, be alive, and be an adult;
- the actor must belong to the appointing realm or another explicitly supported
  candidate source;
- slaves and other fundamentally ineligible identities remain excluded;
- mutually exclusive or conflicting offices remain excluded;
- sex, government, institution, royal-guard, education, and other hard office
  restrictions that are unrelated to career progression remain authoritative;
- the office must be genuinely vacant when fallback selection is evaluated.

## Selection and Rank Grant

Strict and fallback candidates use the existing deterministic candidate scoring
and tie-breaking order. The fallback does not select the first actor returned by
a world scan and does not introduce a second ranking formula.

On fallback appointment, the career service grants at least the rank floor for
the target office. An actor who already has a better rank keeps it. The rank
grant and office appointment occur through the normal appointment transaction,
so a failed appointment cannot leave behind an unearned rank change.

Local offices use `RequiredRankForLocalOfficeGrade`; central offices continue to
use `RequiredRankForOfficeGrade`. The runtime regional-governor projection is
not a persisted office and receives no separate rank grant.

## Automatic and Manual Appointment

Automatic reconciliation always attempts the strict pass before fallback. It
uses fallback independently for each remaining vacancy, recalculating candidate
availability after every successful appointment.

Manual appointment exposes the same behavior only for a currently vacant
office: a candidate who fails progression gates may be appointed through the
vacancy fallback and is then granted the office rank. Manual replacement of an
already occupied office cannot use this exception.

## Failure Handling

If no actor passes the hard validity rules, the office remains vacant. The
system records no partial career mutation and retries through the existing
reconciliation schedule. Fallback activation should use focused diagnostic
logging so actual staffing exceptions can be distinguished from normal strict
appointments without producing per-frame log noise.

## Compatibility

Realms without the nine-rank institution keep their existing appointment
behavior. Existing saves require no data migration. Existing official ranks are
not demoted or rewritten; only a successful vacancy fallback may promote a
newly appointed actor to the target office floor.

## Verification

Tests must prove:

- a strictly qualified candidate is always preferred over a fallback candidate;
- fallback is not evaluated while an office is occupied;
- an otherwise-valid candidate fills a vacancy when all strict candidates fail;
- fallback grants the local or central office's correct rank floor;
- an already superior rank is preserved;
- hard-invalid actors remain ineligible;
- failed appointment does not mutate rank;
- multiple vacancies are filled deterministically without assigning one actor
  to incompatible offices;
- pre-nine-rank behavior and runtime regional-governor projection remain
  unchanged.
