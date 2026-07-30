using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehWartimeGarrisonReinforce :
        BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            WorldTile tile = WartimeGarrisonService.GetDefenseTile(pActor);
            if (tile == null || tile == pActor?.current_tile)
                return BehResult.Stop;

            pActor.beh_tile_target = tile;
            return BehResult.Continue;
        }
    }
}
