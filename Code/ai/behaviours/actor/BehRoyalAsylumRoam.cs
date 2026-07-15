using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehRoyalAsylumRoam : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!RoyalAsylumService.TryGetRoamTile(pActor, out WorldTile target))
                return BehResult.Stop;
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }
}
