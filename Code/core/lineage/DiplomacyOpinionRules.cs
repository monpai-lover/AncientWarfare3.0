using System;

namespace AncientWarfare3.core.lineage
{
    public static class DiplomacyOpinionRules
    {
        public static int Normalize(int rawTotal, int truceContribution,
            double lastWarEndedTime)
        {
            if (lastWarEndedTime > 0d || truceContribution <= 0)
                return rawTotal;
            return rawTotal - truceContribution;
        }
    }
}
