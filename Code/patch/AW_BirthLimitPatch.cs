using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_BirthLimitPatch
    {
        private const int KING_CHILD_CAP = 8;
        private const int LEADER_CHILD_CAP = 6;
        private const int NOBLE_CHILD_CAP = 4;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BabyMaker), nameof(BabyMaker.makeBabiesViaSexual))]
        public static bool MakeBabiesViaSexual_Prefix(Actor pMotherTarget, Actor pParentA, Actor pParentB)
        {
            return !ShouldBlockBirth(pMotherTarget, pParentA, pParentB);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BabyMaker), nameof(BabyMaker.makeBabyFromPregnancy))]
        public static bool MakeBabyFromPregnancy_Prefix(Actor pActor)
        {
            return !ShouldBlockBirth(pActor, pActor?.lover);
        }

        private static bool ShouldBlockBirth(params Actor[] pParents)
        {
            if (pParents == null) return false;
            foreach (Actor parent in pParents)
                if (IsAtBirthCap(parent))
                    return true;
            return false;
        }

        private static bool IsAtBirthCap(Actor pActor)
        {
            int cap = GetChildCap(pActor);
            if (cap < 0) return false;
            return LineageQuery.CountKnownChildren(pActor) >= cap;
        }

        private static int GetChildCap(Actor pActor)
        {
            if (pActor?.data == null) return -1;
            if (!LineageService.IsXia(pActor)) return -1;
            if (pActor.isKing()) return KING_CHILD_CAP;
            if (pActor.isCityLeader()) return LEADER_CHILD_CAP;

            pActor.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
            if (isHeir) return LEADER_CHILD_CAP;

            pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            return status == LineageStatus.NOBLE ? NOBLE_CHILD_CAP : -1;
        }
    }
}
