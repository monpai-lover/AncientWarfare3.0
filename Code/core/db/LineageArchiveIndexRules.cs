using System.Collections.Generic;

namespace AncientWarfare3.core.db
{
    public sealed class LineageArchiveIndexSpec
    {
        public string name;
        public string table;
        public string columns;
        public string where;
        public bool unique;

        public string BuildSql()
        {
            string sql = "CREATE " + (unique ? "UNIQUE " : "") + "INDEX IF NOT EXISTS " +
                         name + " ON " + table + " (" + columns + ")";
            return string.IsNullOrEmpty(where) ? sql : sql + " WHERE " + where;
        }
    }

    public static class LineageArchiveIndexRules
    {
        public static List<LineageArchiveIndexSpec> GetRequiredIndexes()
        {
            return new List<LineageArchiveIndexSpec>
            {
                Index("idx_FamilyEdge_child_slot", FamilyEdgeTableItem.GetTableName(),
                    "CHILD_ID, PARENT_SLOT"),
                Index("idx_FamilyEdge_parent_time", FamilyEdgeTableItem.GetTableName(),
                    "PARENT_ID, CREATED_TIME, CHILD_ID"),

                Index("idx_ActorArchive_parent1_birth", ActorArchiveTableItem.GetTableName(),
                    "PARENT_ID_1, BIRTH_TIME, ID"),
                Index("idx_ActorArchive_parent2_birth", ActorArchiveTableItem.GetTableName(),
                    "PARENT_ID_2, BIRTH_TIME, ID"),
                Index("idx_ActorArchive_shi_alive_birth", ActorArchiveTableItem.GetTableName(),
                    "SHI_ID, IS_ALIVE, BIRTH_TIME, ID"),
                Index("idx_ActorArchive_lineage_alive_birth", ActorArchiveTableItem.GetTableName(),
                    "LINEAGE_ID, IS_ALIVE, BIRTH_TIME, ID"),
                Index("idx_ActorArchive_city_alive_birth", ActorArchiveTableItem.GetTableName(),
                    "CITY_ID, IS_ALIVE, BIRTH_TIME, ID"),
                Index("idx_ActorArchive_kingdom_alive_birth", ActorArchiveTableItem.GetTableName(),
                    "KINGDOM_ID, IS_ALIVE, BIRTH_TIME, ID"),
                Index("idx_ActorArchive_original_clan", ActorArchiveTableItem.GetTableName(),
                    "ORIGINAL_CLAN_ID, IS_ALIVE, BIRTH_TIME, ID"),

                Index("idx_LineageGroup_family_created", LineageGroupTableItem.GetTableName(),
                    "FAMILY_NAME, CREATED_TIME, LINEAGE_ID"),
                Index("idx_ShiBranch_lineage_created", ShiBranchTableItem.GetTableName(),
                    "LINEAGE_ID, CREATED_TIME, SHI_ID"),
                Index("idx_ShiBranch_founder", ShiBranchTableItem.GetTableName(),
                    "FOUNDER_ACTOR_ID, SHI_ID"),
                Index("idx_ShiBranch_origin_city", ShiBranchTableItem.GetTableName(),
                    "ORIGIN_CITY_ID, CREATED_TIME, SHI_ID"),
                Index("idx_ShiBranch_parent", ShiBranchTableItem.GetTableName(),
                    "PARENT_SHI_ID, CREATED_TIME, SHI_ID"),
                Index("idx_ShiBranch_state_name", ShiBranchTableItem.GetTableName(),
                    "STATE_NAME, SHI_ID"),

                Index("idx_KingdomArchive_kingdom", KingdomArchiveTableItem.GetTableName(),
                    "KINGDOM_ID"),
                Index("idx_KingdomArchive_alive_name", KingdomArchiveTableItem.GetTableName(),
                    "IS_ALIVE, KINGDOM_NAME"),
                Index("idx_KingdomHistory_kingdom_time", KingdomHistoryTableItem.GetTableName(),
                    "KINGDOM_ID, WORLD_TIME, EVENT_ID"),
                Index("idx_CityHistory_city_time", CityHistoryTableItem.GetTableName(),
                    "CITY_ID, WORLD_TIME, EVENT_ID"),
                Index("idx_CityHistory_context_kingdom_time", CityHistoryTableItem.GetTableName(),
                    "CONTEXT_KINGDOM_ID, WORLD_TIME, EVENT_ID"),
                Index("idx_PersonBiography_actor_time", PersonBiographyTableItem.GetTableName(),
                    "ACTOR_ID, WORLD_TIME, EVENT_ID"),
                Index("idx_PersonBiography_actor_event_time", PersonBiographyTableItem.GetTableName(),
                    "ACTOR_ID, EVENT_TYPE, WORLD_TIME, EVENT_ID"),

                Index("idx_KingdomReign_kingdom_start", KingdomReignTableItem.GetTableName(),
                    "KINGDOM_ID, START_TIME, REIGN_ID"),
                Index("idx_KingdomReign_king_actor_end", KingdomReignTableItem.GetTableName(),
                    "KING_ACTOR_ID, END_TIME, START_TIME"),
                Index("idx_DynastyPeriod_shi_active_state", DynastyPeriodTableItem.GetTableName(),
                    "END_TIME, SHI_ID, STATE_NAME"),
                Index("idx_EraPeriod_kingdom_open", EraPeriodTableItem.GetTableName(),
                    "KINGDOM_ID, END_TIME, START_TIME, ERA_ID"),
                Index("idx_EraPeriod_shi_name", EraPeriodTableItem.GetTableName(),
                    "SHI_ID, ERA_STEM, START_TIME, ERA_ID"),
                Index("uq_EraPeriod_event", EraPeriodTableItem.GetTableName(),
                    "REIGN_ID, CHANGE_KIND, SOURCE_EVENT_ID",
                    "SOURCE_EVENT_ID<>''", pUnique: true),
                Index("idx_PosthumousTitle_actor_time", PosthumousTitleTableItem.GetTableName(),
                    "ACTOR_ID, DECIDED_TIME, RECORD_ID"),
                Index("idx_PosthumousTitle_mandate_period_reign",
                    PosthumousTitleTableItem.GetTableName(),
                    "MANDATE_PERIOD_ID, REIGN_ID, DECIDED_TIME"),
                Index("idx_WarRecord_attacker_start", WarRecordTableItem.GetTableName(),
                    "ATTACKER_KINGDOM_ID, START_TIME, END_TIME"),
                Index("idx_WarRecord_defender_start", WarRecordTableItem.GetTableName(),
                    "DEFENDER_KINGDOM_ID, START_TIME, END_TIME"),
                Index("idx_PosthumousTitle_shi_kind", PosthumousTitleTableItem.GetTableName(),
                    "SHI_ID, TITLE_KIND, DECIDED_TIME, RECORD_ID"),
                Index("uq_PosthumousTitle_reign", PosthumousTitleTableItem.GetTableName(),
                    "REIGN_ID", "REIGN_ID>=0", pUnique: true),
                Index("uq_PosthumousTitle_retrospective_actor",
                    PosthumousTitleTableItem.GetTableName(),
                    "SHI_ID, ACTOR_ID", "IS_RETROSPECTIVE=1", pUnique: true),
                Index("uq_DynastyTitleRegistry_value", DynastyTitleRegistryTableItem.GetTableName(),
                    "SHI_ID, TITLE_TYPE, TITLE_VALUE, CYCLE_NO", pUnique: true),
                Index("idx_DynastyTitleRegistry_shi_kind",
                    DynastyTitleRegistryTableItem.GetTableName(),
                    "SHI_ID, TITLE_TYPE, USED_TIME, REGISTRY_ID"),

                Index("idx_KingdomCore_kingdom_city_active", KingdomCoreTableItem.GetTableName(),
                    "KINGDOM_ID, CITY_ID, ACTIVE"),
                Index("idx_KingdomCore_city_owner_active", KingdomCoreTableItem.GetTableName(),
                    "CITY_ID, OWNER_KINGDOM_ID, ACTIVE"),
                Index("idx_WarClaim_source_target_active", WarClaimTableItem.GetTableName(),
                    "SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID, ACTIVE, CONSUMED, TARGET_CITY_ID"),
                Index("idx_WarClaim_source_city_active", WarClaimTableItem.GetTableName(),
                    "SOURCE_KINGDOM_ID, TARGET_CITY_ID, ACTIVE, CONSUMED"),
                Index("idx_WarProject_source_city_active", WarProjectTableItem.GetTableName(),
                    "SOURCE_KINGDOM_ID, TARGET_CITY_ID, ACTIVE, COMPLETED"),
                Index("idx_WarProject_source_target_active", WarProjectTableItem.GetTableName(),
                    "SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID, ACTIVE, COMPLETED"),

                Index("idx_MandateCoreCity_kingdom_city_active", MandateCoreCityTableItem.GetTableName(),
                    "ORIGINAL_KINGDOM_ID, CITY_ID, ACTIVE"),
                Index("idx_MandateCoreCity_period_city_active", MandateCoreCityTableItem.GetTableName(),
                    "PERIOD_ID, CITY_ID, ACTIVE"),
                Index("idx_MandateEvent_period_time", MandateEventTableItem.GetTableName(),
                    "PERIOD_ID, WORLD_TIME, EVENT_ID"),
                Index("idx_MandatePeriod_kingdom_start", MandatePeriodTableItem.GetTableName(),
                    "KINGDOM_ID, START_TIME, PERIOD_ID"),

                Index("idx_RoyalClaim_host_active", RoyalClaimTableItem.GetTableName(),
                    "HOST_KINGDOM_ID, ACTIVE, CLAIM_STRENGTH"),
                Index("idx_RoyalClaim_original_active", RoyalClaimTableItem.GetTableName(),
                    "ORIGINAL_KINGDOM_ID, ACTIVE, CLAIM_STRENGTH"),
                Index("idx_RoyalClaim_actor_transfer", RoyalClaimTableItem.GetTableName(),
                    "CLAIMANT_ACTOR_ID, ACTIVE, CLAIM_GENERATION, ORIGINAL_KINGDOM_ID"),
                Index("idx_RoyalClaim_dormant_schedule", RoyalClaimTableItem.GetTableName(),
                    "ACTIVE, RESTORATION_STATE, CLAIM_STRENGTH, LAST_ATTEMPT_YEAR, CLAIM_ID"),
                Index("idx_RoyalClaim_actor_original_active_unique", RoyalClaimTableItem.GetTableName(),
                    "CLAIMANT_ACTOR_ID, ORIGINAL_KINGDOM_ID", "ACTIVE=1", pUnique: true),
                Index("idx_RestorationCampaign_state_year", RestorationCampaignTableItem.GetTableName(),
                    "STATE, LAST_ATTEMPT_YEAR, CAMPAIGN_ID"),
                Index("idx_RestorationCampaign_kingdom_state", RestorationCampaignTableItem.GetTableName(),
                    "ORIGINAL_KINGDOM_ID, STATE, CAMPAIGN_ID"),
                Index("idx_VassalRelation_vassal_active", VassalRelationTableItem.GetTableName(),
                    "VASSAL_ID, ACTIVE, START_TIME"),
                Index("idx_VassalRelation_suzerain_active", VassalRelationTableItem.GetTableName(),
                    "SUZERAIN_ID, ACTIVE, START_TIME"),
                Index("uq_Feudatory_prince_active", FeudatoryTableItem.GetTableName(),
                    "PRINCE_ACTOR_ID", "STATUS=0 AND END_TIME<0", pUnique: true),
                Index("idx_Feudatory_empire_active", FeudatoryTableItem.GetTableName(),
                    "EMPIRE_KINGDOM_ID, STATUS, FEUDATORY_ID"),
                Index("uq_FeudatoryCity_city_active", FeudatoryCityTableItem.GetTableName(),
                    "CITY_ID", "ACTIVE=1", pUnique: true),
                Index("idx_FeudatoryCity_feudatory_active", FeudatoryCityTableItem.GetTableName(),
                    "FEUDATORY_ID, ACTIVE, CITY_ID"),

                Index("idx_KingdomCourtState_kingdom", KingdomCourtStateTableItem.GetTableName(),
                    "KINGDOM_ID"),
                Index("idx_CourtOfficer_kingdom_active", CourtOfficerTableItem.GetTableName(),
                    "KINGDOM_ID, ACTIVE, LAYER, OFFICE_ID"),
                Index("idx_CourtOfficer_actor_active", CourtOfficerTableItem.GetTableName(),
                    "ACTOR_ID, ACTIVE, KINGDOM_ID"),
                Index("idx_CourtOfficer_actor_appointed", CourtOfficerTableItem.GetTableName(),
                    "ACTOR_ID, APPOINTED_TIME, OFFICER_ID"),
                Index("idx_CourtOfficer_actor_layer_active_unique",
                    CourtOfficerTableItem.GetTableName(), "ACTOR_ID, LAYER", "ACTIVE=1",
                    pUnique: true),
                Index("idx_CourtOfficer_central_host_office_unique",
                    CourtOfficerTableItem.GetTableName(), "KINGDOM_ID, LAYER, OFFICE_ID",
                    "ACTIVE=1 AND LAYER='central'", pUnique: true),
                Index("idx_CityBureauState_kingdom_city", CityBureauStateTableItem.GetTableName(),
                    "KINGDOM_ID, CITY_ID"),

                Index("idx_SchoolMembership_actor_active_unique",
                    SchoolMembershipTableItem.GetTableName(), "ACTOR_ID", "ACTIVE=1",
                    pUnique: true),
                Index("idx_SchoolMembership_school_active",
                    SchoolMembershipTableItem.GetTableName(),
                    "SCHOOL_ID, ACTIVE, REPUTATION, ACTOR_ID"),
                Index("idx_CitySchoolLedger_city_school",
                    CitySchoolLedgerTableItem.GetTableName(), "CITY_ID, SCHOOL_ID"),
                Index("idx_HistoricalSchoolMaster_actor",
                    HistoricalSchoolMasterTableItem.GetTableName(), "ACTOR_ID, DEAD"),
                Index("idx_SchoolAffiliation_residence",
                    SchoolAffiliationTableItem.GetTableName(),
                    "RESIDENCE_CITY_ID, LIFECYCLE_STATE, ACTOR_ID"),
                Index("idx_SchoolAffiliation_service",
                    SchoolAffiliationTableItem.GetTableName(),
                    "SERVICE_KINGDOM_ID, SERVICE_END_YEAR, ACTOR_ID"),
                Index("idx_SchoolInstitution_city_active",
                    SchoolInstitutionTableItem.GetTableName(),
                    "CITY_ID, ACTIVE, SCHOOL_ID, CONDITION"),
                Index("idx_SchoolWork_school_preserved", SchoolWorkTableItem.GetTableName(),
                    "SCHOOL_ID, PRESERVED, CITY_ID"),
                Index("idx_SchoolDebate_city_year", SchoolDebateTableItem.GetTableName(),
                    "CITY_ID, DEBATE_YEAR, DEBATE_ID"),
                Index("idx_SchoolEvent_school_year", SchoolEventTableItem.GetTableName(),
                    "SCHOOL_ID, EVENT_YEAR, EVENT_ID"),
                Index("idx_SchoolEvent_school_type_actor_first",
                    SchoolEventTableItem.GetTableName(),
                    "SCHOOL_ID, EVENT_TYPE, ACTOR_ID, EVENT_YEAR, WORLD_TIME"),
                Index("idx_SchoolEvent_guest_operation_unique",
                    SchoolEventTableItem.GetTableName(), "OPERATION_KEY",
                    "OPERATION_KEY<>'' AND EVENT_TYPE IN ('guest_service_started'," +
                    "'guest_service_renewed')", pUnique: true),
                Index("idx_SchoolEvent_teaching_operation_unique",
                    SchoolEventTableItem.GetTableName(), "OPERATION_KEY",
                    "OPERATION_KEY<>'' AND EVENT_TYPE IN ('lecture','persuasion')",
                    pUnique: true),
                Index("idx_SchoolEvent_teaching_actor_year",
                    SchoolEventTableItem.GetTableName(),
                    "EVENT_TYPE, ACTOR_ID, EVENT_YEAR, EVENT_ID",
                    "EVENT_TYPE IN ('lecture','persuasion')"),
                Index("idx_SchoolEvent_lecture_city_school_year",
                    SchoolEventTableItem.GetTableName(),
                    "CITY_ID, SCHOOL_ID, EVENT_YEAR, EVENT_ID", "EVENT_TYPE='lecture'"),
                Index("idx_SchoolEvent_persuasion_kingdom_year",
                    SchoolEventTableItem.GetTableName(),
                    "KINGDOM_ID, EVENT_YEAR, EVENT_ID", "EVENT_TYPE='persuasion'")
            };
        }

        public static bool ContainsIndex(IEnumerable<LineageArchiveIndexSpec> pSpecs, string pName)
        {
            if (pSpecs == null || string.IsNullOrEmpty(pName)) return false;
            foreach (LineageArchiveIndexSpec spec in pSpecs)
                if (spec != null && spec.name == pName) return true;
            return false;
        }

        private static LineageArchiveIndexSpec Index(string pName, string pTable, string pColumns,
            string pWhere = "", bool pUnique = false)
        {
            return new LineageArchiveIndexSpec
            {
                name = pName,
                table = pTable,
                columns = pColumns,
                where = pWhere,
                unique = pUnique
            };
        }
    }
}
