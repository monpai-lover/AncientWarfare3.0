using System;

namespace AncientWarfare3.core.policy
{
    public static class UpdateAgeBenchmarkRules
    {
        public const string ParentGroup = "update_age";
        public const string Total = "aw3_update_age_total";

        public const int ActorRetirementIndex = 0;
        public const int ActorOldHeadIndex = 1;
        public const int CitySlaveFoodIndex = 2;
        public const int KingdomXiaizationIndex = 3;
        public const int KingdomPolicyIndex = 4;
        public const int KingdomCityTechIndex = 5;
        public const int KingdomCityEconomyIndex = 6;
        public const int KingdomWarTerritoryIndex = 7;
        public const int KingdomMandateIndex = 8;
        public const int KingdomMandateDecisionIndex = 9;
        public const int KingdomMandateRebelIndex = 10;
        public const int KingdomForeignOccupationIndex = 11;
        public const int KingdomHeavyScheduleIndex = 12;
        public const int KingdomWarPlotIndex = 13;
        public const int KingdomWarAiIndex = 14;
        public const int KingdomVassalAiIndex = 15;
        public const int KingdomGeneralScheduleIndex = 16;
        public const int KingdomGeneralIndex = 17;

        public const string ActorRetirement = "aw3_actor_update_age_retirement";
        public const string ActorOldHead = "aw3_actor_update_age_old_head";
        public const string CitySlaveFood = "aw3_city_update_age_slave_food";
        public const string KingdomXiaization = "aw3_kingdom_update_age_xiaization";
        public const string KingdomPolicy = "aw3_kingdom_update_age_policy";
        public const string KingdomCityTech = "aw3_kingdom_update_age_city_tech";
        public const string KingdomCityEconomy = "aw3_kingdom_update_age_city_economy";
        public const string KingdomWarTerritory = "aw3_kingdom_update_age_war_territory";
        public const string KingdomMandate = "aw3_kingdom_update_age_mandate";
        public const string KingdomMandateDecision = "aw3_kingdom_update_age_mandate_decision";
        public const string KingdomMandateRebel = "aw3_kingdom_update_age_mandate_rebel";
        public const string KingdomForeignOccupation = "aw3_kingdom_update_age_foreign_occupation";
        public const string KingdomHeavySchedule = "aw3_kingdom_update_age_heavy_schedule";
        public const string KingdomWarPlot = "aw3_kingdom_update_age_war_plot";
        public const string KingdomWarAi = "aw3_kingdom_update_age_war_ai";
        public const string KingdomVassalAi = "aw3_kingdom_update_age_vassal_ai";
        public const string KingdomGeneralSchedule = "aw3_kingdom_update_age_general_schedule";
        public const string KingdomGeneral = "aw3_kingdom_update_age_general";

        public static readonly string[] EntryIds =
        {
            ActorRetirement,
            ActorOldHead,
            CitySlaveFood,
            KingdomXiaization,
            KingdomPolicy,
            KingdomCityTech,
            KingdomCityEconomy,
            KingdomWarTerritory,
            KingdomMandate,
            KingdomMandateDecision,
            KingdomMandateRebel,
            KingdomForeignOccupation,
            KingdomHeavySchedule,
            KingdomWarPlot,
            KingdomWarAi,
            KingdomVassalAi,
            KingdomGeneralSchedule,
            KingdomGeneral
        };

        public static bool Contains(string pId)
        {
            return Array.IndexOf(EntryIds, pId) >= 0;
        }

        public static bool IsValidIndex(int pIndex)
        {
            return pIndex >= 0 && pIndex < EntryIds.Length;
        }
    }
}
