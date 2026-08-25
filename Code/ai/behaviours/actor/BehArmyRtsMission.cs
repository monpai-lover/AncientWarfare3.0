using AncientWarfare3.core.lineage;
using ai.behaviours;
using life.taxi;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehArmyRtsFrontHold : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!ArmyRtsControllerService.HasFrontHoldMission(pActor))
            {
                ArmyRtsControllerService.ReleaseActor(pActor);
                return BehResult.Stop;
            }
            pActor.clearTileTarget();
            pActor.beh_tile_target = null;
            pActor.makeWait(0.2f);
            return BehResult.RepeatStep;
        }
    }

    public sealed class BehArmyRtsMission : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!ArmyRtsControllerService.HasCaptainMission(pActor))
            {
                ArmyRtsControllerService.ReleaseActor(pActor);
                return BehResult.Stop;
            }
            if (!ArmyRtsControllerService.TryGetCaptainTarget(pActor,
                    out WorldTile target))
            {
                ArmyRtsControllerService.TryRecoverMissingCaptainTarget(
                    pActor);
                pActor?.makeWait(0.1f);
                return BehResult.RepeatStep;
            }
            if (target?.data != null &&
                target.data.tile_id == pActor?.current_tile?.data?.tile_id)
            {
                pActor.makeWait(0.1f);
                return BehResult.RepeatStep;
            }
            if (ArmyRtsControllerService.ShouldHandleCaptainTransport(
                    pActor) &&
                ArmyRtsTransportService.TryHandleActor(pActor, target,
                    pMayBegin: true, pForceTransport: true))
            {
                pActor.makeWait(0.2f);
                return BehResult.RepeatStep;
            }
            if (AWArmyMarchService.ShouldWaitForProviderRoute(pActor))
            {
                pActor.makeWait(0.1f);
                return BehResult.RepeatStep;
            }
            if (AWArmyMarchService.TryStartCompleteSharedRoute(pActor))
                return BehResult.RepeatStep;
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }

    public sealed class BehArmyRtsRetreatTarget : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!ArmyRtsControllerService.TryGetRetreatTarget(pActor,
                    out WorldTile target))
            {
                pActor?.makeWait(0.2f);
                return BehResult.RepeatStep;
            }
            if (pActor.current_tile?.isSameIsland(target) != true)
            {
                RequestArmyTaxi(pActor, target);
                pActor.makeWait(1f);
                return BehResult.RepeatStep;
            }
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }

        private static void RequestArmyTaxi(Actor pActor,
            WorldTile pTarget)
        {
            Army army = pActor?.army;
            int count;
            try { count = army?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = army.units[i]; }
                catch { continue; }
                if (actor?.data == null || actor.isRekt() ||
                    !actor.isAlive() || actor.is_inside_boat ||
                    actor.current_tile?.data == null ||
                    actor.current_tile.isSameIsland(pTarget) ||
                    TaxiManager.getRequestForActor(actor) != null) continue;
                TaxiManager.newRequest(actor, pTarget);
            }
            if (TaxiManager.getRequestForActor(pActor) == null &&
                pActor?.is_inside_boat != true)
                TaxiManager.newRequest(pActor, pTarget);
        }
    }

    public sealed class BehArmyRtsFormation : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!ArmyRtsControllerService.HasFollowerMission(pActor))
            {
                ArmyRtsControllerService.ReleaseActor(pActor);
                return BehResult.Stop;
            }
            if (ArmyRtsTransportService.TryGetTarget(pActor.army,
                    out WorldTile transportTarget) &&
                ArmyRtsTransportService.TryHandleActor(pActor,
                    transportTarget, pMayBegin: false))
            {
                pActor.makeWait(0.2f);
                return BehResult.Stop;
            }
            ArmyFollowerTargetResult targetResult =
                ArmyRtsControllerService.ResolveFollowerTarget(pActor,
                    out WorldTile target);
            if (targetResult == ArmyFollowerTargetResult.Unavailable ||
                targetResult == ArmyFollowerTargetResult.Hold)
            {
                pActor?.makeWait(0.15f);
                return BehResult.RepeatStep;
            }
            ArmyFollowerStepResult stepResult =
                AWArmyMarchService.TryStepFollowerDirect(pActor, target);
            if (ArmySharedPathRules.ShouldPreserveInFlightMovement(
                    stepResult, pActor?.is_moving == true))
            {
                pActor.timer_action = 0.1f;
                return BehResult.RepeatStep;
            }
            if (ArmySharedPathRules.ShouldUseLocalReconnect(stepResult))
            {
                pActor.beh_tile_target = target;
                return BehResult.Continue;
            }
            if (stepResult == ArmyFollowerStepResult.Stepped)
                return BehResult.RepeatStep;
            if (stepResult == ArmyFollowerStepResult.Hold)
            {
                pActor.makeWait(0.15f);
                return BehResult.RepeatStep;
            }
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }
}
