namespace AncientWarfare3.core.lineage
{
    public static class PeasantRebelBanditAmnestyRules
    {
        public static bool CanAccept(bool bandit, bool strongholdActive,
            bool originValid, bool offeringIsOrigin, bool authoritative,
            bool applying)
        {
            return bandit && strongholdActive && originValid &&
                   offeringIsOrigin && authoritative && !applying;
        }

        public static bool ShouldEndWars(bool accepted)
        {
            return accepted;
        }

        public static string ResolveSettlementClass(bool accepted)
        {
            return accepted ? "default" : "";
        }

        public static string ResolveFailureKey(bool bandit,
            bool strongholdActive, bool originValid,
            bool offeringIsOrigin)
        {
            if (!bandit || !strongholdActive) return "not_bandit_stronghold";
            if (!originValid) return "origin_missing";
            if (!offeringIsOrigin) return "only_origin_may_amnesty";
            return "amnesty_unavailable";
        }
    }
}
