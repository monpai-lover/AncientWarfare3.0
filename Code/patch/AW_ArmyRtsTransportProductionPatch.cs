using System;
using AncientWarfare3.core.lineage;
using HarmonyLib;
using life.taxi;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ArmyRtsTransportProductionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildingAsset),
            nameof(BuildingAsset.getRandomBoatAssetToBuild))]
        private static bool GetRandomBoatAssetToBuild_Prefix(
            City pCity, ref ActorAsset __result)
        {
            if (!ArmyRtsTransportProductionService.HasDemand(pCity))
                return true;
            try
            {
                string transportId = pCity.getActorAsset()?.
                    architecture_asset?.actor_asset_id_transport;
                if (string.IsNullOrEmpty(transportId)) return true;
                ActorAsset transport = AssetManager.actor_library.get(
                    transportId);
                if (transport == null) return true;
                __result = transport;
                return false;
            }
            catch { return true; }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TaxiRequest), nameof(TaxiRequest.assign))]
        private static void Assign_Postfix(TaxiRequest __instance)
        {
            ArmyRtsTransportProductionService.OnAssigned(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TaxiRequest), nameof(TaxiRequest.cancel))]
        private static void Cancel_Prefix(TaxiRequest __instance)
        {
            ArmyRtsTransportProductionService.Cancel(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TaxiRequest), nameof(TaxiRequest.finish))]
        private static void Finish_Prefix(TaxiRequest __instance)
        {
            ArmyRtsTransportProductionService.Cancel(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TaxiRequest), nameof(TaxiRequest.clear))]
        private static void Clear_Prefix(TaxiRequest __instance)
        {
            ArmyRtsTransportProductionService.Cancel(__instance);
        }
    }
}
