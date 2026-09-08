using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class MilitaryGovernorateAdministrationService
    {
        public static bool CanReclaimCity(Kingdom pSuzerain,
            Kingdom pGovernorate, City pCity)
        {
            if (!IsDirectGovernorate(pSuzerain, pGovernorate) ||
                pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom != pGovernorate ||
                pGovernorate.cities == null)
                return false;
            bool isSeat = pGovernorate.capital == pCity;
            return MilitaryGovernorateRules.CanReclaimCity(
                true, true, isSeat, LiveCityCount(pGovernorate));
        }

        public static bool TryReclaimCity(Kingdom pSuzerain,
            Kingdom pGovernorate, City pCity, out string pReason)
        {
            pReason = "invalid_target";
            if (!CanReclaimCity(pSuzerain, pGovernorate, pCity)) return false;
            try
            {
                bool isSeat = pGovernorate.capital == pCity;
                if (isSeat && LiveCityCount(pGovernorate) == 1)
                    return TryEndGovernorate(pSuzerain, pGovernorate,
                        out pReason);
                pCity.joinAnotherKingdom(pSuzerain, pCaptured: false,
                    pRebellion: false);
                if (pCity.kingdom != pSuzerain)
                {
                    pReason = "transfer_failed";
                    return false;
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Military governorate city reclaim failed: " +
                                    error.Message);
                pReason = "transfer_failed";
                return false;
            }
            VassalService.NotifyTerritoryChanged(pGovernorate, pSuzerain);
            pReason = "";
            return true;
        }

        public static bool CanEndGovernorate(Kingdom pSuzerain,
            Kingdom pGovernorate)
        {
            return IsDirectGovernorate(pSuzerain, pGovernorate) &&
                   LiveCityCount(pGovernorate) > 0;
        }

        public static bool TryEndGovernorate(Kingdom pSuzerain,
            Kingdom pGovernorate, out string pReason)
        {
            pReason = "invalid_target";
            if (!CanEndGovernorate(pSuzerain, pGovernorate)) return false;
            var cities = new List<City>();
            foreach (City city in pGovernorate.cities)
                if (city?.data != null && !city.isRekt()) cities.Add(city);
            var moved = new List<City>();
            try
            {
                foreach (City city in cities)
                {
                    city.joinAnotherKingdom(pSuzerain, pCaptured: false,
                        pRebellion: false);
                    if (city.kingdom != pSuzerain)
                        throw new InvalidOperationException("city transfer failed");
                    moved.Add(city);
                }
                if (!VassalService.EndVassal(pGovernorate,
                        "centralized_reclaim"))
                    throw new InvalidOperationException("relation end failed");
            }
            catch (Exception error)
            {
                for (int i = 0; i < moved.Count; i++)
                    try { moved[i].joinAnotherKingdom(pGovernorate,
                        pCaptured: false, pRebellion: false); }
                    catch { }
                ModClass.LogWarning("Military governorate relation reclaim failed: " +
                                    error.Message);
                pReason = "reclaim_failed";
                return false;
            }
            VassalService.NotifyTerritoryChanged(pGovernorate, pSuzerain);
            pReason = "";
            return true;
        }

        private static bool IsDirectGovernorate(Kingdom pSuzerain,
            Kingdom pGovernorate)
        {
            return pSuzerain?.data != null && pGovernorate?.data != null &&
                   !pSuzerain.isRekt() && !pGovernorate.isRekt() &&
                   pGovernorate != pSuzerain &&
                   VassalService.GetSuzerain(pGovernorate) == pSuzerain &&
                   VassalService.GetSubjectKind(pGovernorate) ==
                       VassalSubjectKind.MilitaryGovernorate &&
                   MilitaryGovernorateStore.TryGetActive(pGovernorate, out _);
        }

        private static int LiveCityCount(Kingdom pKingdom)
        {
            int count = 0;
            if (pKingdom?.cities == null) return 0;
            foreach (City city in pKingdom.cities)
                if (city?.data != null && !city.isRekt()) count++;
            return count;
        }
    }
}
