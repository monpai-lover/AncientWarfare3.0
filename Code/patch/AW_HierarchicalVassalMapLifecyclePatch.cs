using System;
using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Lifecycle-only invalidation hooks.  These hooks deliberately do not
    /// touch zones or build snapshots; they only advance the map-mode
    /// generation so the next label pass can rebuild once.
    /// </summary>
    [HarmonyPatch]
    internal static class AW_HierarchicalVassalMapLifecyclePatch
    {
        private static readonly string[] CityMethods =
        {
            "setKingdom", "joinAnotherKingdom", "addZone", "destroyCity"
        };

        private static readonly string[] KingdomMethods =
        {
            "newCivKingdom", "updateColor", "setKing",
            "isReadyForRemoval"
        };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            for (int index = 0; index < CityMethods.Length; index++)
            {
                MethodBase method = AccessTools.Method(typeof(City),
                    CityMethods[index]);
                if (method != null) yield return method;
            }
            for (int index = 0; index < KingdomMethods.Length; index++)
            {
                MethodBase method = AccessTools.Method(typeof(Kingdom),
                    KingdomMethods[index]);
                if (method != null) yield return method;
            }
        }

        private static void Postfix(object __instance)
        {
            try
            {
                if (__instance is City city)
                {
                    HierarchicalVassalMapModeService.MarkCityDirty(city);
                    if (city.kingdom != null)
                        HierarchicalVassalMapModeService.
                            MarkKingdomDirty(city.kingdom);
                    return;
                }
                if (__instance is Kingdom kingdom)
                {
                    HierarchicalVassalMapModeService.
                        MarkKingdomDirty(kingdom);
                    HierarchicalVassalMapModeService.
                        MarkHierarchyDirty();
                    return;
                }
                // Static kingdom creation has no __instance.  A hierarchy
                // generation bump is still enough to invalidate safely.
                HierarchicalVassalMapModeService.MarkHierarchyDirty();
            }
            catch { }
        }
    }
}
