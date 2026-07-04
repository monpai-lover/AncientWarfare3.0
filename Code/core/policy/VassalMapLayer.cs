using AncientWarfare3.core.lineage;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal sealed class VassalMapLayer : MapLayer
    {
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        private bool _dirty = true;
        private bool _wasActive;
        private readonly Color _spriteColor = new Color(1f, 1f, 1f, 0.66f);

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
            if (sprRnd == null) return;
            if (MapBox.width <= 0 || MapBox.height <= 0) return;

            if (pixels == null)
                createTextureNew();

            bool active = VassalMapModeService.IsActive();
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

            var colorCache = new Dictionary<long, Color32>();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = GetTileColor(i < tiles.Length ? tiles[i] : null, colorCache);

            updatePixels();
            _dirty = false;
        }

        private static Color32 GetTileColor(WorldTile pTile, Dictionary<long, Color32> pColorCache)
        {
            Kingdom kingdom = GetKingdom(pTile);
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return Clear;
            if (pColorCache.TryGetValue(kingdom.id, out Color32 cached)) return cached;

            ColorAsset asset = VassalService.GetMapColor(kingdom, null);
            if (asset == null)
            {
                pColorCache[kingdom.id] = Clear;
                return Clear;
            }

            Color32 color = asset.getColorMain32();
            color.a = 218;
            pColorCache[kingdom.id] = color;
            return color;
        }

        private static Kingdom GetKingdom(WorldTile pTile)
        {
            TileZone zone = pTile?.zone;
            City city = zone?.city;
            if (city?.data == null || city.isRekt()) return null;
            return city.kingdom;
        }
    }
}
