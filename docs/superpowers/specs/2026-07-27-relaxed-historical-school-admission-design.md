# Relaxed Historical School Admission Design

## Goal

Increase the rate at which eligible nobles and officials receive a historical-school education, especially in mature saves, without restoring the annual update spike or allowing ordinary academy enrollment to inflate every school without bound.

## Scope

- Raise each realm's annual discovery, attempt, and successful-enrollment capacity.
- Preserve the existing priority order: ruler, heir, feudatory prince, titled noble, central official, untitled noble, local official, then academy commoner.
- Preserve the existing eligibility rules. Candidates must still be valid adults in the correct realm, have no existing school membership, and have no pending membership write.
- Keep academy commoner admission selective. Commoners must still be local, non-noble, non-slave, sane, available for office, and attached to a city with a usable academy.
- Preserve resumable, staggered processing: prepare at most one realm and attempt at most one candidate per scheduler frame.
- Do not add a global actor scan, synchronous database write, automatic school identity, or direct membership grant without a teacher.

## Capacity Changes

The constants in `HistoricalSchoolEliteEnrollmentRules` change as follows:

| Capacity | Current | New |
| --- | ---: | ---: |
| Base successful joins per realm per year | 4 | 6 |
| Dynamic hard cap per realm per year | 12 | 16 |
| Candidate attempts per realm per year | 16 | 24 |
| Noble archive rows discovered per realm per year | 16 | 24 |
| Academy residents inspected per academy per year | 16 | 24 |
| Commoner admissions per academy per year | 2 | 2 |

Teacher and academy bonuses remain unchanged:

```text
realm annual join limit
= 6
+ min(4, qualified teacher count / 2)
+ min(4, academy count * 2)
capped at 16
```

The maximum teacher IDs inspected per school remains eight. The priority-candidate cache remains bounded relative to the new per-realm attempt limit.

## Runtime Behavior

At the beginning of a school's annual enrollment cycle, the service resumes its existing realm-by-realm preparation. A realm may discover up to 24 archived living nobles and may select up to 24 total candidates. It still processes only one candidate in a frame, so the larger annual workload is spread across more frames rather than concentrated in the annual tick.

Successful joins remain limited separately from attempts. Realms without teachers or academies do not receive free membership merely because their numerical cap increased. A teacher must still have an available direct-disciple seat, and all membership writes continue through the buffered historical-school persistence path.

Academies may inspect a slightly wider resident sample to find genuinely strong commoners, but each academy still admits no more than two commoners per year. Elite candidates therefore consume most of the additional throughput. Existing yearly rotation prevents the same low-ID candidates from monopolizing selection.

## Performance Invariants

- No new work runs from actor update or a per-frame global scan.
- Realm preparation remains limited to one realm per scheduler frame.
- Enrollment remains limited to one candidate attempt per scheduler frame.
- Database writes remain buffered and deduplicated.
- Teacher search, archive reads, officer queries, title queries, and academy resident inspection remain explicitly bounded.
- Increasing annual capacity may lengthen the staggered cycle, but must not increase the maximum work performed in one frame.

## Verification

- Rule tests must assert the new six-join base and sixteen-join hard cap.
- Rule tests must prove teacher and academy bonuses still use the existing formula and never exceed sixteen.
- Candidate-selection tests must cover a 24-candidate realm while preserving elite priority, annual rotation, deduplication, and the two-commoner reserve.
- Source guards must assert the new archive, candidate, and academy inspection limits and prove frame budgets remain one.
- Focused school-enrollment and education-discovery tests must pass.
- Debug and Release builds must pass before deployment.
- Runtime acceptance should compare several world years before and after the change: elite school coverage should rise, commoner admissions should remain small, and the annual benchmark should show no new single-frame spike.

## Non-Goals

- Changing school population targets or forcibly maintaining every school at a fixed membership count.
- Relaxing adulthood, realm membership, slavery, madness, or existing-membership checks.
- Increasing the two-commoner annual admission cap.
- Changing teacher direct-disciple limits, lecture scheduling, school founding, or school extinction recovery.
