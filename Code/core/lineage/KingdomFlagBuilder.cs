using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     从 KingdomArchive 存档值重建王国旗帜(背景 + 图标 + 配色),**不引用活 Kingdom 对象**,
    ///     故亡国也能正常画旗,规避空引用。配方对齐原版 KingdomBanner.load:
    ///       背景 sprite = kingdom_banners_library.getSpriteBackground(bg_id, banner_id),色 = getColorMainSecond()
    ///       图标 sprite = kingdom_banners_library.getSpriteIcon(icon_id, banner_id),色 = getColorBanner()
    /// </summary>
    internal static class KingdomFlagBuilder
    {
        /// <summary>把档案旗帜画进给定的背景/图标 Image。任一资源取不到则隐藏对应 Image(不崩)。</summary>
        public static void Build(string pBannerId, int pIconId, int pBgId, string pColorText, int pColorId,
            Image pBackground, Image pIcon)
        {
            ColorAsset color = ResolveColor(pColorText, pColorId);

            if (pBackground != null)
            {
                Sprite bg = SafeGetBackground(pBannerId, pBgId);
                if (bg != null)
                {
                    pBackground.enabled = true;
                    pBackground.sprite = bg;
                    if (color != null) pBackground.color = color.getColorMainSecond();
                }
                else pBackground.enabled = false;
            }

            if (pIcon != null)
            {
                Sprite icon = SafeGetIcon(pBannerId, pIconId);
                if (icon != null)
                {
                    pIcon.enabled = true;
                    pIcon.sprite = icon;
                    if (color != null) pIcon.color = color.getColorBanner();
                }
                else pIcon.enabled = false;
            }
        }

        /// <summary>颜色:优先存档 hex(getExistingColorAsset),退回 color_id 索引。</summary>
        public static ColorAsset ResolveColor(string pColorText, int pColorId)
        {
            ColorAsset color = null;
            try
            {
                if (!string.IsNullOrEmpty(pColorText))
                    color = ColorAsset.getExistingColorAsset(pColorText);
            }
            catch { color = null; }
            if (color == null && pColorId >= 0)
            {
                try { color = AssetManager.kingdom_colors_library.getColorByIndex(pColorId); }
                catch { color = null; }
            }
            return color;
        }

        private static Sprite SafeGetBackground(string pBannerId, int pBgId)
        {
            if (string.IsNullOrEmpty(pBannerId)) return null;
            try { return AssetManager.kingdom_banners_library.getSpriteBackground(pBgId, pBannerId); }
            catch { return null; }
        }

        private static Sprite SafeGetIcon(string pBannerId, int pIconId)
        {
            if (string.IsNullOrEmpty(pBannerId)) return null;
            try { return AssetManager.kingdom_banners_library.getSpriteIcon(pIconId, pBannerId); }
            catch { return null; }
        }
    }
}
