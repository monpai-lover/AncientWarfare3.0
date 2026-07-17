namespace AncientWarfare3.core.policy
{
    public sealed class RestorationInstitutionState
    {
        public string classState = "";
        public string armyState = "";
        public string nameState = "";
        public string enfeoffmentState = "";
        public float policyPoints;
        public float techPoints;
        public string currentPolicy = "";
        public float policyProgress;
        public string currentTech = "";
        public float techProgress;
        public string completedPolicies = "";
        public string completedTechs = "";
        public string completedDecisions = "";
        public string lockedNodes = "";
        public string currentDecision = "";
        public float decisionProgress;
        public string decisionQueue = "";
        public long coreFabricationCityId = -1L;
        public string coreFabricationCityName = "";
        public float coreFabricationProgress;
        public string coreFabricationQueue = "";
    }

    public static class RestorationInstitutionRules
    {
        public static RestorationInstitutionState SanitizeForRevival(
            RestorationInstitutionState pFallen)
        {
            if (pFallen == null) return null;
            return new RestorationInstitutionState
            {
                classState = SanitizeClassStateForRevival(pFallen.classState),
                armyState = pFallen.armyState ?? "",
                nameState = pFallen.nameState ?? "",
                enfeoffmentState = pFallen.enfeoffmentState ?? "",
                policyPoints = NonNegative(pFallen.policyPoints),
                techPoints = NonNegative(pFallen.techPoints),
                currentPolicy = pFallen.currentPolicy ?? "",
                policyProgress = NonNegative(pFallen.policyProgress),
                currentTech = pFallen.currentTech ?? "",
                techProgress = NonNegative(pFallen.techProgress),
                completedPolicies = pFallen.completedPolicies ?? "",
                completedTechs = pFallen.completedTechs ?? "",
                completedDecisions = pFallen.completedDecisions ?? "",
                lockedNodes = pFallen.lockedNodes ?? "",
                currentDecision = "",
                decisionProgress = 0f,
                decisionQueue = "",
                coreFabricationCityId = -1L,
                coreFabricationCityName = "",
                coreFabricationProgress = 0f,
                coreFabricationQueue = ""
            };
        }

        public static string SanitizeClassStateForRevival(string pClassState)
        {
            if (pClassState == "republic" || pClassState == "peasant_rebel")
                return "default";
            return pClassState ?? "";
        }

        private static float NonNegative(float pValue)
        {
            return pValue < 0f ? 0f : pValue;
        }
    }
}
