using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CityLeaderVacancyRepairService
    {
        private const int MaximumAttempts = 2;

        internal static void Request(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() || pCity.kingdom?.data == null)
                return;
            long kingdomId = pCity.kingdom.id;
            long cityId = pCity.data.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "city-leader-vacancy:" + kingdomId + ":" + cityId,
                DeferredWorkClass.CriticalRuntime,
                () => Repair(kingdomId, cityId, 0));
        }

        private static void Repair(long pKingdomId, long pCityId, int pAttempt)
        {
            Kingdom kingdom = FindKingdom(pKingdomId);
            City city = FindCity(pCityId);
            if (kingdom?.data == null || city?.data == null || kingdom.isRekt() ||
                city.isRekt() || city.kingdom != kingdom || city.hasLeader() ||
                city.isGettingCaptured()) return;

            Actor candidate = PickLocalOfficer(city, kingdom);
            if (candidate != null && TryPromote(candidate, city, kingdom)) return;

            // The native/civil-service selector remains the authoritative fallback.
            AncientWarfare3.patch.AW_CityLeaderPatch.CheckFindLeader_Prefix(city);
            if (city.hasLeader()) return;
            if (pAttempt + 1 < MaximumAttempts)
                DeferredRuntimeWorkService.EnqueueCoalesced(
                    "city-leader-vacancy:" + pKingdomId + ":" + pCityId,
                    DeferredWorkClass.CriticalRuntime,
                    () => Repair(pKingdomId, pCityId, pAttempt + 1));
        }

        private static Actor PickLocalOfficer(City pCity, Kingdom pKingdom)
        {
            var candidates = new List<Actor>();
            try
            {
                foreach (Actor actor in pCity.getUnits())
                {
                    if (!IsEligible(actor, pCity, pKingdom)) continue;
                    candidates.Add(actor);
                }
            }
            catch { return null; }
            return candidates.OrderBy(actor => OfficeGrade(pKingdom, pCity, actor))
                .ThenByDescending(actor => OfficialCareerStateService.
                    EstimateAppointmentRankFast(actor, pKingdom))
                .ThenByDescending(OfficialCareerStateService.ReadMeritFast)
                .ThenByDescending(MainAbility)
                .ThenBy(actor => actor.data.id)
                .FirstOrDefault();
        }

        private static bool IsEligible(Actor pActor, City pCity, Kingdom pKingdom)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                pActor.city != pCity || pActor.kingdom != pKingdom ||
                pActor.isKing() || pActor.isCityLeader() || !pActor.isAdult() ||
                !pActor.isSexMale()) return false;
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_CITY_ID, out long cityId, -1L);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            return layer == CourtOfficeLayer.City && cityId == pCity.data.id &&
                   !string.IsNullOrEmpty(office) &&
                   CivilServiceQualificationService.CanReceiveFormalCivilAppointment(
                       pActor, pKingdom, CourtOfficeLayer.City,
                       CourtService.ResolveCityOffice(pKingdom, pCity),
                       pAllowVacancyPromotion: true,
                       pAllowLocalLowerQualification: true);
        }

        private static bool TryPromote(Actor pActor, City pCity, Kingdom pKingdom)
        {
            using (GovernorRotationRuntimeScope.Enter())
            {
                CourtService.TryDismissOfficer(pActor, pKingdom,
                    "promoted_city_leader");
                pActor.joinCity(pCity);
                pCity.setLeader(pActor, pNew: true);
                return CourtService.TryAssignCityGovernor(pActor, pKingdom, pCity,
                    pVacancyPromotion: true);
            }
        }

        private static int OfficeGrade(Kingdom pKingdom, City pCity, Actor pActor)
        {
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            return OfficialCareerStateService.OfficeGradeForOffice(pKingdom,
                CourtOfficeLayer.City, office, pCity);
        }

        private static int MainAbility(Actor pActor)
        {
            try
            {
                return (int)Math.Max(Math.Max(pActor.stats?["intelligence"] ?? 0f,
                        pActor.stats?["stewardship"] ?? 0f),
                    Math.Max(pActor.stats?["warfare"] ?? 0f,
                        pActor.stats?["diplomacy"] ?? 0f));
            }
            catch { return 0; }
        }

        private static Kingdom FindKingdom(long pId)
        {
            try { return World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }

        private static City FindCity(long pId)
        {
            try { return World.world?.cities?.get(pId); }
            catch { return null; }
        }
    }
}
