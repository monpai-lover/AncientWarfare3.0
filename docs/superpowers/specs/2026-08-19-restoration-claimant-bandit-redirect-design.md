# Restoration Claimant Bandit Redirect Design

## Goal

When an actor selected to found a bandit force owns a valid dormant royal
claim, redirect that event into an immediate restoration rebellion. The new
force uses the historical kingdom identity from the moment it is created,
even when the outlaw base lies outside every persisted core of the old
kingdom.

Ordinary bandit creation must remain unchanged for actors without a valid
claim. This design extends the existing restoration launch machinery; it does
not create a second restoration state machine or redefine Guiyi restoration.

## Confirmed Rules

- A valid dormant royal claim takes priority over every ordinary bandit route.
- Claim resolution happens before any bandit identity, government, diplomacy,
  history, or stronghold ownership is committed.
- The force immediately uses the old kingdom's title and recoverable identity.
- An outlaw base outside the old kingdom's cores is a valid external
  restoration base.
- An external base does not become an old core merely because the restoration
  rebellion owns it.
- The rebellion advances from the external base toward the old kingdom's
  persisted core territory.
- A successfully created restoration kingdom receives the existing ten-year
  restoration protection.
- Before restoration identity commit, a failed preparation may fall back to
  ordinary bandit creation. After identity commit, it may never become a
  bandit force.

## Existing Constraint And Required Extension

`AutonomousRestorationService.TryStartSelfRestorationFromRebellion` currently
requires its seed city to be the old capital or a persisted old core. It
rejects an outside-core seed with `restoration_rebellion_city_not_core`.

That rule remains correct for ordinary core-based autonomous restoration. The
bandit redirect therefore needs an explicit external-seed launch path rather
than weakening the existing rule globally. The external-seed path reuses
claim resolution, historical identity continuity, restoration wars, campaign
state, and protection, but gives the temporary base a distinct role from old
core territory.

## Trigger And Routing

Every route that can finalize an ordinary bandit force must pass through one
shared claim-priority gate. This includes peasant-rebel route selection,
direct bandit stronghold creation, existing-bandit government entry, and
Guiyi-related direct bandit creation where the selected leader would otherwise
become an ordinary bandit ruler.

The route selects and validates the prospective ruler before creating the
bandit kingdom. It then calls
`RoyalClaimService.FindBestDormantClaimIdForActor(actorId)`.

- No valid dormant claim: continue the current bandit flow without behavioral
  changes.
- Valid dormant claim: stop the bandit flow and prepare an external-seed
  restoration launch.

One initiating event can create at most one kingdom. The shared gate must be
idempotent across deferred retries and must prevent both a bandit kingdom and
a restoration kingdom from being created for the same event.

The existing Guiyi restoration subtype retains its foreign-occupation trigger
and identity rules. This redirect applies only where the selected claimant
would otherwise be finalized as an ordinary bandit ruler; it does not replace
an already-qualified Guiyi route.

## Identity And Persistent State

The restoration kingdom immediately receives the historical kingdom title,
flag, recoverable cultural identity, and continuity data selected by the
dormant claim. The claimant becomes its ruler, and the launch record persists
the exact claim ID used.

The selected city or stronghold becomes an external restoration base owned by
the restored kingdom. Its persisted launch state distinguishes:

- the restoration kingdom and claim;
- the claimant;
- the external base city;
- the selected old-core campaign target;
- the preparation, identity-commit, and initialization stages.

Ownership of the external base does not append it to the old-core set and does
not alter the old kingdom's legal territorial history. UI labels, diplomacy,
chronicles, and map identity treat the force as a restoration kingdom from the
identity-commit stage onward; no temporary bandit name or government is
visible or persisted.

The shared successful identity-restoration path starts the existing ten-year
restoration protection. It must write protection once and remain compatible
with the protection deadline already persisted by
`RestorationProtectionService`.

## External-Seed Campaign

Preparation resolves the persisted old-core cities belonging to the selected
historical identity. The initial strategic target is the nearest live,
relevant old-core city from the external base under the existing strategic
distance rules. Land or sea reachability affects movement planning, not
restoration eligibility. The base remains the rebellion's raising,
replenishment, and departure location.

