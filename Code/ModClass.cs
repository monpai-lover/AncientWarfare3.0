using AncientWarfare3.content;
using HarmonyLib;
using NeoModLoader;
using NeoModLoader.api;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.multiplayer;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.presentation;
using AncientWarfare3.patch;

namespace AncientWarfare3
{
    /// <summary>
    ///     AW3.0 模组入口。基于新版 WorldBox(0.51.0+)的 NeoModLoader BasicMod 框架。
    ///     功能按批次逐步添加:种族 → 特质 → 物品 → 建筑/单位 → 命名 → 天命 → 政策 → 附庸 → ...
    /// </summary>
    public class ModClass : BasicMod<ModClass>, IReloadable
    {
        public const string GUID = "ANCIENTWARFARE3";
        private const string NamingCollisionLogMessage =
            "AW3 integrated naming patches disabled: external Chinese Name mod detected.";
        private static readonly TimeSpan RuntimeShutdownTimeout =
            TimeSpan.FromSeconds(2);
        private static bool _namingCollisionLogWritten;
        private bool _runtimeShutdownComplete;

        protected override void OnModLoad()
        {
            MilitaryGovernorateAppearanceService.Initialize();
            PeasantRebelAppearanceService.Initialize();
            HierarchicalVassalMapFontSettings.InitializeConfig();
            AWFramePriorityGovernor.Initialize();
            AWSimulationTickBenchmark.Initialize();
            LogInfo("AW3 simulation scheduler: " +
                    AWPerformanceSettings.Mode.ToString().ToLowerInvariant() +
                    " (AW3 performance setting; native/large)." );
            LogInfo("AW3 Army RTS feature: " +
                    ArmyRtsRuntimeMode.LogName +
                    " (AW3_ENABLE_ARMY_RTS setting; restart required)." );
            LogInfo("AW3 Army RTS scheduler: " +
                    ArmyRtsSchedulingMode.Current.ToString().ToLowerInvariant() +
                    " (AW3_USE_AW3_ARMY_RTS_SCHEDULER setting; restart required)." );
            AWAsyncRuntime.Initialize();
            LogInfo("AW3 async features: db=" +
                    (AWAsyncRuntime.DatabaseEnabled ? "on" : "off") +
                    " ai=" + (AWAsyncRuntime.AiEnabled ? "on" : "off") +
                    " traversal=" +
                    (AWAsyncRuntime.TraversalEnabled ? "on" : "off") +
                    " ui=" + (AWAsyncRuntime.UiEnabled ? "on" : "off") +
                    " shadow=" +
                    (AWAsyncRuntime.ShadowEnabled ? "on" : "off") +
                    " (AW3 performance settings; restart required)." );
            LogInfo("AW3 pathfinding mode: " +
                    AWPathfindingRuntimeMode.LogName +
                    " (AW3_PATHFINDING; restart required).");
            AWPathfindingBootstrap.PrepareOwnership();
            // 注册 Harmony 补丁(扫描本程序集所有 [HarmonyPatch])
            PatchHarmonyByClass();
            AW_FramePrioritySchedulerPatch.SpecialPatch();
            AW_RuntimeBenchmarkAutoLoadPatch.Initialize();
            LogInfo("AW3 goTo Harmony owner active: " +
                    (PathfindingOwnershipService.HasAw3MovementPatch()
                        ? "yes"
                        : "no"));
            AWPathfindingBootstrap.AfterPatchesRegistered();
            AW3WorldLoadCoordinator.Initialize();

            // 通用夺舍工具:扫描 [MethodReplace] 用 Transpiler 重定向目标方法体(保留 Prefix/Postfix 链)
            utils.HarmonyTools.ReplaceMethods();

            AWNamingContent.Initialize(GetDeclaration().FolderPath);

            // 批A:夏朝 Xia 种族 / 王国 / 贴图
            XiaContent.Init();
            ZhuluWorldAgeContent.Init();
            XiaExpansionDecisionContent.Init();
            CivMonkeyNamingContent.Init();
            ArmyRtsContent.Init();

            XiaNaming.Init();

            // 神力:spawn_xia 生成夏人单位(必须在 AW_LineageTab 之前,因 tab 按钮按 id 查 power)
            GodPowerLibrary.Init();

            // 自定义 "aw_raw" tooltip type:动态文本(人名/生卒)原样显示不本地化,避免刷 missing text。
            ui.AW_RawTooltip.Init();

            // 阶段5:姓族 UI —— 自定义 tab + 入口按钮(窗口靠 Harmony patch + showWindow 打开)
            ui.AW_LineageTab.Init();
            AW_UiWorldAgeInfoPatch.SpecialPatch();

            LogInfo("Ancient Warfare 3.0 loaded — batch A (Xia race).");
        }

