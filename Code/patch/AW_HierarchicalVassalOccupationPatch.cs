using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(Zones), nameof(Zones.showKingdomZones))]
    internal static class AW_HierarchicalVassalOccupationPatch
    {
        [HarmonyPostfix]
        private static void ShowKingdomZonesPostfix(ref bool __result)
        {
            if (HierarchicalVassalMapModeService.IsActive())
                __result = true;
        }
    }
}
