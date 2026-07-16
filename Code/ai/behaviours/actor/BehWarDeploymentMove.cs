using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehWarDeploymentMove : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!ArmyDeploymentService.TryPrepareMove(pActor, out WorldTile target))
            {
                try { pActor.ai?.setJob(pActor.getNextJob()); } catch { }
                return BehResult.Stop;
            }
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }

    public sealed class BehWarDeploymentArrive : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            ArmyDeploymentService.MarkArrival(pActor);
            return BehResult.Continue;
        }
    }
}
