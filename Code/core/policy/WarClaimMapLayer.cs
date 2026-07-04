using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal sealed class WarClaimMapLayer : MapLayer
    {
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 StrongClaim = new Color32(49, 172, 82, 224);
        private static readonly Color32 WeakClaim = new Color32(214, 180, 45, 224);
        private static readonly Color32 PendingClaim = new Color32(224, 122, 38, 224);

        private bool _dirty = true;
        private bool _wasActive;
        private readonly Color _spriteColor = new Color(1f, 1f, 1f, 0.72f);

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

            bool active = WarClaimMapModeService.IsActive();
            if (!active)
            {
                sprRnd.enabled = false;
                _wasActive = false;
                return;
            }

            sprRnd.enabled = true;
            sprRnd.color = _spriteColor;
            if (_dirty || !_wasActive) RedrawAll();
            _wasActive = true;
        }

        private void RedrawAll()
        {
            WorldTile[] tiles = World.world?.tiles_list;
            Kingdom focus = WarClaimMapModeService.GetFocusedKingdom();
            if (tiles == null || tiles.Length == 0 || pixels == null || focus?.data == null)
            {
                ClearPixels();
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

            var cache = new Dictionary<long, Color32>();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = GetTileColor(focus, i < tiles.Length ? tiles[i] : null, cache);

            updatePixels();
            _dirty = false;
        }

        private void ClearPixels()
        {
            if (pixels == null) return;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Clear;
            updatePixels();
        }

        private static Color32 GetTileColor(Kingdom pFocus, WorldTile pTile, Dictionary<long, Color32> pCache)
        {
            City city = pTile?.zone?.city;
            if (city?.data == null || city.isRekt()) return Clear;
            if (pCache.TryGetValue(city.data.id, out Color32 cached)) return cached;

            WarTerritoryService.TerritoryStatus status = WarTerritoryService.GetClaimStatus(pFocus, city);
            Color32 color = status.status switch
            {
                "strong_claim" => StrongClaim,
                "weak_claim" => WeakClaim,
                "pending_claim" => PendingClaim,
                _ => Clear
            };
            pCache[city.data.id] = color;
            return color;
        }
    }
}
