using System;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_ActorDeathPatch
    {
        internal static long DyingKingActorId = -1L;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die", new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        public static void Die_Prefix(Actor __instance, AttackType pType)
        {
            if (__instance?.data == null) return;
            if (!__instance.isAlive()) return;

            KingdomMilitaryReadinessService.MarkOrdinaryArmyActorDirty(__instance);
            WarNoticeService.QueueArmyChanged(__instance.kingdom, __instance.army);
            TemporaryLevyService.OnActorInvalidated(__instance);
            TemporarySlaveVanguardService.OnMemberInvalidated(__instance);
            SlavePopulationIndexService.Deactivate(__instance);

            TryRunDeathStage(__instance, "death cause", () =>
                EnsureDeathCause(__instance, pType));
            TryRunDeathStage(__instance, "ruler fact snapshot", () =>
                RulerTitleFactService.ArchivePersonalSnapshot(__instance));
            TryRunDeathStage(__instance, "historical figure death", () =>
            {
                if (__instance.hasTrait(content.figures.HistoricalFigureService.TRAIT_FIRST) ||
                    __instance.hasTrait(content.figures.HistoricalFigureService.TRAIT_FIGURE))
                    content.figures.HistoricalFigureService.OnFigureDied(__instance);
            });

            if (!TryEvaluateDeathStage(__instance, "lineage eligibility",
                    () => LineageService.UsesAwLineageSystem(__instance),
                    out bool usesAwLineage)) return;
            if (!usesAwLineage)
            {
                if (!TryEvaluateDeathStage(__instance, "traceable archive eligibility",
                        () => LineageService.HasTraceableArchive(__instance),
                        out bool hasTraceableArchive) || !hasTraceableArchive) return;
                TryRunDeathStage(__instance, "traceable actor archive", () =>
                    LineageService.ArchiveTraceableActor(__instance, pAlive: false));
                return;
            }

            TryRunDeathStage(__instance, "lineage actor archive", () =>
                LineageService.ArchiveActor(__instance, pAlive: false));

            bool dyingKing = false;
            Kingdom dyingKingdom = null;
            TryRunDeathStage(__instance, "king death context", () =>
            {
                if (__instance.isKing() && __instance.kingdom != null)
                {
                    dyingKing = true;
                    dyingKingdom = __instance.kingdom;
                }
            });

            if (dyingKing)
            {
                DyingKingActorId = __instance.data.id;
                TryRunDeathStage(__instance, "king succession preparation", () =>
                    HeirService.PrepareSuccessionBeforeKingDeath(dyingKingdom, __instance));
                TryRunDeathStage(__instance, "king death chronicle", () =>
                    ChronicleEvents.OnKingDied(dyingKingdom, __instance));
            }
            else
            {
                TryRunDeathStage(__instance, "former ruler death", () =>
                    PosthumousTitleService.OnFormerRulerDied(__instance));
            }

            TryRunDeathStage(__instance, "person death history", () =>
            {
                __instance.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
                if (lid >= 0)
                {
                    string name = __instance.getName();
                    __instance.data.get(LineageKeys.DEATH_CAUSE, out string cause, "");
                    string causeText = string.IsNullOrEmpty(cause) ? "" : "\uFF08\u6B7B\u56E0\uFF1A" + cause + "\uFF09";
                    Kingdom deathContext = PosthumousTitleService.ResolveCapturedRulerLiveKingdom(__instance) ??
                                           __instance.kingdom;
                    HistoryWriter.RecordPerson(
                        __instance.data.id, deathContext, name,
                        PersonEvent.DEATH,
                        HistoryText.Actor(__instance, name) + " \u901D\u4E16" + causeText,
                        ChronicleCategory.LIFE);
                }
            });

            TryRunDeathStage(__instance, "bond death", () =>
                ChronicleEvents.OnBondDeath(__instance));
        }

        private static void TryRunDeathStage(Actor pActor, string pStage, Action pAction)
        {
            if (pAction == null) return;
            try
            {
                pAction();
            }
            catch (Exception error)
            {
                LogDeathStageFailure(pActor, pStage, error);
            }
        }

        private static bool TryEvaluateDeathStage(Actor pActor, string pStage,
            Func<bool> pAction, out bool pResult)
        {
            pResult = false;
            if (pAction == null) return false;
            try
            {
                pResult = pAction();
                return true;
            }
            catch (Exception error)
            {
                LogDeathStageFailure(pActor, pStage, error);
                return false;
            }
        }

        private static void LogDeathStageFailure(Actor pActor, string pStage, Exception pError)
        {
            try
            {
                ModClass.LogWarning("Actor death " + pStage + " failed for actor=" +
                                    (pActor?.data?.id ?? -1L) + ": " + pError.Message);
            }
            catch
            {
                // The engine death path must continue even if diagnostics fail.
            }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Actor), "die", new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        public static Exception Die_Finalizer(Actor __instance, Exception __exception)
        {
            if (__instance?.data != null && DyingKingActorId == __instance.data.id)
                DyingKingActorId = -1L;
            return __exception;
        }

        private static void EnsureDeathCause(Actor pActor, AttackType pType)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.DEATH_CAUSE, out string existing, "");
            if (!string.IsNullOrEmpty(existing)) return;
            pActor.data.set(LineageKeys.DEATH_CAUSE, DescribeDeathType(pType));
        }

        private static string DescribeDeathType(AttackType pType)
        {
            switch (pType)
            {
                case AttackType.Acid: return "\u9178\u8680\u800C\u6B7B";
                case AttackType.Fire: return "\u711A\u70E7\u800C\u6B7B";
                case AttackType.Age: return "\u81EA\u7136\u8001\u6B7B";
                case AttackType.Starvation: return "\u9965\u997F\u800C\u6B7B";
                case AttackType.Plague:
                case AttackType.Infection:
                case AttackType.Tumor:
                case AttackType.AshFever: return "\u75BE\u75C5\u800C\u6B7B";
                case AttackType.Eaten: return "\u88AB\u541E\u98DF";
                case AttackType.Weapon: return "\u6218\u6597\u8EAB\u4EA1";
                case AttackType.Poison: return "\u4E2D\u6BD2\u800C\u6B7B";
                case AttackType.Drowning: return "\u6EBA\u4EA1";
                case AttackType.Water: return "\u6C34\u4E2D\u9047\u96BE";
                case AttackType.Gravity: return "\u5760\u843D\u800C\u6B7B";
                case AttackType.Explosion: return "\u7206\u70B8\u8EAB\u4EA1";
                case AttackType.Divine: return "\u795E\u529B\u6240\u6740";
                case AttackType.Metamorphosis: return "\u5F02\u53D8\u6D88\u4EA1";
                case AttackType.Smile: return "\u795E\u79D8\u6B7B\u4EA1";
                case AttackType.Other: return "\u610F\u5916\u6B7B\u4EA1";
                case AttackType.None: return "\u81EA\u7136\u6B7B\u4EA1";
                default: return "\u672A\u77E5\u6B7B\u56E0";
            }
        }
    }
}
