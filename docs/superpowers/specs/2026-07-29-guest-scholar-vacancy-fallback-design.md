# Guest Scholar Vacancy Fallback Design

## Goal

Keep pre-examination courts staffed without bypassing education or office-fit rules. A kingdom first appoints eligible domestic formal candidates. If a central office remains vacant, an educated scholar with no office may enter that court as a temporary guest official.

## Considered Approaches

1. Keep guest service foreign-only and make domestic scholars formal officials. This preserves the narrow historical meaning of guest official but does not match the requested gameplay rule.
2. Broaden the existing guest-office pipeline to domestic and foreign resident scholars. This reuses durable guest terms, career history, vacancy reservation, and cleanup. This is the selected approach.
3. Add a separate scholar-retainer subsystem. This would distinguish domestic retainers from foreign guest officials, but duplicates appointment and persistence logic without adding required gameplay value.

## Rules

- Normal domestic formal appointment always runs first.
- Guest fallback runs only for a still-vacant central office.
- A guest candidate must be alive, adult, male, educated by an active registered school, free of another court office or protected incompatible role, resident in the host kingdom, and suitable for the office.
- Domestic scholars and foreign resident scholars are both eligible.
- Before the civil-service examination exists, ordinary educated scholars are eligible; teacher, leader, canonical-master, and examination qualification are advantages rather than entry gates.
- After the examination exists, the existing formal/acting examination rules remain authoritative. This change must not create a route that permanently bypasses examination qualification.
- One actor may hold only one court office. Existing durable guest appointment, term, renewal, biography, and cleanup paths remain unchanged.

## Fairness And Scheduling

The world-wide shared appointment budget is replaced by a bounded per-host budget. Each eligible kingdom therefore gets a chance to fill vacancies while total work remains bounded by the existing host and candidate scan limits. The annual scheduler remains incremental.

## Data Flow

1. Court refresh fills domestic formal candidates.
2. Guest fallback builds a bounded resident educated-scholar index for that host.
3. Candidate rules filter availability, education, residence, sex, office fit, and examination-era restrictions.
4. The existing transactional guest-office write commits affiliation, court office, career, and history.
5. Only a committed or durably queued appointment reserves the office and consumes that host's budget.

## Tests

- Ordinary educated scholars are eligible before examinations even when they are not teachers.
- Domestic residence is accepted by the fallback.
- Foreign candidates must still reside in the host kingdom.
- Uneducated, already-serving, incompatible, or non-resident candidates remain rejected.
- Per-host budgets do not let an earlier kingdom exhaust every later kingdom's appointments.
- Existing guest term, ranking, and persistence tests remain green.

## Scope

This change does not alter office counts, examination admission, official ranks, gender rules, school enrollment, or UI terminology.
