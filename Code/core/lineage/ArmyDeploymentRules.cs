namespace AncientWarfare3.core.lineage
{
    public static class ArmyDeploymentRules
    {
        public const float OrdinaryReadyRatio = 0.70f;
        public const int MaxCitiesDiscoveredPerWorkItem = 8;
        public const int MaxArmiesReviewedPerWorkItem = 8;

        public static bool IsOrdinaryArmyReady(int pLivingWarriors, int pWarriorSlots)
        {
            if (pLivingWarriors <= 0 || pWarriorSlots <= 0) return false;
            return (float)pLivingWarriors / pWarriorSlots >= OrdinaryReadyRatio;
        }

        public static bool BlocksDeclarationGate(bool hasLivingWarriors, bool isRoyalGuard,
            bool ready, bool arrived)
        {
            if (!hasLivingWarriors || isRoyalGuard) return false;
            return !ready || !arrived;
        }

        public static bool ShouldResetAssignment(string currentSignature, string nextSignature,
            long currentCityId, long nextCityId)
        {
            return currentSignature != nextSignature || currentCityId != nextCityId;
        }

        public static bool ShouldClearForClosingNotice(string currentSignature, string closingSignature)
        {
            return !string.IsNullOrEmpty(closingSignature) && currentSignature == closingSignature;
        }

        public static bool ShouldMarkArmyArrived(bool actorArrived, bool actorIsCaptain)
        {
            return actorArrived && actorIsCaptain;
        }

        public static int CompareNoticePriority(
            int pLeftEarliestWarYear,
            int pLeftNoticeYear,
            string pLeftSignature,
            int pRightEarliestWarYear,
            int pRightNoticeYear,
            string pRightSignature)
        {
            int earliest = pLeftEarliestWarYear.CompareTo(pRightEarliestWarYear);
            if (earliest != 0) return earliest;
            int issued = pLeftNoticeYear.CompareTo(pRightNoticeYear);
            if (issued != 0) return issued;
            return string.CompareOrdinal(pLeftSignature ?? "", pRightSignature ?? "");
        }

        public static bool ShouldBypassPrewarDeployment(bool pDefenderAlreadyAtWar)
        {
            return pDefenderAlreadyAtWar;
        }
    }
}
