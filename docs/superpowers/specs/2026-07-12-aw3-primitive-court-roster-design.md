# AW3 Primitive Court Roster Design

## Goal

Make the primitive court visibly represent the ruler's working council. Its rank
pyramid shows the king, the registered heir, all active generals, and every
living city leader without requiring the heir to hold another office first.

## Scope

This change affects only the primitive court tier. Official court tiers keep
their current behavior: an heir is shown there only when the same actor already
appears as an officer, general, or city leader, and the heir role is merged into
that node.

The primitive court order is:

1. King.
2. Registered heir.
3. Active generals.
4. Living city leaders.

If no valid registered heir exists, the heir row is omitted rather than rendered
as a vacancy.

## Read Model And Deduplication

`CourtReadModelService` reads the registered heir through the existing O(1)
kingdom heir cache. It adds a primitive-tier heir seed before
`CourtPyramidRules.BuildLayout` runs. The heir seed uses the high-office rank so
it forms a row directly below the king.

The existing actor-ID merge remains authoritative. If the heir is also a general
or city leader, the actor appears once at heir rank and the node retains every
concurrent role label. General ordering by merit and city-leader ordering by city
ID remain unchanged.

Official tiers continue using the existing post-layout cached-heir role merge and
do not gain a forced standalone heir node.

## Presentation

No new UI component, icon, portrait path, or localization key is required.
Occupied heir nodes reuse `CourtActorNodeView`, live portraits, kingdom colors,
tooltips, and click behavior. Existing government-aware title rules render the
role as Crown Prince for monarchies and Elder for republics.

## Performance And Failure Handling

The change must not enumerate kingdom or world units. Missing, dead, foreign, or
otherwise invalid cached heirs are omitted. Portrait construction remains in the
existing per-frame batches.

## Verification

Focused rules must prove that:

- a valid heir is forced into the primitive court;
- official tiers do not force a standalone heir;
- the heir row ranks below the king and above generals;
- an heir who is also a general or city leader produces one node with merged
  roles;
- no population-scan fallback is introduced.

The final change must pass the succession correctness harness, the court rule
harness, the normal net48 build, and the `DEBUG;TRACE` build.
