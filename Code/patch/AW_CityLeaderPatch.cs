using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using ai;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityLeaderPatch
    {
        internal static int FillVacanciesAfterCivilServiceExam(
            Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
            int attempts = 0;
            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || city.isRekt()) continue;
                bool shouldAttempt = CivilServiceExamRules.
                    ShouldAttemptCityVacancyFill(city.hasLeader(),
                        city.isGettingCaptured(), city.kingdom == pKingdom,
                        CivilServiceExamRules.CityVacancyFillBudget - attempts);
                if (!shouldAttempt) continue;
                attempts++;
                CheckFindLeader_Prefix(city);
                if (attempts >= CivilServiceExamRules.CityVacancyFillBudget)
                    break;
            }
            return attempts;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityBehCheckLeader), "checkFindLeader")]
        public static bool CheckFindLeader_Prefix(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return false;
            if (pCity.hasLeader())
            {
                if (IsLiveCityLeader(pCity, pCity.leader) ||
                    pCity.isGettingCaptured()) return false;
                try { pCity.removeLeader(); }
                catch { return false; }
            }
            if (pCity.isGettingCaptured()) return false;
            Kingdom kingdom = pCity.kingdom;
            LocalCourtAppointmentService.ReconcileCity(kingdom, pCity, 1,
                Date.getCurrentYear(), out _, out _);
            return false;
        }

        private static bool IsLiveCityLeader(City pCity, Actor pLeader)
        {
            return pLeader?.data != null && !pLeader.isRekt() &&
                   pLeader.isAlive() && pLeader.city == pCity &&
                   pLeader.kingdom == pCity.kingdom && pLeader.isCityLeader();
        }
    }
}
