using AncientWarfare3.content;
using HarmonyLib;
using NeoModLoader;
using NeoModLoader.api;
using System;
using System.Collections;
using System.Collections.Generic;
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
using AncientWarfare3.core.court;
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

        /// <summary>
        ///     故意不装载的补丁类。
        ///
        ///     这几个类因为缺少类级 <c>[HarmonyPatch]</c> 而**从未真正装载过**:
        ///     PatchClassProcessor 在类上没有该特性时直接返回、一个方法都不打,
        ///     而且不抛异常 —— 所以下面的循环照样打印 "Harmony patch OK"。
        ///     补上特性等于让它们第一次上线,风险不在"改动"而在"从零开始跑",
        ///     因此先在这里显式停用,实机验证过再逐个移出本表。
        ///
        ///     停用理由:
        ///     - <see cref="AW_DirtyMetaActorIndexPatch"/>:12 个 prefix 接管全部
        ///       meta manager 的 updateDirtyUnits(返回 true 就完全跳过原版)。
        ///       索引本身一直由 AWCooperativeWorldMaintenanceRunner 在建 ——
        ///       开销一直在付、收益一次没拿到 —— 接上是设计意图,但没跑过,
        ///       出错的表现会是亚种/家族/军队归属错乱,不易察觉。
        ///     - <see cref="AW_SpecialGovernmentCombatPatch"/>:给极热的
        ///       WorldLawAsset.isEnabled 加 prefix,性能与正确性都要实机确认。
        /// </summary>
        private static readonly HashSet<Type> DormantPatchTypes = new HashSet<Type>
        {
            typeof(AW_DirtyMetaActorIndexPatch),
            typeof(AW_SpecialGovernmentCombatPatch)
        };

        protected override void OnModLoad()
        {
            CustomCourtTemplatePathService.Initialize(
                GetDeclaration().FolderPath);
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
            AWAllocationProbe.Initialize();
            LogInfo("AW3 allocation probe: " + AWAllocationProbe.SourceName +
                    " (net_heap_source=" +
                    (AWAllocationProbe.IsNetHeapSource ? "yes" : "no") + ").");
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
            CourtLocalizationRepair.Ensure();
            CourtImmediateVacancyLocalization.Ensure();

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

                if (DormantPatchTypes.Contains(type))
                {
                    LogInfo("Harmony patch 显式停用(待实机验证): " +
                        type.FullName);
                    continue;
                }

                try
                {
                    var patched = harmony.CreateClassProcessor(type).Patch();
                    // Patch() 返回 null/空 = 一个方法都没打上。最常见的成因是类上
                    // 漏了 [HarmonyPatch] —— 而 PatchClassProcessor 对此不报错,
                    // 于是补丁静默失效、日志里却是一片 OK。这里必须显式喊出来。
                    //
                    // 但 [HarmonyPrepare] 返回 false 也会得到同样的空结果,那是
                    // **有意**关闭,不是事故。不排除它就会天天喊狼来了,告警很快
                    // 就没人看了。
                    if (patched != null && patched.Count > 0)
                        LogInfo("Harmony patch OK: " + type.FullName +
                            " (" + patched.Count + ")");
                    else if (IsPrepareDisabled(type))
                        LogInfo("Harmony patch 按 [HarmonyPrepare] 关闭: " +
                            type.FullName);
                    else
                        LogWarning("Harmony patch 未生效(类级 " +
                            "[HarmonyPatch] 缺失?): " + type.FullName);
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

        /// <summary>
        ///     这个类是否带一个返回 false 的 [HarmonyPrepare]。
        ///
        ///     Harmony 用 Prepare 做条件装载,返回 false 就整类跳过 —— 结果和
        ///     「类上漏了 [HarmonyPatch]」一样是空的 patch 列表,但性质相反:
        ///     一个是有意关闭,一个是事故。只按结果判断会把前者也报成告警。
        ///
        ///     只认无参、静态、返回 bool 的常量式 Prepare(读不到就当没有,
        ///     照常告警 —— 宁可多喊一次,不可漏掉真事故)。
        /// </summary>
        private static bool IsPrepareDisabled(Type pType)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Static;
                foreach (MethodInfo method in pType.GetMethods(flags))
                {
                    if (method.GetCustomAttributes(
                            typeof(HarmonyPrepare), true).Length == 0)
                        continue;
                    if (method.ReturnType != typeof(bool) ||
                        method.GetParameters().Length != 0)
                        continue;
                    if (!(bool)method.Invoke(null, null)) return true;
                }
            }
            catch (Exception)
            {
                // Prepare 有副作用或抛异常时不做判断,退回告警。
            }

            return false;
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
