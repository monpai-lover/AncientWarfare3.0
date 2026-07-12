using AncientWarfare3.core.schools;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehHistoricalSchoolArrive : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            HistoricalSchoolTravelService.TryCompletePhysicalArrival(pActor);
            return BehResult.Stop;
        }
    }
}
