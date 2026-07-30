# Civil-Service Youth Pipeline And Appointment Rank Design

## Goal

Keep each examination supplied with domestic young candidates and fill civil
vacancies without requiring an actor to possess the rank that the vacant
office itself grants.

## Confirmed Rules

- A realm with the examination technology targets at least 24 eligible local
  candidates for every three-year sitting.
- Existing valid school members remain eligible after a failed sitting. Old
  failed attempts, archived slaves, and an existing unranked career row must
  not consume a bounded candidate window.
- New admissions prefer domestic young adults. Nobles and declined nobles are
  considered first, followed by academically capable commoners from academy
  cities.
- A disciple who completes three years of study may undertake basic teaching.
  Reputation continues to affect school prestige and lecture authority, but
  zero reputation must not permanently prevent the creation of new teachers.
- When the local pipeline is below 24, annual school admissions expand only by
  the missing amount and remain bounded across frames. A small realm admits all
  available valid students rather than manufacturing actors.
- A formal qualification is required after the examination technology is
  completed. A pre-existing official rank is not required for appointment.
- A successful appointment assigns the office and applies its rank floor in
  the same persistence operation. Rank affects candidate scoring and later
  promotion, not eligibility for the vacant office that grants it.
- Service history and evaluation may influence competitive promotion when an
  incumbent exists. They cannot leave an actually vacant office unfilled when
  a qualified, educated, domestic candidate is available.

## Data Flow

1. The annual school scheduler counts educated domestic actors without a host
   qualification and computes the deficit against 24.
2. The bounded enrollment planner scans rotating noble archives and academy
   residents, prioritizing young adults and reserving enough admission slots
   to close the deficit before the next sitting.
3. Three-year disciples replenish the teacher index, preventing the per-teacher
   disciple cap from collapsing realm-wide admissions.
4. The examination candidate query performs SQL preselection before its limit,
   then validates live actor state. Rejected rows do not consume the local
   source budget.
5. Final qualifications populate the formal-candidate index.
6. Vacancy filling selects a qualified candidate without requiring their old
   rank. Appointment persistence records the office and projects the matching
   rank floor atomically.

## Performance And Failure Handling

- Realm preparation remains one realm per scheduler frame and candidate work
  remains one admission attempt per frame.
- SQL filtering occurs before `LIMIT`; no whole-world actor scan is added.
- Per-realm admission and scan limits are adaptive but hard bounded.
- Failed or unavailable teachers release reservations and leave the candidate
  eligible for a later annual pass.

## Verification

- Rule tests prove a young candidate outranks an old otherwise-equal candidate
  and that a deficit of 24 expands admissions within the hard cap.
- Qualification tests prove an unranked qualified actor can enter a vacant
  high, middle, or entry office and receives that office's rank floor.
- SQL tests prove stale rows do not consume source limits.
- The 100-year simulation must produce no zero-candidate sitting and finish
  with zero fillable vacancies.
- Autosave runtime validation must show at least 24 domestic candidates when
  population permits and must fill every office for which a valid graduate is
  available.
