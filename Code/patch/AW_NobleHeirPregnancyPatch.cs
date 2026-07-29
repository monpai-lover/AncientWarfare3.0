using System;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_NobleHeirPregnancyPatch
    {
        [ThreadStatic]
        private static int NonSexualPregnancyDepth;

        private readonly struct PregnancyStartState
        {
            public readonly Actor Mother;
            public readonly long FatherId;

            public PregnancyStartState(Actor pMother, long pFatherId)
            {
                Mother = pMother;
                FatherId = pFatherId;
            }

            public bool IsManaged => Mother?.data != null && FatherId >= 0L;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BaseSimObject), "addStatusEffect",
            new[] { typeof(string), typeof(float), typeof(bool) })]
        private static void AddStatusEffect_Prefix(BaseSimObject __instance,
            string pID, ref float pOverrideTimer,
            out PregnancyStartState __state)
        {
            __state = default;
            if (NonSexualPregnancyDepth > 0 || pID != "pregnant" ||
                !(__instance is Actor mother) ||
                !NobleHeirPregnancyService.TryPreparePregnancy(mother,
                    out long fatherId))
                return;

            pOverrideTimer = NobleHeirPregnancyRules.TenMonthPregnancySeconds;
            __state = new PregnancyStartState(mother, fatherId);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseSimObject), "addStatusEffect",
            new[] { typeof(string), typeof(float), typeof(bool) })]
        private static void AddStatusEffect_Postfix(bool __result,
            PregnancyStartState __state)
        {
            if (__result && __state.IsManaged)
                NobleHeirPregnancyService.OnPregnancyStarted(
                    __state.Mother, __state.FatherId);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BabyMaker),
            nameof(BabyMaker.makeBabyFromPregnancy))]
        private static void PregnancyDelivery_Postfix(Actor pActor)
        {
            NobleHeirPregnancyService.OnPregnancyDeliveryCompleted(pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.loadObject))]
        private static void ActorLoad_Postfix(Actor __result)
        {
            NobleHeirPregnancyService.OnActorLoaded(__result);
            DynasticMaleLineContinuityService.OnActorLoaded(__result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BabyMaker),
            nameof(BabyMaker.startMiracleBirth))]
        private static void MiracleBirth_Prefix()
        {
            NonSexualPregnancyDepth++;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(BabyMaker),
            nameof(BabyMaker.startMiracleBirth))]
        private static Exception MiracleBirth_Finalizer(Exception __exception)
        {
            ExitNonSexualPregnancyScope();
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BabyMaker),
            nameof(BabyMaker.startSoulborneBirth))]
        private static void SoulborneBirth_Prefix()
        {
            NonSexualPregnancyDepth++;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(BabyMaker),
            nameof(BabyMaker.startSoulborneBirth))]
        private static Exception SoulborneBirth_Finalizer(
            Exception __exception)
        {
            ExitNonSexualPregnancyScope();
            return __exception;
        }

        private static void ExitNonSexualPregnancyScope()
        {
            if (NonSexualPregnancyDepth > 0) NonSexualPregnancyDepth--;
        }
    }
}
