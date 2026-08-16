using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_BanditStrongholdZonePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(City), nameof(City.addZone))]
        internal static bool CityAddZonePrefix(City __instance,
            TileZone pZone)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                AW3MultiplayerReplicaScope.IsApplying) return true;
            return PeasantRebelBanditStrongholdService.CanAcquireZone(
                __instance, pZone);
        }
    }
}
