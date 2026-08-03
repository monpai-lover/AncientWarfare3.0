using AncientWarfare3.core.policy;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace AncientWarfare3.patch
{
    // The hierarchical map mode owns its labels through the AW3 world-space
    // layer.  The vanilla manager indexes map_modes_nameplates by the active
    // MetaType and throws for 219 because this mode intentionally has no
    // vanilla NameplateAsset.
    [HarmonyPatch]
    internal static class AW_HierarchicalVassalMapNameplatePatch
    {
        private static readonly MethodInfo ClearAll = AccessTools.Method(
            typeof(NameplateManager), "clearAll");
        private static NameplateManager _suppressedInstance;
        private static Canvas _suppressedCanvas;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NameplateManager), nameof(NameplateManager.update))]
        private static bool SkipVanillaNameplatesForHierarchicalMode(
            NameplateManager __instance)
        {
            try
            {
                if (!HierarchicalVassalMapModeService.IsActive())
                {
                    if (_suppressedCanvas != null)
                        _suppressedCanvas.enabled = true;
                    _suppressedCanvas = null;
                    _suppressedInstance = null;
                    return true;
                }

                if (!ReferenceEquals(_suppressedInstance, __instance))
                {
                    if (_suppressedCanvas != null)
                        _suppressedCanvas.enabled = true;
                    _suppressedInstance = __instance;
                    _suppressedCanvas = __instance.GetComponent<Canvas>();
                    ClearAll?.Invoke(__instance, null);
                }
                if (_suppressedCanvas != null)
                    _suppressedCanvas.enabled = false;
                return false;
            }
            catch
            {
                // Keep the dedicated layer alive even if the optional
                // reflection cleanup is unavailable on a client build.
                return false;
            }
        }
    }
}
