using System;
using AncientWarfare3.content;

namespace AncientWarfare3.core.presentation
{
    /// <summary>
    ///     夏人国王高分辨率单位的缩放规则。贴图按四倍像素绘制,
    ///     单位缩放乘四分之一抵消,净视觉大小不变。
    /// </summary>
    public static class XiaKingScaleRules
    {
        /// <summary>国王贴图相对普通单位的绘制倍率。</summary>
        public const int BodyResolutionFactor = 4;

        /// <summary>渲染缩放系数:纯视觉,不影响 target_scale(体型/碰撞)。</summary>
        public const float BodyScaleMultiplier = 0.0825f;

        /// <summary>
        ///     头像框是反向缩放的(原版按 2.5f / scale 保持边框大小恒定),
        ///     所以头像本体缩小多少,边框就要放大回来多少。
        /// </summary>
        public const float FrameScaleMultiplier = BodyResolutionFactor;

        /// <summary>
        ///     只有夏人国王走高分辨率身体,其余夏人单位保持原分辨率。
        /// </summary>
        public static bool UsesHighResolutionBody(string pAssetId, bool pIsKing)
        {
            return pIsKing && string.Equals(pAssetId, XiaRace.ID,
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     当前缩放与目标差得足够远时必须瞬时对齐,不能走成长补间——
        ///     高分辨率的 ¼ 缩放是对贴图倍率的抵消,补间会让刚即位的国王
        ///     先缩成一个点再慢慢长回来。
        /// </summary>
        public static bool NeedsImmediateScale(float pCurrentScale,
            float pTargetScale)
        {
            return Math.Abs(pCurrentScale - pTargetScale) > 0.001f;
        }

        /// <summary>
        ///     检视面板里国王头像图片的额外下移量(像素)。
        ///     头帧 4× 放大后图像整体偏上,这里在 showStatic 设好 anchoredPosition
        ///     之后再减去这个偏移让画像回到正确位置。
        /// </summary>
        public const float InspectPortraitOffsetY = 20f;

        /// <summary>
        ///     检视头像的绝对缩放,复刻原版 UnitAvatarLoader.load 的公式
        ///     (inspect_avatar_scale * avatarSize)再乘抵消系数。
        ///     必须是绝对值:头像加载器是池化复用的,基于当前 localScale
        ///     做乘法会在反复打开面板时累积缩小。
        /// </summary>
        public static float ResolveAvatarScale(float pInspectAvatarScale,
            float pAvatarSize)
        {
            return pInspectAvatarScale * pAvatarSize * BodyScaleMultiplier;
        }
    }
}
