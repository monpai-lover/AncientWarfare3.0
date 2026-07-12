namespace AncientWarfare3.core.policy
{
    public static class MapModeMetaCacheRules
    {
        public static bool IsDynamicMetaKey(string pKey)
        {
            if (string.IsNullOrEmpty(pKey)) return false;
            return pKey.StartsWith("Tech:") ||
                   pKey.StartsWith("Development:") ||
                   pKey.StartsWith("Vassal:") ||
                   pKey.StartsWith("WarCore:") ||
                   pKey.StartsWith("WarClaim:") ||
                   pKey.StartsWith("MandateDynasty:") ||
                   pKey.StartsWith("MandateCore:") ||
                   pKey.StartsWith("aw3_tech_map:") ||
                   pKey.StartsWith("aw3_development_map:") ||
                   pKey.StartsWith("aw3_vassal_map:") ||
                   pKey.StartsWith("aw3_war_core_map:") ||
                   pKey.StartsWith("aw3_war_claim_map:") ||
                   pKey.StartsWith("aw3_mandate_dynasty_map:") ||
                   pKey.StartsWith("aw3_mandate_core_map:") ||
                   pKey.StartsWith("aw3_school_map:") ||
                   pKey.StartsWith("210:") ||
                   pKey.StartsWith("211:") ||
                   pKey.StartsWith("212:") ||
                   pKey.StartsWith("213:") ||
                   pKey.StartsWith("214:") ||
                   pKey.StartsWith("215:") ||
                   pKey.StartsWith("216:") ||
                   pKey.StartsWith("217:");
        }

        public static bool ShouldClearForWorldSwitch(bool pHadAnyDynamicMeta)
        {
            return pHadAnyDynamicMeta;
        }
    }
}
