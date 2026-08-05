using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class KingdomPolicyInheritanceDiagnosticRules
    {
        public static string BuildKey(long pWorldGeneration, long pChildId,
            long pSourceId, string pState)
        {
            return pWorldGeneration + ":" + pChildId + ":" + pSourceId + ":" +
                   (pState ?? string.Empty);
        }

        public static bool ShouldLog(ISet<string> pEmitted, string pKey)
        {
            return pEmitted != null && !string.IsNullOrEmpty(pKey) &&
                   pEmitted.Add(pKey);
        }
    }
}
