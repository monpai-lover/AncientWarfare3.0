using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomPolicyAI
    {
        private static readonly string[] TechOrder =
        {
            "aw_tech_writing",
            "aw_tech_pottery_casting",
            "aw_tech_bronze_casting",
            "aw_tech_well_field_survey",
            "aw_tech_iron_plow",
            "aw_tech_chariot_training",
            "aw_tech_enfeoffment_study",
            "aw_tech_granary_accounting",
            "aw_tech_city_defense",
            "aw_tech_rites_music"
        };

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
            "aw_policy_name_integration",
            "aw_policy_military_merit",
            "aw_policy_base_enfeoffment",
            "aw_policy_border_enfeoffment",
            "aw_policy_favor_order",
            "aw_policy_continuous_enfeoffment",
            "aw_policy_early_law",
            "aw_policy_mandate_rites",
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
            TryStartIfEmpty(pKingdom, PolicyNodeKind.Decision, PickDecision(pKingdom));
            TryStartIfEmpty(pKingdom, PolicyNodeKind.Tech, PickResearch(pKingdom, PolicyNodeKind.Tech));
            TryStartIfEmpty(pKingdom, PolicyNodeKind.Social, PickResearch(pKingdom, PolicyNodeKind.Social));
        }

        public static KingdomPolicyDef PickDecision(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            return KingdomPolicyDefs.Decisions
                .Where(def => IsAvailable(pKingdom, def))
                .Where(def => ShouldAutoStartDecision(pKingdom, def))
                .OrderByDescending(def => ScoreDecision(pKingdom, def))
                .FirstOrDefault();
        }

        public static KingdomPolicyDef PickResearch(Kingdom pKingdom, PolicyNodeKind pKind)
        {
            if (pKingdom?.data == null) return null;
            IEnumerable<KingdomPolicyDef> defs = pKind == PolicyNodeKind.Tech
                ? KingdomPolicyDefs.Techs
                : KingdomPolicyDefs.SocialPolicies;

            return defs
                .Where(def => IsAvailable(pKingdom, def))
                .OrderByDescending(def => ScoreResearch(pKingdom, def))
                .FirstOrDefault();
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
                case "aw_decision_appease_xia_cities":
                    pKingdom.data.set(LineageKeys.POLICY_AI_LAST_XIA_APPEASE_YEAR, year);
                    break;
            }
        }

        private static bool IsAvailable(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            return pDef != null && KingdomPolicyService.GetStatus(pKingdom, pDef) == PolicyNodeStatus.Available;
        }

        private static int ScoreDecision(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_NAME, out string yearName, "");
            return KingdomDecisionPriorityRules.ScoreDecision(
                pDef.Id,
                MandateService.CanStabilizeMandate(pKingdom),
                RoyalExpansionDecisionService.CanExecute(pKingdom),
                CountCities(pKingdom),
                SlaveService.IsSlaveryEnabled(pKingdom),
                XiaizationService.ScoreResearch(pKingdom, pDef),
                string.IsNullOrEmpty(yearName));
        }

        private static bool ShouldAutoStartDecision(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return false;
            if (YearsSince(pKingdom, LineageKeys.POLICY_AI_LAST_DECISION_YEAR, -99999) < 8) return false;

            switch (pDef.Id)
            {
                case "aw_decision_claim_mandate":
                    return !MandateService.Exists && MandateService.CanDeclareMandate(pKingdom, out _);
                case "aw_decision_mandate_ritual":
                    return MandateService.CanStabilizeMandate(pKingdom);
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
                case "aw_decision_appease_xia_cities":
                    return YearsSince(pKingdom, LineageKeys.POLICY_AI_LAST_XIA_APPEASE_YEAR, -99999) >= 12 &&
                           XiaizationService.SpecialRequirementMet(pKingdom, pDef.Id);
                case "aw_decision_fabricate_core":
                    return WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom)?.data != null;
                case "aw_decision_year_name":
                    pKingdom.data.get(LineageKeys.KINGDOM_YEAR_NAME, out string yearName, "");
                    return string.IsNullOrEmpty(yearName);
                default:
                    return false;
            }
        }

        private static int ScoreYearNameDecision(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_NAME, out string yearName, "");
            return string.IsNullOrEmpty(yearName) ? 620 : 220;
        }

        private static int ScoreResearch(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            int orderScore = 1000 - PreferredIndex(pDef) * 20;
            int context = 0;
            context += XiaizationService.ScoreResearch(pKingdom, pDef);

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

            return orderScore + context;
        }

        private static int PreferredIndex(KingdomPolicyDef pDef)
        {
            string[] order = pDef.Kind == PolicyNodeKind.Tech ? TechOrder : SocialOrder;
            int index = Array.IndexOf(order, pDef.Id);
            return index >= 0 ? index : order.Length + Math.Max(0, pDef.Column * 3 + pDef.Row);
        }

        private static int CountCities(Kingdom pKingdom)
        {
            int count = 0;
            foreach (City city in pKingdom.getCities())
                if (city?.data != null && !city.isRekt()) count++;
            return count;
        }

        private static int CountUnits(Kingdom pKingdom)
        {
            int count = 0;
            foreach (Actor unit in pKingdom.getUnits())
                if (unit?.data != null && !unit.isRekt()) count++;
            return count;
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

        private static bool HasClearlyBetterCapital(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.capital == null || IsAtWar(pKingdom)) return false;
            City current = pKingdom.capital;
            float currentScore = CapitalScore(current, current, pKingdom);
            float bestScore = currentScore;

            foreach (City city in pKingdom.getCities())
            {
                if (!CapitalMoveRules.CanConsiderCandidate(
                        pCandidateAlive: city?.data != null && city.isAlive(),
                        pIsCurrentCapital: city == current,
                        pIsCoreCity: WarTerritoryService.HasCore(pKingdom, city),
                        pHasOwnNeighbor: CountOwnNeighbors(city, pKingdom) > 0))
                    continue;
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
