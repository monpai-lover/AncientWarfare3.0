# RTS War Plan PNG Design

## Goal

Generate a strategic debug image when a war starts and whenever its RTS plan
materially changes. The image must show country and city control areas, fronts,
Army assignments, recovery of friendly occupied cities, and cross-water
transport. It is a diagnostic artifact, not a screenshot, so the whole war is
visible regardless of the current camera position or map mode.

## Output Contract

Images are written beneath the active WorldBox save directory:

`aw3_rts_plans/war_<war-id>_<world-year>_<revision>.png`

The initial image uses revision `000`. Later revisions are emitted only when a
stable plan fingerprint changes, with a bounded cooldown to prevent file and
log spam. A small adjacent text manifest records the war id, world time,
participants, revision reason, and every Army-to-city assignment so failures
remain inspectable even when labels cannot contain all details.

If a new world has not yet acquired a save directory, snapshots are staged in
the AW3 runtime directory. The next successful save copies staged artifacts
into that save's `aw3_rts_plans` directory. Loading or clearing a world resets
all pending revision and path state so files cannot leak between worlds.

## Strategic Snapshot

The authoritative main thread captures one immutable snapshot containing:

- map dimensions and land/water classification;
- kingdom colors and the current owner of every city zone;
- city id, center, owner, original owner, and wartime controller;
- war attackers and defenders;
- Army id, kingdom, captain position, role, state, target city, route anchor,
  transport state, and player-order flag;
- fronts and each friendly city currently occupied by another kingdom.

The capture reuses the RTS strategic indexes and mission projections. It never
scans Actors and never reads Unity or WorldBox objects after leaving the main
thread. Multiplayer clients do not generate authoritative plans.

## Rendering

The renderer builds a fixed-aspect raster directly from the immutable
snapshot. It does not render the Unity camera.

- dark blue: water;
- neutral dark gray: unowned land;
- muted kingdom color: owned city zones;
- brighter outline: the war's participant boundary;
- city marker and compact id label: city center;
- red arrow: attack or pursuit;
- gold arrow: reclaim a friendly occupied city;
- blue dashed arrow: retreat or defensive redeployment;
- cyan dashed arrow with ship marker: queued or active sea transport;
- white line: rally or march without a combat endpoint;
- thicker front segment: active border/front assignment.

Each arrow starts at the Army captain, passes through a route/transport anchor
when one exists, and terminates at the assigned city. Arrowheads indicate the
actual movement direction. The legend includes war id, year, participant
colors, revision reason, and counts for assigned, unassigned, stalled, and
transporting Armies.

The canvas is at most 2048 pixels on its long edge and preserves the WorldBox
map aspect ratio. Zone and terrain samples are reduced deterministically when
the world is larger, preventing image generation cost from scaling without a
bound.

## Trigger And Revision Rules

The war-start hook requests revision `000` only after the war director and RTS
mission assignment have completed. If mission assignment is deferred, the
request remains pending until at least one planning pass has run.

A new revision is requested when any of these material facts changes:

- an Army receives a different target city, role, or front;
- a friendly occupied city is added or removed from recovery objectives;
- a target city is captured and the Army is handed to the next city;
- a land operation enters, changes, or leaves the transport queue;
- a participant joins or leaves the war.

Position-only movement does not create another image. Repeated requests with
the same plan fingerprint are discarded. Each war has a short cooldown and a
bounded pending slot; the newest pending plan replaces an obsolete one.

## Threading And Failure Handling

WorldBox and Unity state is read only on the authoritative main thread. Plain
data rasterization, PNG encoding, manifest formatting, directory creation, and
file writing run through one bounded AW3 background worker. Writes use a
temporary file followed by an atomic rename, so interrupted output does not
leave a valid-looking partial PNG.

An image failure logs one concise warning containing war id, revision, stage,
and exception message. It never cancels the war, changes an Army mission, or
retries every frame. Queue overflow drops the oldest superseded revision but
preserves the newest state for each active war.

## Integration Boundaries

- `AW_WarPatch` announces war creation after RTS war-start services run.
- `KingdomWarDirectorService` and mission persistence announce material plan
  changes through a small snapshot scheduler API.
- a presentation-side capture adapter converts live indexed state to immutable
  DTOs on the main thread;
- a pure renderer converts DTOs to pixels and PNG bytes;
- `AW_SavePatch` publishes staged files into the successful save directory;
- world clear, load, war end, and mod shutdown flush or invalidate runtime
  state without blocking gameplay.

The PNG feature is controlled by the existing RTS visualization setting. It
does not introduce an environment-variable switch.

## Verification

Tests must first demonstrate failure, then cover:

- war-start output waits for the first plan assignment;
- identical fingerprints do not produce duplicate revisions;
- changing an Army target or adding a friendly recovery objective does;
- a friendly occupied city produces a gold recovery arrow;
- transport produces a cyan dashed arrow and ship marker;
- the pixel projection preserves map aspect ratio and stays in bounds;
- generated bytes have a valid PNG signature and decodable dimensions;
- unsaved-world staging is copied to the next successful save;
- replica clients never write plans;
- clear/load/end-war removes pending runtime state;
- renderer or file failures do not change war or Army state.

Runtime acceptance starts a fresh war in a real save, verifies revision `000`
appears under `aw3_rts_plans`, opens the PNG, and compares every rendered Army
assignment with the live RTS mission view. It then captures one enemy city,
confirms the Army receives a next-city arrow, occupies one friendly city,
confirms a gold recovery assignment, and checks a cross-water target produces
the transport path.
