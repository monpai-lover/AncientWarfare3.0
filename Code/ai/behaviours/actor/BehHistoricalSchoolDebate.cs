using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehHistoricalSchoolPrepareDebate : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!core.schools.HistoricalSchoolDebateActivityService.TryPrepareActor(pActor,
                    out WorldTile target)) return BehResult.Stop;
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }

    public sealed class BehHistoricalSchoolBeginDebate : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            return core.schools.HistoricalSchoolDebateActivityService.BeginDebateTask(pActor)
                ? forceTaskImmediate(pActor,
                    content.schools.HistoricalSchoolContent.DebateTaskId,
                    pClean: true, pForceAction: true)
                : BehResult.Stop;
        }
    }

    public sealed class BehHistoricalSchoolCompleteDebate : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            core.schools.HistoricalSchoolDebateActivityService.MarkActorReady(pActor);
            return BehResult.Stop;
        }
    }
}
