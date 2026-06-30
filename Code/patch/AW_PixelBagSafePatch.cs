using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     修复新版游戏 PixelBag 预加载对"头身分离"逐帧贴图的越界崩溃。
    ///
    ///     背景(版本回归):
    ///     - 夏人单位贴图沿用 AW2 的逐帧格式:身体帧 walk_N.png + 1x1 占位头帧 walk_N_head.png,
    ///       sprites.json 给头帧设**负 RectY**(如 -2/-4)做头身对位。NML(新旧版一致)用
    ///       Sprite.Create(new Rect(RectX, RectY, ...)) 建 sprite,负 RectY → sprite.rect.y &lt; 0。
    ///     - 老版 WorldBox **没有** PixelBag phenotype 预加载,这些负 rect 的 sprite 不被遍历,故不崩。
    ///     - 新版游戏新增 PreloadHelpers.preloadPixelBagsUnits():遍历所有单位 sprite 建 PixelBag,
    ///       PixelBag..ctor 用 pixels[(i+rect.x)+(j+rect.y)*width] 取色,rect.y&lt;0 → 负索引越界崩。
    ///
    ///     修复:Prefix PixelBagManager.preloadPixelBagUnit —— 对"rect 会导致越界"的 sprite 跳过预加载。
    ///     这些是 1x1 空占位头/手持帧,跳过其 phenotype 预缓存无害(运行时真正用到时仍正常构建)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_PixelBagSafePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PixelBagManager), nameof(PixelBagManager.preloadPixelBagUnit))]
        public static bool PreloadPixelBagUnit_Prefix(Sprite pSpriteSource)
        {
            if (pSpriteSource == null || pSpriteSource.texture == null) return false; // 无贴图,跳过

            if (RectOutOfTextureBounds(pSpriteSource))
                return false; // 跳过越界 sprite,不进原版 PixelBag 构造(否则 pixels[负/超界] 崩)

            return true; // 正常 sprite,走原版预加载
        }

        /// <summary>
        ///     复现 PixelBag..ctor 的索引范围,判断该 sprite 的 rect 是否会让 pixels[] 越界。
        ///     PixelBag 遍历 i∈[0,rect.width)、j∈[0,rect.height),取 pixels[(i+rect.x)+(j+rect.y)*texW]。
        ///     pixels 长度 = texW*texH。任一索引 &lt;0 或 ≥长度 即越界。
        /// </summary>
        private static bool RectOutOfTextureBounds(Sprite pSprite)
        {
            Rect rect = pSprite.rect;
            int texW = pSprite.texture.width;
            int texH = pSprite.texture.height;

            int rx = (int)rect.x;
            int ry = (int)rect.y;
            int rw = (int)rect.width;
            int rh = (int)rect.height;

            // 最小索引(i=j=0):rx + ry*texW;最大索引(i=rw-1,j=rh-1):(rx+rw-1)+(ry+rh-1)*texW
            if (rx < 0 || ry < 0) return true;                 // 负原点 → 负索引
            if (rx + rw > texW) return true;                   // 右越界
            if (ry + rh > texH) return true;                   // 上越界
            return false;
        }
    }
}
