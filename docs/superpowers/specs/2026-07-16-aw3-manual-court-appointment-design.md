# Manual Court Appointment Design

## Goal

Allow the player to click a vacant central-office card in the court pyramid, inspect every currently eligible domestic actor, and appoint one through the same durable career transaction used by automatic court appointments.

## Approved Interaction

- A vacant court card is an active button. Its tooltip explicitly says that clicking opens manual appointment.
- The click opens a native AW3/WorldBox list window scoped to the selected kingdom and office.
- Every row shows the actor's live portrait, name, age, current roles, four governing stats, and school identity.
- A row-level appointment button performs the action. Clicking the rest of the row opens the actor inspector.
- A successful appointment returns to and refreshes the court window. A stale or failed appointment remains in the list and displays a localized failure message.

## Eligibility And School Policy

Candidates must be alive, male for a central office, a domestic member of the kingdom, not enslaved, not mad, not in royal asylum, not the king, not already holding a central office, and available for office under the historical-school affiliation lifecycle. City leaders, generals, heirs, actors of another school, and actors with no school remain eligible when those rules pass.

School identity is never a qualification gate. Existing office-school compatibility remains a scoring bonus only, so a matching school sorts higher when ability is otherwise comparable while unmatched and schoolless actors still appear.

## Architecture

- `CourtManualAppointmentRules` owns pure eligibility, commit-gate, score, and stable ordering rules.
- `CourtAffiliationResolver` exposes the existing persisted-home-kingdom authority as a domestic-membership query.
- `CourtService` owns the on-demand candidate scan and `TryManualAppointment`. The commit path revalidates the current tier, vacancy, and actor before calling the existing private `SetOfficer` transaction.
- `CourtAppointmentWindow` and `CourtAppointmentCandidateListItem` contain presentation only. They hold actor IDs, resolve live actors at bind/click time, and never add yearly or per-frame scans.
- `CourtActorNodeView` only routes a valid vacancy click to the appointment window.

## Failure Handling

The service returns a typed result for invalid kingdom, invalid office, occupied office, invalid actor, ineligible actor, and persistence failure. The window maps these results to localized feedback and refreshes its candidate rows after any rejected attempt.

## Performance

The complete kingdom roster is scanned only when the player opens or explicitly refreshes this window. Automatic yearly filling retains its existing bound. Candidate results are sorted once by descending office score and ascending actor ID, and no appointment UI work is added to `updateAge`, kingdom-year, or frame update paths.

## Alternatives Considered

1. Reuse the existing automatic best-candidate method and appoint immediately from the empty card. This is fast but gives the player no choice.
2. Add a school-filtered shortlist. This conflicts with the requirement that schoolless and other-school actors are valid.
3. Use the approved on-demand full candidate list. This preserves player choice and keeps runtime cost out of simulation hot paths.

## Acceptance Evidence

- Rule tests prove schoolless and unmatched-school candidates remain eligible, matching school is only a bonus, invalid identities are rejected, and a filled office cannot commit.
- Source guards prove the vacant card opens the manual window and that the UI calls the revalidating service instead of `SetOfficer` directly.
- Debug and Release builds compile, and the list/localization assets are packaged.

