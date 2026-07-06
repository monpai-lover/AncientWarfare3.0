using System;

namespace AncientWarfare3.core.policy
{
    public static class KingdomYearSchedulerRules
    {
        public static bool ShouldRunHeavySystem(int pYear, long pKingdomId, int pModulo, int pSlot)
        {
            if (pModulo <= 0) return true;
            int slot = Math.Abs(pSlot % pModulo);
            long raw = pYear + pKingdomId;
            int current = (int)(Math.Abs(raw) % pModulo);
            return current == slot;
        }
    }
}
