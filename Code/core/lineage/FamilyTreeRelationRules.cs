using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class FamilyTreeRelationRules
    {
        public static bool ShouldBuildLiveLineageNode(bool isAlive, bool isXia, bool usesAwLineageSystem)
        {
            return isAlive && (isXia || usesAwLineageSystem);
        }

        public static bool ShouldUseReverseLiveParentLookup(int currentParentCount, bool hasLiveChild,
            bool requestedByUi)
        {
            return requestedByUi && hasLiveChild && currentParentCount < 2;
        }

        public static bool ShouldShowStatusInGenealogy(string pStatus)
        {
            return true;
        }

        // 氏族大谱(氏族大树)只显示男性;女性只在家族树里可见。status 钩子保留以后可扩展。
        public static bool ShouldShowInBigTree(int pSex, string pStatus)
        {
            if (pSex != 0) return false; // 非男性(女性)不进氏族大树
            return ShouldShowStatusInGenealogy(pStatus);
        }

        public static List<long> MergeRelationIds(params IEnumerable<long>[] pSources)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();
            if (pSources == null) return result;

            foreach (var source in pSources)
            {
                if (source == null) continue;
                foreach (long id in source)
                {
                    if (id < 0) continue;
                    if (seen.Add(id)) result.Add(id);
                }
            }

            return result;
        }

        public static (long slot1, long slot2) MergeParentSlots(
            long currentSlot1, long currentSlot2,
            long fallbackSlot1, long fallbackSlot2)
        {
            if (currentSlot1 >= 0 && currentSlot2 >= 0 && currentSlot1 != currentSlot2)
                return (currentSlot1, currentSlot2);

            var ids = MergeRelationIds(
                new[] { fallbackSlot1, fallbackSlot2 },
                new[] { currentSlot1, currentSlot2 });

            long slot1 = ids.Count > 0 ? ids[0] : -1;
            long slot2 = ids.Count > 1 ? ids[1] : -1;
            return (slot1, slot2);
        }
    }
}
