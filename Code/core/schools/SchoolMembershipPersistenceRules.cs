using System;

namespace AncientWarfare3.core.schools
{
    public static class SchoolMembershipPersistenceRules
    {
        public static bool ReputationMatches(double pPersisted, float pRuntime)
        {
            if (double.IsNaN(pPersisted) || double.IsInfinity(pPersisted) ||
                float.IsNaN(pRuntime) || float.IsInfinity(pRuntime)) return false;
            return (float)pPersisted == pRuntime;
        }

        public static bool CanPersistPendingActor(bool pHasData, bool pAlive,
            bool pRekt, long pExpectedActorId, long pRuntimeActorId)
        {
            return pHasData && pAlive && !pRekt && pExpectedActorId >= 0 &&
                   pRuntimeActorId == pExpectedActorId;
        }
    }
}
