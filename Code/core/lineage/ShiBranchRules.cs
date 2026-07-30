using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct ShiBranchSeed
    {
        public readonly long ParentShiId;
        public readonly string ClanName;
        public readonly bool RequiresGeneratedClanName;

        public ShiBranchSeed(long pParentShiId, string pClanName, bool pRequiresGeneratedClanName)
        {
            ParentShiId = pParentShiId;
            ClanName = pClanName ?? "";
            RequiresGeneratedClanName = pRequiresGeneratedClanName;
        }
    }

    public readonly struct ShiBranchDisplayProjection
    {
        public readonly string BranchDisplay;
        public readonly string ParentDisplay;
        public readonly string RootDisplay;

        public ShiBranchDisplayProjection(string pBranchDisplay,
            string pParentDisplay, string pRootDisplay)
        {
            BranchDisplay = pBranchDisplay ?? "";
            ParentDisplay = pParentDisplay ?? "";
            RootDisplay = pRootDisplay ?? "";
        }
    }

    public static class ShiBranchRules
    {
        public static ShiBranchSeed ResolveSeed(long currentShiId, string currentClanName,
            string generatedClanName)
        {
            string current = (currentClanName ?? "").Trim();
            if (currentShiId >= 0 && current.Length > 0)
                return new ShiBranchSeed(currentShiId, current, false);

            string generated = (generatedClanName ?? "").Trim();
            return generated.Length > 0
                ? new ShiBranchSeed(-1, generated, false)
                : new ShiBranchSeed(-1, "", true);
        }

        public static string BuildDisplayName(string pOriginCityName, string pClanName)
        {
            string city = (pOriginCityName ?? "").Trim();
            string clan = (pClanName ?? "").Trim();
            if (clan.Length == 0) return "";
            return city + clan + "氏";
        }

        public static string BuildFeudatoryDisplayName(string pTitleName,
            string pParentClanName)
        {
            string feudatory = FeudatoryRules.BuildFeudatoryName(pTitleName);
            string clan = (pParentClanName ?? "").Trim();
            return clan.Length == 0 ? "" : feudatory + clan + "氏";
        }

        public static string BuildDisplayName(string pOriginCityName,
            string pClanName, string pSourceType, string pStateName)
        {
            return string.Equals(pSourceType, "feudatory",
                StringComparison.Ordinal)
                ? BuildFeudatoryDisplayName(pStateName, pClanName)
                : BuildDisplayName(pOriginCityName, pClanName);
        }

        public static ShiBranchDisplayProjection ResolveDisplayProjection(
            long currentShiId, long parentShiId, string currentDisplay,
            string parentDisplay, string rootDisplay)
        {
            string current = (currentDisplay ?? "").Trim();
            string parent = (parentDisplay ?? "").Trim();
            string root = (rootDisplay ?? "").Trim();
            bool isRoot = parentShiId < 0 || parentShiId == currentShiId;
            if (root.Length == 0) root = current;
            if (isRoot)
                return new ShiBranchDisplayProjection("", "", root);
            return new ShiBranchDisplayProjection(current, parent, root);
        }

        public static long[] TraceParents(long startShiId, Func<long, long> parentOf, int maxDepth)
        {
            if (startShiId < 0 || parentOf == null || maxDepth <= 0) return Array.Empty<long>();

            var result = new List<long>();
            var visited = new HashSet<long> { startShiId };
            long current = startShiId;
            for (int i = 0; i < maxDepth; i++)
            {
                long parent = parentOf(current);
                if (parent < 0 || !visited.Add(parent)) break;
                result.Add(parent);
                current = parent;
            }

            return result.ToArray();
        }
    }
}
