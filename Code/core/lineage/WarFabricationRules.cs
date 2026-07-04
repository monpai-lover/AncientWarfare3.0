namespace AncientWarfare3.core.lineage
{
    public static class WarFabricationRules
    {
        public static bool CanFabricate(bool pForeignCivilTarget, bool pTargetCityOwnedByTarget,
            bool pNeighboringCity, bool pBlockedByVassalRelation, out string pReason)
        {
            if (!pForeignCivilTarget)
            {
                pReason = "same_kingdom_or_invalid";
                return false;
            }

            if (!pTargetCityOwnedByTarget)
            {
                pReason = "target_city_invalid";
                return false;
            }

            if (pBlockedByVassalRelation)
            {
                pReason = "vassal_annex_by_decision";
                return false;
            }

            if (!pNeighboringCity)
            {
                pReason = "not_neighbor";
                return false;
            }

            pReason = "";
            return true;
        }
    }
}
