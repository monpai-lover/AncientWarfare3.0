# AW3 Royal Political Asylum Design

## Goal

Protect the immediate royal succession family during a defensive war without
changing their nationality merely because they reside in a foreign city.  The
system prevents a captured capital from killing every viable dynastic branch
and causing an avoidable republican vacancy.

## Protected Family Scope

For a living non-republic realm with a living king, the protected set is
bounded to:

1. every living direct child of the current king except the registered heir;
2. every living direct child of the registered heir;
3. neither the current king nor the registered heir.

The whole royal clan, spouses, collateral cousins, and earlier dynastic
branches are not evacuated.  An actor must still belong to the home kingdom
when evacuation begins.  Slaves, dead actors, rekt actors, and actors already
serving as a foreign king are ineligible.

## Engine Constraint

Vanilla `Actor.joinCity(foreignCity)` and `Actor.setCity(foreignCity)` both
change `actor.kingdom` to `foreignCity.kingdom`.  Political asylum therefore
must not use either API to represent foreign residence.

At evacuation, AW3 calls `setCity(null)`, keeps `actor.kingdom` equal to the
home realm, records the host city separately, and places the actor physically
on a safe tile in that host city.  A dedicated actor job keeps the refugee
inside the host city and prevents vanilla no-city jobs from naturalizing the
actor.

## Persistent State

Each refugee stores the following actor-data fields:

- active asylum flag;
- home kingdom ID and name snapshot;
- former home city ID;
- host kingdom ID and host city ID;
- asylum start year;
- last relocation year.

Each home kingdom stores a bounded, comma-separated roster of active refugee
actor IDs.  Runtime indexes are rebuilt from these kingdom rosters after world
load.  No scan of every living actor is permitted.  Existing saves do not need
migration because the mod has not been released.

## Trigger And Host Selection

Evacuation begins when the home kingdom is a defender in an active war.  The
war-start callback handles immediate main participants; the yearly kingdom
reconciliation covers realms added to an existing war later.

A host candidate must be a living foreign civilization kingdom with at least
one living city and no active war.  The host cannot be the home realm, an enemy
participant, a rekt kingdom, a neutral kingdom, or a wild kingdom.  Selection
is deterministic:

1. prefer a candidate city on the same island as the home capital;
2. choose the smallest squared tile distance from the home capital;
3. break ties by kingdom ID and then city ID.

If no host exists, the family remains at home and reconciliation retries on
the next kingdom year.  Evacuation never creates a new realm or city.

## Evacuation And Host Life

Before relocation, AW3 removes military and temporary city work that would
pull the refugee back into combat.  It does not alter blood lineage, clan,
shi, culture, religion, or home nationality.  The actor is removed from the
home city, spawned at a stable non-border tile in the host city, receives a
localized political-asylum status, and is assigned a dedicated asylum job.

The job repeatedly selects bounded idle locations inside the host city and
waits between moves.  It has no join-city, find-city, enlistment, office,
lecture, debate, or capture task.  An active refugee cannot be selected as a
court officer, general, royal guard, slave soldier, ordinary soldier, or city
leader.  The actor remains mortal and can still be affected by ordinary world
hazards.

If the host kingdom enters any war or its host city is destroyed or changes
owner, AW3 immediately attempts relocation to another peaceful host.  If no
replacement exists, the actor returns to a living home city when possible;
otherwise the actor remains physically in place with the asylum job and is
retried annually.

## Return

A refugee returns only after the home kingdom has no active defensive war.
An unrelated offensive war does not delay return.  The destination order is:

1. the former home city if it is alive and still belongs to the home kingdom;
2. the home capital;
3. the nearest living city of the home kingdom.

Return uses the normal same-kingdom city API, restores ordinary citizen job
selection, removes the asylum status and data, and removes the actor from the
kingdom roster.

## Home Realm Extinction

If the home kingdom is destroyed while the actor is in asylum, the actor does
not remain attached to a rekt realm and does not return.  The current living
host becomes the actor's permanent nationality and residence through a formal
`joinCity(hostCity)` transition.  AW3 then clears the asylum state and lets
normal host-city job selection resume.

If the recorded host is no longer usable at the extinction moment, AW3 first
chooses another peaceful host using the same deterministic rule.  If none
exists, it leaves the actor uncommitted and retries from the bounded stale
roster; it never assigns a null or rekt kingdom.

This extinction naturalization replaces the earlier idea of keeping a
permanent exiled nationality.  Existing restoration-claim logic may still use
the actor's archived lineage and former-realm snapshots, but asylum itself is
finished after naturalization.

## History And UI

The person biography records one `royal_asylum_started` event with the home
realm and host city, one event for each host relocation, and one terminal
event for return or naturalization after realm extinction.  Duplicate annual
events are forbidden.

Actor UI shows the political-asylum status and logical host city while asylum
is active.  Kingdom and lineage identity continue to use the home realm until
formal extinction naturalization completes.

## Performance And Safety

- War start examines only the two war sides and their bounded royal families.
- Yearly reconciliation reads only the kingdom's bounded asylum roster.
- Host selection scans living kingdoms and one stable city candidate per
  kingdom only when evacuation or relocation is required.
- No per-frame world scan, database scan, or pathfinding request is added.
- A host transition is committed only after all actor, home, host, and city
  references are revalidated.

## Verification

Pure rule tests cover family eligibility, defensive-war activation, peaceful
host requirements, deterministic host ranking, return readiness, and
extinction naturalization.  Source guards reject foreign `setCity` use and
require the dedicated no-city asylum job.

Runtime tests cover evacuation without nationality change, host-war
relocation, return after the last defensive war, former-city loss, home-realm
extinction followed by host naturalization, save/load restoration, and the
absence of join-city, enlistment, or null-kingdom exceptions in `Player.log`.
