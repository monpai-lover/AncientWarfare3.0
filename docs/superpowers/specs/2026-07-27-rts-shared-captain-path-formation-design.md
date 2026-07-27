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

A walkable offset tile is preferred. If the offset is blocked,
liquid-incompatible, or on another island, the follower targets the same
center-line tile used by the captain. Direct adjacent movement remains the
normal correction. Only a follower that has fallen off the retained trail or
cannot make a safe direct step may request a local path, and that target is
clamped to eight tiles.

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
- unsafe offsets fall back to the shared center line;
- only blocked or disconnected followers use the eight-tile local path;
- active transport pauses cursors and landing rebases exactly once;
- ordinary director cycles do not replace a valid mission;
- a living captain cannot be replaced or detached;
- task, mission, route, transport, Army, load, and world cleanup remove state.

Runtime acceptance observes a 64-member Army on a turning or obstructed land
route, through one transport voyage, and after occupying a city. The captain
and flag must remain stable, at least 80 percent of the roster must advance
through the same corridor, no soldier may remain stationary until retirement,
and the objective must not oscillate before completion.
