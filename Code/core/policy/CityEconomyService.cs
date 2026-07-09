using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.policy
{
    internal sealed class CityEconomySnapshot
    {
        public bool has_record;
        public float policy_points;
        public float tech_points;
        public float tax_value;
        public float manpower;
        public float food_stability;
        public float unrest_risk;
    }

    internal sealed class CityEconomyContributionSums
    {
        public int year = int.MinValue;
        public float policy_points;
        public float tech_points;
    }

    internal static class CityEconomyService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private const int SQL_IN_CHUNK_SIZE = 128;
        private static readonly Dictionary<long, CityEconomyContributionSums> ContributionCache =
            new Dictionary<long, CityEconomyContributionSums>();

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom)) return;
            if (!Ready) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.CITY_ECONOMY_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.CITY_ECONOMY_LAST_YEAR, year);

            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                List<City> cities = GetCities(pKingdom);
                Dictionary<long, CityTechReport> techReports =
                    CityEconomyUpdateRules.ShouldUseBatchTechReports(Ready, cities.Count)
                        ? CityTechService.GetCityReportsForCities(cities, pIncludeNeighborBonus: false)
                        : null;
                Dictionary<long, CityEconomyStoredState> storedStates =
                    CityEconomyUpdateRules.ShouldUseBatchStoredStates(Ready, cities.Count)
                        ? ReadStoredStatesForCities(cities)
                        : null;
                bool slaveryEnabled = SlaveService.IsSlaveryEnabled(pKingdom);
                int cityCount = cities.Count;
                foreach (City city in cities)
                    UpdateCity(pKingdom, city, year, cityCount, slaveryEnabled, techReports, storedStates);
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.CityEconomyUpdateCitiesIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { DevelopmentMapModeService.DirtyMapIfActive(); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.CityEconomyMapDirtyIndex, benchmark); }
        }

        public static float GetPolicyContribution(Kingdom pKingdom)
        {
            return GetContributionSums(pKingdom).policy_points;
        }

        public static float GetTechContribution(Kingdom pKingdom)
        {
            return GetContributionSums(pKingdom).tech_points;
        }

        public static CityEconomySnapshot GetSnapshot(City pCity)
        {
            var snapshot = new CityEconomySnapshot();
            if (pCity?.data == null || !Ready) return snapshot;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT POLICY_POINTS,TECH_POINTS,TAX_VALUE,MANPOWER,FOOD_STABILITY,UNREST_RISK " +
                                  "FROM " + CityEconomyStateTableItem.GetTableName() + " WHERE CITY_ID=@city LIMIT 1";
                cmd.Parameters.AddWithValue("@city", pCity.id);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return snapshot;
                snapshot.has_record = true;
                snapshot.policy_points = ReadFloat(reader, 0);
                snapshot.tech_points = ReadFloat(reader, 1);
                snapshot.tax_value = ReadFloat(reader, 2);
                snapshot.manpower = ReadFloat(reader, 3);
                snapshot.food_stability = ReadFloat(reader, 4);
                snapshot.unrest_risk = ReadFloat(reader, 5);
            }
            catch
            {
            }
            return snapshot;
        }

        private static void UpdateCity(Kingdom pKingdom, City pCity, int pYear, int pCityCount,
            bool pSlaveryEnabled, Dictionary<long, CityTechReport> pTechReports,
            Dictionary<long, CityEconomyStoredState> pStoredStates)
        {
            if (pCity?.data == null || pCity.isRekt()) return;
            bool activeFief = FiefService.IsActiveFief(pCity);
            long benchmark = UpdateAgeBenchmark.Begin();
            CityTechReport tech = null;
            try
            {
                if (pTechReports == null || !pTechReports.TryGetValue(pCity.id, out tech))
                    tech = CityTechService.GetCityReport(pCity, pIncludeNeighborBonus: false);
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.CityEconomyTechReportIndex, benchmark); }
            if (tech == null) tech = new CityTechReport();
            int population = SafePopulation(pCity);
            bool nonCore = IsNonCore(pKingdom, pCity);
            CityEconomyRole role = SelectRole(pKingdom, pCity, activeFief, tech, population, nonCore, pCityCount);
            benchmark = UpdateAgeBenchmark.Begin();
            int slavePopulation;
            try
            {
                slavePopulation = CityEconomyUpdateRules.ShouldCountSlavesForEconomy(
                    pSlaveryEnabled, pCity?.data != null)
                    ? CountSlavePopulation(pCity)
                    : 0;
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.CityEconomySlaveCountIndex, benchmark); }
            CityEconomyContribution contribution = CityEconomyRules.CalculateContribution(role, population,
                tech.adopted_count, tech.total_count, DistanceFromCapital(pKingdom, pCity),
                slavePopulation, nonCore, activeFief);
            benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                CityEconomyStoredState previous = null;
                pStoredStates?.TryGetValue(pCity.id, out previous);
                Upsert(pKingdom, pCity, role, contribution, pYear, previous);
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.CityEconomyDbUpsertIndex, benchmark); }
        }

        private static CityEconomyRole SelectRole(Kingdom pKingdom, City pCity, bool pActiveFief,
            CityTechReport pTech, int pPopulation, bool pNonCore, int pCityCount)
        {
            return CityEconomyRules.SelectRole(pKingdom.capital == pCity, pPopulation,
                CountBuildings(pCity, "market"), CountBuildings(pCity, "farm"),
                CountBuildings(pCity, "barracks"), CountBuildings(pCity, "workshop"),
                pTech.adopted_count, pTech.total_count, IsBorderCity(pKingdom, pCity, pCityCount),
                pNonCore, pActiveFief);
        }

        private static void Upsert(Kingdom pKingdom, City pCity, CityEconomyRole pRole,
            CityEconomyContribution pContribution, int pYear, CityEconomyStoredState pCachedState = null)
        {
            string role = pRole.ToString();
            CityEconomyStoredState previousState = pCachedState ?? ReadStoredState(pCity.id);
            bool existed = previousState.has_record;
            string previous = previousState.role;
            bool metadataChanged = previousState.has_record &&
                                   (!string.Equals(previousState.city_name, pCity.data.name ?? "",
                                        StringComparison.Ordinal) ||
                                    !string.Equals(previousState.kingdom_name, pKingdom.name ?? "",
                                        StringComparison.Ordinal));
            if (CityEconomyUpdateRules.ShouldSkipStableUpdate(previousState, pKingdom.id, role, pContribution,
                    metadataChanged))
                return;
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

            InvalidateContributionCache(pKingdom.id);
            RecordEconomyMilestone(pKingdom, pCity, previous, role, pContribution, existed);
        }

        private static CityEconomyStoredState ReadStoredState(long pCityId)
        {
            var state = new CityEconomyStoredState();
            if (!Ready || pCityId < 0) return state;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT KINGDOM_ID,CITY_NAME,KINGDOM_NAME,ROLE,POLICY_POINTS,TECH_POINTS," +
                                  "TAX_VALUE,MANPOWER,FOOD_STABILITY,UNREST_RISK FROM " +
                                  CityEconomyStateTableItem.GetTableName() + " WHERE CITY_ID=@city LIMIT 1";
                cmd.Parameters.AddWithValue("@city", pCityId);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (!reader.Read()) return state;
                state.has_record = true;
                state.kingdom_id = reader.IsDBNull(0) ? -1L : Convert.ToInt64(reader.GetValue(0));
                state.city_name = reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1));
                state.kingdom_name = reader.IsDBNull(2) ? "" : Convert.ToString(reader.GetValue(2));
                state.role = reader.IsDBNull(3) ? "" : Convert.ToString(reader.GetValue(3));
                state.policy_points = ReadFloat(reader, 4);
                state.tech_points = ReadFloat(reader, 5);
                state.tax_value = ReadFloat(reader, 6);
                state.manpower = ReadFloat(reader, 7);
                state.food_stability = ReadFloat(reader, 8);
                state.unrest_risk = ReadFloat(reader, 9);
            }
            catch
            {
            }
            return state;
        }

        private static Dictionary<long, CityEconomyStoredState> ReadStoredStatesForCities(List<City> pCities)
        {
            var result = new Dictionary<long, CityEconomyStoredState>();
            if (!Ready || pCities == null || pCities.Count == 0) return result;

            var cityIds = new List<long>();
            var seen = new HashSet<long>();
            foreach (City city in pCities)
            {
                if (city?.data == null || !seen.Add(city.id)) continue;
                cityIds.Add(city.id);
            }
            if (cityIds.Count == 0) return result;

            for (int offset = 0; offset < cityIds.Count; offset += SQL_IN_CHUNK_SIZE)
                ReadStoredStatesChunk(cityIds, offset, Math.Min(SQL_IN_CHUNK_SIZE, cityIds.Count - offset), result);
            return result;
        }

        private static void ReadStoredStatesChunk(List<long> pCityIds, int pOffset, int pCount,
            Dictionary<long, CityEconomyStoredState> pResult)
        {
            if (pCityIds == null || pResult == null || pCount <= 0) return;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                var parameters = new List<string>(pCount);
                for (int i = 0; i < pCount; i++)
                {
                    string parameter = "@city" + i;
                    parameters.Add(parameter);
                    cmd.Parameters.AddWithValue(parameter, pCityIds[pOffset + i]);
                }

                cmd.CommandText = "SELECT CITY_ID,KINGDOM_ID,CITY_NAME,KINGDOM_NAME,ROLE,POLICY_POINTS,TECH_POINTS," +
                                  "TAX_VALUE,MANPOWER,FOOD_STABILITY,UNREST_RISK FROM " +
                                  CityEconomyStateTableItem.GetTableName() +
                                  " WHERE CITY_ID IN (" + string.Join(",", parameters.ToArray()) + ")";
                using SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long cityId = reader.IsDBNull(0) ? -1L : Convert.ToInt64(reader.GetValue(0));
                    if (cityId < 0) continue;
                    pResult[cityId] = new CityEconomyStoredState
                    {
                        has_record = true,
                        kingdom_id = reader.IsDBNull(1) ? -1L : Convert.ToInt64(reader.GetValue(1)),
                        city_name = reader.IsDBNull(2) ? "" : Convert.ToString(reader.GetValue(2)),
                        kingdom_name = reader.IsDBNull(3) ? "" : Convert.ToString(reader.GetValue(3)),
                        role = reader.IsDBNull(4) ? "" : Convert.ToString(reader.GetValue(4)),
                        policy_points = ReadFloat(reader, 5),
                        tech_points = ReadFloat(reader, 6),
                        tax_value = ReadFloat(reader, 7),
                        manpower = ReadFloat(reader, 8),
                        food_stability = ReadFloat(reader, 9),
                        unrest_risk = ReadFloat(reader, 10)
                    };
                }
            }
            catch
            {
            }
        }

        private static CityEconomyContributionSums GetContributionSums(Kingdom pKingdom)
        {
            var empty = new CityEconomyContributionSums { year = Date.getCurrentYear() };
            if (pKingdom?.data == null || !Ready) return empty;
            int year = Date.getCurrentYear();
            if (ContributionCache.TryGetValue(pKingdom.id, out CityEconomyContributionSums cached) &&
                CityEconomyUpdateRules.ShouldUseContributionCache(true, cached.year, year))
                return cached;

            var sums = new CityEconomyContributionSums { year = year };
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT SUM(POLICY_POINTS),SUM(TECH_POINTS) FROM " +
                                  CityEconomyStateTableItem.GetTableName() + " WHERE KINGDOM_ID=@kingdom";
                cmd.Parameters.AddWithValue("@kingdom", pKingdom.id);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    sums.policy_points = reader.IsDBNull(0) ? 0f : Convert.ToSingle(reader.GetValue(0));
                    sums.tech_points = reader.IsDBNull(1) ? 0f : Convert.ToSingle(reader.GetValue(1));
                }
            }
            catch
            {
            }
            ContributionCache[pKingdom.id] = sums;
            return sums;
        }

        private static void InvalidateContributionCache(long pKingdomId)
        {
            if (pKingdomId >= 0) ContributionCache.Remove(pKingdomId);
        }

        private static float ReadFloat(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0f : Convert.ToSingle(pReader.GetValue(pIndex));
        }

        private static void RecordEconomyMilestone(Kingdom pKingdom, City pCity, string pPreviousRole,
            string pRole, CityEconomyContribution pContribution, bool pExisted)
        {
            if (pCity?.data == null || pKingdom?.data == null) return;
            bool roleChanged = pExisted && !string.Equals(pPreviousRole, pRole, StringComparison.Ordinal);
            int year = Date.getCurrentYear();
            pCity.data.get(LineageKeys.CITY_ECONOMY_MAJOR_TAX_YEAR, out int lastMajorTaxYear, -99999);
            if (!CityEconomyMilestoneRules.ShouldRecord(pExisted, roleChanged, pContribution.TaxValue,
                    year, lastMajorTaxYear)) return;
            if (pContribution.TaxValue >= CityEconomyMilestoneRules.MajorTaxThreshold)
                pCity.data.set(LineageKeys.CITY_ECONOMY_MAJOR_TAX_YEAR, year);

            string roleName = LocalizedRoleName(pRole);
            HistoryText text = HistoryText.City(pCity, pKingdom) +
                               HistoryText.PlainText(" \u57ce\u5e02\u7ecf\u6d4e\u5b9a\u578b\u4e3a " + roleName +
                                                     "\uff0c\u7a0e\u6536 " + Math.Round(pContribution.TaxValue, 1));
            HistoryWriter.RecordCity(pCity, pKingdom, "city_economy_role", text, HistoryTarget.City(pCity));

            if (!pExisted || roleChanged)
                HistoryWriter.RecordKingdom(pKingdom, "city_economy_role",
                    HistoryText.Kingdom(pKingdom) + HistoryText.PlainText(" \u8c03\u6574\u57ce\u5e02\u7ecf\u6d4e\uff1a") +
                    HistoryText.City(pCity, pKingdom) + HistoryText.PlainText(" -> " + roleName),
                    HistoryTarget.City(pCity));
        }

        public static string LocalizedRoleName(string pRole)
        {
            if (Enum.TryParse(pRole, out CityEconomyRole role))
            {
                string key = CityEconomyRules.RoleNameKey(role);
                return AW_L10n.Text(key, pRole);
            }
            return pRole ?? "";
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

        private static bool IsBorderCity(Kingdom pKingdom, City pCity, int pCityCount)
        {
            return pKingdom?.capital != pCity && pCityCount > 1;
        }

        private static bool IsNonCore(Kingdom pKingdom, City pCity)
        {
            try
            {
                return WarTerritoryService.IsOwnedNonCore(pKingdom, pCity);
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

        private static List<City> GetCities(Kingdom pKingdom)
        {
            var result = new List<City>();
            if (pKingdom?.data == null) return result;
            foreach (City city in pKingdom.getCities())
                if (city?.data != null && !city.isRekt() && city.isAlive()) result.Add(city);
            return result;
        }
    }
}
