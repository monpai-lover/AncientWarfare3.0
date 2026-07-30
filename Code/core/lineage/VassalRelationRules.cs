namespace AncientWarfare3.core.lineage
{
    public static class VassalRelationRules
    {
        public static bool CanSetVassal(bool pBasicValid, bool pVassalIsRebel, bool pSuzerainIsRebel,
            bool pSuzerainTitleAboveVassal, bool pCycleDetected, bool pDirectlyAdjacent, out string pReason)
        {
            if (!pBasicValid)
            {
                pReason = "invalid";
                return false;
            }

            if (pVassalIsRebel)
            {
                pReason = "rebel_no_vassal";
                return false;
            }

            if (pSuzerainIsRebel)
            {
                pReason = "rebel_no_suzerain";
                return false;
            }

            if (!pSuzerainTitleAboveVassal)
            {
                pReason = "title_too_low";
                return false;
            }

            if (pCycleDetected)
            {
                pReason = "cycle";
                return false;
            }

            if (!pDirectlyAdjacent)
            {
                pReason = "not_adjacent";
                return false;
            }

            pReason = "";
            return true;
        }

        public static bool CanAbsorbVassal(bool pBaseAllowed, bool pSuzerainIsRebel, bool pVassalIsRebel,
            out string pReason)
        {
            if (!pBaseAllowed)
            {
                pReason = "blocked";
                return false;
            }

            if (pSuzerainIsRebel)
            {
                pReason = "rebel_no_suzerain";
                return false;
            }

            if (pVassalIsRebel)
            {
                pReason = "rebel_no_vassal";
                return false;
            }

            pReason = "";
            return true;
        }

        public static bool CanSetTributary(bool pBasicValid,
            bool pTributaryIsRebel, bool pSuzerainIsRebel,
            bool pSuzerainIndependent, bool pTargetIndependent,
            bool pCycleDetected, bool pDirectlyAdjacent,
            out string pReason)
        {
            if (!pBasicValid)
            {
                pReason = "invalid";
                return false;
            }
            if (pTributaryIsRebel || pSuzerainIsRebel)
            {
                pReason = "rebel_blocked";
                return false;
            }
            if (!pSuzerainIndependent || !pTargetIndependent)
            {
                pReason = "subject_blocked";
                return false;
            }
            if (pCycleDetected)
            {
                pReason = "cycle";
                return false;
            }
            if (!pDirectlyAdjacent)
            {
                pReason = "not_adjacent";
                return false;
            }

            pReason = "";
            return true;
        }

        public static bool CanEnforceWarVictory(bool pBasicValid, bool pVassalIsRebel,
            bool pSuzerainIsRebel, bool pCycleDetected)
        {
            return pBasicValid && !pVassalIsRebel && !pSuzerainIsRebel && !pCycleDetected;
        }
    }
}
