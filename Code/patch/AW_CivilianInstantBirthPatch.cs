using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CivilianInstantBirthPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BaseSimObject), "addStatusEffect",
            new[] { typeof(string), typeof(float), typeof(bool) })]
        private static bool AddStatusEffect_Prefix(BaseSimObject __instance,
            string pID)
        {
            if (pID != "pregnant" ||
                AW_NobleHeirPregnancyPatch.IsNonSexualPregnancyScope ||
                AW3MultiplayerReplicaScope.IsReplicaSession ||
                !(__instance is Actor mother) ||
                !FamilyExpansionService.ShouldDeliverCivilianImmediately(
                    mother, out Actor father)) return true;

            // 合成兵不参与生育，跳过时仍然返回 false（拦截 pregnant 状态本身）。
            if (SyntheticLevyService.IsSynthetic(mother) ||
                SyntheticLevyService.IsSynthetic(father)) return false;
            mother.birthEvent();
            BabyMaker.makeBaby(mother, father, ActorSex.None,
                pCloneTraits: false, 0, null, pAddToFamily: true);
            float chance = .5f;
            int additionalBirthRolls = System.Math.Max(0,
                (int)mother.stats["birth_rate"]);
            for (int index = 0; index < additionalBirthRolls; index++)
            {
                if (!Randy.randomChance(chance)) break;
                BabyMaker.makeBaby(mother, father, ActorSex.None,
                    pCloneTraits: false, 0, null, pAddToFamily: true);
                chance *= .85f;
            }
            return false;
        }
    }
}
