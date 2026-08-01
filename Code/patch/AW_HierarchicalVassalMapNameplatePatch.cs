using AncientWarfare3.core.policy;
using HarmonyLib;
using System.Reflection;

namespace AncientWarfare3.patch
{
    // The hierarchical map mode owns its labels through the AW3 world-space
    // layer.  The vanilla manager indexes map_modes_nameplates by the active
    // MetaType and throws for 219 because this mode intentionally has no
    // vanilla NameplateAsset.
    [HarmonyPatch]
    internal static class AW_HierarchicalVassalMapNameplatePatch
    {
        private static bool _cleared;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NameplateManager), nameof(NameplateManager.update))]
        private static bool SkipVanillaNameplatesForHierarchicalMode(
            NameplateManager __instance)
        {
            try
            {
                if (!HierarchicalVassalMapModeService.IsActive())
                {
                    _cleared = false;
                    return true;
                }

                if (!_cleared)
                {
                    MethodInfo clearAll = AccessTools.Method(
                        typeof(NameplateManager), "clearAll");
                    clearAll?.Invoke(__instance, null);
                    _cleared = true;
                }
                return false;
            }
            catch
            {
                _cleared = true;
                // Keep the dedicated layer alive even if the optional
                // reflection cleanup is unavailable on a client build.
                return false;
            }
        }
    }
}
