using System;
using System.IO;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.startTheGame))]
    internal static class AW_RuntimeBenchmarkAutoLoadPatch
    {
        private static bool _configured;
        private static bool _dispatched;
        private static int _slot = -1;
        private static string _path = string.Empty;
        private static long _validationActorId = -1L;

        internal static void Initialize()
        {
            if (_configured) return;
            string configuredActor = Environment.GetEnvironmentVariable(
                RuntimePerformanceDiagnosticRules.
                    FamilyTreeActorEnvironmentVariable);
            RuntimePerformanceDiagnosticRules.TryResolveBenchmarkFamilyTreeActor(
                configuredActor, out _validationActorId);
            string configuredPath = Environment.GetEnvironmentVariable(
                RuntimePerformanceDiagnosticRules.AutoLoadPathEnvironmentVariable);
            if (RuntimePerformanceDiagnosticRules.TryResolveBenchmarkAutoLoadPath(
                    configuredPath, out _path))
            {
                _configured = true;
                MapBox.on_world_loaded += OnWorldLoaded;
                return;
            }
            string configured = Environment.GetEnvironmentVariable(
                RuntimePerformanceDiagnosticRules.AutoLoadSlotEnvironmentVariable);
            if (!RuntimePerformanceDiagnosticRules.TryResolveBenchmarkAutoLoadSlot(
                    configured, out _slot)) return;
            _configured = true;
            MapBox.on_world_loaded += OnWorldLoaded;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void StartTheGame_Prefix(ref bool pForceGenerate)
        {
            string configuredPath = Environment.GetEnvironmentVariable(
                RuntimePerformanceDiagnosticRules.AutoLoadPathEnvironmentVariable);
            bool hasPath = RuntimePerformanceDiagnosticRules.
                TryResolveBenchmarkAutoLoadPath(configuredPath, out _);
            string configured = Environment.GetEnvironmentVariable(
                RuntimePerformanceDiagnosticRules.AutoLoadSlotEnvironmentVariable);
            int slot = -1;
            bool hasSlot = !hasPath && RuntimePerformanceDiagnosticRules.
                TryResolveBenchmarkAutoLoadSlot(configured, out slot);
            if (!hasPath && !hasSlot) return;

            pForceGenerate = RuntimePerformanceDiagnosticRules.
                ResolveBenchmarkForceGenerate(pForceGenerate,
                    hasBenchmarkSlot: true);
            Config.load_new_map = false;
            Config.load_random_test_map = false;
            Config.load_dragon = false;
            Config.load_save_from_path = false;
            Config.load_test_map = false;
            Config.load_save_on_start = RuntimePerformanceDiagnosticRules.
                ResolveStockStartupSave(Config.load_save_on_start,
                    hasBenchmarkTarget: hasPath || hasSlot);
        }

        private static void OnWorldLoaded()
        {
            if (!RuntimePerformanceDiagnosticRules.ShouldDispatchBenchmarkAutoLoad(
                    _configured, _dispatched)) return;
            _dispatched = true;
            MapBox.on_world_loaded -= OnWorldLoaded;

            string path = string.IsNullOrEmpty(_path)
                ? SaveManager.getSlotSavePath(_slot)
                : _path;
            if (!Directory.Exists(path))
            {
                ModClass.LogWarning(
                    "AW3 benchmark auto-load directory is missing: " + path);
                return;
            }
            if (_validationActorId > 0L)
                MapBox.on_world_loaded += OnValidationWorldLoaded;
            LoadingScreen.TransitionAction load = () =>
                World.world?.save_manager?.loadWorld(path, false);
            ModClass.LogInfo("AW3 benchmark auto-load dispatch: " + path);
            if (World.world?.transition_screen != null)
                World.world.transition_screen.startTransition(load);
            else
                load();
        }

        private static void OnValidationWorldLoaded()
        {
            MapBox.on_world_loaded -= OnValidationWorldLoaded;
            if (_validationActorId <= 0L) return;
            try
            {
                FamilyTreeWindow.OpenFamilyTree(_validationActorId, -1L);
                ModClass.LogInfo(
                    "AW3 benchmark family-tree validation opened actor=" +
                    _validationActorId);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "AW3 benchmark family-tree validation failed: " + error);
            }
        }
    }
}
