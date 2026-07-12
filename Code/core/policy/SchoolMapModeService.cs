using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;
using AncientWarfare3.ui.windows;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class SchoolMapModeService
    {
        public const string POWER_ID = "aw_school_mapmode";
        private const double DirtyMinInterval = 0.25;
        private static readonly Dictionary<string, ColorAsset> Colors =
            new Dictionary<string, ColorAsset>(StringComparer.Ordinal);
        private static double _lastDirtyTime = -1d;
        private static int _windowDepth;
        private static bool _wasEnabledBeforeWindow;
        private static string _focusBeforeWindow = CourtSchoolId.None;

        public static string FocusSchoolId { get; private set; } = CourtSchoolId.None;

        public static bool IsActive() => AWMapModeCoordinator.IsActive(POWER_ID);

        public static void BeginWindowMode()
        {
            _windowDepth++;
            if (_windowDepth > 1) return;
            _wasEnabledBeforeWindow = IsOptionEnabled();
            _focusBeforeWindow = FocusSchoolId;
            if (!_wasEnabledBeforeWindow) SetOption(true);
            Prepare();
        }

        public static void EndWindowMode()
        {
            if (_windowDepth <= 0) return;
            _windowDepth--;
            if (_windowDepth > 0) return;
            SetOption(_wasEnabledBeforeWindow);
            FocusSchoolId = _wasEnabledBeforeWindow ? _focusBeforeWindow : CourtSchoolId.None;
            _wasEnabledBeforeWindow = false;
            _focusBeforeWindow = CourtSchoolId.None;
            DirtyMap();
        }

        public static void ProcessFrame()
        {
            CitySchoolSnapshotService.ProcessDirty(IsActive() ? 4 : 1);
            SchoolMapBottomBarController.ProcessFrame();
        }

        public static void SetFocus(string pSchoolId)
        {
            FocusSchoolId = CourtSchoolRegistry.Find(pSchoolId) == null
                ? CourtSchoolId.None
                : pSchoolId;
            DirtyMap();
        }

        public static string GetCityColorHex(City pCity)
        {
            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(pCity);
            if (snapshot == null) return SchoolMapModeRules.NeutralHex;
            return string.IsNullOrEmpty(FocusSchoolId)
                ? SchoolMapModeRules.OverviewHex(snapshot.DominantSchool)
                : SchoolMapModeRules.FocusHex(FocusSchoolId, snapshot.Share(FocusSchoolId));
        }

        public static ColorAsset GetColorAsset(City pCity)
        {
            string hex = GetCityColorHex(pCity);
            if (Colors.TryGetValue(hex, out ColorAsset cached) && cached != null) return cached;
            ColorAsset color = ColorAsset.tryMakeNewColorAsset(hex);
            color?.initColor();
            if (color != null) Colors[hex] = color;
            return color;
        }

        public static string BuildTooltip(City pCity)
        {
            if (pCity?.data == null) return "";
            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(pCity);
            if (snapshot == null || snapshot.TotalScore <= 0f)
                return AW_L10n.Text("aw_school_map_no_influence", "No school influence");

            var lines = new List<string>
            {
                AW_L10n.Text("aw_school_map_dominant", "Dominant") + ": " +
                GetSchoolDisplayName(snapshot.DominantSchool)
            };
            foreach (KeyValuePair<string, float> item in snapshot.Scores
                         .OrderByDescending(p => p.Value)
                         .ThenBy(p => RegistryOrder(p.Key))
                         .Take(3))
            {
                lines.Add(GetSchoolDisplayName(item.Key) + "  " +
                          Mathf.RoundToInt(item.Value / snapshot.TotalScore * 100f) + "%");
            }
            lines.Add(SchoolLandmarkService.Describe(pCity));
            return string.Join("\n", lines.ToArray());
        }

        public static bool InspectCity(WorldTile pTile, string pPowerId = null)
        {
            City city = pTile?.zone?.city;
            if (city?.data == null) return false;
            SchoolWindow.OpenCity(city.data.id);
            return true;
        }

        public static bool SelectCity(WorldTile pTile, string pPowerId = null)
        {
            return SelectCity(pTile?.zone?.city);
        }

        public static bool SelectCity(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() || !pCity.isAlive()) return false;
            SelectedUnit.clear();
            SelectedMetas.selected_city = pCity;
            SelectedObjects.setNanoObject(pCity);
            SchoolMapBottomBarController.Show(pCity);
            return true;
        }

        public static void Prepare()
        {
            try
            {
                if (World.world?.kingdoms != null)
                    foreach (Kingdom kingdom in World.world.kingdoms)
                        CitySchoolSnapshotService.MarkKingdomDirty(kingdom, pOnlyMissing: true);
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
            if (!MapModeDirtyThrottleRules.ShouldDirty(IsActive(), now, _lastDirtyTime,
                    DirtyMinInterval)) return;
            _lastDirtyTime = now;
            DirtyMap();
        }

        internal static string GetSchoolDisplayName(string pSchoolId)
        {
            CourtSchoolDefinition definition = CourtSchoolRegistry.Find(pSchoolId);
            return definition == null
                ? AW_L10n.Text("aw_court_school_none", "No school")
                : AW_L10n.Text(definition.NameKey, definition.Id);
        }

        private static int RegistryOrder(string pSchoolId)
        {
            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
                if (CourtSchoolRegistry.All[i].Id == pSchoolId) return i;
            return int.MaxValue;
        }

        private static void SetOption(bool pEnabled)
        {
            try
            {
                string optionId = AWMapModeMetaRules.ResolveOptionId(POWER_ID);
                if (PlayerConfig.dict.TryGetValue(optionId, out PlayerOptionData data))
                    data.boolVal = pEnabled;
            }
            catch { }
        }

        private static bool IsOptionEnabled()
        {
            try
            {
                string optionId = AWMapModeMetaRules.ResolveOptionId(POWER_ID);
                return PlayerConfig.dict.TryGetValue(optionId, out PlayerOptionData data) && data.boolVal;
            }
            catch { return false; }
        }
    }
}
