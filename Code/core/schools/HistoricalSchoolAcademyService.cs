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
            if (!HistoricalSchoolXiaAccessService.CanHostAcademy(pCity))
                return null;
            try
            {
                System.Collections.Generic.List<Building> academies =
                    pCity.getBuildingListOfType(
                        SchoolAcademyBuildingContent.BuildingTypeId);
                if (academies == null) return null;
                for (int i = 0; i < academies.Count; i++)
                {
                    Building academy = academies[i];
                    if (IsUsable(academy, pCity)) return academy;
                }
            }
            catch { }
            return null;
        }

        public static bool HasLiveAcademy(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return false;
            try
            {
                System.Collections.Generic.List<Building> academies =
                    pCity.getBuildingListOfType(
                        SchoolAcademyBuildingContent.BuildingTypeId);
                if (academies == null) return false;
                for (int i = 0; i < academies.Count; i++)
                    if (IsLiveAcademyForCity(academies[i], pCity)) return true;
            }
            catch { }
            return false;
        }

        public static bool IsLiveAcademyForCity(Building pAcademy, City pCity)
        {
            bool isAcademy = pAcademy?.asset != null &&
                (pAcademy.asset.id == SchoolAcademyBuildingContent.BuildingId ||
                 pAcademy.asset.type == SchoolAcademyBuildingContent.BuildingTypeId);
            if (pCity?.data == null || pCity.isRekt() ||
                !HistoricalSchoolAcademyRepairRules.IsLiveAcademy(
                    pAcademy != null, isAcademy,
                    pAcademy?.isAlive() == true,
                    pAcademy?.isOnRemove() == true,
                    pAcademy?.isRemoved() == true,
                    pAcademy?.isRuin() == true,
                    pAcademy?.isUsable() == true,
                    pAcademy?.isAbandoned() == true)) return false;
            City attachedCity = null;
            try { attachedCity = pAcademy.getCity(); }
            catch { }
            if (ReferenceEquals(attachedCity, pCity)) return true;
            return attachedCity == null &&
                   ReferenceEquals(pAcademy.current_tile?.zone?.city, pCity);
        }

        public static bool IsAcademy(Building pBuilding)
        {
            return pBuilding?.asset != null &&
                   (pBuilding.asset.id == SchoolAcademyBuildingContent.BuildingId ||
                    pBuilding.asset.type ==
                    SchoolAcademyBuildingContent.BuildingTypeId);
        }

        public static bool IsUsable(Building pAcademy, City pCity)
        {
            if (!HistoricalSchoolXiaAccessService.CanHostAcademy(pCity))
                return false;
            if (!IsLiveAcademyForCity(pAcademy, pCity)) return false;
            City attachedCity = null;
            try { attachedCity = pAcademy?.getCity(); }
            catch { }
            bool attachedToRequestedCity =
                HistoricalSchoolVenueRules.IsAttachedToRequestedCity(
                    directAttachment: ReferenceEquals(attachedCity, pCity),
                    tileAttachment: attachedCity == null &&
                                    ReferenceEquals(
                                        pAcademy?.current_tile?.zone?.city,
                                        pCity));
            return HistoricalSchoolVenueRules.IsAcademyUsable(
                buildingExists: pAcademy != null,
                buildingUsable: pAcademy?.isUsable() == true,
                underConstruction: pAcademy?.isUnderConstruction() == true,
                attachedToCity: attachedToRequestedCity,
                belongsToRequestedCity: attachedToRequestedCity);
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