The restoration force uses the existing war and army systems to advance from
the external base to that target. After recovering one old core, normal
restoration campaign selection continues across the remaining cores.

Possession of the external base alone never satisfies restoration completion.
Existing core-recovery and campaign-completion rules remain authoritative.
The external-seed extension changes where a restoration campaign may begin,
not what territory counts as restored.

If no live or persisted old-core target can be resolved during preparation,
restoration preparation fails before identity commit and the initiating event
may continue as an ordinary bandit event. An unreachable but otherwise valid
core still qualifies as a target; route planning, sea transport, and later
campaign retries handle terrain and connectivity through the existing
movement systems.

## Atomic Preparation, Commit, And Recovery

The redirect is a staged, persisted, idempotent operation:

1. Select the prospective ruler and best dormant claim.
2. Validate the historical identity, external base, old-core target, and all
   data needed to create the restoration kingdom without mutating world state.
3. Reserve the initiating event so a deferred retry cannot create a duplicate
   force.
4. Commit the restored identity and external-base ownership atomically.
5. Initialize the claimant, campaign, first war, histories, and ten-year
   protection through existing restoration services.
6. Mark launch initialization complete.

Failure before step 4 releases the reservation and may resume ordinary bandit
creation. No kingdom, war, history entry, government state, or protection
deadline may have been written at that point.

Failure during or after step 4 never falls back to bandit creation. The launch
remains a restoration launch and retries only its incomplete initialization
stages. Each stage re-resolves stable IDs and must not duplicate kingdom
creation, wars, histories, targets, or protection. If the claimant or external
base becomes invalid at the commit boundary, the atomic commit rolls back
rather than leaving a leaderless or cityless kingdom.

## History And Presentation

History records the claimant raising the old kingdom's title from an external
base and names the first old-core objective. It does not record an ordinary
bandit founding followed by a conversion. Relevant kingdom and city histories
receive the event through the same bounded history-writing patterns used by
existing restoration launches.

The feature adds localized failure or diagnostic reasons for invalid claim,
missing identity, invalid external base, missing old-core target, duplicate
launch reservation, and incomplete initialization retry. It adds no per-frame
logging or world-wide actor scan.

## Verification

Rules, isolated tests, and source guards must prove:

- a claimant outside all old cores creates a restoration kingdom under the
  old title and never creates an ordinary bandit kingdom;
- a claimant inside an old core uses the same claim-priority gate without
  duplicate creation;
- an actor without a dormant claim follows the unchanged ordinary bandit
  route;
- multiple dormant claims use the result selected by
  `FindBestDormantClaimIdForActor`;
- the external base is not inserted into the persisted old-core set;
- the nearest valid persisted old core becomes the first campaign target;
- owning only the external base does not complete restoration;
- pre-commit validation failure safely resumes bandit creation without
  restoration side effects;
- post-commit failure retains restoration identity and retries idempotently;
- load recovery and repeated authority updates cannot duplicate a kingdom,
  war, history event, target, or protection record;
- peasant-rebel, direct-stronghold, existing-bandit, and Guiyi-adjacent entry
  paths all pass through the shared priority decision at the correct point;
- established Guiyi restoration behavior remains unchanged;
- the shared success path applies exactly one ten-year protection deadline.

Run the focused restoration and bandit rule tests, source guards for every
finalization entry, the broader rules suite where its existing harness permits,
and a production build. Runtime verification must cover outside-core and
inside-core claimants, no-claim fallback, save/load during incomplete
initialization, and recovery of the first old core.

## Non-Goals

- Do not make every bandit leader search all historical kingdoms or world
  actors; claim lookup remains actor-scoped and bounded.
- Do not convert an already-created ordinary bandit kingdom after the fact.
- Do not automatically add the external base to old-core territory.
- Do not change ordinary autonomous restoration's core-seed restriction.
- Do not replace Guiyi Army restoration or its foreign-occupation rules.
- Do not change restoration completion thresholds, army movement, combat, or
  the ten-year protection policy.
