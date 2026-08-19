using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.court;
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
        public float tax_value;
        public bool has_foreign_land_border;
    }

    internal static class CityEconomyService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private const int SQL_IN_CHUNK_SIZE = 128;
        private static readonly Dictionary<long, CityEconomyContributionSums> ContributionCache =
            new Dictionary<long, CityEconomyContributionSums>();
        private static readonly HashSet<long> PendingRealmRefreshes =
            new HashSet<long>();

        public static void ClearRuntime()
        {
            ContributionCache.Clear();
            PendingRealmRefreshes.Clear();
        }

        internal static void OnRealmSupplyChanged(City pCity)
        {
            Kingdom realm = pCity?.kingdom;
            if (pCity?.data == null || realm?.data == null) return;
            PendingRealmRefreshes.Add(realm.id);
            bool providesToRealm =
                OccupiedCitySupplyService.CanProvideToRealm(pCity, realm);
            if (!providesToRealm)
                ZeroRealmContribution(pCity.id, realm.id);
            InvalidateContributionCache(realm.id);
            realm.data.set(LineageKeys.CITY_ECONOMY_LAST_YEAR,
                int.MinValue);
            ScheduleRealmRefresh(realm.id);
        }

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
                CentralizationEffects centralization =
                    CentralizationService.ReadSnapshot(pKingdom).effects;
                CourtInstitutionEffects institution =
                    CourtInstitutionEffectService.Read(pKingdom);
                KingdomPolicyEffects policyEffects =
                    KingdomPolicyEffectService.Read(pKingdom);
                float administrationMultiplier =
                    policyEffects.AdministrationMultiplier;
                float workshopTechMultiplier = 1f +
                    policyEffects.ExtraWorkshopAttempts * 0.05f;
                Dictionary<long, CustomCourtCityEffectModifiers>
                    cityEffectModifiers =
                    CustomCourtRuntimeEffectService.BuildCityModifiers(
                        pKingdom, cities);
                var sums = new CityEconomyContributionSums { year = year };
                foreach (City city in cities)
                {
                    cityEffectModifiers.TryGetValue(city.id,
                        out CustomCourtCityEffectModifiers customEffects);
                    CustomCourtEffectModifier customTax = customEffects?.Tax ??
                        CustomCourtEffectModifier.Identity;
                    CustomCourtEffectModifier customFood =
                        customEffects?.Food ?? CustomCourtEffectModifier.Identity;
                    CustomCourtEffectModifier customOrder =
                        customEffects?.Order ?? CustomCourtEffectModifier.Identity;
                    bool providesToRealm =
                        OccupiedCitySupplyService.CanProvideToRealm(
                            city, pKingdom);
                    if (providesToRealm &&
                        HasForeignLandNeighbour(pKingdom, city))
                        sums.has_foreign_land_border = true;
                    if (!UpdateCity(pKingdom, city, year, cityCount,
                            slaveryEnabled, providesToRealm, techReports,
                            storedStates,
                            centralization.TaxMultiplier *
                            institution.TaxMultiplier *
                            policyEffects.TaxMultiplier,
                            centralization.ManpowerMultiplier *
                            institution.ManpowerMultiplier,
                            centralization.UnrestReduction +
                            institution.UnrestReduction,
                            institution.PolicyOutputMultiplier *
                            administrationMultiplier,
                             institution.TechOutputMultiplier *
                             administrationMultiplier * workshopTechMultiplier,
                             policyEffects.FarmOutputMultiplier,
                             customTax, customFood, customOrder,
                             out CityEconomyContribution contribution))
                        continue;
                    if (!providesToRealm) continue;
                    sums.policy_points += contribution.PolicyPoints;
                    sums.tech_points += contribution.TechPoints;
                    sums.tax_value += contribution.TaxValue;
                }
                ContributionCache[pKingdom.id] = sums;
                PendingRealmRefreshes.Remove(pKingdom.id);
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

        public static float GetTaxContribution(Kingdom pKingdom)
        {
            return GetContributionSums(pKingdom).tax_value;
        }

        public static bool TryGetLatestCachedTaxContribution(Kingdom pKingdom, out float pTaxValue)
        {
            pTaxValue = 0f;
            if (pKingdom?.data == null ||
                PendingRealmRefreshes.Contains(pKingdom.id) ||
                !ContributionCache.TryGetValue(pKingdom.id, out CityEconomyContributionSums cached) ||
                !CityEconomyUpdateRules.ShouldUseContributionCache(true,
                    cached.year, Date.getCurrentYear()))
                return false;
            pTaxValue = cached.tax_value;
            return true;
        }

        public static bool TryGetLatestCachedForeignLandBorder(Kingdom pKingdom, out bool pHasBorder)
        {
            pHasBorder = false;
            if (pKingdom?.data == null ||
                PendingRealmRefreshes.Contains(pKingdom.id) ||
                !ContributionCache.TryGetValue(pKingdom.id, out CityEconomyContributionSums cached) ||
                !CityEconomyUpdateRules.ShouldUseContributionCache(true,
                    cached.year, Date.getCurrentYear()))
                return false;
            pHasBorder = cached.has_foreign_land_border;
            return true;
        }

        public static bool HasForeignLandBorder(Kingdom pKingdom)
        {
            return GetContributionSums(pKingdom).has_foreign_land_border;
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

        private static bool UpdateCity(Kingdom pKingdom, City pCity,
            int pYear, int pCityCount, bool pSlaveryEnabled,
            bool pProvidesToRealm,
            Dictionary<long, CityTechReport> pTechReports,
            Dictionary<long, CityEconomyStoredState> pStoredStates, float pTaxMultiplier,
            float pManpowerMultiplier, float pUnrestReduction,
            float pPolicyMultiplier, float pTechMultiplier,
            float pFarmOutputMultiplier,
            CustomCourtEffectModifier pCustomTax,
            CustomCourtEffectModifier pCustomFood,
            CustomCourtEffectModifier pCustomOrder,
            out CityEconomyContribution pContribution)
        {
            pContribution = default;
            if (pCity?.data == null || pCity.isRekt()) return false;
            bool activeFief = FiefService.IsActiveFief(pCity);
            bool activeFeudatory = FeudatoryService.TryGetByCity(pCity.id,
                out FeudatorySnapshot feudatory);
            float feudatoryRemittance = activeFeudatory
                ? FeudatoryAutonomyRules.CentralRemittanceMultiplier(
                    feudatory.Autonomy)
                : 1f;
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
            CityEconomyRole role = SelectRole(pKingdom, pCity, activeFief,
                activeFeudatory, tech, population, nonCore, pCityCount);
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
                slavePopulation, nonCore, activeFief, pTaxMultiplier, pManpowerMultiplier,
                pUnrestReduction, feudatoryRemittance, pPolicyMultiplier,
                pTechMultiplier, pFarmOutputMultiplier);
            float realmMultiplier = OccupiedCitySupplyRules.
                RealmContributionMultiplier(
                    enemyFrozenControl: !pProvidesToRealm);
            if (realmMultiplier < 1f)
            {
                contribution = new CityEconomyContribution(
                    contribution.PolicyPoints * realmMultiplier,
                    contribution.TechPoints * realmMultiplier,
                    contribution.TaxValue * realmMultiplier,
                    contribution.Manpower * realmMultiplier,
                    contribution.FoodStability,
                    contribution.UnrestRisk);
            }
            contribution = new CityEconomyContribution(
                contribution.PolicyPoints,
                contribution.TechPoints,
                Math.Max(0f, pCustomTax.Apply(contribution.TaxValue)),
                contribution.Manpower,
                Math.Max(0f, pCustomFood.Apply(
                    contribution.FoodStability)),
                CustomCourtEffectRules.ApplyCivilOrder(
                    contribution.UnrestRisk, pCustomOrder));
            benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                CityEconomyStoredState previous = null;
                pStoredStates?.TryGetValue(pCity.id, out previous);
                Upsert(pKingdom, pCity, role, contribution, pYear, previous);
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.CityEconomyDbUpsertIndex, benchmark); }
            pContribution = contribution;
            return true;
        }

        private static CityEconomyRole SelectRole(Kingdom pKingdom, City pCity,
            bool pActiveFief, bool pActiveFeudatory, CityTechReport pTech,
            int pPopulation, bool pNonCore, int pCityCount)
        {
            return CityEconomyRules.SelectRole(pKingdom.capital == pCity, pPopulation,
                CountBuildings(pCity, "market"), CountBuildings(pCity, "farm"),
                CountBuildings(pCity, "barracks"), CountBuildings(pCity, "workshop"),
                pTech.adopted_count, pTech.total_count, IsBorderCity(pKingdom, pCity, pCityCount),
                pNonCore, pActiveFief || pActiveFeudatory);
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

        public static bool IsFrontierMilitary(Kingdom pKingdom, City pCity)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom || pCity.isRekt()) return false;
            CityEconomyStoredState state = ReadStoredState(pCity.id);
            return state.has_record && state.kingdom_id == pKingdom.id &&
                   string.Equals(state.role,
                       CityEconomyRole.FrontierMilitary.ToString(),
                       StringComparison.Ordinal);
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
            if (PendingRealmRefreshes.Contains(pKingdom.id)) return empty;
            int year = Date.getCurrentYear();
            if (ContributionCache.TryGetValue(pKingdom.id, out CityEconomyContributionSums cached) &&
                CityEconomyUpdateRules.ShouldUseContributionCache(true, cached.year, year))
                return cached;

            var sums = new CityEconomyContributionSums { year = year };
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT SUM(POLICY_POINTS),SUM(TECH_POINTS),SUM(TAX_VALUE) FROM " +
                                  CityEconomyStateTableItem.GetTableName() + " WHERE KINGDOM_ID=@kingdom";
                cmd.Parameters.AddWithValue("@kingdom", pKingdom.id);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    sums.policy_points = reader.IsDBNull(0) ? 0f : Convert.ToSingle(reader.GetValue(0));
                    sums.tech_points = reader.IsDBNull(1) ? 0f : Convert.ToSingle(reader.GetValue(1));
                    sums.tax_value = reader.IsDBNull(2) ? 0f : Convert.ToSingle(reader.GetValue(2));
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

        private static void ZeroRealmContribution(long pCityId,
            long pKingdomId)
        {
            if (!Ready || pCityId < 0 || pKingdomId < 0) return;
            try
            {
                DB.UpdateValue(CityEconomyStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("CITY_ID", pCityId),
                        SimpleColumnConstraint.CreateEq("KINGDOM_ID",
                            pKingdomId)
                    },
                    ColumnVal.Create("POLICY_POINTS", 0f),
                    ColumnVal.Create("TECH_POINTS", 0f),
                    ColumnVal.Create("TAX_VALUE", 0f),
                    ColumnVal.Create("MANPOWER", 0f),
                    ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
            }
            catch
            {
            }
        }

        private static void ScheduleRealmRefresh(long pKingdomId)
        {
            if (pKingdomId < 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "city_economy_supply:" + pKingdomId,
                DeferredWorkClass.Runtime,
                () =>
                {
                    Kingdom realm;
                    try { realm = World.world?.kingdoms?.get(pKingdomId); }
                    catch { realm = null; }
                    if (realm?.data == null || realm.isRekt()) return;
                    realm.data.set(LineageKeys.CITY_ECONOMY_LAST_YEAR,
                        int.MinValue);
                    OnKingdomYear(realm);
                });
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
                               HistoryLocalizationRules.H("aw_hist_city_economy_role_mid") +
                               HistoryText.PlainText(roleName) +
                               HistoryLocalizationRules.H("aw_hist_city_economy_tax_mid") +
                               HistoryText.PlainText(Math.Round(pContribution.TaxValue, 1).ToString());
            HistoryWriter.RecordCity(pCity, pKingdom, "city_economy_role", text, HistoryTarget.City(pCity));

            if (!pExisted || roleChanged)
                HistoryWriter.RecordKingdom(pKingdom, "city_economy_role",
                    HistoryText.Kingdom(pKingdom) +
                    HistoryLocalizationRules.H("aw_hist_city_economy_adjusted_mid") +
                    HistoryText.City(pCity, pKingdom) +
                    HistoryLocalizationRules.H("aw_hist_city_economy_role_arrow") +
                    HistoryText.PlainText(roleName),
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

        private static bool HasForeignLandNeighbour(Kingdom pKingdom, City pCity)
        {
            if (pKingdom?.data == null || pCity?.data == null) return false;
            try
            {
                foreach (Kingdom neighbour in pCity.neighbours_kingdoms)
                {
                    if (neighbour?.data == null || neighbour == pKingdom || neighbour.isRekt()) continue;
                    return true;
                }
            }
            catch
            {
            }
            return false;
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
