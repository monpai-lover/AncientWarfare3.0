using System.Collections.Generic;

namespace AncientWarfare3.core.db
{
    public sealed class LineageArchiveIndexSpec
    {
        public string name;
        public string table;
        public string columns;
        public string where;

        public string BuildSql()
        {
            string sql = "CREATE INDEX IF NOT EXISTS " + name + " ON " + table + " (" + columns + ")";
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

                Index("idx_KingdomArchive_kingdom", KingdomArchiveTableItem.GetTableName(),
                    "KINGDOM_ID"),
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
                Index("idx_PosthumousTitle_actor_time", PosthumousTitleTableItem.GetTableName(),
                    "ACTOR_ID, DECIDED_TIME, RECORD_ID"),

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
                Index("idx_VassalRelation_vassal_active", VassalRelationTableItem.GetTableName(),
                    "VASSAL_ID, ACTIVE, START_TIME"),
                Index("idx_VassalRelation_suzerain_active", VassalRelationTableItem.GetTableName(),
                    "SUZERAIN_ID, ACTIVE, START_TIME")
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
            string pWhere = "")
        {
            return new LineageArchiveIndexSpec
            {
                name = pName,
                table = pTable,
                columns = pColumns,
                where = pWhere
            };
        }
    }
}
