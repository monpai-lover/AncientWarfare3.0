using AncientWarfare3.core.lineage;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal sealed class MandateDynastyMapLayer : MapLayer
    {
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private readonly Color _spriteColor = new Color(1f, 1f, 1f, 0.68f);
        private bool _dirty = true;
        private bool _wasActive;

        public void MarkDirty()
        {
            _dirty = true;
        }

        public void HideImmediate()
        {
            if (sprRnd == null) sprRnd = GetComponent<SpriteRenderer>();
            if (sprRnd != null) sprRnd.enabled = false;
        }

        public override void update(float pElapsed)
        {
            if (sprRnd == null) sprRnd = GetComponent<SpriteRenderer>();
            if (sprRnd == null || MapBox.width <= 0 || MapBox.height <= 0) return;
            if (pixels == null) createTextureNew();

            bool active = MandateDynastyMapModeService.IsActive();
            if (!active)
            {
                sprRnd.enabled = false;
                _wasActive = false;
                return;
            }

            sprRnd.enabled = true;
            sprRnd.color = _spriteColor;
            if (_dirty || !_wasActive)
                RedrawAll();
            _wasActive = true;
        }

        private void RedrawAll()
        {
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || tiles.Length == 0 || pixels == null)
            {
                _dirty = false;
                return;
            }
            if (pixels.Length != tiles.Length)
            {
                createTextureNew();
                if (pixels == null || pixels.Length != tiles.Length)
                {
                    _dirty = false;
                    return;
                }
            }

            var kingdomColorCache = new Dictionary<long, Color32>();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = GetTileColor(i < tiles.Length ? tiles[i] : null, kingdomColorCache);
            updatePixels();
            _dirty = false;
        }

        private static Color32 GetTileColor(WorldTile pTile, Dictionary<long, Color32> pKingdomColorCache)
        {
            City city = pTile?.zone?.city;
            if (city?.data == null || city.isRekt()) return Clear;
            Kingdom kingdom = city.kingdom;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return Clear;
            if (pKingdomColorCache.TryGetValue(kingdom.id, out Color32 cached)) return cached;
            Color32 color = MandateService.GetDynastyTileColor(city);
            pKingdomColorCache[kingdom.id] = color;
            return color;
        }
    }
}
