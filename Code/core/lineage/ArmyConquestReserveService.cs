using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
#if !AW3_RULES_TESTS
    internal static class ArmyConquestReserveService
    {
        internal static int Get(Army pArmy)
        {
            if (pArmy?.data == null) return 0;
            try
            {
                pArmy.data.get(LineageKeys.AW_ARMY_CONQUEST_RESERVE,
                    out int reserve, 0);
                return Math.Max(0, reserve);
            }
            catch { return 0; }
        }

        internal static int GrantForConqueredCity(Army pArmy, City pCity,
            long pWarId)
        {
            if (pArmy?.data == null || pCity?.data == null) return 0;
            long cityId = pCity.id;
            if (cityId < 0L || HasGrantedCity(pArmy, cityId)) return 0;
            if (pWarId >= 0L)
            {
                try
                {
                    pCity.data.get(
                        LineageKeys.AW_CITY_CONQUEST_RESERVE_LAST_WAR_ID,
                        out long lastWarId, -1L);
                    if (lastWarId == pWarId) return 0;
                }
                catch { }
            }
            int population;
            try { population = Math.Max(0, pCity.getPopulationPeople()); }
            catch { population = 0; }
            int grant = ArmyConquestReserveRules.GrantForPopulation(population);
            int current = Get(pArmy);
            int next = ArmyConquestReserveRules.Add(current, grant);
            try
            {
                pArmy.data.set(LineageKeys.AW_ARMY_CONQUEST_RESERVE, next);
                MarkGrantedCity(pArmy, cityId);
                if (pWarId >= 0L)
                    pCity.data.set(
                        LineageKeys.AW_CITY_CONQUEST_RESERVE_LAST_WAR_ID,
                        pWarId);
                return next - current;
            }
            catch { return 0; }
        }

        internal static int Consume(Army pArmy, int pRequested)
        {
            if (pArmy?.data == null || pRequested <= 0) return 0;
            int next = ArmyConquestReserveRules.Consume(Get(pArmy),
                pRequested, out int consumed);
            if (consumed <= 0) return 0;
            try
            {
                pArmy.data.set(LineageKeys.AW_ARMY_CONQUEST_RESERVE, next);
                return consumed;
            }
            catch { return 0; }
        }

        internal static void Refund(Army pArmy, int pCount)
        {
            if (pArmy?.data == null || pCount <= 0) return;
            int next = ArmyConquestReserveRules.Add(Get(pArmy), pCount);
            try { pArmy.data.set(LineageKeys.AW_ARMY_CONQUEST_RESERVE, next); }
            catch { }
        }

        private static bool HasGrantedCity(Army pArmy, long pCityId)
        {
            string encoded = ReadCityIds(pArmy);
            if (encoded.Length == 0) return false;
            string token = pCityId.ToString();
            string[] parts = encoded.Split(',');
            for (int i = 0; i < parts.Length; i++)
                if (string.Equals(parts[i], token,
                        StringComparison.Ordinal)) return true;
            return false;
        }

        private static void MarkGrantedCity(Army pArmy, long pCityId)
        {
            string encoded = ReadCityIds(pArmy);
            string token = pCityId.ToString();
            if (encoded.Length == 0) encoded = token;
            else encoded = encoded + "," + token;
            pArmy.data.set(LineageKeys.AW_ARMY_CONQUEST_RESERVE_CITY_IDS,
                encoded);
        }

        private static string ReadCityIds(Army pArmy)
        {
            try
            {
                pArmy.data.get(
                    LineageKeys.AW_ARMY_CONQUEST_RESERVE_CITY_IDS,
                    out string encoded, string.Empty);
                return encoded ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
#endif
}
