namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluPeaceGuard
    {
        public static bool BlocksOrdinarySettlement(War war)
        {
            bool active = false;
            try { active = war?.data != null && !war.hasEnded(); }
            catch { }
            return ZhuluWarRules.BlocksOrdinarySettlement(
                war?.getAsset()?.id ?? "", active);
        }

        public static string Reason(War war)
        {
            return BlocksOrdinarySettlement(war)
                ? ZhuluWarRules.SettlementBlockedReason
                : "";
        }
    }
}
