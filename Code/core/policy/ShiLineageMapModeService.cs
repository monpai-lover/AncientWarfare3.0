using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.policy
{
    internal static class ShiLineageMapModeService
    {
        public const string POWER_ID = "aw_shi_lineage_mapmode";
        private const double DirtyMinInterval = .25d;
        private static readonly Dictionary<string, ColorAsset> Colors =
            new Dictionary<string, ColorAsset>(StringComparer.Ordinal);
        private static double _lastDirtyTime = -1d;

        public static long FocusShiId { get; private set; } = -1L;
        public static bool IsActive() => AWMapModeCoordinator.IsActive(POWER_ID);

        public static void ProcessFrame()
        {
            if (IsActive()) CityShiInfluenceSnapshotService.ProcessDirty(4);
            else if (CityShiInfluenceSnapshotService.HasPendingDemand)
                CityShiInfluenceSnapshotService.ProcessDirty(1, true);
            ShiLineageMapBottomBarController.ProcessFrame();
        }

        public static void SetFocus(long pShiId)
        {
            FocusShiId = pShiId >= 0L ? pShiId : -1L;
            DirtyMap();
        }

        public static string GetCityColorHex(City pCity)
        {
            CityShiInfluenceSnapshot snapshot =
                CityShiInfluenceSnapshotService.GetSnapshot(pCity);
            if (snapshot == null) return ShiLineageMapModeRules.NeutralHex;
            return FocusShiId < 0L
                ? ShiLineageMapModeRules.OverviewHex(snapshot.DominantShiId)
                : ShiLineageMapModeRules.FocusHex(FocusShiId,
                    snapshot.SharePerThousand(FocusShiId) / 1000f);
        }

        public static ColorAsset GetColorAsset(City pCity)
        {
            string hex = GetCityColorHex(pCity);
            if (Colors.TryGetValue(hex, out ColorAsset cached) && cached != null)
                return cached;
            ColorAsset color = ColorAsset.tryMakeNewColorAsset(hex);
            color?.initColor();
            if (color != null) Colors[hex] = color;
            return color;
        }

        public static string BuildTooltip(City pCity)
        {
            CityShiInfluenceSnapshot snapshot =
                CityShiInfluenceSnapshotService.GetSnapshot(pCity);
            if (snapshot == null || snapshot.TotalWeight <= 0)
                return AW_L10n.Text("aw_shi_map_no_influence", "No Shi influence");
            var lines = new List<string>
            {
                AW_L10n.Text("aw_shi_map_dominant", "Dominant") + ": " +
                DisplayName(snapshot.FindBranch(snapshot.DominantShiId))
            };
            int count = Math.Min(3, snapshot.Branches.Count);
            for (int i = 0; i < count; i++)
            {
                CityShiInfluenceBranch branch = snapshot.Branches[i];
                lines.Add(branch.DisplayName + "  " +
                    snapshot.SharePercent(branch.ShiId) + "%");
            }
            return string.Join("\n", lines.ToArray());
        }

        public static bool SelectCity(WorldTile pTile, string pPowerId = null)
        {
            return SelectCity(pTile?.zone?.city);
        }

        public static bool SelectCity(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() || !pCity.isAlive() ||
                ScrollWindow.getCurrentWindow() != null) return false;
            SelectedUnit.clear();
            SelectedMetas.selected_city = pCity;
            SelectedObjects.setNanoObject(pCity);
            ShiLineageMapBottomBarController.Show(pCity);
            return true;
        }

        public static void Prepare()
        {
            try
            {
                if (World.world?.kingdoms != null)
                    foreach (Kingdom kingdom in World.world.kingdoms)
                        CityShiInfluenceSnapshotService.MarkKingdomDirty(kingdom,
                            pOnlyMissing: true);
            }
            catch { }
            DirtyMap();
        }

        public static void DirtyMap()
        {
            try
            {
                AWMapModeMetaLibrary.ClearDynamicMetaCache();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
        }

        public static void DirtyMapIfActive()
        {
            double now = LineageService.CurTime();
            if (!MapModeDirtyThrottleRules.ShouldDirty(IsActive(), now,
                    _lastDirtyTime, DirtyMinInterval)) return;
            _lastDirtyTime = now;
            DirtyMap();
        }

        internal static void ResetRuntime()
        {
            Colors.Clear();
            FocusShiId = -1L;
            _lastDirtyTime = -1d;
            ShiLineageMapBottomBarController.Hide();
        }

        internal static string DisplayName(CityShiInfluenceBranch pBranch)
        {
            if (pBranch == null || pBranch.ShiId < 0L)
                return AW_L10n.Text("aw_shi_map_none", "No Shi");
            return pBranch.IsValid && !string.IsNullOrWhiteSpace(
                    pBranch.DisplayName)
                ? pBranch.DisplayName
                : AW_L10n.Text("aw_shi_map_unknown", "Unknown Shi");
        }
    }
}
