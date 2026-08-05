# Virtual Noble Titles and Kingdom Title Roster Design

**Date:** 2026-08-06  
**Status:** Design approved, implementation pending

## Goal

Add player-managed, text-only hereditary virtual noble titles while giving the Kingdom window a reliable roster of all title holders and keeping national, city, person, and genealogy records consistent. A virtual title is the primary ceremonial title when the holder has no other hereditary title.

## Current Findings

### Wartime deployment

Removing the retired AW3 temporary levy and annual mobilization entry points did not remove pre-war deployment. The active path is:

`WarNoticeService.EnsureCurrentNotice` -> `ArmyDeploymentService.ActivateNotice` -> side assignment/discovery/review -> army movement through the existing war/RTS march services.

This path deploys already-existing armies on both sides toward their assigned frontier targets. It must remain independent from the retired temporary recruitment calls. Tests must distinguish “army moves to the front” from “new actors are recruited.”

### City and Kingdom chronicles

The normal `City.setKingdom` path is patched by `AW_ChroniclePatch`. `ChronicleEvents.OnCityTransferred` currently writes:

- a `CITY_TRANSFER` row in the city chronicle;
- `CITY_LOST` in the old Kingdom chronicle;
- `CITY_GAINED` in the new Kingdom chronicle.

Load-time ownership restoration (`pFromLoad`) intentionally skips history to avoid duplicate entries. Special migration paths that call `RecordCity` directly require coverage tests and must produce the equivalent Kingdom-side events.

## Non-goals

- Virtual titles do not represent land, cities, fiefs, offices, or claims.
- Virtual titles do not participate in formal rank comparison, war-score calculation, succession eligibility, or Kingdom rank limits.
- Virtual titles do not replace a ruler's imperial, royal, posthumous, or ceremonial appellation.
- This feature does not revive the removed AW3 temporary recruitment or annual mobilization systems.

## Terminology and data model

### Formal title versus virtual title

Formal titles remain owned by `NobleRankService` and retain their existing rank/style semantics. A virtual title is an independent, arbitrary text label attached to a Kingdom and a living or archived holder. It is not merely a secondary annotation: when the holder has no other hereditary title, it becomes that person's first ceremonial title in all archive-facing views.

The virtual title text is never parsed as a rank. For example, `“天意大将军”`, `“守护者 of the North”`, and `“首席学士”` are all opaque display strings.

### Ceremonial title priority

Use one shared resolver for Actor UI, Kingdom roster rows, family-tree nodes, person archive snapshots, chronicle export, and historical tooltips. The resolver must apply this order:

1. A deceased person's existing posthumous/temple or谥号 layer remains the deceased-name layer defined by current rules.
2. A living sovereign's imperial/royal ceremonial appellation remains authoritative for the sovereign role.
3. A formal hereditary title takes precedence when the Actor has one.
4. Otherwise, the active virtual title is the Actor's primary ceremonial title.
5. Other active virtual titles are rendered after the primary title in stable grant order.

The virtual title must be copied into the archived Actor snapshot at the time the snapshot is created or updated. This prevents a later title change from rewriting an old genealogy or person record. A title succession creates a new primary-title projection for the successor while retaining the predecessor's historical text.

### Persistent record

Introduce a dedicated virtual-title table and model with these fields:

- `TITLE_ID`: stable primary key;
- `KINGDOM_ID`: granting/owning Kingdom;
- `CURRENT_ACTOR_ID`: current holder, or `-1` when vacant;
- `TITLE_TEXT`: user-entered title text;
- `GRANTOR_ACTOR_ID`: ruler or authority who granted it;
- `GRANTED_WORLD_TIME` and `GRANTED_YEAR`;
- `PREDECESSOR_TITLE_ID`: previous record in the same succession chain;
- `SUCCESSION_STATE`: active, inherited, vacant, extinct, or revoked;
- `ACTIVE`: persisted projection flag.

Title text is stored as plain text. It must be length-limited before persistence and escaped through the existing structured history text APIs when rendered.

### Stable identity

The title record, not the Actor data blob, is authoritative. Actor data may contain a read-through projection for fast UI display, but it must be rebuildable from the table after loading a save.

## Granting behavior

### Player flow

1. The player selects an Actor.
2. The Actor window displays the virtual-title input and grant action.
3. The player enters any non-empty text within the configured length limit.
4. The authoritative command validates and commits the grant.
5. The Actor and Kingdom read models are invalidated and refreshed.

### Validation

Reject the command with a localized reason when:

- the text is empty or exceeds the limit;
- the target Actor or Kingdom is unavailable;
- the client is a read-only replica;
- the same Kingdom already has an active virtual title with identical normalized text;
- the target is dead or otherwise cannot receive a living title;
- the request is not issued by the player-authoritative path.

The same Actor may hold multiple different virtual titles. Granting a title to the ruler is allowed, but the virtual title remains an additional title and never replaces the ruler's formal appellation.

### Noble identity

When the target is not already noble, the successful transaction also establishes the existing noble identity projection used by court and lineage systems. This must be an atomic part of the grant transaction from the caller's perspective: a title must not appear active if the noble identity write fails. The new virtual title becomes the primary ceremonial title unless the target already has a higher-priority formal hereditary title.

