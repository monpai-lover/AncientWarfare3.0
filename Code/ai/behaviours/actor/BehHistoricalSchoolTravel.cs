using AncientWarfare3.core.schools;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehHistoricalSchoolTravel : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!HistoricalSchoolTravelService.TryPreparePhysicalTravel(pActor,
                    out WorldTile target)) return BehResult.Stop;
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }
}
