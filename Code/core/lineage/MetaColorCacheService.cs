using System;
using System.Reflection;

namespace AncientWarfare3.core.lineage
{
    internal static class MetaColorCacheService
    {
        private static bool _fieldResolved;
        private static FieldInfo _cachedColorField;

        public static void RefreshKingdomAfterGeneratedColor(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;

            int colorCount = 0;
            try { colorCount = AssetManager.kingdom_colors_library?.list?.Count ?? 0; }
            catch { }

            if (!MetaColorCacheRules.ShouldRefreshAfterGeneratedColor(
                    pHasMetaObject: true,
                    pColorId: pKingdom.data.color_id,
                    pColorCount: colorCount))
                return;

            try
            {
                ColorAsset color = AssetManager.kingdom_colors_library.getColorByIndex(pKingdom.data.color_id);
                pKingdom.updateColor(color);
            }
            catch
            {
            }
            ClearCachedColor(pKingdom);
            MarkKingdomUnitsDirty(pKingdom);
            try { World.world?.zone_calculator?.dirtyAndClear(); }
            catch { }
        }

        internal static bool ClearCachedColor(object pMetaObject)
        {
            if (pMetaObject == null) return false;
            FieldInfo field = GetCachedColorField(pMetaObject.GetType());
            if (field == null) return false;
            try
            {
                field.SetValue(pMetaObject, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static FieldInfo GetCachedColorField(Type pType)
        {
            if (_fieldResolved) return _cachedColorField;
            _fieldResolved = true;

            Type current = pType;
            while (current != null)
            {
                FieldInfo field = current.GetField("_cached_color", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    _cachedColorField = field;
                    break;
                }
                current = current.BaseType;
            }

            return _cachedColorField;
        }

        private static void MarkKingdomUnitsDirty(Kingdom pKingdom)
        {
            try
            {
                foreach (Actor actor in pKingdom.getUnits())
                {
                    if (actor == null) continue;
                    actor.dirty_sprite_main = true;
                    actor.dirty_sprite_head = true;
                }
            }
            catch
            {
            }
        }
    }
}
