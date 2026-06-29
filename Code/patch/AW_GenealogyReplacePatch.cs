using System.Collections;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     替换原版"死后即消失"的家族树。
    ///
    ///     原版族谱由 UnitGenealogyElement.showContent()(IEnumerator)绘制父母/祖父母/子女/兄弟,
    ///     死者被 isRekt() 隐藏 → 死后家谱即消失。
    ///
    ///     本 patch Prefix 该协程入口:当被查看单位是 Xia 且有谱系记录(aw_lineage_id>=0),
    ///     接管 → 打开我们的持久化家族树窗(含死人),并用空协程跳过原版绘制(return false)。
    ///     其他单位放行原版(return true)。
    ///
    ///     actor 取 __instance.actor(UnitElement.actor,= unit_window.actor)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_GenealogyReplacePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UnitGenealogyElement), "showContent")]
        public static bool ShowContent_Prefix(UnitGenealogyElement __instance, ref IEnumerator __result)
        {
            var actor = __instance.actor;
            if (actor == null || actor.data == null) return true;   // 放行原版
            if (!LineageService.IsXia(actor)) return true;

            actor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            if (lineageId < 0) return true;                          // 无谱系→放行原版

            actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            FamilyTreeWindow.OpenFamilyTree(actor.data.id, shiId);

            __result = Empty();                                      // 空协程,跳过原版绘制
            return false;
        }

        private static IEnumerator Empty()
        {
            yield break;
        }
    }
}
