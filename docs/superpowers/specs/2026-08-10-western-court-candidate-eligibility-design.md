# Western Court Candidate Eligibility Design

## Problem

Western central offices reuse the historical-school education gate from the
Eastern civil-service path. A new Western realm can therefore have many valid
local adults but no eligible officers because nobody has completed historical
school education.

## Design

Keep the common safety filters for every candidate: the actor must be alive,
adult, domestic, male where the office requires it, free, sane, outside royal
asylum, not the king or a royal guard, not already holding a conflicting
office, and available for service.

After those filters pass, Western-profile offices do not require historical
school education. Non-Western courts retain the current education rule. Noble
status remains a scoring preference rather than a prerequisite, so an
existing noble is favored but a valid commoner can fill an otherwise empty
candidate pool.

The appointment remains transactional. Only after the official-career row is
committed does `ApplyCommittedOfficerProjection` call
`LineageService.EnsureOfficialShiAndClan`; its Western path admits the selected
actor as an official noble. Rejected and failed candidates are never promoted.

## Verification

Add a pure rules regression covering the Western education bypass and the
unchanged non-Western behavior, plus a source guard proving the runtime uses
the rule at the candidate boundary and preserves post-commit noble promotion.
Run focused Western court guards, the complete rules suite, and the Release
build.
