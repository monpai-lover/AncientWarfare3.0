# Consort Frequency And Alliance Distance Design

## Goal

Make AI ruler households visibly use the existing consort system while preventing new alliances between geographically remote realms.

## Consort Requests

- Keep the current household capacities, candidate age range of 18-33, noble-lineage requirement, independence requirement, pending-proposal guard, and rejection cooldown.
- Reduce the minimum opinion for an AI realm requesting a consort from 60 to 30.
- Give a household proposal a strong AI priority when the recipient ruler has no principal spouse or no consorts.
- Reduce that priority after the first active consort so household proposals continue to occur without dominating ordinary diplomacy.
- Keep proposal acceptance and candidate validation unchanged. Increasing frequency must not bypass eligibility rules or force another realm to accept.
- Apply equivalent priority data in synchronous and read-only asynchronous AI planning paths.

## Alliance Distance

- Existing alliances remain valid and are not dissolved by this change.
- A new alliance is geographically valid when the two realms share a land border.
- Non-bordering realms may form an alliance only when the distance between their capitals is at most 120 tiles. This permits nearby islands and realms separated by a narrow sea.
- If either capital cannot be resolved, alliance creation fails closed with the existing unavailable reason rather than silently allowing a remote alliance.
- The rule is checked when assessing or creating a proposal and checked again immediately before execution. This covers player actions, AI actions, asynchronous proposals, and proposals created before the world changed.
- A distance failure uses the stable reason `alliance_too_distant` and has simplified Chinese, English, and traditional Chinese localization.

## Implementation Boundaries

- Put the pure distance decision in diplomacy rules so it can be tested without WorldBox runtime objects.
- Resolve shared-border and capital-distance facts in `DiplomacyProposalService.AllianceExecutionFailure`.
- Carry household vacancy urgency through `DiplomacyProposalAiCandidate` and apply it only to household scoring.
- Do not change alliance acceptance scoring, household capacity, pregnancy behavior, marriage rules, or existing alliance membership.

## Verification

- A bordering pair is eligible regardless of capital distance.
- A non-bordering pair at exactly 120 tiles is eligible; a pair beyond 120 tiles is rejected.
- A missing-capital pair is unavailable.
- Alliance availability returns `alliance_too_distant`, and final execution repeats the same guard.
- A household with no consorts outranks an otherwise equal alliance proposal.
- A household with one or more consorts receives only normal household priority.
- Existing household eligibility, full-capacity, age, and cooldown tests remain green.
- Run the complete standalone rules test project and relevant PowerShell source guards. Do not compile the mod DLL.

