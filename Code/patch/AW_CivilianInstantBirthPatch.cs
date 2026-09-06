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
                !(__instance is Actor mother)) return true;

            // Synthetic actors must be rejected before vanilla adds pregnancy.
            if (SyntheticLevyService.IsSynthetic(mother)) return false;
            if (!FamilyExpansionService.ShouldDeliverCivilianImmediately(
                    mother, out Actor father))
                return SyntheticLevyService.IsSynthetic(father) ? false : true;
            if (SyntheticLevyService.IsSynthetic(father)) return false;
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
