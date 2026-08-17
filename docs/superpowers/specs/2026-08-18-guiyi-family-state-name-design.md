# Guiyi Family Historical State Name Design

## Goal

When a Guiyi force restores an extinct kingdom, prefer the restoration
leader's family historical state name over the archived kingdom name.

## Name Priority

1. Resolve the Guiyi leader's current Shi branch.
2. Read that branch's current bound state name through
   `StateNameService.GetBoundStateName`.
3. If the bound name is valid, pass it through
   `KingdomRestorationRequest.state_name`.
4. If the family has no valid bound state name, leave `state_name` empty so
   `KingdomIdentityContinuityService` falls back to the archived kingdom name.

The current bound state name represents the family's most recent valid
historical state name. The implementation must not independently scan older
dynasty rows and accidentally select a superseded or collateral-branch name.

## Identity Data

The Guiyi restoration request also carries the leader's current lineage ID,
Shi ID, and clan name using the same identity-resolution APIs as autonomous
restoration. This keeps the restored state name, legitimate family branch,
and founding ruler identity consistent.

## Scope

- Applies only when Guiyi restores an extinct kingdom.
- Returning occupied cities to an original kingdom that is still alive does
  not rename that kingdom.
- Existing non-Guiyi restoration behavior remains unchanged.
- Invalid or missing family state names use the archived-name fallback.

## Verification

- Pure rules verify family state name priority and archived-name fallback.
- A source guard verifies that Guiyi restoration resolves Shi identity and
  populates `KingdomRestorationRequest.state_name` before calling
  `KingdomIdentityContinuityService.RestoreFromCity`.
- The full rules suite and Release build must pass before push.
