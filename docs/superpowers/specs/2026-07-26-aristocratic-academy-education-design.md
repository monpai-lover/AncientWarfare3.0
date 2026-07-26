# Aristocratic And Academy Education Design

## Goal

Prevent historical schools from fading after the canonical masters die by
making education a continuing social process. Uneducated nobles actively seek
teachers, academies recruit talented commoners from their own city, and one
completed year of school membership becomes the entry requirement for central
civil office.

This system must remain bounded. It may not enumerate every Actor in the world,
run authority changes from a render-frame postfix, or create an unbounded path
request population.

## Scope

Education is required only for a new appointment to a central civil office.
Central military offices, city offices, feudatory offices, temporary military
commands, rulers, heirs, and feudatory-prince identities are exempt. Existing
central officers are not dismissed when this feature is introduced. The gate
applies when a later appointment or replacement is committed.

The feature does not add a child limit, alter age fertility, increase the
vanilla offspring cap, or grant school membership without a teacher or academy.

## Education Qualification

An Actor is educated when all of the following are true:

1. the Actor has a live, active `SchoolMembership` row;
2. the referenced school is registered;
3. the membership started before the current world year; and
4. the membership has not ended or entered a pending-failure state.

Canonical masters are always educated. A student admitted in year 120 may
enter central civil office beginning in year 121. Reputation, standing, and
later promotion affect influence and teaching eligibility, but do not add a
second appointment delay.

One pure rule class owns office classification and the one-year boundary. All
AI selection, manual candidate projection, replacement, and final appointment
commit use the same rule so no alternate appointment path bypasses education.

## Candidate Sources

### Nobles

Every living adult Actor with an AW3 noble identity is eligible for education,
not only kings and title holders. Rulers, heirs, feudatory princes, titled
nobles, and incumbent uneducated central officers retain priority, followed by
other noble adults.

Event hooks mark candidates when they become an heir, receive a title, found a
feudatory branch, accede, or become adult. A bounded annual archive query acts
as a recovery path for missed events and loaded saves. It uses a kingdom/alive
index, a persisted cursor, and a hard row limit; it does not scan the live world
Actor collection.

### Academy Commoners

Each active academy may nominate talented adults from its own city. Candidate
quality is based primarily on intelligence, with stewardship and diplomacy as
secondary factors. Babies, slaves, mad Actors, existing members, pending
students, foreign residents, and Actors unavailable for office or travel are
excluded.

The academy reads only a bounded slice of its city's existing unit list and
advances a city-local cursor. Each academy may admit at most two commoners per
world year. This creates a non-noble route into education and later central
civil service without a global scan.

## Teacher Attraction And Study Journey

Selection does not immediately write membership. A chosen student enters a
bounded education journey:

1. Prefer a qualified teacher or academy custodian in the student's city.
2. Otherwise, nobles may seek a teacher elsewhere in their own kingdom.
3. If land travel is impossible, reuse the existing historical-school taxi and
   transport-ship lifecycle.
4. On arrival, revalidate the student, teacher, school, residence, capacity,
   and pending-write state.
5. Only then queue the durable `SchoolMembership` transaction and record the
   study history event.

Commoner academy recruits remain local. Nobles may travel because their purpose
is explicitly to seek a teacher. A failed, destroyed, dead, full, or moved
teacher releases the student for a later annual selection instead of granting a
phantom membership.

Journey state stores only student ID, teacher ID, school ID, destination city
ID, start year, and retry count. It is persisted in Actor custom data and
revalidated after load. Existing school travel owns water transport; this
feature must not create a second boat implementation.

## School Continuity And Capacity

Admissions are limited by actual teaching capacity. Canonical masters and
qualified teachers contribute capacity; academies add local commoner seats.
The realm budget scales from four successful admissions to a hard maximum of
twelve per year based on living qualified teachers and academies. Processing
remains one candidate per authority cycle.

The early world has many canonical masters and few people, so most suitable
students can find teachers quickly. Later, students become members and then
qualified teachers through the existing standing and reputation rules. Those
teachers attract the next generation. The existing low-population recovery
service remains an emergency floor, while ordinary education journeys become
the main long-term propagation mechanism.

Teacher direct-disciple caps remain authoritative. The scheduler rotates
schools, kingdoms, academies, and candidates deterministically so a large
school cannot permanently starve smaller schools.

## Court Integration

The final appointment boundary evaluates:

- whether the target layer is central;
- whether the office is civil rather than `SiMa`, marshal, military ministry,
  or another registered military office; and
- whether the candidate completed one year of valid education.

AI vacancy filling excludes ineligible candidates before scoring. Manual lists
show only candidates who can legally be chosen. Direct service calls and
replacement transactions repeat the rule immediately before commit. Existing
officers are grandfathered until their office ends, but uneducated incumbents
receive high education priority so the historical population converges without
a mass dismissal.

## Scheduling And Performance

All authoritative work runs from the historical-school annual scheduler and
the authority-cycle deferred queue. Render-frame hooks may update presentation
only.

Hard limits are required:

- one realm prepared per scheduler frame;
- one education candidate advanced per authority cycle;
- at most 16 noble archive rows inspected per realm per year;
- at most 16 city residents inspected per academy per year;
- at most two commoner admissions per academy per year;
- at most twelve total successful admissions per realm per year; and
- at most eight teacher IDs checked per school selection pass.

Candidate discovery uses existing city lists, indexed archive reads, and the
historical-school runtime index. No code path may use `World.world.units` as an
enumeration source.

## Failure And Save Handling

Every asynchronous or deferred completion revalidates IDs against the current
world. Destroyed kingdoms, cities, academies, teachers, or students cancel the
attempt cleanly. A membership persistence failure leaves the Actor uneducated
and eligible for retry; it never updates the court gate optimistically.

World clear removes journey runtime indexes. Load reconstructs only recorded
journeys and pending memberships, with fixed per-frame budgets. Save flushing
uses the existing historical write pipeline and does not introduce a new
SQLite writer.

## Verification

Verification must include:

1. pure rule tests for central civil versus military and non-central offices;
2. one-year qualification boundary tests;
3. noble identity coverage, including untitled and female nobles;
4. academy commoner scoring, cursor, quota, and exclusion tests;
5. local, cross-city, failed-teacher, and water-transport journey tests;
6. source guards against global Actor scans and render-frame authority work;
7. AI, manual, replacement, and direct-commit appointment gate tests;
8. save/load and world-clear journey lifecycle tests;
9. Debug and Release builds; and
10. a long-running game check showing living students and teachers in every
    surviving school, continued yearly admissions, bounded scheduler cost, and
    no uneducated new central civil appointments.
