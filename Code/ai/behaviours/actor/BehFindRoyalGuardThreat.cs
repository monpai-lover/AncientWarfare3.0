using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehFindRoyalGuardThreat : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            Actor target = RoyalGuardService.FindThreatNearKing(pActor);
            if (target == null)
            {
                pActor.beh_actor_target = null;
                RoyalGuardService.WaitAfterGuardNoThreat(pActor);
                return BehResult.Stop;
            }

            pActor.beh_actor_target = target;
            return BehResult.Continue;
        }
    }
}
