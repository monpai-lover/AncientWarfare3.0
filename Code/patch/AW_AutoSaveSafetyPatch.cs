using System;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Prevents a malformed/legacy autosave metadata row from reaching the
    /// vanilla map-name grouping dictionary with a null key.
    /// </summary>
    [HarmonyPatch]
    internal static class AW_AutoSaveSafetyPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AutoSaveManager),
            nameof(AutoSaveManager.getAutoSaves))]
        private static void NormalizeAutoSaveNames_Postfix(
            ListPool<AutoSaveData> __result)
        {
            if (__result == null) return;

            try
            {
                for (int index = 0; index < __result.Count; index++)
                {
                    AutoSaveData data = __result[index];
                    if (data == null ||
                        !string.IsNullOrWhiteSpace(data.name)) continue;
                    data.name = "(unnamed map)";
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Autosave metadata name normalization failed: " +
                    error.Message);
            }
        }
    }
}
