using System;

namespace AncientWarfare3.core.lineage
{
    public static class NobleIdentityRules
    {
        public static bool IsNobleIdentity(bool isCurrentRuler,
            int formalNobleRank, string lineageStatus)
        {
            return isCurrentRuler || formalNobleRank > 0 ||
                   string.Equals(lineageStatus, LineageStatus.NOBLE,
                       StringComparison.Ordinal);
        }
    }
}
