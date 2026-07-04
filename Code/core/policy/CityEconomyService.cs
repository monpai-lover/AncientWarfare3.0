using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.policy
{
    internal static class CityEconomyService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom)) return;
            if (!Ready) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.CITY_ECONOMY_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.CITY_ECONOMY_LAST_YEAR, year);

            foreach (City city in pKingdom.getCities())
                UpdateCity(pKingdom, city, year);
        }

        public static float GetPolicyContribution(Kingdom pKingdom)
        {
            return SumContribution(pKingdom, "POLICY_POINTS");
        }

        public static float GetTechContribution(Kingdom pKingdom)
        {
            return SumContribution(pKingdom, "TECH_POINTS");
        }

        private static void UpdateCity(Kingdom pKingdom, City pCity, int pYear)
        {
            if (pCity?.data == null || pCity.isRekt()) return;
            CityEconomyRole role = SelectRole(pKingdom, pCity);
            CityTechReport tech = CityTechService.GetCityReport(pCity);
            int population = SafePopulation(pCity);
            bool nonCore = IsNonCore(pKingdom, pCity);
            CityEconomyContribution contribution = CityEconomyRules.CalculateContribution(role, population,
                tech.adopted_count, tech.total_count, DistanceFromCapital(pKingdom, pCity),
                CountSlavePopulation(pCity), nonCore);
            Upsert(pKingdom, pCity, role, contribution, pYear);
        }

        private static CityEconomyRole SelectRole(Kingdom pKingdom, City pCity)
        {
            CityTechReport tech = CityTechService.GetCityReport(pCity);
            return CityEconomyRules.SelectRole(pKingdom.capital == pCity, SafePopulation(pCity),
                CountBuildings(pCity, "market"), CountBuildings(pCity, "farm"),
                CountBuildings(pCity, "barracks"), CountBuildings(pCity, "workshop"),
                tech.adopted_count, tech.total_count, IsBorderCity(pKingdom, pCity), IsOccupiedUnrest(pKingdom, pCity));
        }

        private static void Upsert(Kingdom pKingdom, City pCity, CityEconomyRole pRole,
            CityEconomyContribution pContribution, int pYear)
        {
            string role = pRole.ToString();
            string previous = ReadRole(pCity.id);
            bool existed = !string.IsNullOrEmpty(previous);
            var constraints = new List<SimpleColumnConstraint>
            {
                SimpleColumnConstraint.CreateEq("CITY_ID", pCity.id)
            };

            if (existed)
            {
                DB.UpdateValue(CityEconomyStateTableItem.GetTableName(), constraints,
                    ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                    ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                    ColumnVal.Create("PREVIOUS_ROLE", previous),
                    ColumnVal.Create("ROLE", role),
                    ColumnVal.Create("POLICY_POINTS", pContribution.PolicyPoints),
                    ColumnVal.Create("TECH_POINTS", pContribution.TechPoints),
                    ColumnVal.Create("TAX_VALUE", pContribution.TaxValue),
                    ColumnVal.Create("MANPOWER", pContribution.Manpower),
                    ColumnVal.Create("FOOD_STABILITY", pContribution.FoodStability),
                    ColumnVal.Create("UNREST_RISK", pContribution.UnrestRisk),
                    ColumnVal.Create("LAST_YEAR", pYear),
                    ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
            }
            else
            {
                DB.Insert(CityEconomyStateTableItem.GetTableName(),
                    ColumnVal.Create("CITY_ID", pCity.id),
                    ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                    ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                    ColumnVal.Create("ROLE", role),
                    ColumnVal.Create("PREVIOUS_ROLE", ""),
                    ColumnVal.Create("POLICY_POINTS", pContribution.PolicyPoints),
                    ColumnVal.Create("TECH_POINTS", pContribution.TechPoints),
                    ColumnVal.Create("TAX_VALUE", pContribution.TaxValue),
                    ColumnVal.Create("MANPOWER", pContribution.Manpower),
                    ColumnVal.Create("FOOD_STABILITY", pContribution.FoodStability),
                    ColumnVal.Create("UNREST_RISK", pContribution.UnrestRisk),
                    ColumnVal.Create("LAST_YEAR", pYear),
                    ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
            }

            RecordEconomyMilestone(pKingdom, pCity, previous, role, pContribution, existed);
        }

        private static string ReadRole(long pCityId)
        {
            if (!Ready || pCityId < 0) return "";
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT ROLE FROM " + CityEconomyStateTableItem.GetTableName() + " WHERE CITY_ID=@city";
                cmd.Parameters.AddWithValue("@city", pCityId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? "" : Convert.ToString(value);
            }
            catch
            {
                return "";
            }
        }

        private static float SumContribution(Kingdom pKingdom, string pColumn)
        {
            if (pKingdom?.data == null || !Ready) return 0f;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT SUM(" + pColumn + ") FROM " +
                                  CityEconomyStateTableItem.GetTableName() + " WHERE KINGDOM_ID=@kingdom";
                cmd.Parameters.AddWithValue("@kingdom", pKingdom.id);
                object value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value) return 0f;
                return Convert.ToSingle(value);
            }
            catch
            {
                return 0f;
            }
        }

        private static void RecordEconomyMilestone(Kingdom pKingdom, City pCity, string pPreviousRole,
            string pRole, CityEconomyContribution pContribution, bool pExisted)
        {
            if (pCity?.data == null || pKingdom?.data == null) return;
            bool roleChanged = pExisted && !string.Equals(pPreviousRole, pRole, StringComparison.Ordinal);
            bool majorTax = pContribution.TaxValue >= 25f;
            if (pExisted && !roleChanged && !majorTax) return;

            HistoryText text = HistoryText.City(pCity, pKingdom) +
                               HistoryText.PlainText(" \u57ce\u5e02\u7ecf\u6d4e\u5b9a\u578b\u4e3a " + pRole +
                                                     "\uff0c\u7a0e\u6536 " + Math.Round(pContribution.TaxValue, 1));
            HistoryWriter.RecordCity(pCity, pKingdom, "city_economy_role", text, HistoryTarget.City(pCity));

            if (!pExisted || roleChanged)
                HistoryWriter.RecordKingdom(pKingdom, "city_economy_role",
                    HistoryText.Kingdom(pKingdom) + HistoryText.PlainText(" \u8c03\u6574\u57ce\u5e02\u7ecf\u6d4e\uff1a") +
                    HistoryText.City(pCity, pKingdom) + HistoryText.PlainText(" -> " + pRole),
                    HistoryTarget.City(pCity));
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int CountSlavePopulation(City pCity)
        {
            int count = 0;
            try
            {
                foreach (Actor unit in pCity.units)
                    if (unit?.data != null && SlaveService.IsSlave(unit)) count++;
            }
            catch { }
            return count;
        }

        private static bool IsBorderCity(Kingdom pKingdom, City pCity)
        {
            return pKingdom?.capital != pCity && SafeCityCount(pKingdom) > 1;
        }

        private static bool IsOccupiedUnrest(Kingdom pKingdom, City pCity)
        {
            return IsNonCore(pKingdom, pCity);
        }

        private static bool IsNonCore(Kingdom pKingdom, City pCity)
        {
            try
            {
                WarTerritoryService.TerritoryStatus status = WarTerritoryService.GetCoreStatus(pKingdom, pCity);
                return status.status == "owned_non_core";
            }
            catch
            {
                return false;
            }
        }

        private static int CountBuildings(City pCity, string pKind)
        {
            return 0;
        }

        private static float DistanceFromCapital(Kingdom pKingdom, City pCity)
        {
            try
            {
                if (pKingdom?.capital == null || pCity?.data == null || pKingdom.capital == pCity) return 0f;
                return Toolbox.DistVec2(pKingdom.capital.getTile().pos, pCity.getTile().pos);
            }
            catch
            {
                return pKingdom?.capital == pCity ? 0f : 40f;
            }
        }

        private static int SafeCityCount(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }
    }
}
