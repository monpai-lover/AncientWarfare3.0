namespace AncientWarfare3.core.lineage
{
    public static class SuccessionDisputeDisplayRules
    {
        public static string CanonicalNameForLiveCommit(
            string pCanonicalName)
        {
            return pCanonicalName?.Trim() ?? "";
        }

        public static bool NeedsLiveNameCommit(string pCurrent,
            string pProjected)
        {
            return !string.IsNullOrWhiteSpace(pProjected) &&
                   !string.Equals(pCurrent?.Trim(), pProjected.Trim(),
                       System.StringComparison.Ordinal);
        }

        public static bool HasPersistedClosingQualifier(
            SuccessionDisputeStatus pStatus, long rivalKingdomId)
        {
            return pStatus >= SuccessionDisputeStatus.RivalCreated &&
                   pStatus != SuccessionDisputeStatus.Closed &&
                   rivalKingdomId >= 0L;
        }

        public static string BuildQualifiedName(string pCanonicalName,
            string pQualifierId, bool active, string language)
        {
            string canonical = pCanonicalName ?? "";
            if (!active || string.IsNullOrEmpty(pQualifierId))
                return canonical;
            if (language == "en")
            {
                string adjective = EnglishQualifier(pQualifierId);
                return string.IsNullOrEmpty(adjective)
                    ? canonical
                    : adjective + " " + canonical;
            }
            string prefix = ChineseQualifier(pQualifierId,
                traditional: language == "zh-tw" || language == "tw");
            return string.IsNullOrEmpty(prefix)
                ? canonical
                : prefix + canonical;
        }

        public static (string Original, string Rival) BuildDistinctPair(
            string pCanonicalName, string pOriginalQualifierId,
            string pRivalQualifierId, bool active, string language)
        {
            return (BuildQualifiedName(pCanonicalName,
                    pOriginalQualifierId, active, language),
                BuildQualifiedName(pCanonicalName, pRivalQualifierId,
                    active, language));
        }

        private static string ChineseQualifier(string pQualifierId,
            bool traditional)
        {
            _ = traditional;
            return pQualifierId switch
            {
                "east" => "东",
                "west" => "西",
                "south" => "南",
                "north" => "北",
                "former" => "前",
                "later" => "后",
                _ => ""
            };
        }

        private static string EnglishQualifier(string pQualifierId)
        {
            return pQualifierId switch
            {
                "east" => "Eastern",
                "west" => "Western",
                "south" => "Southern",
                "north" => "Northern",
                "former" => "Former",
                "later" => "Later",
                _ => ""
            };
        }
    }
}
