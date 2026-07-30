using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehWartimeGarrisonPatrol : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            WorldTile tile = WartimeGarrisonService.GetPatrolTile(pActor);
            if (tile == null || tile == pActor?.current_tile)
            {
                pActor?.makeWait(Randy.randomFloat(2f, 5f));
                return BehResult.Stop;
            }

            pActor.beh_tile_target = tile;
            return BehResult.Continue;
        }
    }
}
