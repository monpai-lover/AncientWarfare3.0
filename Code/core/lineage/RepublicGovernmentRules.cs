using AncientWarfare3.content.policies;

namespace AncientWarfare3.core.lineage
{
    public static class RepublicGovernmentRules
    {
        public const string NameplateSuffix = "\u5171\u548c\u56fd";

        public static bool ShouldBecomeRepublic(bool pIsCiv, bool pIsRekt, bool pHasKing,
            bool pHasMonarchyCandidate, bool pIsRebelGovernment)
        {
            return pIsCiv && !pIsRekt && !pHasKing && !pHasMonarchyCandidate && !pIsRebelGovernment;
        }

        public static bool IsRepublicClass(string pClassState)
        {
            return pClassState == KingdomPolicyDefs.ClassRepublic;
        }

        public static bool ShouldClearRepublic(bool pHasKing, bool pIsRepublic)
        {
            return pHasKing && pIsRepublic;
        }

        public static string SuffixForNameplate(bool pIsRepublic)
        {
            return pIsRepublic ? NameplateSuffix : "";
        }
    }
}
