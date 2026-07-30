using System;

namespace AncientWarfare3.core.lineage
{
    public static class DynasticTitleRules
    {
        public static bool ShouldMarkUnresolvedAdultRoyalProbeAsProcessed(
            bool adult, bool royalChild, bool processed,
            bool foundCurrentEmperorParent)
        {
            return adult && !royalChild && !processed &&
                   !foundCurrentEmperorParent;
        }

        public static string Resolve(string pFormalTitle,
            string pFeudatoryName, bool activePrince,
            bool feudatorySuccessor, bool princeChild, bool royalChild,
            bool male, bool adult, string historicalTitle)
        {
            string formal = (pFormalTitle ?? "").Trim();
            string historical = (historicalTitle ?? "").Trim();
            if (historical.Length > 0) return historical;
            if (activePrince)
            {
                if (formal.Length > 0) return formal;
                string name = (pFeudatoryName ?? "").Trim();
                if (name.EndsWith("藩", StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - 1).Trim();
                return name.Length == 0 ? "王" : name + "王";
            }
            if (feudatorySuccessor) return "世子";
            if (princeChild) return male ? "王子" : "郡主";
            if (royalChild && !adult) return male ? "皇子" : "公主";
            return formal;
        }
    }
}
