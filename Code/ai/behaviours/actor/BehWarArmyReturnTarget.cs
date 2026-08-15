using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehWarArmyReturnTarget : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!WarArmyReturnService.TryGetTarget(pActor,
                    out WorldTile target))
            {
                pActor?.makeWait(0.2f);
                return BehResult.RepeatStep;
            }
            if (WarArmyReturnService.TryHandleTransport(pActor, target))
            {
                pActor.makeWait(0.5f);
                return BehResult.RepeatStep;
            }
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }
}