        private void PatchHarmonyByClass()
        {
            bool loadedModsConflict =
                DetectLoadedExternalChineseNameConflict();
            bool registryScanSucceeded =
                TryDetectRecognizedExternalChineseNameConflict(
                    out bool registryConflictDetected);
            bool disableIntegratedNamingPatches =
                AWNamingCollisionRules.ShouldDisableIntegratedNamingPatches(
                    loadedModsConflict, registryScanSucceeded,
                    registryConflictDetected);
            if (disableIntegratedNamingPatches)
                LogNamingCollisionOnce();

            var harmony = new Harmony(GUID);
            var patchTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(HasHarmonyPatch)
                .OrderBy(t => t.FullName);

            foreach (var type in patchTypes)
            {
                if (AWNamingCollisionRules.ShouldSkipHarmonyPatch(
                        type.Namespace, disableIntegratedNamingPatches))
                    continue;

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    LogInfo("Harmony patch OK: " + type.FullName);
                }
                catch (Exception e)
                {
                    LogWarning("Harmony patch FAIL: " + type.FullName);
                    LogWarning(e.ToString());
                    throw;
                }
            }
        }

        private bool DetectLoadedExternalChineseNameConflict()
        {
            try
            {
                return WorldBoxMod.LoadedMods.Any(pMod =>
                    AWNamingCollisionRules.IsRecognizedModConflict(
                        pMod?.GetDeclaration()?.UID, "LOADED"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryDetectRecognizedExternalChineseNameConflict(
            out bool pConflictDetected)
        {
            pConflictDetected = false;
            try
            {
                FieldInfo registryField = typeof(WorldBoxMod).GetField(
                    "AllRecognizedMods",
                    BindingFlags.Static | BindingFlags.NonPublic);
                IDictionary registry =
                    registryField?.GetValue(null) as IDictionary;
                if (registry == null)
                    return false;

                foreach (DictionaryEntry entry in registry)
                {
                    if (!(entry.Key is ModDeclare declaration))
                        continue;

                    if (AWNamingCollisionRules.IsRecognizedModConflict(
                            declaration.UID,
                            Convert.ToString(entry.Value)))
                        pConflictDetected = true;
                }

                return true;
            }
            catch (Exception)
            {
                pConflictDetected = false;
                return false;
            }
        }

        private static void LogNamingCollisionOnce()
        {
            if (_namingCollisionLogWritten)
                return;

            _namingCollisionLogWritten = true;
            LogWarning(NamingCollisionLogMessage);
        }

        private static bool HasHarmonyPatch(Type pType)
        {
            if (pType.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                return true;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.Instance;
            return pType.GetMethods(flags)
                .Any(m => m.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0);
        }

        private void OnApplicationQuit()
        {
            ShutdownRuntime(pPublishArmyRtsPlans: true);
        }

        private void OnDestroy()
        {
            ShutdownRuntime(pPublishArmyRtsPlans: false);
        }

        private void ShutdownRuntime(bool pPublishArmyRtsPlans)
        {
            if (_runtimeShutdownComplete) return;
            try
            {
                AWCooperativeSimulationRunner.Instance.Abort();
                AWAuthorityCycleService.Reset();
                ArmyRtsVisualizationService.Shutdown();
                ArmyMapInformationService.Shutdown();
                ArmyRtsAttackSpeechBubbleService.Shutdown();
                if (pPublishArmyRtsPlans) ArmyRtsPlanSnapshotService.Shutdown();
                else ArmyRtsPlanSnapshotService.DiscardAndShutdown();
                AWPathfindingBootstrap.ClearWorld();
                AW3WorldLoadCoordinator.Shutdown();
                if (!AWAsyncWorldLifecycle.TryShutdown(
                        RuntimeShutdownTimeout, out string error))
                {
                    LogWarning("AW3 async runtime shutdown failed: " + error);
                    return;
                }
                _runtimeShutdownComplete = true;
            }
            catch (Exception error)
            {
                LogWarning("AW3 runtime shutdown failed: " + error.Message);
            }
        }

        public void Reload()
        {
            AWNamingContent.Reload();
            CivMonkeyNamingContent.Init();
            XiaNaming.Init();
        }
    }
}
