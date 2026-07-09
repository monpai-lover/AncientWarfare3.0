using System;

namespace AncientWarfare3.core.policy
{
    public static class UpdateAgeBenchmarkRules
    {
        public const string ParentGroup = "update_age";
        public const string Total = "aw3_update_age_total";
        public const string FullWall = "aw3_update_object_age_wall";
        public const string UnaccountedWall = "aw3_update_age_unaccounted_wall";

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
        public const int TopLevelEntryCount = 18;
        public const int KingdomPolicyPointsIndex = 18;
        public const int KingdomPolicyAiIndex = 19;
        public const int KingdomPolicyAdvanceTechIndex = 20;
        public const int KingdomPolicyAdvanceSocialIndex = 21;
        public const int KingdomPolicyAdvanceDecisionIndex = 22;
        public const int KingdomPolicySnapshotIndex = 23;
        public const int KingdomPolicyMapDirtyIndex = 24;
        public const int KingdomCourtYearTickIndex = 25;
        public const int KingdomCourtCandidateRefreshIndex = 26;
        public const int KingdomCourtOfficerValidateIndex = 27;
        public const int KingdomCourtFactionRecalcIndex = 28;
        public const int KingdomCourtAiBiasIndex = 29;
        public const int KingdomCourtUiBuildIndex = 30;
        public const int KingdomCityBureauRefreshIndex = 31;
        public const int CityTechSpreadCompletedIndex = 32;
        public const int CityTechNeighborExposureIndex = 33;
        public const int CityTechNeighborInfluenceIndex = 34;
        public const int CityEconomyUpdateCitiesIndex = 35;
        public const int CityEconomyMapDirtyIndex = 36;
        public const int CityEconomyTechReportIndex = 37;
        public const int CityEconomySlaveCountIndex = 38;
        public const int CityEconomyDbUpsertIndex = 39;
        public const int ActorFullWallIndex = 40;
        public const int CityFullWallIndex = 41;
        public const int KingdomFullWallIndex = 42;

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
        public const string KingdomPolicyPoints = "aw3_kingdom_policy_points";
        public const string KingdomPolicyAi = "aw3_kingdom_policy_ai";
        public const string KingdomPolicyAdvanceTech = "aw3_kingdom_policy_advance_tech";
        public const string KingdomPolicyAdvanceSocial = "aw3_kingdom_policy_advance_social";
        public const string KingdomPolicyAdvanceDecision = "aw3_kingdom_policy_advance_decision";
        public const string KingdomPolicySnapshot = "aw3_kingdom_policy_snapshot";
        public const string KingdomPolicyMapDirty = "aw3_kingdom_policy_map_dirty";
        public const string KingdomCourtYearTick = "aw3_court_year_tick";
        public const string KingdomCourtCandidateRefresh = "aw3_court_candidate_refresh";
        public const string KingdomCourtOfficerValidate = "aw3_court_officer_validate";
        public const string KingdomCourtFactionRecalc = "aw3_court_faction_recalc";
        public const string KingdomCourtAiBias = "aw3_court_ai_policy_bias";
        public const string KingdomCourtUiBuild = "aw3_court_ui_build";
        public const string KingdomCityBureauRefresh = "aw3_city_bureau_refresh";
        public const string CityTechSpreadCompleted = "aw3_city_tech_spread_completed";
        public const string CityTechNeighborExposure = "aw3_city_tech_neighbor_exposure";
        public const string CityTechNeighborInfluence = "aw3_city_tech_neighbor_influence";
        public const string CityEconomyUpdateCities = "aw3_city_economy_update_cities";
        public const string CityEconomyMapDirty = "aw3_city_economy_map_dirty";
        public const string CityEconomyTechReport = "aw3_city_economy_tech_report";
        public const string CityEconomySlaveCount = "aw3_city_economy_slave_count";
        public const string CityEconomyDbUpsert = "aw3_city_economy_db_upsert";
        public const string ActorFullWall = "aw3_actor_update_age_wall";
        public const string CityFullWall = "aw3_city_update_age_wall";
        public const string KingdomFullWall = "aw3_kingdom_update_age_wall";

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
            KingdomGeneral,
            KingdomPolicyPoints,
            KingdomPolicyAi,
            KingdomPolicyAdvanceTech,
            KingdomPolicyAdvanceSocial,
            KingdomPolicyAdvanceDecision,
            KingdomPolicySnapshot,
            KingdomPolicyMapDirty,
            KingdomCourtYearTick,
            KingdomCourtCandidateRefresh,
            KingdomCourtOfficerValidate,
            KingdomCourtFactionRecalc,
            KingdomCourtAiBias,
            KingdomCourtUiBuild,
            KingdomCityBureauRefresh,
            CityTechSpreadCompleted,
            CityTechNeighborExposure,
            CityTechNeighborInfluence,
            CityEconomyUpdateCities,
            CityEconomyMapDirty,
            CityEconomyTechReport,
            CityEconomySlaveCount,
            CityEconomyDbUpsert,
            ActorFullWall,
            CityFullWall,
            KingdomFullWall
        };

        public static bool Contains(string pId)
        {
            if (pId == FullWall || pId == UnaccountedWall) return true;
            return Array.IndexOf(EntryIds, pId) >= 0;
        }

        public static bool IsValidIndex(int pIndex)
        {
            return pIndex >= 0 && pIndex < EntryIds.Length;
        }

        public static bool IsTopLevelIndex(int pIndex)
        {
            return pIndex >= 0 && pIndex < TopLevelEntryCount;
        }

        public static string ParentForIndex(int pIndex)
        {
            if (pIndex >= KingdomPolicyPointsIndex && pIndex <= KingdomPolicyMapDirtyIndex)
                return KingdomPolicy;
            if (pIndex >= KingdomCourtYearTickIndex && pIndex <= KingdomCityBureauRefreshIndex)
                return KingdomPolicy;
            if (pIndex >= CityTechSpreadCompletedIndex && pIndex <= CityTechNeighborInfluenceIndex)
                return KingdomCityTech;
            if (pIndex >= CityEconomyUpdateCitiesIndex && pIndex <= CityEconomyDbUpsertIndex)
                return KingdomCityEconomy;
            if (pIndex >= ActorFullWallIndex && pIndex <= KingdomFullWallIndex)
                return FullWall;
            return Total;
        }
    }
}
