# Restoration Rebellion Redirect Design

## Problem

An actor can hold an active dormant restoration claim for an extinct kingdom and still enter the vanilla rebellion path. Vanilla `DiplomacyHelpersRebellion.startRebellion` and `City.useInspire` call `City.makeOwnKingdom`, which allocates a new kingdom identity and generated name. In the reproduced save, Ji Yi therefore founded `North Guo` even though his lineage branch and claim identify the extinct kingdom of Zhou.

Renaming the new kingdom after creation is not sufficient. It would leave the original kingdom dead, allocate the wrong kingdom ID, retain a dormant claim, and break banner, color, legitimacy, continuity, and history data.

## Required Behavior

Before a vanilla rebellion creates a kingdom, AW3 checks whether the rebel leader can start a restoration from that rebellion city. The rebellion is redirected only when all of these conditions hold:

- no restoration kingdom is currently being created;
- the actor and rebellion city are valid and alive;
- the actor has an active dormant restoration claim;
- the claimed original kingdom is extinct;
- the rebellion city is a persisted core city or original capital of that kingdom;
- the existing restoration campaign guards permit the campaign to start;
- the city passes the existing restoration seed validation.

On success, AW3 starts the existing restoration campaign from the specified city, restores the original kingdom ID and identity, consumes or advances the claim through the existing campaign state, and suppresses the vanilla rebellion method. In the reproduced case, actor 1003 restores kingdom 1 as Zhou instead of creating kingdom 9 as North Guo.

If no eligible claim exists or restoration startup fails before committing a restored kingdom, AW3 does not consume the vanilla event. The original rebellion proceeds unchanged.

## Architecture

### Pure Redirect Rules

A small rule object decides whether a rebellion context is eligible for restoration. It takes only primitive facts: restoration recursion state, actor/city validity, dormant claim availability, original kingdom liveness, core/capital membership, and campaign/seed eligibility. This makes the critical routing decision independently testable.

### Explicit-City Restoration Entry

`AutonomousRestorationService` gains an entry point that accepts the claimant, the exact rebellion city, and the matched dormant claim. It reuses the existing campaign creation, `KingdomIdentityContinuityService.RestoreFromCity`, uprising mobilization, rollback, history, and follow-up war code.

The explicit city is validated again immediately before campaign persistence and immediately before identity creation. The entry point must not silently choose another eligible city, because the event being redirected is specifically the actor's current uprising.

The current host-city exclusion used by scheduled autonomous restoration does not apply to this exact city. A vanilla rebellion event proves that the actor is no longer peacefully hosted there. Population, occupation, defender, supporter, core, and owner-liveness checks still apply, and scheduled restoration keeps the original peaceful-host exclusion.

### Harmony Integration

The prefixes for `DiplomacyHelpersRebellion.startRebellion` and `City.useInspire` call a shared redirect service. They return `false` only after a restoration has successfully created and initialized the restored kingdom. Otherwise they return `true` and allow vanilla behavior.

The lower-level `City.makeOwnKingdom` patch remains an observer for policy inheritance. It is not used as the redirect point because that method is shared by ordinary collapse, succession disputes, mandate rebels, coups, and other intentional new-kingdom flows.

`KingdomIdentityContinuityService.IsCreatingRestoration` prevents the internal `RestoreFromCity -> makeOwnKingdom` call from being intercepted recursively.

## Data Flow

1. Vanilla is about to start a rebellion or inspired revolt.
2. AW3 captures the actor and the exact rebellion city.
3. AW3 finds the actor's eligible dormant restoration claims.
4. Claims are filtered to extinct kingdoms for which the city is a core or original capital.
5. If multiple claims qualify, the strongest claim wins; ties use the oldest claim ID for deterministic behavior.
6. The explicit-city restoration entry performs all existing campaign guards and seed checks.
7. A successful entry restores the original kingdom identity and starts its restoration campaign.
8. Harmony suppresses the vanilla rebellion so no second random kingdom or duplicate war is created.
9. A pre-commit failure falls back to vanilla rebellion. A post-commit failure uses the existing provisional-restoration rollback and must not create a second kingdom in the same event.

## History and War Semantics

Successful redirects use restoration history events rather than the generic rebellion history event. Existing restoration campaign logic remains responsible for the uprising war, core recovery, stability period, completion, and failure.

Ordinary rebels without a valid restoration claim continue to receive the generic rebellion event. The redirect must not change mandate rebellions, succession civil wars, feudatory campaigns, coups, or kingdom-collapse splits.

## Failure Handling

- Database or claim lookup failure: log one concise warning and allow the vanilla rebellion.
- Eligibility failure: no warning and allow the vanilla rebellion.
- Campaign conflict or seed invalidation before identity creation: return the existing restoration error and allow the vanilla rebellion.
- Failure after restored identity creation: run the existing rollback path and consume the event, preventing a second kingdom from being created over partial state.
- Multiplayer replica application: do not originate a redirect on the client replica.

## Verification

Automated slice tests will cover:

- an extinct Zhou claim and Zhou core city redirect to restoration;
- a live original kingdom does not redirect;
- a non-core city does not redirect;
- an actor without a dormant claim does not redirect;
- restoration recursion does not redirect;
- deterministic selection when multiple claims qualify;
- pre-commit failure falls back to vanilla;
- post-commit failure consumes the event and relies on rollback;
- ordinary rebellion behavior remains unchanged.

Build verification will compile the full mod. Runtime verification will load the reproduced autosave and confirm that Ji Yi's uprising restores kingdom ID 1 with the Zhou identity, creates a restoration campaign, changes the claim out of `dormant`, and does not create North Guo as a second kingdom.
