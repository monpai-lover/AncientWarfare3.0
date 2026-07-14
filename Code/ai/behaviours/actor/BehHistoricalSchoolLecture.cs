using AncientWarfare3.core.schools;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehHistoricalSchoolPrepareLecture : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!HistoricalSchoolActivityQueue.TryPrepareLectureActor(pActor,
                    out WorldTile target)) return BehResult.Stop;
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }

    public sealed class BehHistoricalSchoolCompleteLecture : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            HistoricalSchoolActivityQueue.MarkLectureActorReady(pActor);
            return BehResult.Stop;
        }
    }
}
