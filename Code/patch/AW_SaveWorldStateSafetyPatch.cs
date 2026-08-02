using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    // SaveManager.currentWorldToSavedMap dereferences the item manager before
    // creating a SavedMap. During world clear/load that manager is transiently
    // absent; returning no snapshot is safer than entering the vanilla method
    // and spamming an error that can block the main loop.
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveWorldToDirectory))]
    internal static class AW_SaveWorldStateSafetyPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        private static bool Prefix(ref SavedMap __result)
        {
            try
            {
                MapBox world = World.world;
                if (SaveWorldSafetyRules.CanEnterSave(
                        worldPresent: world != null,
                        itemsPresent: world?.items != null)) return true;
            }
            catch { }

            __result = null;
            return false;
        }
    }
}
