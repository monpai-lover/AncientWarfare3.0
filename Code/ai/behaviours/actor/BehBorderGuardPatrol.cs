using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehBorderGuardPatrol : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            WorldTile tile = MandateBorderDefenseService.GetBorderGuardPatrolTile(pActor);
            if (tile == null)
            {
                MandateBorderDefenseService.WaitAfterBorderGuardPatrol(pActor);
                return BehResult.Stop;
            }

            if (tile == pActor.current_tile)
            {
                MandateBorderDefenseService.WaitAfterBorderGuardPatrol(pActor);
                return BehResult.Stop;
            }

            pActor.beh_tile_target = tile;
            return BehResult.Continue;
        }
    }
}
