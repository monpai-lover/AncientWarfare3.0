namespace AncientWarfare3.core.lineage
{
    public static class RebellionDirectTerritoryTransferRules
    {
        public const string SettlementBlockedReason =
            "rebellion_uses_direct_territory_transfer";

        public static bool ShouldTransfer(bool pCityValid,
            bool pOwnerValid, bool pCapturerValid, bool pSameKingdom,
            bool pActiveWar, bool pOpposingSides,
            bool pAuthoritativeRebellion)
        {
            return pCityValid && pOwnerValid && pCapturerValid &&
                   !pSameKingdom && pActiveWar && pOpposingSides &&
                   pAuthoritativeRebellion;
        }

        public static bool BlocksOrdinarySettlement(bool pWarValid,
            bool pActiveWar, bool pAuthoritativeRebellion)
        {
            return pWarValid && pActiveWar && pAuthoritativeRebellion;
        }
    }
}
