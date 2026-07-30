using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_XiaNamingPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { "set_alliance_name" })]
        [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logAllianceCreated))]
        private static void WorldLog_LogAllianceCreated_Prefix(Alliance pAlliance)
        {
            if (pAlliance?.data == null) return;
            bool usesXiaName = false;
            try
            {
                foreach (Kingdom founder in pAlliance.kingdoms_list)
                {
                    if (!LineageService.IsXiaKingdom(founder)) continue;
                    usesXiaName = true;
                    break;
                }
            }
            catch { return; }
            if (!XiaAllianceNamingRules.ShouldFinalizeCreation(
                    usesXiaName, pAlliance.data.custom_name)) return;

            string name = XiaNamingRepair.GenerateAllianceName(pAlliance);
            bool valid = !XiaNameRepairRules.IsInvalidGeneratedMetaName(name);
            if (!XiaAllianceNamingRules.ShouldRenameAfterCreation(usesXiaName, valid)) return;
            pAlliance.setName(name, pTrack: false);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { "set_kingdom_name" })]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.newCivKingdom))]
        private static void Kingdom_NewCivKingdom_Postfix(Kingdom __instance, Actor pActor)
        {
            XiaNamingRepair.TryRenameKingdom(__instance, pActor, pForce: false);
            RulerAppellationService.RefreshLivingProjection(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { "set_lang_name" })]
        [HarmonyPatch(typeof(Language), "generateName")]
        private static void Language_GenerateName_Postfix(Language __instance, Actor pActor)
        {
            XiaNamingRepair.TryRenameLanguage(__instance, pActor, pForce: true);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { "set_religion_name" })]
        [HarmonyPatch(typeof(Religion), "generateName")]
        private static void Religion_GenerateName_Postfix(Religion __instance, Actor pActor)
        {
            XiaNamingRepair.TryRenameReligion(__instance, pActor, pForce: true);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { "set_subspecies_name" })]
        [HarmonyPatch(typeof(Subspecies), "generateName", new[] { typeof(ActorAsset), typeof(WorldTile) })]
        private static void Subspecies_GenerateName_Postfix(Subspecies __instance, ActorAsset pAsset)
        {
            XiaNamingRepair.TryRenameSubspecies(__instance, pAsset, pForce: true);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { "set_culture_name" })]
        [HarmonyPatch(typeof(Culture), nameof(Culture.createCulture))]
        private static void Culture_CreateCulture_Postfix(Culture __instance, Actor pActor)
        {
            XiaNamingRepair.TryRenameCulture(__instance, pActor, pForce: true);
        }
    }
}
