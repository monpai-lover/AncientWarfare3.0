using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     KingdomWindow 注入:
    ///     - **顶部中段**(年号框 + 国王头像 + 国策占位框):由 `KingdomWindowAddition` 组件接管(NML 嵌套布局)。
    ///       本补丁在 `showTopPartInformation` Prefix 把组件挂上(查重)。
    ///       (钩 showTopPartInformation 因 KingdomWindow 直接 override 了它;不能钩只声明在泛型基类、
    ///        KingdomWindow 未 override 的 startShowingWindow——会解析 null 致整个 PatchAll 失败、mod 被禁用。)
    ///     - **头衔**:showStatsRows Postfix 注 aw_kingdom_title 行。
    ///     - **继承人**:不自己画——原版 showStatsRows 已用 tryToShowActor("heir",...) 在国王框下方画同款继承人框;
    ///       仅由 TryToShowActor_Prefix 抑制「无继承人时的空行」。
    /// </summary>
    [HarmonyPatch]
    public static class AW_KingdomWindowPatch
    {
        // ── 挂载顶部中段组件(查重,避免重复挂)──
        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomWindow), "showTopPartInformation")]
        public static void ShowTopPartInformation_Prefix(KingdomWindow __instance)
        {
            if (__instance == null) return;
            if (__instance.GetComponent<KingdomWindowAddition>() == null)
                __instance.gameObject.AddComponent<KingdomWindowAddition>();
        }

        // ── 头衔:stats 行(继承人交给原版 tryToShowActor 画,不在此重复)──
        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(KingdomWindow __instance)
        {
            var kingdom = SelectedMetas.selected_kingdom;
            if (kingdom == null || kingdom.data == null) return;

            string title = core.lineage.KingdomTitleService.GetTitleString(
                core.lineage.KingdomTitleService.GetTitle(kingdom));
            if (!string.IsNullOrEmpty(title))
                __instance.showStatRow("aw_kingdom_title", title);
        }

        // ── 无继承人时不画空行 ──
        // 原版 tryToShowActor(pTitle,pID,pName,pObject,pIconPath):actor 为 null/已死 → showEmptyStatRow(空行)。
        // 用户要「没继承人时不绘制」→ 当 pTitle=="heir" 且 actor 无效时跳过原方法(不画空行)。
        // typeof 必须用方法**实际声明类 StatsWindow**(StatsWindow.cs:331,protected,publicized 可 patch);
        // 用 KingdomWindow(继承未 override)会解析 null 致 PatchAll 失败(见记忆 aw3-harmony-inherited-method-pitfall)。
        [HarmonyPrefix]
        [HarmonyPatch(typeof(StatsWindow), "tryToShowActor")]
        public static bool TryToShowActor_Prefix(string pTitle, long pID, Actor pObject)
        {
            if (pTitle != "heir") return true; // 只管继承人行
            Actor actor = pObject != null ? pObject : World.world.units.get(pID);
            if (actor == null || actor.isRekt()) return false; // 无继承人 → 不画空行
            return true;
        }
    }
}
