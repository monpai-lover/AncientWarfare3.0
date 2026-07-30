using System;
using AncientWarfare3.ui;
using HarmonyLib;
using NeoModLoader.General;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(WindowCreator), nameof(WindowCreator.CreateEmptyWindow))]
    internal static class AW_WindowCreationPatch
    {
        [HarmonyPrefix]
        private static void CreateEmptyWindowPrefix(string pWindowID,
            out ScrollWindow __state)
        {
            __state = IsAwWindow(pWindowID)
                ? ScrollWindow.getCurrentWindow()
                : null;
        }

        [HarmonyPostfix]
        private static void CreateEmptyWindowPostfix(string pWindowID,
            ScrollWindow __state)
        {
            bool hadCurrentWindow = __state != null;
            bool currentRegistryCleared =
                ScrollWindow.getCurrentWindow() == null;
            bool previousWindowStillActive = hadCurrentWindow &&
                                             __state.gameObject.activeInHierarchy;
            if (!AWWindowCreationRules.ShouldRestoreCurrent(
                    IsAwWindow(pWindowID), hadCurrentWindow,
                    currentRegistryCleared, previousWindowStillActive))
                return;

            __state.setActive(true, pSkipAnimation: true);
        }

        private static bool IsAwWindow(string pWindowID)
        {
            return !string.IsNullOrEmpty(pWindowID) &&
                   pWindowID.StartsWith("aw_", StringComparison.Ordinal);
        }
    }
}
