using AncientWarfare3.core.schools;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehHistoricalSchoolEducationArrive :
        BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            HistoricalSchoolEducationJourneyService.
                TryCompletePhysicalArrival(pActor);
            return BehResult.Stop;
        }
    }
}
