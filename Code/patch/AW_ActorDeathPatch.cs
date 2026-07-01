using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_ActorDeathPatch
    {
        internal static long DyingKingActorId = -1L;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die")]
        public static void Die_Prefix(Actor __instance, AttackType pType)
        {
            if (__instance?.data == null) return;
            if (!__instance.isAlive()) return;

            if (__instance.hasTrait(content.figures.HistoricalFigureService.TRAIT_FIRST) ||
                __instance.hasTrait(content.figures.HistoricalFigureService.TRAIT_FIGURE))
            {
                content.figures.HistoricalFigureService.OnFigureDied(__instance);
            }

            if (!LineageService.IsXia(__instance)) return;

            EnsureDeathCause(__instance, pType);
            LineageService.ArchiveActor(__instance, pAlive: false);

            if (__instance.isKing() && __instance.kingdom != null)
            {
                DyingKingActorId = __instance.data.id;
                ChronicleEvents.OnKingDied(__instance.kingdom, __instance);
            }

            __instance.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
            if (lid >= 0)
            {
                string name = __instance.getName();
                __instance.data.get(LineageKeys.DEATH_CAUSE, out string cause, "");
                string causeText = string.IsNullOrEmpty(cause) ? "" : "\uFF08\u6B7B\u56E0\uFF1A" + cause + "\uFF09";
                HistoryWriter.RecordPerson(
                    __instance.data.id, __instance.kingdom, name,
                    PersonEvent.DEATH,
                    HistoryText.Actor(__instance, name) + " \u901D\u4E16" + causeText,
                    ChronicleCategory.LIFE);
            }

            ChronicleEvents.OnBondDeath(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "die")]
        public static void Die_Postfix(Actor __instance)
        {
            if (__instance?.data != null && DyingKingActorId == __instance.data.id)
                DyingKingActorId = -1L;
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
                case AttackType.Age: return "\u81EA\u7136\u8001\u6B7B";
                case AttackType.Starvation: return "\u9965\u997F\u800C\u6B7B";
                case AttackType.Plague:
                case AttackType.Infection:
                case AttackType.Tumor:
                case AttackType.AshFever: return "\u75C5\u75AB\u800C\u6B7B";
                case AttackType.Poison: return "\u4E2D\u6BD2\u800C\u6B7B";
                case AttackType.Drowning: return "\u6EBA\u4EA1";
                case AttackType.Gravity: return "\u5760\u843D\u800C\u6B7B";
                case AttackType.Divine: return "\u795E\u529B\u6240\u6740";
                case AttackType.Metamorphosis: return "\u5F02\u53D8\u6D88\u4EA1";
                case AttackType.None: return "\u81EA\u7136\u6B7B\u4EA1";
                default: return "\u6B7B\u4EA1";
            }
        }
    }
}
