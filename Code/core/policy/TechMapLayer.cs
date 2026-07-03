using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal sealed class TechMapLayer : MapLayer
    {
        private const float RefreshInterval = 1.0f;
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        private bool _dirty = true;
        private bool _wasActive;
        private float _refreshTimer;
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

            _refreshTimer -= pElapsed;
            if (_dirty || !_wasActive || _refreshTimer <= 0f)
            {
                RedrawAll();
                _refreshTimer = RefreshInterval;
            }

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

            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = GetTileColor(i < tiles.Length ? tiles[i] : null);

            updatePixels();
            _dirty = false;
        }

        private static Color32 GetTileColor(WorldTile pTile)
        {
            Kingdom kingdom = GetKingdom(pTile);
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return Clear;
            if (!KingdomPolicyService.CanUsePolicySystem(kingdom)) return Clear;

            ColorAsset asset = TechMapModeService.GetColor(kingdom, null);
            if (asset == null) return Clear;

            Color32 color = asset.getColorMain32();
            color.a = 215;
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
