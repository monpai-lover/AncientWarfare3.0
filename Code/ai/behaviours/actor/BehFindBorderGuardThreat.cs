using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehFindBorderGuardThreat : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            Actor target = MandateBorderDefenseService.FindThreatNearBorderGuard(pActor);
            if (target == null)
            {
                pActor.beh_actor_target = null;
                MandateBorderDefenseService.WaitAfterBorderGuardNoThreat(pActor);
                return BehResult.Stop;
            }

            pActor.beh_actor_target = target;
            return BehResult.Continue;
        }
    }
}
