using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;
using ai.behaviours;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_XiaExpansionPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.canGrowZones))]
        private static void CanGrowZones_Postfix(City __instance,
            ref bool __result)
        {
            if (!__result || __instance?.data == null) return;
            try
            {
                __result = CityTechService.CanXiaCityGrowZones(__instance, __result);
            }
            catch
            {
                // Preserve the vanilla result if a city is being destroyed.
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehClaimZoneForCityActorBorder),
            nameof(BehClaimZoneForCityActorBorder.tryClaimZone))]
        private static bool ClaimZoneWithinTechCap_Prefix(Actor pActor,
            ref BehResult __result)
        {
            City city = pActor?.city;
            if (city?.data == null ||
                !LineageService.IsXiaKingdom(city.kingdom))
                return true;

            int allowance = CityTechService.GetXiaCityZoneAllowance(city);
            if (allowance == int.MaxValue) return true;
            int startingZoneCount = city.countZones();
            if (startingZoneCount >= allowance)
            {
                __result = BehResult.Stop;
                return false;
            }

            TileZone zone = pActor?.current_tile?.zone;
            WorldTile cityTile = city?.getTile();
            if (zone == null || cityTile == null ||
                !city.isZoneToClaimStillGood(pActor, zone, cityTile))
            {
                __result = BehResult.Stop;
                return false;
            }

            bool claimNeighbours =
                pActor.hasCultureTrait("expansionists") ||
                DebugConfig.isOn(DebugOption.CityFastZonesGrowth);
            bool claimedFromAnotherCity = zone.city != null && zone.city != city;
            city.addZone(zone);
            if (claimedFromAnotherCity) claimNeighbours = false;

            TileZone[] neighbours = claimNeighbours
                ? zone.neighbours_all
                : null;
            int vanillaBatchCount = 1;
            if (claimNeighbours)
            {
                foreach (TileZone neighbour in neighbours)
                {
                    if (IsVanillaNeighbourClaimable(city, pActor, neighbour,
                            cityTile))
                        vanillaBatchCount++;
                }
            }

            int allowedClaimCount =
                XiaExpansionDecisionRules.ClaimCountWithinZoneAllowance(
                    vanillaBatchCount, startingZoneCount, allowance);
            int neighbourClaimsRemaining = allowedClaimCount - 1;
            if (claimNeighbours && neighbourClaimsRemaining > 0)
            {
                foreach (TileZone neighbour in neighbours)
                {
                    if (!IsVanillaNeighbourClaimable(city, pActor, neighbour,
                            cityTile)) continue;
                    city.addZone(neighbour);
                    neighbourClaimsRemaining--;
                    if (neighbourClaimsRemaining == 0) break;
                }
            }
            pActor.addLoot(SimGlobals.m.coins_for_zone);
            __result = BehResult.Continue;
            return false;
        }

        private static bool IsVanillaNeighbourClaimable(City pCity,
            Actor pActor, TileZone pZone, WorldTile pCityTile)
        {
            return !pZone.hasCity() &&
                   pZone.centerTile.isSameIsland(pCityTile) &&
                   pCity.isZoneToClaimStillGood(pActor, pZone, pCityTile);
        }
    }
}
