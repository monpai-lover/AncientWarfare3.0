using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomPolicyAI
    {
        private static readonly string[] SocialOrder =
        {
            "aw_policy_household_registry",
            "aw_policy_start_slavery",
            "aw_policy_corvee_labor",
            "aw_policy_control_slaves",
            "aw_policy_slave_army",
            "aw_policy_start_halfaristocrat",
            "aw_policy_noble_council",
            "aw_policy_ancestral_rites",
            "aw_policy_mandate_rites",
            "aw_policy_name_integration",
            "aw_policy_military_merit",
            "aw_policy_base_enfeoffment",
            "aw_policy_border_enfeoffment",
            "aw_policy_continuous_enfeoffment",
            "aw_policy_early_law",
            "aw_policy_imperial_court",
            "aw_policy_adopt_xia_rites",
            "aw_policy_xia_law_institutions",
            "aw_policy_abolish_slavery"
        };

        public static void TryFillEmptySlots(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom)) return;
            if (!KingdomPolicyService.IsPolicyAIEnabled(pKingdom)) return;

            KingdomPolicyService.EnsureInitialized(pKingdom);
            WesternPolicyNeedFacts? westernFacts =
                KingdomPolicyService.GetPolicyProfile(pKingdom) ==
                KingdomPolicyProfileId.WesternGeneral
                    ? BuildWesternPolicyNeedFacts(pKingdom)
                    : null;
            TryStartIfEmpty(pKingdom, PolicyNodeKind.Decision, PickDecision(pKingdom));
            TryStartIfEmpty(pKingdom, PolicyNodeKind.Tech,
                PickResearch(pKingdom, PolicyNodeKind.Tech, westernFacts));
            TryStartIfEmpty(pKingdom, PolicyNodeKind.Social,
                PickResearch(pKingdom, PolicyNodeKind.Social, westernFacts));
        }

        public static KingdomPolicyDef PickDecision(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            return KingdomPolicyService.GetNodes(pKingdom,
                    PolicyNodeKind.Decision)
                .Where(def => KingdomDecisionPriorityRules.
                    ShouldUseGeneralDecisionSlot(def.Id))
                .Where(def => !KingdomPolicyService.IsNodeLocked(pKingdom, def.Id))
                .Where(def => IsAvailable(pKingdom, def))
                .Where(def => ShouldAutoStartDecision(pKingdom, def))
                .OrderByDescending(def => ScoreDecision(pKingdom, def))
                .FirstOrDefault();
        }

        public static KingdomPolicyDef PickResearch(Kingdom pKingdom, PolicyNodeKind pKind)
        {
            return PickResearch(pKingdom, pKind, null);
        }

        private static KingdomPolicyDef PickResearch(Kingdom pKingdom,
            PolicyNodeKind pKind, WesternPolicyNeedFacts? pWesternFacts)
        {
            if (pKingdom?.data == null) return null;
            KingdomPolicyProfileId profile =
                KingdomPolicyService.GetPolicyProfile(pKingdom);
            KingdomPolicyDef[] defs = KingdomPolicyService.GetNodes(
                pKingdom, pKind).ToArray();

            if (profile == KingdomPolicyProfileId.WesternGeneral)
            {
                WesternPolicyNeedFacts facts = pWesternFacts ??
                    BuildWesternPolicyNeedFacts(pKingdom);
                return PickWesternResearch(pKingdom, defs, profile, facts);
            }

            bool officialCourtCompleted = pKind != PolicyNodeKind.Tech ||
                KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Tech, "aw_tech_official_court");
            bool ritesMusicCompleted = pKind != PolicyNodeKind.Tech ||
                KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Tech, "aw_tech_rites_music");
            bool nineRankCompleted = pKind != PolicyNodeKind.Tech ||
                KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Tech, "aw_tech_nine_rank_system");

            return defs
                .Where(def => pKind != PolicyNodeKind.Tech || KingdomPolicyTechOrderRules.CanConsider(
                    def.Id, officialCourtCompleted, ritesMusicCompleted, nineRankCompleted))
                .Where(def => !KingdomPolicyService.IsNodeLocked(pKingdom, def.Id))
                .Where(def => IsAvailable(pKingdom, def))
                .OrderByDescending(def => ScoreResearch(pKingdom, def))
                .FirstOrDefault();
        }

        private static KingdomPolicyDef PickWesternResearch(
            Kingdom pKingdom, IReadOnlyList<KingdomPolicyDef> pDefinitions,
            KingdomPolicyProfileId pProfile,
            WesternPolicyNeedFacts pFacts)
        {
            if (pDefinitions == null || pDefinitions.Count == 0) return null;
            var candidates = new WesternPolicyCandidate[pDefinitions.Count];
            for (int index = 0; index < pDefinitions.Count; index++)
            {
                KingdomPolicyDef definition = pDefinitions[index];
                candidates[index] = new WesternPolicyCandidate(
                    definition?.Id,
                    KingdomPolicyCatalogRules.BelongsTo(definition, pProfile),
                    IsAvailable(pKingdom, definition),
                    definition != null &&
                    KingdomPolicyService.IsNodeLocked(
                        pKingdom, definition.Id),
                    definition == null
                        ? int.MaxValue
                        : Math.Max(0, definition.Column * 100 +
                                      definition.Row));
            }

            string selectedId = WesternPolicyAiRules.SelectBest(candidates,
                pFacts);
            if (string.IsNullOrEmpty(selectedId)) return null;
            for (int index = 0; index < pDefinitions.Count; index++)
            {
                KingdomPolicyDef definition = pDefinitions[index];
                if (definition != null && string.Equals(definition.Id,
                        selectedId, StringComparison.Ordinal))
                    return definition;
            }
            return null;
        }

        private static void TryStartIfEmpty(Kingdom pKingdom, PolicyNodeKind pKind, KingdomPolicyDef pDef)
        {
            if (pDef == null) return;
            if (!string.IsNullOrEmpty(KingdomPolicyService.GetCurrent(pKingdom, pKind))) return;
            if (!KingdomPolicyService.StartResearch(pKingdom, pDef.Id)) return;

            if (pKind != PolicyNodeKind.Decision) return;

            int year = Date.getCurrentYear();
            pKingdom.data.set(LineageKeys.POLICY_AI_LAST_DECISION_YEAR, year);
            switch (pDef.Id)
            {
                case "aw_decision_title_upgrade":
                    pKingdom.data.set(LineageKeys.POLICY_AI_LAST_PROMOTION_YEAR, year);
                    break;
                case "aw_decision_royal_expansion":
                    pKingdom.data.set(LineageKeys.POLICY_AI_LAST_ROYAL_EXPANSION_YEAR, year);
                    break;
                case "aw_decision_change_capital":
                    pKingdom.data.set(LineageKeys.POLICY_AI_LAST_CAPITAL_MOVE_YEAR, year);
                    break;
                case "aw_decision_control_slaves":
                    pKingdom.data.set(LineageKeys.POLICY_AI_LAST_SLAVE_CONTROL_YEAR, year);
                    break;
                case "aw_decision_appease_foreign_cities":
                    pKingdom.data.set(
                        LineageKeys.POLICY_AI_LAST_FOREIGN_APPEASE_YEAR,
                        year);
                    break;
            }
        }

        private static bool IsAvailable(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            return pDef != null && KingdomPolicyService.GetStatus(pKingdom, pDef) == PolicyNodeStatus.Available;
        }

        private static int ScoreDecision(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            int cities = CountCities(pKingdom);
            int baseScore = KingdomDecisionPriorityRules.ScoreDecision(
                pDef.Id,
                RoyalExpansionDecisionService.CanExecute(pKingdom),
                cities,
                SlaveService.IsSlaveryEnabled(pKingdom),
                XiaizationService.ScoreResearch(pKingdom, pDef),
                false);

            if (pDef.Id == "aw_decision_clean_corruption")
            {
                int corruption = CorruptionService.ReadCountry(pKingdom).Score;
                return corruption >= 80 ? 1600 :
                       corruption >= 60 ? 1100 : 650;
            }

            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                return baseScore + CourtAIRules.ScoreDecision(court.dominant_school, pDef.Id,
                    cities, IsAtWar(pKingdom), court.efficiency < 35f,
                    court.livelihood, court.aggression, court.peace, court.war, court.order);
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtAiBiasIndex, benchmark); }
        }

        private static bool ShouldAutoStartDecision(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return false;
            if (KingdomDecisionPriorityRules.ShouldApplyGeneralCooldown(
                    pDef.Id) &&
                YearsSince(pKingdom,
                    LineageKeys.POLICY_AI_LAST_DECISION_YEAR,
                    -99999) < 8) return false;

            switch (pDef.Id)
            {
                case "aw_decision_claim_mandate":
                    return !MandateService.Exists && MandateService.CanDeclareMandate(pKingdom, out _);
                case "aw_decision_title_upgrade":
                    return YearsSince(pKingdom, LineageKeys.POLICY_AI_LAST_PROMOTION_YEAR, -99999) >= 10;
                case "aw_decision_royal_expansion":
                    return YearsSince(pKingdom, LineageKeys.POLICY_AI_LAST_ROYAL_EXPANSION_YEAR, -99999) >= 15 &&
                           RoyalExpansionDecisionService.CanExecute(pKingdom);
                case "aw_decision_change_capital":
                    return YearsSince(pKingdom, LineageKeys.POLICY_AI_LAST_CAPITAL_MOVE_YEAR, -99999) >= 30 &&
                           HasClearlyBetterCapital(pKingdom);
                case "aw_decision_control_slaves":
                    return SlaveService.IsSlaveryEnabled(pKingdom) &&
                           YearsSince(pKingdom, LineageKeys.POLICY_AI_LAST_SLAVE_CONTROL_YEAR, -99999) >= 25;
                case "aw_decision_appease_foreign_cities":
                    return YearsSinceForeignAppeasement(pKingdom) >= 12 &&
                           XiaizationService.SpecialRequirementMet(pKingdom, pDef.Id);
                case "aw_decision_clean_corruption":
                    return CorruptionService.CanStartCleanup(pKingdom);
                case "aw_decision_fabricate_core":
                    return WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom)?.data != null;
                case "aw_decision_year_name":
                    return false;
                default:
                    return false;
            }
        }

        private static int ScoreResearch(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            int orderScore = 1000 - PreferredIndex(pDef) * 20;
            int context = 0;
            context += XiaizationService.ScoreResearch(pKingdom, pDef);
            context += CourtInstitutionRules.ResearchEraScore(
                pDef.Id, Date.getCurrentYear());

            int cities = CountCities(pKingdom);
            int units = CountUnits(pKingdom);
            bool atWar = IsAtWar(pKingdom);

            switch (pDef.Id)
            {
                case "aw_tech_chariot_training":
                case "aw_policy_military_merit":
                case "aw_policy_slave_army":
                    if (atWar || units >= 80) context += 90;
                    break;
                case "aw_tech_city_defense":
                case "aw_policy_border_enfeoffment":
                case "aw_policy_continuous_enfeoffment":
                    if (cities >= 3 || atWar) context += 80;
                    break;
                case "aw_tech_granary_accounting":
                case "aw_tech_iron_plow":
                    if (cities >= 2 || units >= 60) context += 70;
                    break;
                case "aw_tech_civil_service_examination":
                    int vacancies = CountCivilServiceVacancies(pKingdom);
                    int educatedWithoutQualification =
                        CivilServiceExamCandidateQuery.
                            CountEducatedWithoutQualification(pKingdom, 32);
                    bool imperial =
                        MandateService.IsMandateKingdom(pKingdom) ||
                        KingdomTitleService.GetTitle(pKingdom) >=
                        KingdomTitle.Emperor;
                    context += KingdomPolicyTechOrderRules.
                        CivilServiceExaminationContextScore(vacancies,
                            educatedWithoutQualification, cities, imperial);
                    break;
                case "aw_policy_control_slaves":
                    if (SlaveService.IsSlaveryEnabled(pKingdom)) context += 70;
                    break;
                case "aw_policy_abolish_slavery":
                    if (cities >= 4 || units >= 120) context += 40;
                    break;
                case "aw_policy_name_integration":
                    context += 50;
                    break;
                case "aw_policy_mandate_rites":
                case "aw_policy_imperial_court":
                    if (KingdomTitleService.GetTitle(pKingdom) >= KingdomTitle.King || MandateService.Exists)
                        context += 120;
                    break;
            }

            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                context += CourtAIRules.ScoreResearch(court.dominant_school, pDef.Id,
                    atWar, MandateService.Exists, court.livelihood, court.commerce,
                    court.technology, court.order);
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtAiBiasIndex, benchmark); }

            return orderScore + context;
        }

        private static int PreferredIndex(KingdomPolicyDef pDef)
        {
            int layoutFallback = Math.Max(0, pDef.Column * 3 + pDef.Row);
            if (pDef.Kind == PolicyNodeKind.Tech)
                return KingdomPolicyTechOrderRules.PreferredIndex(pDef.Id, layoutFallback);

            int index = Array.IndexOf(SocialOrder, pDef.Id);
            return index >= 0 ? index : SocialOrder.Length + layoutFallback;
        }

        private static WesternPolicyNeedFacts BuildWesternPolicyNeedFacts(
            Kingdom pKingdom)
        {
            int population = 0;
            int hungry = 0;
            int cityCount = 0;
            int citiesWithFood = 0;
            int slaves = 0;
            int gold = 0;
            bool borderThreat = false;

            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    cityCount++;
                    int cityPopulation = Math.Max(0,
                        city.status?.population ?? city.getPopulationPeople());
                    population += cityPopulation;
                    hungry += Math.Max(0, city.status?.hungry ?? 0);
                    if (city.countFoodTotal() > 0) citiesWithFood++;
                    slaves += SlavePopulationIndexService.Count(city);
                    gold += Math.Max(0, city.getResourcesAmount("gold"));

                    if (borderThreat || city.neighbours_cities == null)
                        continue;
                    foreach (City neighbour in city.neighbours_cities)
                    {
                        Kingdom neighbourKingdom = neighbour?.kingdom;
                        if (neighbour?.data != null &&
                            neighbourKingdom?.data != null &&
                            neighbourKingdom != pKingdom &&
                            !neighbourKingdom.isRekt() &&
                            !neighbourKingdom.isNeutral())
                        {
                            borderThreat = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            float hungerSecurity = population <= 0
                ? 0.5f
                : 1f - Math.Min(1f, hungry / (float)population);
            float stockSecurity = cityCount <= 0
                ? 0f
                : citiesWithFood / (float)cityCount;
            float foodSecurity = Math.Max(0f, Math.Min(1f,
                hungerSecurity * 0.7f + stockSecurity * 0.3f));
            float treasuryRatio = population <= 0
                ? 0f
                : Math.Max(0f, Math.Min(1f,
                    gold / (float)Math.Max(20, population * 2)));
            float slaveShare = population <= 0
                ? 0f
                : Math.Max(0f, Math.Min(1f,
                    slaves / (float)population));

            KingdomPolicyEffects effects =
                KingdomPolicyEffectService.Read(pKingdom);
            float equipmentQuality = Math.Max(0f, Math.Min(1f,
                effects.EquipmentQualityBonus /
                (float)KingdomPolicyEffectRules.
                    MaximumEquipmentQualityBonus));
            pKingdom.data.get(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                out int royalAuthority, 0);
            IReadOnlyList<CourtAristocraticGroup> groups =
                CourtAristocraticGroupService.GetCachedGroups(pKingdom);
            int nobleOpposition = groups != null && groups.Count > 0 &&
                                  groups[0] != null
                ? Math.Max(0, Math.Min(100, groups[0].Power))
                : 0;
            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);

            return new WesternPolicyNeedFacts(foodSecurity,
                equipmentQuality, IsAtWar(pKingdom), borderThreat,
                treasuryRatio, CountCivilServiceVacancies(pKingdom),
                cityCount, royalAuthority, nobleOpposition, slaveShare,
                court?.dominant_school ?? string.Empty);
        }

        private static int CountCities(Kingdom pKingdom)
        {
            try { return Math.Max(0, pKingdom?.countCities() ?? 0); }
            catch { return 0; }
        }

        private static int CountUnits(Kingdom pKingdom)
        {
            try { return Math.Max(0, pKingdom?.getPopulationTotal() ?? 0); }
            catch { return 0; }
        }

        private static int CountCivilServiceVacancies(Kingdom pKingdom)
        {
            string[] expected =
                CourtService.CentralOfficeIdsForCurrentProfile(pKingdom);
            if (expected.Length == 0) return 0;

            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (CourtOfficerView officer in CourtService.GetActiveOfficers(
                         pKingdom, 96))
            {
                if (officer?.layer == CourtOfficeLayer.Central &&
                    !string.IsNullOrEmpty(officer.office_id))
                    occupied.Add(officer.office_id);
            }

            int vacancies = 0;
            for (int index = 0; index < expected.Length; index++)
                if (!occupied.Contains(expected[index])) vacancies++;
            return vacancies;
        }

        private static bool IsAtWar(Kingdom pKingdom)
        {
            try { return pKingdom.getWars().Any(); }
            catch { return false; }
        }

        private static int YearsSince(Kingdom pKingdom, string pKey, int pFallback)
        {
            pKingdom.data.get(pKey, out int lastYear, pFallback);
            return Date.getCurrentYear() - lastYear;
        }

        private static int YearsSinceForeignAppeasement(Kingdom pKingdom)
        {
            pKingdom.data.get(
                LineageKeys.POLICY_AI_LAST_FOREIGN_APPEASE_YEAR,
                out int currentKeyYear, -99999);
            pKingdom.data.get(LineageKeys.POLICY_AI_LAST_XIA_APPEASE_YEAR,
                out int legacyKeyYear, -99999);
            return Date.getCurrentYear() -
                   Math.Max(currentKeyYear, legacyKeyYear);
        }

        private static bool HasClearlyBetterCapital(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.capital == null || IsAtWar(pKingdom)) return false;
            City current = pKingdom.capital;
            float currentScore = CapitalScore(current, current, pKingdom);
            float bestScore = currentScore;

            foreach (City city in pKingdom.getCities())
            {
                if (!CapitalMoveCandidateService.CanConsider(city, pKingdom, current)) continue;
                float score = CapitalScore(city, current, pKingdom);
                if (score > bestScore) bestScore = score;
            }

            return CapitalMoveRules.ShouldMoveCapital(currentScore, bestScore);
        }

        private static float CapitalScore(City pCity, City pCurrent, Kingdom pKingdom)
        {
            if (pCity?.data == null || !pCity.isAlive()) return 0f;
            return CapitalMoveRules.ScoreCity(
                SafeAge(pCity),
                SafeAge(pCurrent),
                SafePopulation(pCity),
                SafePopulation(pCurrent),
                SafeZones(pCity),
                SafeZones(pCurrent),
                CountOwnNeighbors(pCity, pKingdom),
                CapitalCentralityScore(pCity, pKingdom));
        }

        private static int CountOwnNeighbors(City pCity, Kingdom pKingdom)
        {
            if (pCity?.data == null || pKingdom?.data == null) return 0;
            int count = 0;
            try
            {
                foreach (City other in pCity.neighbours_cities)
                    if (other?.data != null && other.kingdom == pKingdom) count++;
            }
            catch { }
            return count;
        }

        private static float CapitalCentralityScore(City pCity, Kingdom pKingdom)
        {
            if (pCity?.data == null || pKingdom?.data == null) return 0f;
            float distance = 0f;
            int count = 0;
            try
            {
                foreach (City other in pKingdom.getCities())
                {
                    if (other?.data == null || other == pCity) continue;
                    WorldTile a = pCity.getTile();
                    WorldTile b = other.getTile();
                    if (a == null || b == null) continue;
                    distance += Toolbox.DistVec2(a.pos, b.pos);
                    count++;
                }
            }
            catch { }
            return count <= 0 ? 0f : 60f / (1f + distance / count);
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int SafeZones(City pCity)
        {
            try { return pCity?.countZones() ?? 0; }
            catch { return 0; }
        }

        private static float SafeAge(City pCity)
        {
            try { return pCity?.getAge() ?? 0f; }
            catch { return 0f; }
        }
    }
}
