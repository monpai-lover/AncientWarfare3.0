using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehRoyalGuardAttackThreat : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            Actor target = pActor.beh_actor_target?.a;
            if (!RoyalGuardService.IsValidThreatForGuard(pActor, target))
            {
                pActor.beh_actor_target = null;
                return BehResult.Stop;
            }

            if (pActor.isInAttackRange(target))
                pActor.tryToAttack(target);

            return BehResult.Continue;
        }
    }
}
