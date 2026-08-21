using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    internal sealed class BehIslandEscapeTransport : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!IslandEscapeService.TryExecute(pActor,
                    out WorldTile moveTarget))
                return BehResult.Stop;
            if (moveTarget != null)
            {
                pActor.beh_tile_target = moveTarget;
                return BehResult.Continue;
            }
            pActor.makeWait(0.2f);
            return BehResult.RepeatStep;
        }
    }

    internal sealed class BehIslandEscapeArrival : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            IslandEscapeService.HandleArrival(pActor);
            return BehResult.Continue;
        }
    }
}
