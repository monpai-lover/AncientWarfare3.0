using System.Collections.Generic;
using AncientWarfare3.core.presentation;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     高分辨率单位贴图(夏人国王的 4× 帧)的落地实现,两步:
    ///
    ///     1. 合成图的 pixelsPerUnit 设为倍率。DynamicSpriteCreator
    ///        把身体和头逐像素合成进单位图集后,createFinalSprite
    ///        (DynamicSpriteCreator.cs:57)把结果的 ppu **写死成 1f**,
    ///        于是 32px 的帧就是 32 个世界单位 × current_scale。改成 4 之后
    ///        世界尺寸自动回到普通单位的水平,而 current_scale 保持不动 ——
    ///        影子、手持物、星标、选择框、状态图标这些普通分辨率的附属物
    ///        全都继续用同一个 current_scale,不需要任何特判。
    ///
    ///     2. 帧数据里的像素偏移除以倍率。AnimationFrameData 的
    ///        pos_head / pos_item / size_unit 是按 4× 帧标定的,消费方一律
    ///        乘 current_scale(Actor.cs:4701、ActorManager.cs:187 等),
    ///        不除就会偏出四倍。动画容器按贴图路径缓存且国王独占
    ///        .../Xia/king,所以在容器创建完成时改一次即可,全体消费方受益。
    ///
    ///        pos_head_new 不能除 —— 合成器(DynamicSpriteCreator.cs:407,436)
    ///        在图集**像素**空间里用它定位头部。
    /// </summary>
    [HarmonyPatch]
    internal static class AW_HighResolutionSpritePatch
    {
        // 身体帧精灵的引用集合。createFinalSprite 只拿得到身体精灵,
        // 拿不到 Actor 也拿不到路径,所以在动画容器加载时把它们记下来。
        private static readonly HashSet<Sprite> HighResolutionBodies =
            new HashSet<Sprite>();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActorAnimationLoader), "createAnimationContainer")]
        private static void CreateAnimationContainerPostfix(string pTexturePath,
            AnimationContainerUnit __result)
        {
            if (__result == null ||
                !XiaKingScaleRules.IsHighResolutionTexturePath(pTexturePath))
                return;

            RegisterBodies(__result);
            ScaleDownFrameOffsets(__result);
        }

        private static void RegisterBodies(AnimationContainerUnit pContainer)
        {
            if (pContainer.sprites == null) return;
            lock (HighResolutionBodies)
            {
                foreach (KeyValuePair<string, Sprite> entry in pContainer.sprites)
                {
                    if (entry.Value != null)
                        HighResolutionBodies.Add(entry.Value);
                }
            }
        }

        /// <summary>
        ///     把按 4× 帧标定、但消费方会乘 current_scale 的像素量除回来。
        ///     容器每条路径只创建一次(ActorAnimationLoader._dict_units 缓存),
        ///     所以不会重复缩小。
        /// </summary>
        private static void ScaleDownFrameOffsets(AnimationContainerUnit pContainer)
        {
            if (pContainer.dict_frame_data == null) return;
            const float factor = XiaKingScaleRules.BodyResolutionFactor;
            foreach (KeyValuePair<string, AnimationFrameData> entry in
                     pContainer.dict_frame_data)
            {
                AnimationFrameData frame = entry.Value;
                if (frame == null) continue;
                frame.pos_head /= factor;
                frame.size_unit /= factor;
                // 除回普通分辨率之后再补手部锚点的偏差,单位一致。
                frame.pos_item = frame.pos_item / factor + new Vector2(
                    XiaKingScaleRules.ItemAnchorOffsetX,
                    XiaKingScaleRules.ItemAnchorOffsetY);
            }
        }

        /// <summary>
        ///     Sprite 一旦创建 pixelsPerUnit 就不可改,只能按同样的
        ///     texture / rect / pivot 重建一张。合成图由 DynamicSprites
        ///     按 ID 缓存,所以每个独立单位外观只会走一次。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(DynamicSpriteCreator), "createFinalSprite")]
        private static void CreateFinalSpritePostfix(Sprite pMain,
            ref Sprite __result)
        {
            if (__result == null || pMain == null) return;
            lock (HighResolutionBodies)
            {
                if (!HighResolutionBodies.Contains(pMain)) return;
            }

            Rect rect = __result.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            Vector2 normalizedPivot = new Vector2(
                __result.pivot.x / rect.width,
                __result.pivot.y / rect.height);
            Sprite scaled = Sprite.Create(__result.texture, rect,
                normalizedPivot, XiaKingScaleRules.BodyResolutionFactor);
            scaled.name = __result.name;
            __result = scaled;
        }
    }
}
