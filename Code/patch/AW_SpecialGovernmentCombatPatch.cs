using System;
using HarmonyLib;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.patch
{
    // 类级 [HarmonyPatch] 必需:PatchClassProcessor 在类上没有这个特性时
    // 直接返回,方法级特性一个都不会被处理(且不报错)。本类长期缺这一行,
    // 所以下面的补丁从未执行过。
    //
    // ⚠ 目前本类仍在 ModClass.DormantPatchTypes 里被显式停用。要启用请从那张
    //   表里移除,并注意 AngryCivilianLaw_Prefix 挂在极热的
    //   WorldLawAsset.isEnabled 上,需要实机确认帧耗与判定都没问题。
    [HarmonyPatch]
    internal static class AW_SpecialGovernmentCombatPatch
    {
        [ThreadStatic] private static int _angryCivilianDepth;

        [HarmonyPatch(typeof(BaseSimObject), "canAttackTarget")]
        [HarmonyPrefix]
        private static void CanAttackTarget_Prefix(BaseSimObject __instance,
            BaseSimObject pTarget, out bool __state)
        {
            __state = false;
            try
            {
                if (__instance?.isActor() != true || pTarget?.isActor() != true)
                    return;
                if (!SpecialGovernmentWarParticipationService
                        .IsEligibleAngryInteraction(__instance.a, pTarget.a))
                    return;
                _angryCivilianDepth++;
                __state = true;
            }
            catch { }
        }

        [HarmonyPatch(typeof(BaseSimObject), "canAttackTarget")]
        [HarmonyFinalizer]
        private static Exception CanAttackTarget_Finalizer(
            Exception __exception, bool __state)
        {
            if (__state && _angryCivilianDepth > 0) _angryCivilianDepth--;
            return __exception;
        }

        [HarmonyPatch(typeof(WorldLawAsset), nameof(WorldLawAsset.isEnabled))]
        [HarmonyPrefix]
        private static bool AngryCivilianLaw_Prefix(WorldLawAsset __instance,
            ref bool __result)
        {
            if (_angryCivilianDepth <= 0 || __instance == null ||
                __instance != WorldLawLibrary.world_law_angry_civilians)
                return true;
            __result = true;
            return false;
        }
    }
}
