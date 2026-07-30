using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.policy
{
    internal static class FeudatoryMapModeService
    {
        public const string POWER_ID = "aw_feudatory_mapmode";
        private const double DirtyMinimumInterval = 0.25;
        private static double _lastDirtyTime = -1.0;

        public static bool IsActive()
        {
            return AWMapModeCoordinator.IsActive(POWER_ID);
        }

        public static bool IsMandateCity(City pCity)
        {
            return pCity?.data != null && !pCity.isRekt() && pCity.isAlive() &&
                   IsMandateKingdom(pCity.kingdom);
        }

        public static bool IsMandateKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   MandateService.IsRuntimeMandateKingdom(pKingdom);
        }

        public static bool TryGetSnapshot(City pCity,
            out FeudatorySnapshot pSnapshot)
        {
            pSnapshot = null;
            return IsMandateCity(pCity) &&
                   FeudatoryService.TryGetByCity(pCity.id, out pSnapshot);
        }

        public static string GetColorHex(FeudatorySnapshot pSnapshot,
            Kingdom pParent)
        {
            if (pSnapshot == null) return HistoryColors.FromKingdom(pParent);
            return FeudatoryMapModeRules.ColorHex(pSnapshot.EmpireKingdomId,
                pSnapshot.FeudatoryId, HistoryColors.FromKingdom(pParent));
        }

        public static string BuildTooltip(City pCity)
        {
            if (!IsMandateCity(pCity)) return "";
            if (!TryGetSnapshot(pCity, out FeudatorySnapshot snapshot))
                return AW_L10n.Text("aw_feudatory_mapmode_direct",
                    "Direct imperial administration");

            string seat = string.IsNullOrEmpty(snapshot.SeatName)
                ? "#" + snapshot.SeatCityId
                : snapshot.SeatName;
            string prince = string.IsNullOrEmpty(snapshot.PrinceName)
                ? "#" + snapshot.PrinceActorId
                : snapshot.PrinceName;
            string princeTitle = DynasticTitleService.ResolveLivingTitle(
                snapshot.PrinceActorId);
            if (!string.IsNullOrEmpty(princeTitle))
                prince = princeTitle + "  " + prince;
            string feudatoryName = string.IsNullOrEmpty(
                snapshot.FeudatoryName)
                ? seat
                : snapshot.FeudatoryName;
            return feudatoryName +
                   "\n" + AW_L10n.Text("aw_feudatory_mapmode_prince", "Prince") +
                   ": " + prince +
                   "\n" + AW_L10n.Text("aw_feudatory_mapmode_seat", "Seat") +
                   ": " + seat +
                   "\n" + AW_L10n.Text("aw_feudatory_mapmode_cities", "Cities") +
                   ": " + snapshot.CityIds.Count +
                   "\n" + AW_L10n.Text("aw_feudatory_mapmode_autonomy", "Autonomy") +
                   ": " + snapshot.Autonomy +
                   "\n" + AW_L10n.Text("aw_feudatory_mapmode_loyalty", "Loyalty") +
                   ": " + snapshot.Loyalty;
        }

        public static bool SelectPrince(WorldTile pTile,
            string pPowerId = null)
        {
            City city = pTile?.zone?.city;
            if (!TryGetSnapshot(city, out FeudatorySnapshot snapshot))
                return false;
            Actor prince;
            try { prince = World.world?.units?.get(snapshot.PrinceActorId); }
            catch { prince = null; }
            if (prince?.data == null || prince.isRekt() ||
                !prince.isAlive())
                return false;
            MetaTypeAsset unitMeta = MetaType.Unit.getAsset();
            if (unitMeta == null) return false;
            ScrollWindow.finishAnimations();
            unitMeta.selectAndInspect(prince, pFromNameplate: false,
                pCheckNameplate: false, pClearAction: false);
            return true;
        }

        public static void DirtyMap()
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                AWMapModeMetaLibrary.ClearDynamicMetaCache();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.MapDirtyIndex, benchmark);
            }
        }

        public static void DirtyMapIfActive()
        {
            double now = LineageService.CurTime();
            if (!MapModeDirtyThrottleRules.ShouldDirty(IsActive(), now,
                    _lastDirtyTime, DirtyMinimumInterval))
                return;
            _lastDirtyTime = now;
            DirtyMap();
        }

        internal static void ResetRuntime()
        {
            _lastDirtyTime = -1.0;
        }
    }
}
