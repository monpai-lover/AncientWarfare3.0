namespace AncientWarfare3.core.lineage
{
    public static class WarFabricationRules
    {
        public static bool CanFabricate(bool pForeignCivilTarget, bool pTargetCityOwnedByTarget,
            bool pNeighboringCity, bool pBlockedByVassalRelation, out string pReason)
        {
            return CanFabricateClaim(pForeignCivilTarget, pTargetCityOwnedByTarget, pNeighboringCity,
                pBlockedByVassalRelation, pExistingProject: false, out pReason);
        }

        public static bool CanFabricateClaim(bool pForeignCivilTarget, bool pTargetCityOwnedByTarget,
            bool pNeighboringCity, bool pBlockedByVassalRelation, bool pExistingProject, out string pReason)
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

            if (pExistingProject)
            {
                pReason = "project_exists";
                return false;
            }

            pReason = "";
            return true;
        }

        public static bool CanFabricateCore(bool pSourceValid, bool pTargetOwnCity, bool pAlreadyCore,
            bool pExistingProject, out string pReason)
        {
            if (!pSourceValid)
            {
                pReason = "source_invalid";
                return false;
            }

            if (!pTargetOwnCity)
            {
                pReason = "not_own_city";
                return false;
            }

            if (pAlreadyCore)
            {
                pReason = "already_core";
                return false;
            }

            if (pExistingProject)
            {
                pReason = "project_exists";
                return false;
            }

            pReason = "";
            return true;
        }

        public static bool CanExposeFabricationDecision(string pDecisionId, bool pHasCoreProjectTarget,
            bool pHasClaimProjectTarget)
        {
            switch (pDecisionId ?? "")
            {
                case "aw_decision_fabricate_core":
                    return pHasCoreProjectTarget;
                case "aw_decision_fabricate_weak_claim":
                case "aw_decision_fabricate_strong_claim":
                    return pHasClaimProjectTarget;
                default:
                    return false;
            }
        }
    }
}
