# AW3 Vassal Independence And Alliance Safety Design

## Goal

Suspend every military obligation to the suzerain while a vassal is fighting
an independence war, and remove the null-return contract violation that makes
the vanilla alliance-creation plot throw `NullReferenceException`.

This design is implemented in the same verification slice as the already
approved aristocratic-succession-before-republic design. It does not change
the succession ranking described there.

## Confirmed Root Causes

### Independence War Support

`VassalService.OnWarStarted` treats an independence war specially, but it only
joins loyal vassals to the suzerain's side. It does not remove the rebel from
other wars in which the rebel was previously supporting the suzerain.

`VassalService.OnKingdomYear` then enumerates every active vassal relation and
calls `PullVassalsIntoSuzerainWar`. Because the relation remains active until
the independence war is won, an event-only removal would be undone by the
next yearly military-obligation pass.

### Alliance Plot Exception

The latest `Player.log` contains one real runtime exception:

```text
NullReferenceException
PlotsLibrary+<>c.<addBasic>b__18_15
Plot.updateProgressTarget
```

The supplied vanilla source shows that `alliance_create.action` calls
`AllianceManager.newAlliance(...)` and later unconditionally calls
`alliance.recalculate()` on its return value.

AW3's `AW_VassalDiplomacyPatch.NewAlliance_Prefix` returns `null` when either
participant is a vassal. That violates the vanilla method contract. The saved
world contains an active vassal relation from world time `660.1599`, so a
vanilla alliance plot can select that vassal and reach this unsafe return.
The only ordinary vanilla call sites for `newAlliance` are the alliance plot
and `forceAlliance`; the latter already has its own AW3 guard.

## Independence War State

When an `independence_war` starts, AW3 records the war ID and old suzerain ID
on the rebel kingdom before changing any other war membership. The state is
persisted in normal kingdom custom data so save and reload preserve the
suspension.

The service snapshots the rebel's active wars before mutating them. It skips
the independence war itself and peacefully removes the rebel from every other
war where the rebel and old suzerain are on the same side. This includes wars
where the suzerain is attacking, defending, or supporting another participant.
It does not remove the rebel from an unrelated war fought independently.

Every path that pulls vassals into suzerain wars consults the recorded state.
While the referenced independence war is active and has the rebel and recorded
suzerain on opposing sides, the pull is rejected. A stale marker is cleared if
the referenced war no longer exists, has ended, or no longer represents that
opposition.

When the independence war ends, AW3 always clears the suspension marker. An
attacker victory ends or reparents the vassal relation through the existing
settlement logic. A defender victory or peace retains the relation, so normal
military obligations resume afterward.

## Alliance Plot Safety

AW3 removes the `AllianceManager.newAlliance` prefix that can return `null`.
It instead filters `DiplomacyHelpers.getAllianceTarget` so a vassal cannot be
the alliance-plot initiator or selected target. A rejected target is returned
as no target, which is already a normal and safely handled vanilla result in
both the plot eligibility check and its action.

The existing `AllianceManager.forceAlliance` guard remains, and the existing
`Alliance.join` guard continues to reject non-forced vassal membership. The
fix therefore preserves the no-independent-alliance rule without changing the
return contract of alliance construction.

## Aristocratic Succession Integration

The same implementation slice completes the approved vacancy order:

1. registered direct or collateral hereditary heir;
2. strongest eligible domestic noble house and a new monarchy;
3. republican election only when no eligible domestic noble house remains.

Existing republics remain elective. Initial founder selection remains separate
from extinction succession. No old-save migration or world scan is added.

## Failure Handling

War lists are copied before calling vanilla `removeFromWar` because that call
mutates membership and can end a war. Each removal is isolated so one stale war
does not prevent the remaining cleanup. The independence war is never removed
by this cleanup.

Alliance filtering fails closed by returning no plot target. It never creates
a placeholder alliance and never catches and suppresses the original null
exception after the fact.

## Verification

Pure rules cover suspension activation, stale-state rejection, same-side war
removal decisions, resumption after defeat or peace, and alliance target
filtering. Source guards forbid a null-returning `newAlliance` prefix and require
the yearly vassal-pull path to consult independence state.

Integration verification covers these scenarios:

- a rebel immediately leaves all existing wars shared with its suzerain;
- it remains only in the independence war during yearly updates;
- it keeps unrelated wars of its own;
- failed independence restores future military support;
- successful independence ends or reparents the relation;
- a vassal alliance plot is rejected without an exception;
- aristocratic house succession occurs before republic entry.

Debug and Release builds, rule tests, source guards, `git diff --check`, deploy
hash checks, and a fresh-world runtime smoke test are required before the slice
is considered complete.
