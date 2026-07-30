using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehFindWartimeGarrisonThreat :
        BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            Actor target = WartimeGarrisonService.FindThreatNearGarrison(
                pActor);
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
