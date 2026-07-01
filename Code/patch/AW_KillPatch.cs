using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     击杀编年史:Postfix Actor.newKillAction(Actor pDeadUnit, Kingdom pPrevKingdom, AttackType)
    ///     (newKillAction 在 Actor 自身声明,typeof 正确)。__instance = 凶手,pDeadUnit = 被杀者,
    ///     pPrevKingdom = 被杀者原属国。
    ///
    ///     - 重要击杀:凶手贵族且(被杀重要 或 凶手重要)→ 记凶手传记;被杀是王/城主/名人 → 国家史留痕。
    ///     - 批2 追加:战争击杀累加(WarRecordWriter.AddKill)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_KillPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.newKillAction))]
        public static void NewKillAction_Postfix(Actor __instance, Actor pDeadUnit, Kingdom pPrevKingdom, AttackType pAttackType)
        {
            if (__instance == null || pDeadUnit == null) return;
            if (pDeadUnit.data != null)
                pDeadUnit.data.set(LineageKeys.DEATH_CAUSE, "被 " + __instance.getName() + " 击杀");
            ChronicleEvents.OnImportantKill(__instance, pDeadUnit, pPrevKingdom);
            // 批2:战争击杀累加到内存缓存(WarRecordWriter 在 endWar 时落库)。
            WarRecordWriter.AddKill(__instance, pDeadUnit, pPrevKingdom);
        }
    }
}
