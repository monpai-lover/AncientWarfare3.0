# RTS Shared Captain Path Formation Design

## Goal

Make every RTS Army follow one captain path with stable formation offsets so
the captain and soldiers advance as one force instead of leaving the roster
stationary, scattering it across unrelated paths, or multiplying strategic
path requests by soldier count.

## Ownership

Each Army publishes one bounded, read-only trail of its captain's actual land
path. The RTS captain movement task is a supported long-march owner, and each
accepted path step receives a monotonically increasing sequence number.

Followers do not share or mutate `Actor.current_path` and do not submit their
own long-distance route. Each follower stores only a stable formation slot and
its own monotonic cursor into the Army trail. Provider validation data remains
separate from the captain's actual movement trail.

## Formation Movement

During `March`, `Retreat`, and contact-free approach, a follower advances along
the captain trail. Its slot row limits how close its cursor may approach the
latest captain sequence. Its slot column becomes a lateral offset rotated to
the local trail direction. This follows bends in the captain's actual route
instead of cutting directly across terrain.

A walkable offset tile is preferred. Every tracked follower owns a unique
primary slot and unique bounded alternate slots; the captain center tile is
never a follower slot. If the preferred tile is blocked, liquid-incompatible,
or on another island, the follower selects its first safe dedicated alternate
instead of collapsing onto the captain or another follower. Direct adjacent
movement remains the normal correction. Only a follower that has fallen off
the retained trail or cannot make a safe direct step may request a local path,
and that target is clamped to eight tiles.

Formation width and depth adapt to roster size and safe terrain. Immediate
combat with a live nearby hostile combatant temporarily releases the actor
from formation correction. After local combat ends, the same stable slot is
used to reform the Army without reshuffling its members.

Follower cursors never move backward. When the bounded trail evicts old steps,
a lagging cursor clamps to the oldest retained sequence and reconnects through
the bounded local fallback. A target replacement, route rebuild, captain
replacement, Army disposal, load, or world reset clears the trail and cursors.

## Combat And Transport

A live, nearby, attackable military target may temporarily preempt formation
movement. The follower retains its cursor and resumes the trail immediately
when contact clears. Stale combat state cannot suppress RTS ownership.

An active cross-water transport pauses land-trail consumption. On the first
post-voyage land update, the Army rebases the trail at the living captain's
landing tile and realigns follower cursors exactly once before marching resumes.
Repeated transport polling must not repeatedly clear or rebase the trail.

## Mission And Captain Stability

Shared movement depends on stable strategic ownership. An existing valid,
incomplete, non-cooling objective remains leased across ordinary director
planning cycles. It may change only for completion, invalidation, final route
failure, a higher-priority homeland emergency, or an explicit player order.

A living captain keeps the Army flag and mission until death or formal Army
disposal. Movement, replenishment, routine maintenance, authority profession,
or formation refresh cannot replace or detach it.

## Prewar Deployment

Both the attacker and defender publish deployment projections before the war
declaration gate opens. A side is ready only when every required ordinary Army
is ready and has arrived; an Army with a living roster may not be represented
by its captain alone. The existing forced-declaration deadline remains the only
time-based escape from this gate.

Overlapping notices remain identity-isolated. Declaration readiness reads the
exact notice-and-side projection being declared and returns not-ready while
that notice is not the kingdom's primary deployment. A later notice cannot
borrow discovery, blocking-Armies, or arrival state from an earlier notice.

During the march to the frontier, the formation anchor follows the captain's
current tile. The final frontier tile must not become the formation anchor
while the captain is still in transit, because follower correction is local
and cannot safely bridge an arbitrary strategic distance. Once the captain is
within the deployment arrival radius, the Army switches to its final frontier
anchor and deployment quorum observation begins. At least 80 percent of the
eligible formation must be at its assigned slots before the Army is marked
arrived.

When a vanilla captain path reaches its endpoint, the completed leader trail
is retained while the prewar assignment remains active and living followers
still need it. Provider-owned complete routes remain authoritative and must not
be replaced by vanilla trail bootstrap when their target matches the captain's
current target. A stale provider target is cancelled before the new vanilla
deployment trail is created.

Each completed retained trail records its exact deployment assignment key. A
same-target path may reuse that trail only while the active assignment key is
unchanged. A replacement notice or post-declaration command creates a fresh
state even when it targets the same tile, so delayed cleanup of the old notice
cannot delete the new route. The retained trail is cleared by matching
assignment cleanup, last-follower removal, mission replacement, Army disposal,
load, or world reset.

## Performance

The Army keeps at most 256 trail nodes and one small cursor record per tracked
follower. Cursor evaluation is bounded and never scans the world. Long route
requests scale with Army count; only the existing eight-tile recovery path may
scale briefly with disconnected followers.

## Verification

Automated coverage must prove:

- the RTS captain task publishes accepted movement steps;
- one Army trail is shared without exposing `Actor.current_path`;
- each follower cursor clamps to the retained range and never decreases;
- slot rows lag behind the captain and slot columns create lateral offsets;
- all 128 tracked follower slots remain unique at every supported width;
- unsafe offsets use collision-free alternate slots and never the captain tile;
- only blocked or disconnected followers use the eight-tile local path;
- active transport pauses cursors and landing rebases exactly once;
- ordinary director cycles do not replace a valid mission;
- a living captain cannot be replaced or detached;
- both attacker and defender deployment projections block declaration;
- a moving deployment uses the captain as its formation anchor;
- only a captain inside the arrival radius enables the final frontier anchor;
- a completed vanilla captain trail remains available to lagging soldiers;
- vanilla trail bootstrap never replaces a valid provider route;
- overlapping notices cannot share deployment readiness;
- a mismatched stale provider route yields to the current deployment target;
- same-target assignment replacement rebuilds the retained vanilla state;
- closing an assignment and losing the last follower release its retained trail;
- task, mission, route, transport, Army, load, and world cleanup remove state.

Runtime acceptance observes a 64-member Army on a turning or obstructed land
route, through one transport voyage, and after occupying a city. The captain
and flag must remain stable, at least 80 percent of the roster must advance
through the same corridor, no soldier may remain stationary until retirement,
no two non-combat formation members may share a slot, post-combat members must
reform, and the objective must not oscillate before completion.

Prewar acceptance observes both sides of one declaration. A captain reaching
the frontier without at least 80 percent of its eligible soldiers must keep the
declaration gate closed. The gate may open only after the followers consume the
retained trail, reform at the frontier, and satisfy the quorum, unless the
explicit forced-declaration year has been reached.
