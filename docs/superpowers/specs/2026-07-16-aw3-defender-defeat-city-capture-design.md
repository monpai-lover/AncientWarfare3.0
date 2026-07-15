# AW3 Defender Defeat City Capture Design

## Goal

Complete a city occupation as soon as its military defense has been defeated,
instead of making attackers remain among civilians while an artificial capture
bar advances. This shortens post-battle exposure and reduces non-combatant
deaths.

## Current Code Path

Vanilla `City.updateCapture` advances `_capture_ticks` over time and calls
`finishCapture` at 100. AW3 currently attempts to add extra progress only while
enemy capture units are present and active defenders are absent. Its helper
does not start progress from zero, caps its own writes below 100, and delegates
all final settlement to the later vanilla branch.

The 99.5 internal cap is a code fact, not an observed player-facing percentage:
the reported runtime experience does not show that value or demonstrate that
the acceleration branch is visibly taking effect. The defect addressed here
is therefore the absence of a deterministic battle-victory settlement, not a
claim that the bar was reproduced at exactly 99.5 percent.

## Completion Rule

AW3 immediately calls the vanilla `City.finishCapture` when all conditions are
true:

1. a living enemy kingdom is the dominant active capturer;
2. at least one active unit of that kingdom is contributing capture presence;
3. the city owner has no active military defender in the city capture zone;
4. no mutually hostile rival capturer is active in the city;
5. city ownership is not already changed and the city manager is not locked.

The rule does not require killing civilians, destroying buildings, waiting for
a progress threshold, or having a special AW3 war goal. Vanilla
`finishCapture` remains responsible for king flight, occupation ownership,
soldier cleanup, city transfer, and the existing AW3 city-transfer hooks.

## Contested Cities

If two hostile attacking kingdoms are simultaneously present after the old
defenders are gone, neither receives an instant occupation. The existing
capture and battle logic continues until only one non-hostile controlling side
remains. Allied or same-side units do not count as a hostile rival.

## Integration

`CityOccupationAccelerationService.TryCompleteAfterDefenderDefeat` evaluates
the live city state at the start of `City.updateCapture`. It snapshots the old
owner, invokes `finishCapture` once, and reports success only when ownership
actually changes. The Harmony prefix skips the remainder of the old
`updateCapture` call after successful transfer so stale pre-transfer state is
not processed.

If the city manager is temporarily locked or `finishCapture` cannot transfer
ownership, the prefix allows vanilla processing to continue and retries on a
later update. The existing incremental acceleration remains as a fallback for
contested or temporarily locked cases.

## Verification

Pure rules cover the five required conditions and reject active defenders,
absent attackers, non-enemies, hostile rival capturers, already-transferred
cities, and manager locks. Source guards require the `updateCapture` prefix to
be able to skip vanilla only after a successful transfer.

Runtime verification creates a defended city, defeats or drives out every
defending warrior, and confirms ownership changes on the next capture update.
A second scenario places two hostile invaders in the city and confirms no
instant transfer occurs until one side is removed. `Player.log` must contain no
capture, transfer, or collection-mutation exception.
