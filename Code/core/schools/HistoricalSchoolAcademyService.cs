using AncientWarfare3.content.schools;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolAcademyService
    {
        public static void Init()
        {
            HistoricalSchoolVenueProvider.SetAcademySource(
                new HistoricalSchoolAcademyVenueSource());
        }

        public static Building FindUsable(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return null;
            Building academy = pCity.getBuildingOfType(
                SchoolAcademyBuildingContent.BuildingTypeId,
                pCountOnlyFinished: true);
            return IsUsable(academy, pCity) ? academy : null;
        }

        public static bool IsUsable(Building pAcademy, City pCity)
        {
            City attachedCity = null;
            try { attachedCity = pAcademy?.getCity(); }
            catch { }
            return HistoricalSchoolVenueRules.IsAcademyUsable(
                buildingExists: pAcademy != null,
                buildingUsable: pAcademy?.isUsable() == true,
                underConstruction: pAcademy?.isUnderConstruction() == true,
                attachedToCity: attachedCity?.data != null && !attachedCity.isRekt(),
                belongsToRequestedCity: ReferenceEquals(attachedCity, pCity));
        }

        public static bool IsInside(Actor pActor, Building pAcademy)
        {
            return pActor?.data != null && pAcademy != null &&
                   pActor.is_inside_building &&
                   ReferenceEquals(pActor.inside_building, pAcademy);
        }

        public static void Exit(Actor pActor, Building pAcademy)
        {
            if (pActor == null || pAcademy == null) return;
            if (ReferenceEquals(pActor.beh_building_target, pAcademy))
                pActor.beh_building_target = null;
            if (IsInside(pActor, pAcademy)) pActor.exitBuilding();
        }
    }

    internal sealed class HistoricalSchoolAcademyVenueSource :
        IHistoricalSchoolVenueSource
    {
        public bool TryFind(
            City pCity,
            Actor pActor,
            string pSchoolId,
            HistoricalSchoolVenueKind pKind,
            out WorldTile pPrimary,
            out WorldTile pSecondary,
            out Building pAcademy)
        {
            pPrimary = null;
            pSecondary = null;
            pAcademy = null;
            if (!HistoricalSchoolVenueRules.RequiresAcademy(pKind)) return false;
            pAcademy = HistoricalSchoolAcademyService.FindUsable(pCity);
            if (pAcademy?.current_tile == null)
            {
                pAcademy = null;
                return false;
            }
            pPrimary = pAcademy.current_tile;
            if (pKind == HistoricalSchoolVenueKind.Debate) pSecondary = pPrimary;
            return true;
        }
    }
}
