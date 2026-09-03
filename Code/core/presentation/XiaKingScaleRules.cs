using System;
using AncientWarfare3.content;

namespace AncientWarfare3.core.presentation
{
    /// <summary>
    ///     夏人国王高分辨率单位的规则。贴图按四倍像素绘制,
    ///     靠**合成图的 pixelsPerUnit** 把世界尺寸压回去,
    ///     actor_scale / current_scale 一律不动。
    ///
    ///     为什么用 ppu 而不是缩放 current_scale:
    ///     原版只有 current_scale 这一个缩放源,主贴图和所有普通分辨率的
    ///     附属物(影子、手持物、星标、选择框、状态图标、气泡……)共用它。
    ///     把它 ÷4 会把附属物一起缩掉,而附属物散落在原版各处,逐个特判改不完。
    ///     pixelsPerUnit 只影响这张精灵自己的世界尺寸,正是为这种情形准备的。
    ///
    ///     代价只有一处:AnimationFrameData 里 pos_head / pos_item / size_unit
    ///     是按 4× 帧标定的像素,消费方一律乘 current_scale,所以要在动画容器
    ///     加载完成后除以倍率(见 AW_HighResolutionSpritePatch)。
    ///     pos_head_new 例外 —— 合成器在图集像素空间里用它,必须保持原值。
    /// </summary>
    public static class XiaKingScaleRules
    {
        /// <summary>国王贴图相对普通单位的绘制倍率。</summary>
        public const int BodyResolutionFactor = 4;

        /// <summary>国王身体帧所在的贴图目录后缀,用来认出高分辨率动画容器。</summary>
        public const string KingTexturePathSuffix = "/king";

        /// <summary>
        ///     检视面板里国王画像的下移量,单位是头像的**局部**像素
        ///     (4× 空间,身体帧高 44,所以 44 ≈ 一整个身高)。
        ///     纯视觉微调:画像偏上调大、偏下调小。
        /// </summary>
        public const float InspectPortraitOffsetY = 21f;

        /// <summary>
        ///     手持物锚点的微调,单位是**普通分辨率像素**,直接加在
        ///     pos_item 上(在它被除回普通分辨率之后)。
        ///
        ///     加在帧数据上而不是各个消费方,是为了一处生效两处受益:
        ///     地图上 pos_item 要乘 current_scale,面板里它是局部像素再乘倍率,
        ///     所以同一个值在面板里自动放大成四倍,两边位移量一致。
        ///
        ///     偏差的来源是手部锚点在 4× 重绘时挪了位,属于帧数据本身。
        /// </summary>
        public const float ItemAnchorOffsetX = -1f;

        /// <summary>
        ///     见 <see cref="ItemAnchorOffsetX"/>。+y 朝上:原版把 pos_item.y
        ///     直接加进世界坐标(ActorManager.cs:236),面板里也是加进
        ///     anchoredPosition.y,两边同向。
        /// </summary>
        public const float ItemAnchorOffsetY = 0f;

        /// <summary>
        ///     只有成年夏人国王走高分辨率身体,其余单位保持原分辨率。
        /// </summary>
        public static bool UsesHighResolutionBody(string pAssetId, bool pIsKing,
            bool pIsBaby)
        {
            return pIsKing && !pIsBaby && string.Equals(pAssetId, XiaRace.ID,
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     这条贴图路径是不是高分辨率单位的帧目录。
        ///     动画容器按贴图路径缓存,国王独占 .../Xia/king,
        ///     所以按路径认最省事,也不需要 Actor 在场。
        /// </summary>
        public static bool IsHighResolutionTexturePath(string pTexturePath)
        {
            if (string.IsNullOrEmpty(pTexturePath)) return false;
            return pTexturePath.Replace('\\', '/')
                       .EndsWith(KingTexturePathSuffix, StringComparison.Ordinal)
                   && pTexturePath.IndexOf(XiaRace.ID,
                       StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        ///     检视面板头像的缩放:原版公式(UnitAvatarLoader.cs:155)再除以贴图倍率。
        ///     地图上靠 pixelsPerUnit 抵消 4× 贴图,检视面板不走那条路径 ——
        ///     setImageParams 直接按 sprite.rect 定 sizeDelta(:487),
        ///     完全无视 ppu,所以必须在这里单独抵消一次。
        ///
        ///     必须**绝对赋值**,不能在现有 localScale 上乘系数:
        ///     UnitAvatarLoader 是池化复用的,同一个实例每次开面板都会重新 load,
        ///     乘法会逐次累积,表现为头像每点一次就更小。
        /// </summary>
        public static float ResolveAvatarScale(float pInspectAvatarScale,
            float pAvatarSize)
        {
            return pInspectAvatarScale * pAvatarSize / BodyResolutionFactor;
        }
    }
}
