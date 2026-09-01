using AncientWarfare3.core.presentation;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     夏人国王用四倍像素绘制的身体帧,在**检视面板**里的收尾。
    ///
    ///     地图上的世界尺寸由合成图的 pixelsPerUnit 抵消
    ///     (见 AW_HighResolutionSpritePatch),actor_scale / current_scale
    ///     一律保持原版数值,所以影子、手持物、星标、选择框那些
    ///     普通分辨率的附属物不需要任何特判。
    ///
    ///     但检视面板不吃 ppu —— UnitAvatarLoader.setImageParams
    ///     (UnitAvatarLoader.cs:487)直接按 sprite.rect 定 sizeDelta,
    ///     所以 4× 的帧在面板里仍然是四倍大,只能在这里单独缩回去。
    /// </summary>
    [HarmonyPatch]
    internal static class AW_XiaKingScalePatch
    {
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
            if (__instance == null || __instance._died) return;
            ActorAvatarData data = __instance.getData();
            if (!XiaKingScaleRules.UsesHighResolutionBody(data?.asset?.id,
                    data?.is_king ?? false)) return;

            float scale = XiaKingScaleRules.ResolveAvatarScale(
                data.asset.inspect_avatar_scale, __instance.avatarSize);
            __instance.transform.localScale = new Vector3(scale, scale, 0f);

            // 原版把边框反向缩放以保持它在屏幕上大小恒定
            // (UnitAvatarLoader.cs:157-160),头像缩了边框就得同比放大,
            // 否则边框会跟着缩成四分之一。
            if (__instance._frame != null)
                __instance._frame.localScale =
                    new Vector3(2.5f / scale, 2.5f / scale, 0f);

        }

        /// <summary>
        ///     面板里画像与手持物的落位修正,挂在两者共用的 setImageParams 上。
        ///
        ///     位置量都在头像的**局部像素**空间里,而国王的帧是 4× 的,
        ///     整个头像又被 ResolveAvatarScale ÷4 缩回去,所以按普通分辨率
        ///     标定的偏移(asset.inspect_avatar_offset_x/y)在屏幕上只剩四分之一,
        ///     要乘回倍率。手持物还要再补一项:它的偏移里含 pos_item,
        ///     而 pos_item 已经被 AW_HighResolutionSpritePatch 除以倍率了
        ///     (地图上它要乘 current_scale),面板不乘,所以同样靠这里乘回来。
        ///
        ///     为什么必须挂 setImageParams 而不是 load:手持物的位置在
        ///     Update → syncItemWithUnit(:299-300)里**每换一帧就重设一次**,
        ///     挂 load 会被覆盖。画像只在 Update 里换 sprite(:237)不重设位置,
        ///     但一并放这里更稳。setImageParams 是**绝对赋值**(:491),
        ///     所以跟在它后面变换不会累积。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitAvatarLoader), "setImageParams")]
        private static void SetImageParamsPostfix(UnitAvatarLoader __instance,
            Image pImage)
        {
            if (__instance == null || pImage == null || __instance._died) return;
            bool isActor = ReferenceEquals(pImage, __instance._actor_image);
            if (!isActor && !ReferenceEquals(pImage, __instance._item_image))
                return;

            ActorAvatarData data = __instance.getData();
            if (!XiaKingScaleRules.UsesHighResolutionBody(data?.asset?.id,
                    data?.is_king ?? false)) return;

            RectTransform rt = pImage.rectTransform;
            Vector2 pos = rt.anchoredPosition;
            float x = pos.x * XiaKingScaleRules.BodyResolutionFactor;
            float y = pos.y * XiaKingScaleRules.BodyResolutionFactor -
                      XiaKingScaleRules.InspectPortraitOffsetY;

            if (!isActor)
            {
                // 手持物贴图是普通分辨率的,而 setImageParams 按 sprite.rect 定
                // sizeDelta(:487),整个头像又被 ÷4 —— 身体是 4× 帧缩完刚好,
                // 手持物是 1× 帧就被白缩了四倍,尺寸要乘回来。
                // pivot 是归一化的,放大绕 pivot 进行,不影响这里定好的位置。
                rt.sizeDelta *= XiaKingScaleRules.BodyResolutionFactor;
            }

            rt.anchoredPosition = new Vector2(x, y);
        }
    }
}
