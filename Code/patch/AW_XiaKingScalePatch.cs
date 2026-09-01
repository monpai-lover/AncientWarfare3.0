using AncientWarfare3.content;
using AncientWarfare3.core.presentation;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     夏人国王的身体帧与头帧都按四倍像素绘制,单位缩放相应压到四分之一,
    ///     使贴图的每一个像素都能上屏,同时视觉大小与普通单位一致。
    ///
    ///     为什么必须整个单位一起 4x:
    ///     DynamicSpriteCreator.createNewSpriteUnit 把头**逐像素**贴进单位图集
    ///     (与身体合成为一张图),createFinalSprite 又把结果的 pixelsPerUnit
    ///     写死成 1f。头在这条管线里没有自己的变换可缩,所以"头 4x、身体 1x"
    ///     做不到——只有让身体也进入 4x 空间,头身 1:1 合成才是对的。
    /// </summary>
    [HarmonyPatch]
    internal static class AW_XiaKingScalePatch
    {
        /// <summary>
        ///     只改渲染缩放(actor_scale / current_scale),不碰体型(target_scale)。
        ///     target_scale 是游戏内体型的唯一来源,影响碰撞、行为 AI、体型等级判断,
        ///     不能动。actor_scale 纯粹驱动视觉合成。
        ///
        ///     updateChangeScale 会每帧把 actor_scale 补间回 target_scale,
        ///     所以还需要在 updateChangeScale 里拦截(见下方 prefix patch)。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "updateStats")]
        private static void UpdateStatsPostfix(Actor __instance)
        {
            if (!XiaKingScaleRules.UsesHighResolutionBody(
                    __instance?.asset?.id, __instance?.isKing() ?? false))
                return;
            __instance.setActorScale(XiaKingScaleRules.BodyScaleMultiplier);
        }

        /// <summary>
        ///     阻止 updateChangeScale 把视觉缩放拉回体型值。
        ///     国王的 actor_scale = target_scale * 0.25,
        ///     updateChangeScale 认为它们不等就会每帧慢慢补间——直接跳过。
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "updateChangeScale")]
        private static bool UpdateChangeScalePrefix(Actor __instance)
        {
            return !XiaKingScaleRules.UsesHighResolutionBody(
                __instance?.asset?.id, __instance?.isKing() ?? false);
        }

        /// <summary>
        ///     检视面板/家族树的头像走 UnitAvatarLoader,它按 asset 的
        ///     inspect_avatar_scale 定尺寸——那是**整个种族共用**的字段,不能改,
        ///     否则所有夏人头像都会缩水。这里只对国王的头像实例重设缩放。
        ///
        ///     必须**绝对赋值**,不能在现有 localScale 上乘系数:
        ///     UnitAvatarLoader 是**池化复用**的,同一个实例每次开检视面板都会
        ///     重新 load,乘法会逐次累积(0.25 → 0.0625 → …),表现为头像
        ///     每点一次就更小。原版自己也是绝对赋值,这里照抄它的公式。
        ///
        ///     load 有三个重载,设 localScale 的是私有的 load(bool),
        ///     必须显式给出参数类型,否则 Harmony 报 Ambiguous match。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitAvatarLoader), "load", typeof(bool))]
        private static void LoadAvatarPostfix(UnitAvatarLoader __instance)
        {
            ActorAvatarData data = __instance?.getData();
            if (!XiaKingScaleRules.UsesHighResolutionBody(data?.asset?.id,
                    data?.is_king ?? false)) return;

            float scale = XiaKingScaleRules.ResolveAvatarScale(
                data.asset.inspect_avatar_scale, __instance.avatarSize);
            __instance.transform.localScale = new Vector3(scale, scale, 0f);
        }

        /// <summary>
        ///     国王头像帧比普通夏人单位高 4 倍,检视面板里偏上。
        ///     showStatic 设好 _actor_image 的 anchoredPosition 之后,
        ///     再整体下移 20 像素,这里 actor_image 是 _actor_and_item_container
        ///     下的第一个(也是控件 _actor_image)直接子节点。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitAvatarLoader), "showStatic")]
        private static void ShowStaticPostfix(UnitAvatarLoader __instance)
        {
            ActorAvatarData data = __instance?.getData();
            if (!XiaKingScaleRules.UsesHighResolutionBody(data?.asset?.id,
                    data?.is_king ?? false)) return;

            RectTransform rt = __instance._actor_image?.rectTransform;
            if (rt == null) return;
            Vector2 pos = rt.anchoredPosition;
            rt.anchoredPosition = new Vector2(pos.x,
                pos.y - XiaKingScaleRules.InspectPortraitOffsetY * 2f);
        }
    }
}
