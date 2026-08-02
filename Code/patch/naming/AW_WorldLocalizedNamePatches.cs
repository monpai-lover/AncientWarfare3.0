using AncientWarfare3.core.naming;
using HarmonyLib;

namespace AncientWarfare3.patch.naming
{
    [HarmonyPatch]
    internal static class AW_WorldLocalizedNamePatches
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(City), "generateName",
            new[] { typeof(Actor) })]
        private static void CityGenerateName_Postfix(City __instance,
            Actor pActor)
        {
            AWLocalizedNameService.ApplyCity(__instance, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Clan), nameof(Clan.newClan))]
        private static void ClanNewClan_Postfix(Clan __instance,
            Actor pFounder)
        {
            AWLocalizedNameService.ApplyClan(__instance, pFounder);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.newCivKingdom))]
        private static void KingdomNewCivKingdom_Postfix(Kingdom __instance,
            Actor pActor)
        {
            AWLocalizedNameService.ApplyKingdom(__instance, pActor);
            if (__instance?.data != null)
                AWLocalizedMottoService.ProjectKingdom(__instance,
                    __instance.data.motto);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.getMotto))]
        private static void KingdomGetMotto_Postfix(Kingdom __instance,
            ref string __result)
        {
            __result = AWLocalizedMottoService.ProjectKingdom(__instance,
                __result);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Clan), nameof(Clan.getMotto))]
        private static void ClanGetMotto_Postfix(Clan __instance,
            ref string __result)
        {
            __result = AWLocalizedMottoService.ProjectClan(__instance,
                __result);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Alliance), nameof(Alliance.getMotto))]
        private static void AllianceGetMotto_Postfix(Alliance __instance,
            ref string __result)
        {
            __result = AWLocalizedMottoService.ProjectAlliance(__instance,
                __result);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Culture), nameof(Culture.createCulture))]
        private static void CultureCreateCulture_Postfix(Culture __instance,
            Actor pActor)
        {
            AWLocalizedNameService.ApplyCulture(__instance, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Language), "generateName",
            new[] { typeof(Actor) })]
        private static void LanguageGenerateName_Postfix(Language __instance,
            Actor pActor)
        {
            AWLocalizedNameService.ApplyLanguage(__instance, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Religion), "generateName",
            new[] { typeof(Actor) })]
        private static void ReligionGenerateName_Postfix(Religion __instance,
            Actor pActor)
        {
            AWLocalizedNameService.ApplyReligion(__instance, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Subspecies), "generateName",
            new[] { typeof(ActorAsset), typeof(WorldTile) })]
        private static void SubspeciesGenerateName_Postfix(
            Subspecies __instance, ActorAsset pAsset)
        {
            AWLocalizedNameService.ApplySubspecies(__instance, pAsset);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logAllianceCreated))]
        private static void AllianceCreated_Prefix(Alliance pAlliance)
        {
            AWLocalizedNameService.ApplyAlliance(pAlliance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.newWar))]
        private static void WarNewWar_Postfix(War __result,
            WarTypeAsset pType)
        {
            AWLocalizedNameService.ApplyWar(__result, pType);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Book), nameof(Book.newBook))]
        private static void BookNewBook_Postfix(Book __instance,
            BookTypeAsset pBookType)
        {
            AWLocalizedNameService.ApplyBook(__instance, pBookType);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.generateItem))]
        private static void ItemGenerateItem_Postfix(Item __result,
            EquipmentAsset pItemAsset, Actor pActor)
        {
            AWLocalizedNameService.ApplyItem(__result, pItemAsset, pActor);
        }
    }
}
