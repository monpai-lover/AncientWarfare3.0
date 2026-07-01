using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     退位编年史:Prefix Kingdom.kingLeftEvent 捕获当前国王,
    ///     Postfix 判断该王是否仍在世 → 主动退位则记录,死亡触发则跳过(已在 Die_Prefix 记 king_died)。
    ///     去重依赖 AW_ActorDeathPatch.DyingKingActorId:Die_Prefix 设值,Die_Postfix 清除,
    ///     确保死亡路径和退位路径不重叠。kingLeftEvent 在 Kingdom 自身声明,typeof 正确。
    /// </summary>
    [HarmonyPatch]
    public static class AW_AbdicatePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.kingLeftEvent))]
        public static void KingLeft_Prefix(Kingdom __instance, out Actor __state)
        {
            // 执行前捕获当前国王(方法内会调 removeKing → king 置 null)。
            __state = __instance?.king;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.kingLeftEvent))]
        public static void KingLeft_Postfix(Kingdom __instance, Actor __state)
        {
            if (__state?.data == null) return;
            // 若此 king 正是 Die_Prefix 标记的"正在死亡的王",则跳过(驾崩已记录)。
            if (__state.data.id == AW_ActorDeathPatch.DyingKingActorId) return;
            // 否则视为主动退位。
            ChronicleEvents.OnAbdicate(__instance, __state);
        }
    }
}
