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
            CourtVacancyReconciliationService.RegisterCityVacancies(
                kingdom, pCity);
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
