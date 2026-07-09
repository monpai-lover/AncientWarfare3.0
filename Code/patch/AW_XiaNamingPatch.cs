using AncientWarfare3.content;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_XiaNamingPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { "set_kingdom_name" })]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.newCivKingdom))]
        private static void Kingdom_NewCivKingdom_Postfix(Kingdom __instance, Actor pActor)
        {
            XiaNamingRepair.TryRenameKingdom(__instance, pActor, pForce: false);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(WindowMetaGeneric<Kingdom, KingdomData>), "loadNameInput")]
        private static void Kingdom_LoadNameInput_Prefix(WindowMetaGeneric<Kingdom, KingdomData> __instance)
        {
            XiaNamingRepair.TryRenameKingdom(__instance?.getMetaObject(), null, pForce: false);
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
