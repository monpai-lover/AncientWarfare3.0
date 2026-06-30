using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     国家名牌(地图上方那条)加爵位等级:夏朝系王国名牌显示"伯/侯/公/王/帝"前缀(用户反馈名牌没显示爵位)。
    ///     Postfix NameplateText.showTextKingdom(NameplateText.cs:329,每帧/刷新调):仅对主种族 Xia 的王国,
    ///     用 KingdomTitleService 取爵位单字 + 原名 + 人口,重设名牌文字。非 Xia 王国不动(避免给所有国乱加爵位)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_NameplateTitlePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NameplateText), "showTextKingdom")]
        public static void ShowTextKingdom_Postfix(NameplateText __instance, Kingdom pMetaObject, Vector2 pPosition)
        {
            if (pMetaObject?.data == null) return;
            if (pMetaObject.data.original_actor_asset != LineageService.XIA_ASSET_ID) return; // 只夏朝系

            var title = KingdomTitleService.GetTitle(pMetaObject);
            string titleSuffix = KingdomTitleService.GetTitleString(title); // 伯国/侯国/...
            if (string.IsNullOrEmpty(titleSuffix)) return;

            // 国名 + 国号后缀(如"晋伯国"),而非"伯晋"。is_full 才有完整文字(mini 名牌不动)。
            if (!__instance.is_full) return;
            int pop = pMetaObject.getPopulationPeople();
            string baseStr = __instance.getStringForNameplate(pMetaObject.name + titleSuffix, pop);
            __instance.setText(baseStr, pPosition);
        }
    }
}
