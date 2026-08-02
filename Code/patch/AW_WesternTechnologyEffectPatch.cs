using AncientWarfare3.core.policy;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_WesternTechnologyEffectPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BehMakeItem), nameof(BehMakeItem.execute))]
        private static void MakeAdditionalEquipment_Postfix(Actor pActor)
        {
            City city = pActor?.city;
            Kingdom kingdom = city?.kingdom;
            if (kingdom?.data == null || kingdom.isRekt()) return;

            int attempts = KingdomPolicyEffectService.Read(kingdom)
                .ExtraWorkshopAttempts;
            for (int i = 0; i < attempts; i++)
            {
                if (!ItemCrafting.tryToCraftRandomEquipment(pActor, city))
                    break;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ItemCrafting), nameof(ItemCrafting.craftItem))]
        private static void ImproveEquipmentQuality_Prefix(City pCity,
            ref int pTries)
        {
            Kingdom kingdom = pCity?.kingdom;
            if (kingdom?.data == null || kingdom.isRekt()) return;

            int bonus = KingdomPolicyEffectService.Read(kingdom)
                .EquipmentQualityBonus;
            if (bonus > 0) pTries += bonus;
        }
    }
}
