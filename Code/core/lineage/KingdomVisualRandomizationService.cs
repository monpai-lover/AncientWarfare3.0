using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class KingdomVisualRandomizationService
    {
        private static readonly Random Rng = new Random();

        public static bool RerollNewCivVisuals(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;

            ActorAsset actorAsset;
            BannerAsset banner;
            ColorLibrary colorLibrary;
            bool isCiv;
            bool isNeutral;
            try
            {
                actorAsset = pKingdom.getActorAsset();
                banner = actorAsset == null
                    ? null
                    : AssetManager.kingdom_banners_library?.get(actorAsset.banner_id);
                colorLibrary = AssetManager.kingdom_colors_library;
                isCiv = pKingdom.isCiv();
                isNeutral = pKingdom.isNeutral();
            }
            catch
            {
                return false;
            }

            int colorCount = colorLibrary?.list?.Count ?? 0;
            int backgroundCount = banner?.backgrounds?.Count ?? 0;
            int iconCount = banner?.icons?.Count ?? 0;
            if (!KingdomVisualRandomizationRules.ShouldRerollNewCivVisuals(
                    pHasKingdom: true,
                    pIsCivilized: isCiv,
                    pIsNeutral: isNeutral,
                    pColorCount: colorCount,
                    pBackgroundCount: backgroundCount,
                    pIconCount: iconCount))
                return false;

            bool changed = false;
            bool colorChanged = false;
            int colorIndex = PickColorIndex(pKingdom, actorAsset,
                colorLibrary, pKingdom.data.color_id);
            if (colorIndex >= 0 && colorIndex < colorCount && colorIndex != pKingdom.data.color_id)
            {
                ColorAsset color = colorLibrary.getColorByIndex(colorIndex);
                if (color != null)
                {
                    try { colorChanged = pKingdom.updateColor(color); }
                    catch
                    {
                        pKingdom.data.setColorID(colorIndex);
                        colorChanged = true;
                    }
                    changed |= colorChanged;
                }
            }

            int background = KingdomVisualRandomizationRules.NormalizeVisualIndex(
                Rng.Next(), pKingdom.data.banner_background_id, backgroundCount);
            if (background >= 0 && background != pKingdom.data.banner_background_id)
            {
                pKingdom.data.banner_background_id = background;
                changed = true;
            }

            int icon = KingdomVisualRandomizationRules.NormalizeVisualIndex(
                Rng.Next(), pKingdom.data.banner_icon_id, iconCount);
            if (icon >= 0 && icon != pKingdom.data.banner_icon_id)
            {
                pKingdom.data.banner_icon_id = icon;
                changed = true;
            }

            if (changed || colorChanged)
                MetaColorCacheService.RefreshKingdomAfterGeneratedColor(
                    pKingdom);
            return changed;
        }

        private static int PickColorIndex(Kingdom pKingdom, ActorAsset pActorAsset, ColorLibrary pLibrary,
            int pCurrentColorId)
        {
            if (pLibrary?.list == null || pLibrary.list.Count == 0) return -1;

            var preferred = new List<int>();
            var main = new List<int>();
            var bonus = new List<int>();
            var fallback = new List<int>();
            HashSet<int> usedColorIds = CollectUsedColorIds(pKingdom);
            int count = pLibrary.list.Count;

            for (int i = 0; i < count; i++)
            {
                ColorAsset color = pLibrary.list[i];
                if (color == null) continue;
                if (count > 1 && i == pCurrentColorId) continue;
                if (usedColorIds.Contains(i)) continue;

                if (pActorAsset?.preferred_colors != null && pActorAsset.preferred_colors.Contains(color.id))
                    preferred.Add(i);
                else if (color.favorite)
                    main.Add(i);
                else
                    bonus.Add(i);
            }

            if (preferred.Count > 0) return preferred[Rng.Next(preferred.Count)];
            if (main.Count > 0) return main[Rng.Next(main.Count)];
            if (bonus.Count > 0) return bonus[Rng.Next(bonus.Count)];

            for (int i = 0; i < count; i++)
            {
                if (count > 1 && i == pCurrentColorId) continue;
                fallback.Add(i);
            }
            if (fallback.Count > 0) return fallback[Rng.Next(fallback.Count)];
            return pCurrentColorId >= 0 && pCurrentColorId < count ? pCurrentColorId : 0;
        }

        private static HashSet<int> CollectUsedColorIds(Kingdom pKingdom)
        {
            var used = new HashSet<int>();
            try
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom == null || kingdom == pKingdom || kingdom.data == null) continue;
                    if (kingdom.data.color_id >= 0)
                        used.Add(kingdom.data.color_id);
                }
            }
            catch
            {
            }

            try
            {
                foreach (Alliance alliance in World.world.alliances)
                {
                    if (alliance?.data == null) continue;
                    if (alliance.data.color_id >= 0)
                        used.Add(alliance.data.color_id);
                }
            }
            catch
            {
            }

            return used;
        }
    }
}
