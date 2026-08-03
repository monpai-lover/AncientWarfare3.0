using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class VisibleClanRenameRules
    {
        public static long ResolveTargetShiId(long pSnapshotShiId,
            long pNavigationFallbackShiId)
        {
            return pSnapshotShiId >= 0
                ? pSnapshotShiId
                : pNavigationFallbackShiId;
        }

        public static List<long> MergeMemberIds(long pFounderActorId,
            IEnumerable<long> pArchiveIds, IEnumerable<long> pLiveIds)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();
            AddUnique(result, seen, pFounderActorId);
            AddRange(result, seen, pArchiveIds);
            AddRange(result, seen, pLiveIds);
            return result;
        }

        public static bool TryNormalizeClanName(string pRaw, out string pClanName)
        {
            pClanName = (pRaw ?? "").Trim();
            while (pClanName.EndsWith("氏") && pClanName.Length > 0)
                pClanName = pClanName.Substring(0, pClanName.Length - 1).Trim();
            return !string.IsNullOrEmpty(pClanName);
        }

        public static List<long> CollectValidVisibleActorIds(IEnumerable<long> pIds)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();
            if (pIds == null) return result;

            foreach (long id in pIds)
            {
                if (id < 0 || !seen.Add(id)) continue;
                result.Add(id);
            }
            return result;
        }

        public static bool ShouldUpdateBranchName(bool pModeIsBigTree, long pShiId, int pVisibleActorCount)
        {
            return pModeIsBigTree && pShiId >= 0 && pVisibleActorCount > 0;
        }

        public static bool ShouldUseWholeShiTreeScope(bool pModeIsBigTree, long pShiId)
        {
            return pModeIsBigTree && pShiId >= 0;
        }

        private static void AddRange(List<long> pResult, HashSet<long> pSeen,
            IEnumerable<long> pIds)
        {
            if (pIds == null) return;
            foreach (long id in pIds) AddUnique(pResult, pSeen, id);
        }

        private static void AddUnique(List<long> pResult, HashSet<long> pSeen,
            long pActorId)
        {
            if (pActorId >= 0 && pSeen.Add(pActorId)) pResult.Add(pActorId);
        }
    }
}
