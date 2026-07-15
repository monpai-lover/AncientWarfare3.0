# AW3 Defender-Defeat Capture Balance Design

## Goal

Keep the new fast conclusion for a city assault whose defending army has
actually been defeated, while preventing an undefended border city from being
transferred the instant the first hostile unit enters it.

## Root Cause

The current immediate-transfer rule only asks whether hostile capture units
are active and whether an active defending `Warrior` is absent at the moment
`City.updateCapture` runs.  A city that had no garrison before the assault
therefore looks identical to a city whose garrison fought and was destroyed.

Static garrison assignment is not sufficient evidence.  A soldier registered
to the city may be elsewhere, and a defender from another city may be the army
that actually contests the assault.

## Engagement Evidence

AW3 records a short-lived engagement latch for the exact tuple:

- city ID;
- current owner kingdom ID;
- dominant hostile attacker kingdom ID.

The latch is created only when the same completed zone-presence cycle has
observed at least one living `Warrior` belonging to the current city owner and
at least one living `Warrior` belonging to that hostile attacker.  Civilian,
king, heir, leader, watchtower, historical assignment, and capture progress do
not count as proof of combat.

Actor processing order within a zone cycle is irrelevant: the latch is set as
soon as the second side is observed.

## Transfer Rule

Immediate `finishCapture` is allowed only when all existing safety conditions
hold and the exact engagement latch exists:

- the dominant capturer is still an enemy of the owner;
- that attacker still has active capture units in the city;
- no active owner defender remains;
- no hostile rival army is contesting the attacker;
- ownership has not already changed;
- the city manager is not locked;
- the latch matches the city, current owner, and dominant attacker.

An initially empty city has no latch and therefore follows the normal capture
bar.  AW3's bounded acceleration may still advance that bar up to `99.5`, but
it does not call `finishCapture` until vanilla completion.  This preserves the
benefit of occupying an undefended city without making every ungarrisoned
border settlement an instant loss.

## Lifecycle And Invalidation

The active-presence set is rebuilt each zone cycle.  Before starting the next
cycle, AW3 uses the just-completed set to invalidate a latch whose attacker is
no longer present.  A latch is also invalidated when:

- city ownership differs from the recorded owner;
- the dominant capturer differs from the recorded attacker;
- the attacker is no longer an enemy of the owner;
- the related war ends;
- the city or either kingdom becomes invalid.

Transient clearing at the start of a new zone scan does not itself erase a
valid latch; otherwise actor scan order could make a real defender defeat look
like attacker withdrawal.

All runtime dictionaries remain bounded and are cleared on world reset.  No
state is persisted because old-save compatibility is not required and a live
combat latch has no valid meaning after loading.

## Performance

The implementation extends the existing per-city military presence cache.  It
does not scan city residents, kingdom armies, wars, or the world.  Recording a
unit remains an `O(1)` set insertion, and transfer checks remain `O(1)` apart
from the existing bounded capture-participant dictionary.

## Verification

Pure rule tests prove that an empty city cannot transfer immediately, a city
can transfer after matched combat followed by defender disappearance, and a
stale or mismatched latch cannot transfer a city.  Source guards require the
engagement evidence at the `finishCapture` call and require reset hooks.

Runtime acceptance covers:

1. an attacker entering an empty border city advances the bar but does not
   instantly change ownership;
2. an attacker and real defending warriors fight in a city, and ownership
   changes immediately after the last defender is defeated;
3. attacker withdrawal, attacker replacement, peace, and ownership changes
   discard the old combat evidence;
4. no new exceptions appear in `Player.log` during repeated city assaults.
