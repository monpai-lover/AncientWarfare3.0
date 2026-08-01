using System;
using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(WorldAgesWindow), "Awake")]
    internal static class AW_ZhuluWorldAgeAwakePatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            ZhuluWorldAgeContent.Init();
        }
    }

    [HarmonyPatch(typeof(WorldAgesWindow), "OnEnable")]
    internal static class AW_ZhuluWorldAgeEnablePatch
    {
        private static bool _warningWritten;

        [HarmonyPrefix]
        private static void Prefix(WorldAgesWindow __instance)
        {
            ZhuluWorldAgeContent.Init();
            EnsureWindowButton(__instance);
        }

        private static void EnsureWindowButton(WorldAgesWindow window)
        {
            if (window == null) return;
            WorldAgeAsset age = AssetManager.era_library?.get(
                ZhuluAgeRules.AgeId);
            if (age == null) return;
            try
            {
                FieldInfo buttonsField = AccessTools.Field(
                    typeof(WorldAgesWindow), "_buttons");
                MethodInfo initButton = AccessTools.Method(
                    typeof(WorldAgesWindow), "initButton");
                var buttons = buttonsField?.GetValue(window) as
                    Dictionary<WorldAgeAsset, WorldAgeButton>;
                if (buttons == null || buttons.ContainsKey(age) ||
                    initButton == null) return;
                var button = initButton.Invoke(window,
                    new object[] { age }) as WorldAgeButton;
                if (button != null) buttons.Add(age, button);
            }
            catch (Exception exception)
            {
                if (_warningWritten) return;
                _warningWritten = true;
                ModClass.LogWarning(
                    "Zhulu age window button repair failed: " +
                    exception.Message);
            }
        }
    }
}
