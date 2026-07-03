using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehFindSlaveCaptureTarget : BehaviourActionActor
    {
        private const int SEARCH_RADIUS_TILES = 80;

        public override BehResult execute(Actor pActor)
        {
            Actor target = SlaveService.FindSlaveCaptureTarget(pActor, SEARCH_RADIUS_TILES);
            if (target == null)
            {
                pActor.beh_actor_target = null;
                return BehResult.Stop;
            }

            pActor.beh_actor_target = target;
            return BehResult.Continue;
        }
    }
}
