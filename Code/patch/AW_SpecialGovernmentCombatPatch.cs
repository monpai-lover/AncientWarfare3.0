using System;
using HarmonyLib;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.patch
{
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
