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

        /// <summary>
        ///     共和国推举首领的候选门槛:成年在世男性平民(非奴隶/非现任君主/非在谱贵族),且属本系(Xia/伪officialdom)。
        ///     首领选举产生、不世袭,故取"平民"而非贵族;男性以过 setKing 性别闸。
        /// </summary>
        public static bool IsEligibleCommonerLeader(bool pInLineageSystem, bool pIsMale, bool pIsAdult,
            bool pIsAlive, bool pIsSlave, bool pIsKing, bool pIsNoble)
        {
            if (!pInLineageSystem || !pIsMale || !pIsAdult || !pIsAlive) return false;
            if (pIsSlave || pIsKing || pIsNoble) return false;
            return true;
        }

        /// <summary>新王被设立时:仅当他不是共和推举的首领时,才结束共和政体(恢复君主制)。</summary>
        public static bool ShouldClearRepublicOnNewKing(bool pNewKingIsRepublicLeader)
        {
            return !pNewKingIsRepublicLeader;
        }

        public static string SuffixForNameplate(bool pIsRepublic)
        {
            return pIsRepublic ? NameplateSuffix : "";
        }
    }
}
