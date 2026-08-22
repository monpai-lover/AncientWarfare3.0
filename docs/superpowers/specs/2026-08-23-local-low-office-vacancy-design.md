# Local Low-Office Vacancy Recruitment Design

**Date:** 2026-08-23

## Goal

Keep the lowest local-government offices filled under the civil-service examination system without weakening qualification rules for governors, commandery chiefs, regional chiefs, or central offices.

## Scope

This change applies only to local city offices whose resolved office grade is the lowest local grade (`30`, rendered as ninth rank). Existing city-leader assignment remains authoritative. Higher local offices and all central, military, and feudatory offices keep their current formal qualification gates.

## Candidate priority

For each vacant lowest local seat, candidates are considered in this order:

1. Candidates with a host-issued civil-service qualification, preferring the existing score and hometown ordering.
2. Eligible living adults with a recorded active or historical aristocratic/clan affiliation.
3. Eligible living adults without a qualification or clan affiliation.

Every tier still requires the existing safety and service checks: same kingdom/residence rules, male civil-office rule, alive, adult, non-slave, sane, not already holding an office, and not a king or registered heir. A lower tier is used only after the higher tier cannot fill the current seat.

## Rank assignment

An unranked local candidate appointed to a lowest local office receives the minimum local career rank required for grade 30 (ninth rank). A clan candidate follows the same floor. Existing ranked candidates retain their rank unless the normal vacancy-promotion rules raise it. No candidate receives a central-office rank floor from this fallback.

## Candidate discovery budget

Candidate discovery uses a bounded, resumable scan rather than stopping permanently at the first 96 kingdom units. The scan keeps a cursor per reconcile attempt and consumes a fixed budget per pass. Waiting-pool candidates are merged first; direct kingdom-unit candidates are then scanned until the budget is exhausted. A later retry resumes from the next cursor position and wraps once, so large populations cannot starve the lower offices based on unit order.

## Retry and timing

If a vacancy reconcile completes without an eligible candidate, the city remains marked vacant and a coalesced persistent retry is retained. Retries are bounded per authority cycle and back off through the existing deferred-work queue. A successful appointment clears the retry key. Database write failures continue to use the existing write retry limit and must not create duplicate appointments.

## Data flow

`RequestImmediateReconcile` -> `ProcessImmediate` -> `ReconcileCity` -> candidate tier discovery -> `CanReceiveFormalCivilAppointment` -> `TryAssignLocalOfficer` -> `OfficialCareerStateService.StageAppointment`.

The qualification service receives the resolved office grade and fallback flag. It may accept an unqualified candidate only when all of the following hold: city layer, vacancy promotion, lowest local grade, and the caller selected the lower-qualification fallback tier.

## Error handling

Invalid actors and stale cities are skipped. SQL/query failures return an empty page and retain the coalesced retry instead of treating the vacancy as permanently reconciled. Appointment persistence remains atomic; failed commits do not consume the candidate or clear the retry.

## Tests

- lowest local grade accepts an unranked ordinary adult only in vacancy fallback mode;
- higher local and central grades still reject an unqualified adult;
- a clan-affiliated candidate is selected before an unqualified ordinary candidate;
- a qualified candidate is selected before both fallback tiers;
- a failed candidate page keeps a retry pending;
- a successful appointment receives the ninth-rank floor;
- the bounded scan resumes past the first 96 units without duplicating candidates.
