using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehFindSlaveCaptureTarget : BehaviourActionActor
    {
        private const int SEARCH_RADIUS_TILES = 80;

        public override BehResult execute(Actor pActor)
        {
            CaptureTargetSearchState state = SlaveService.FindSlaveCaptureTarget(
                pActor, SEARCH_RADIUS_TILES, out Actor target);
            if (state == CaptureTargetSearchState.Pending)
            {
                pActor.beh_actor_target = null;
                SlaveService.WaitAfterSlaveCapturePending(pActor);
                return BehResult.Stop;
            }
            if (state == CaptureTargetSearchState.Miss || target == null)
            {
                pActor.beh_actor_target = null;
                SlaveService.WaitAfterSlaveCaptureNoTarget(pActor);
                return BehResult.Stop;
            }

            pActor.beh_actor_target = target;
            return BehResult.Continue;
        }
    }
}