## Succession

Virtual titles are hereditary, but they do not alter formal succession or land ownership.

On holder death:

1. Resolve eligible heirs using the existing lineage/family rules.
2. Create a new active title record linked by `PREDECESSOR_TITLE_ID`.
3. Close the predecessor record with `SUCCESSION_STATE=inherited`.
4. Project the title text to the successor.
5. If no eligible successor exists, close the chain as `extinct` and leave no active holder.

The succession service must tolerate archived holders and save reloads. It must not infer succession from a transient Actor data field alone.

Revocation, if added later, must close the active record and preserve the historical grant; it must not delete the chain.

## Kingdom window roster

Add a side panel to the Kingdom window titled “Title Holders.” The panel is a read-only projection combining:

- active formal noble titles;
- active virtual titles;
- vacant records when a title has no current holder.

Each row contains the title text, holder name, title kind, primary/secondary ceremonial status, and succession state. Formal titles are listed first for the roster grouping, but the holder's displayed ceremonial name uses the shared priority resolver; a virtual title is marked primary when no other hereditary title exists. Ordering within each group is stable by display text and Actor/Title ID so rows do not reshuffle between refreshes.

Clicking a holder opens the existing Actor window/navigation path. Clicking a vacant row does not fabricate an Actor and instead shows the vacancy state.

The panel reads a cached Kingdom roster. It must not execute a database query on every render frame. Invalidation occurs after a grant, succession, revocation, Actor death, Kingdom destruction, or save reload.

## Actor window controls

Add a right-side virtual-title section containing:

- a text input;
- a grant button with the existing icon/button conventions;
- a compact list of the Actor's active virtual titles;
- localized validation/error feedback.

After a successful grant, the window refreshes the Actor projection and notifies the open Kingdom roster. The control is hidden or disabled for read-only replicas and unavailable targets. The Actor header and title block must use the shared ceremonial-title resolver so a newly granted virtual title is visible immediately when it is primary.

## Chronicle events

Every grant writes two entries from the same authoritative event:

- Kingdom chronicle entry;
- person chronicle entry for the recipient.

The localized text structure is:

`[ruler ceremonial appellation]响应天意爷的号令，授予[actor]爵位：[title text]`

The ruler reference must use the existing ceremonial-appellation resolver. The recipient uses the Actor structured text token. The title text is plain user content and must not be interpreted as a localization key.

The same resolver must be used when a grant is rendered in a person archive or genealogy node. The grant event itself may show the explicit `爵位：title text` suffix even when the recipient later acquires a different primary title.

Use `HistoryTarget.Actor(target)` for the Kingdom row and `HistoryTarget.Kingdom(kingdom)` for the person row. Inheritance, extinction, and revocation use separate event types and never overwrite the original grant.

## Authority, persistence, and failure handling

- Player writes use the existing authoritative command/replica gate.
- The database transaction creates the title record, closes any predecessor, and updates the noble identity projection as one logical operation.
- On failure, no active title is projected and the UI receives a specific localized reason.
- Save loading rebuilds Actor projections and Kingdom roster caches from persistent records.
- Kingdom destruction closes or archives its virtual-title chains according to the existing archive policy; it must not leave live rows pointing at a destroyed Kingdom.

## City/Kingdom territory history requirement

The existing `City.setKingdom` patch remains the canonical owner-change hook. Any future or existing special transfer path must satisfy the same invariant:

1. city chronicle records the transfer;
2. old Kingdom chronicle records the loss when an old owner exists;
3. new Kingdom chronicle records the gain when a new owner exists.

Load-time restoration is the only intentional exception. Add source-guard or integration tests for direct transfer services so they cannot silently write only the city row.

## Testing plan

### Unit/rule tests

- arbitrary Unicode and ASCII title text;
- empty, whitespace-only, and over-limit input;
- duplicate normalized title rejection within one Kingdom;
- multiple different titles on one Actor;
- non-noble target becomes noble;
- virtual title becomes the primary ceremonial title when no formal hereditary title exists;
- formal hereditary title remains primary when present;
- archived/genealogy snapshots preserve the title that was primary at snapshot time;
- ruler target keeps formal appellation;
- succession with eligible child, archived holder, and no heir;
- extinction and revoked chains remain historically queryable;
- stable roster sorting and vacancy rows.

### Integration tests

- Kingdom row click navigates to the intended Actor;
- successful grant refreshes both open windows;
- replica client cannot write;
- save/reload rebuilds title holders and Actor projections;
- Kingdom destruction closes live title rows;
- ordinary and special city transfers produce city + old Kingdom + new Kingdom history entries;
- load-time transfer does not duplicate history;
- war preparation still activates `ArmyDeploymentService` without invoking retired recruitment/mobilization entry points.

### Manual acceptance

1. Open a Kingdom with formal nobles and virtual title holders.
2. Grant an arbitrary title to a commoner and verify the noble identity change.
3. Verify both chronicle windows show the grant with the ruler's ceremonial appellation.
4. Kill the holder and verify inheritance or extinction.
5. Transfer a city during a war and verify city, old Kingdom, and new Kingdom chronicles.
6. Enter and leave the war preparation phase and verify existing armies move toward the front while no retired temporary levy UI/state is created.
