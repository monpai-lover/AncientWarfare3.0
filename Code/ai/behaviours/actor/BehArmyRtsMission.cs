using AncientWarfare3.core.lineage;
using ai.behaviours;

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
            if (target == pActor?.current_tile)
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
            pActor.beh_tile_target = target;
            return BehResult.Continue;
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
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }
}
