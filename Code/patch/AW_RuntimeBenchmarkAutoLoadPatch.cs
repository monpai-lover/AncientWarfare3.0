using System;
using System.IO;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_RuntimeBenchmarkAutoLoadPatch
    {
        private static readonly TimeSpan AutoLoadTimeout =
            TimeSpan.FromMinutes(2);

        private static bool _configured;
        private static bool _dispatched;
        private static bool _waitingForBenchmarkWorld;
        private static int _slot = -1;
        private static string _path = string.Empty;
        private static string _pendingPath = string.Empty;
        private static long _pendingDeadlineUtcTicks;
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
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.startTheGame))]
        private static void StartTheGame_Prefix(ref bool pForceGenerate)
        {
            if (_dispatched) return;
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
                    "AW3 benchmark auto-load invalid: directory is missing " +
                    "path=" + path);
                return;
            }

            _pendingPath = Path.GetFullPath(path);
            _pendingDeadlineUtcTicks =
                DateTime.UtcNow.Add(AutoLoadTimeout).Ticks;
            _waitingForBenchmarkWorld = true;
            MapBox.on_world_loaded -= OnBenchmarkWorldLoaded;
            MapBox.on_world_loaded += OnBenchmarkWorldLoaded;
            LoadingScreen.TransitionAction load = () => LoadBenchmarkWorld(path);
            ModClass.LogInfo("AW3 benchmark auto-load dispatch: " + path);
            try
            {
                if (World.world?.transition_screen != null)
                    World.world.transition_screen.startTransition(load);
                else
                    load();
            }
            catch (Exception error)
            {
                FailBenchmarkAutoLoad("dispatch failed", error.ToString());
            }
        }

        private static void LoadBenchmarkWorld(string path)
        {
            try
            {
                SaveManager saveManager = World.world?.save_manager;
                if (saveManager == null)
                {
                    FailBenchmarkAutoLoad("load manager is missing", path);
                    return;
                }
                saveManager.loadWorld(path, false);
            }
            catch (Exception error)
            {
                FailBenchmarkAutoLoad("load invocation failed", error.ToString());
            }
        }

        private static void OnBenchmarkWorldLoaded()
        {
            long nowUtcTicks = DateTime.UtcNow.Ticks;
            if (RuntimePerformanceDiagnosticRules.
                HasBenchmarkAutoLoadTimedOut(_waitingForBenchmarkWorld,
                    nowUtcTicks, _pendingDeadlineUtcTicks))
            {
                FailBenchmarkAutoLoad("timed out",
                    "path=" + _pendingPath);
                return;
            }

            string expectedPath = _pendingPath;
            if (!AW3SaveDirectoryRegistry.TryGet(out string loadedPath))
            {
                StopWaitingForBenchmarkWorld();
                ModClass.LogWarning(
                    "AW3 benchmark auto-load invalid: completed world has no " +
                    "save directory expected=" + expectedPath);
                return;
            }
            if (!IsSameBenchmarkPath(_pendingPath, loadedPath))
            {
                StopWaitingForBenchmarkWorld();
                ModClass.LogWarning(
                    "AW3 benchmark auto-load invalid: unexpected world loaded " +
                    "expected=" + expectedPath + " actual=" + loadedPath);
                return;
            }

            StopWaitingForBenchmarkWorld();
            Config.paused = false;
            try
            {
                Config.setWorldSpeed("x20");
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "AW3 benchmark auto-load invalid: failed to select x20 " +
                    error);
                return;
            }

            bool selectedX20 = Config.time_scale_asset != null &&
                               Config.time_scale_asset.id == "x20";
            float multiplier = Config.time_scale_asset?.multiplier ?? 0f;
            if (!selectedX20 || Math.Abs(multiplier - 20f) > 0.001f)
            {
                string speed = Config.time_scale_asset?.id ?? "missing";
                ModClass.LogWarning(
                    "AW3 benchmark auto-load invalid: x20 was not selected " +
                    "speed=" + speed + " multiplier=" + multiplier);
                return;
            }

            ModClass.LogInfo(
                "AW3 benchmark auto-load ready: speed=x20 multiplier=20 " +
                "path=" + loadedPath);
            OpenValidationFamilyTree();
        }

        private static bool IsSameBenchmarkPath(string expectedPath,
            string loadedPath)
        {
            if (!RuntimePerformanceDiagnosticRules.
                    TryResolveBenchmarkAutoLoadPath(expectedPath,
                        out string expected) ||
                !RuntimePerformanceDiagnosticRules.
                    TryResolveBenchmarkAutoLoadPath(loadedPath,
                        out string actual))
                return false;
            return string.Equals(expected, actual,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void FailBenchmarkAutoLoad(string reason, string detail)
        {
            StopWaitingForBenchmarkWorld();
            ModClass.LogWarning("AW3 benchmark auto-load invalid: " + reason +
                                " detail=" + detail);
        }

        private static void StopWaitingForBenchmarkWorld()
        {
            _waitingForBenchmarkWorld = false;
            MapBox.on_world_loaded -= OnBenchmarkWorldLoaded;
            _pendingPath = string.Empty;
            _pendingDeadlineUtcTicks = 0L;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void CheckBenchmarkAutoLoadTimeout_Postfix()
        {
            CheckPendingBenchmarkAutoLoadTimeout();
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static Exception CheckBenchmarkAutoLoadTimeout_Finalizer(Exception __exception)
        {
            if (__exception != null)
                CheckPendingBenchmarkAutoLoadTimeout();
            return __exception;
        }

        private static void CheckPendingBenchmarkAutoLoadTimeout()
        {
            if (!_waitingForBenchmarkWorld) return;
            long nowUtcTicks = DateTime.UtcNow.Ticks;
            if (!RuntimePerformanceDiagnosticRules.
                    HasBenchmarkAutoLoadTimedOut(_waitingForBenchmarkWorld,
                        nowUtcTicks, _pendingDeadlineUtcTicks))
                return;
            FailBenchmarkAutoLoad("timed out", "path=" + _pendingPath);
        }

        private static void OpenValidationFamilyTree()
        {
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
