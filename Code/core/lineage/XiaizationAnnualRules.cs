using System;

namespace AncientWarfare3.core.lineage
{
    public static class XiaizationAnnualRules
    {
        public const int FullyXiaizedLevel = 5;

        public static bool ShouldRunAnnualWork(bool isNativeXiaKingdom,
            int xiaizationLevel)
        {
            return !isNativeXiaKingdom &&
                   xiaizationLevel < FullyXiaizedLevel;
        }
    }
}
