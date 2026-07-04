using UnityEngine;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    internal sealed class TechMapLayer : MapLayer
    {
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        private bool _dirty = true;
        private bool _wasActive;
        private readonly Color _spriteColor = new Color(1f, 1f, 1f, 0.68f);

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

            bool active = TechMapModeService.IsActive();
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
            City city = GetCity(pTile);
            if (city?.data == null || city.isRekt()) return Clear;
            if (pColorCache.TryGetValue(city.id, out Color32 cached)) return cached;
            Color32 color = CityTechService.GetCityMapColor(city);
            pColorCache[city.id] = color;
            return color;
        }

        private static City GetCity(WorldTile pTile)
        {
            TileZone zone = pTile?.zone;
            City city = zone?.city;
            if (city?.data == null || city.isRekt()) return null;
            return city;
        }
    }
}
