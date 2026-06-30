using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.ui.items
{
    /// <summary>
    ///     把一张 Sprite 转成黑白灰(去色)副本并缓存。用于家族树死者头像。
    ///     按源 Sprite 缓存,首次计算后复用,避免每次重建。
    ///     非可读贴图(readable=false)或异常时回退原图(不崩、不留空)。
    /// </summary>
    internal static class GrayscaleSpriteCache
    {
        private static readonly Dictionary<Sprite, Sprite> _cache = new Dictionary<Sprite, Sprite>();

        public static Sprite Get(Sprite pSource)
        {
            if (pSource == null) return null;
            if (_cache.TryGetValue(pSource, out var cached)) return cached;

            Sprite result = pSource;
            try
            {
                result = Build(pSource) ?? pSource;
            }
            catch
            {
                result = pSource; // 贴图不可读等情况回退原图
            }

            _cache[pSource] = result;
            return result;
        }

        private static Sprite Build(Sprite pSource)
        {
            Texture2D srcTex = pSource.texture;
            if (srcTex == null) return null;

            Rect r = pSource.rect;
            int x = (int)r.x, y = (int)r.y, w = (int)r.width, h = (int)r.height;
            if (w <= 0 || h <= 0) return null;
            if (x < 0 || y < 0 || x + w > srcTex.width || y + h > srcTex.height) return null;

            Color[] pixels = srcTex.GetPixels(x, y, w, h); // 不可读贴图会抛 → 上层 catch 回退
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f; // 标准亮度去色
                pixels[i] = new Color(lum, lum, lum, c.a);              // 保留 alpha(透明区不变)
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels(pixels);
            tex.Apply();

            // pivot 用归一化(源 sprite.pivot 是像素,这里 region 从 0 起,换算成 0..1)。
            Vector2 pivot = new Vector2(
                w > 0 ? pSource.pivot.x / w : 0.5f,
                h > 0 ? pSource.pivot.y / h : 0.5f);
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, pSource.pixelsPerUnit);
        }
    }
}
