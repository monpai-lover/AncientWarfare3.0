using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehCatchTargetAsSlave : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            Actor target = pActor.beh_actor_target?.a;
            pActor.beh_actor_target = null;

            if (!SlaveService.CaptureTargetAsSlave(pActor, target))
            {
                SlaveService.WaitAfterSlaveCaptureFailure(pActor);
                return BehResult.RestartTask;
            }

            SlaveService.WaitAfterSlaveCaptureSuccess(pActor);
            return BehResult.Continue;
        }
    }
}
