using AncientWarfare3.core.policy;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehCivicLeaderClaimArrival :
        BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (pActor?.city?.data == null)
                return BehResult.Stop;
            if (!CivicLeaderLandClaimService.IsCivicLeader(pActor))
                return BehResult.Continue;
            return CivicLeaderLandClaimService.IsValidArrival(pActor)
                ? BehResult.Continue
                : BehResult.Stop;
        }
    }
}
