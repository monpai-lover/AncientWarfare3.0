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
