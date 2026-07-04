using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class WarClaimMapModeService
    {
        public const string POWER_ID = "aw_claim_mapmode";

        private static WarClaimMapLayer _layer;
        private static long _focusedKingdomId = -1;

        public static bool IsActive()
        {
            return IsOptionActive() || IsSelectedPower();
        }

        public static void SetFocus(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return;
            if (_focusedKingdomId == pKingdom.id) return;
            _focusedKingdomId = pKingdom.id;
            DirtyMap();
        }

        public static Kingdom GetFocusedKingdom()
        {
            Kingdom selected = SelectedMetas.selected_kingdom;
            if (selected?.data != null && selected.isCiv() && !selected.isNeutral() && !selected.isRekt())
                _focusedKingdomId = selected.id;

            if (_focusedKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(_focusedKingdomId); }
            catch { return null; }
        }

        public static string BuildTooltip(Kingdom pHover)
        {
            SetFocus(pHover);
            Kingdom focus = GetFocusedKingdom();
            return WarTerritoryService.BuildClaimTooltip(focus, pHover);
        }

        public static void DirtyMap()
        {
            try
            {
                EnsureLayer();
                _layer?.MarkDirty();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
        }

        public static void DirtyMapIfActive()
        {
            if (IsActive()) DirtyMap();
        }

        public static void EnsureLayer()
        {
            if (World.world == null) return;
            _layer = World.world.GetComponentInChildren<WarClaimMapLayer>();
            if (_layer == null)
            {
                var obj = new GameObject("[layer]AW3 Claim Map", typeof(SpriteRenderer), typeof(WarClaimMapLayer));
                obj.transform.SetParent(World.world.transform, false);
                _layer = obj.GetComponent<WarClaimMapLayer>();
                _layer.create();
            }
            RegisterLayer(_layer);
            _layer.HideImmediate();
        }

        private static bool IsOptionActive()
        {
            try { return PlayerConfig.optionBoolEnabled(POWER_ID); }
            catch { return false; }
        }

        private static bool IsSelectedPower()
        {
            try { return World.world != null && World.world.isSelectedPower(POWER_ID); }
            catch { return false; }
        }

        private static void RegisterLayer(MapLayer pLayer)
        {
            if (pLayer == null || World.world == null) return;
            try
            {
                var list = AccessTools.Field(typeof(MapBox), "_map_layers")?.GetValue(World.world) as List<MapLayer>;
                if (list != null && !list.Contains(pLayer)) list.Add(pLayer);
            }
            catch { }
        }
    }
}
