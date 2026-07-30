using System.Collections.Generic;

namespace AncientWarfare3.core.db
{
    public sealed class DiplomacyActionIndexSpec
    {
        public string Name = "";
        public string Table = "";
        public string Columns = "";
        public string Where = "";
        public bool Unique;

        public string BuildSql()
        {
            string sql = "CREATE " + (Unique ? "UNIQUE " : "") +
                         "INDEX IF NOT EXISTS " + Name + " ON " + Table +
                         " (" + Columns + ")";
            return string.IsNullOrEmpty(Where)
                ? sql
                : sql + " WHERE " + Where;
        }
    }

    public static class DiplomacyActionIndexRules
    {
        public static IReadOnlyList<DiplomacyActionIndexSpec>
            GetRequiredIndexes()
        {
            return new[]
            {
                Index("uq_DiplomaticCoalition_active_pair_target",
                    "DiplomaticCoalition",
                    "MEMBER_A_ID, MEMBER_B_ID, TARGET_KINGDOM_ID",
                    "STATUS=0 AND END_TIME<0", true),
                Index("idx_DiplomaticCoalition_target_active",
                    "DiplomaticCoalition",
                    "TARGET_KINGDOM_ID, STATUS, END_YEAR, COALITION_ID"),
                Index("idx_DiplomaticCoalition_member_a_active",
                    "DiplomaticCoalition",
                    "MEMBER_A_ID, STATUS, END_YEAR, COALITION_ID"),
                Index("idx_DiplomaticCoalition_member_b_active",
                    "DiplomaticCoalition",
                    "MEMBER_B_ID, STATUS, END_YEAR, COALITION_ID"),
                Index("uq_DiplomaticMarriage_active_actors",
                    "DiplomaticMarriage", "ACTOR_A_ID, ACTOR_B_ID",
                    "STATUS=0 AND END_TIME<0", true),
                Index("idx_DiplomaticMarriage_actor_a_active",
                    "DiplomaticMarriage", "ACTOR_A_ID",
                    "STATUS=0 AND END_TIME<0"),
                Index("idx_DiplomaticMarriage_actor_b_active",
                    "DiplomaticMarriage", "ACTOR_B_ID",
                    "STATUS=0 AND END_TIME<0"),
                Index("idx_DiplomaticMarriage_realms_active",
                    "DiplomaticMarriage",
                    "KINGDOM_A_ID, KINGDOM_B_ID, STATUS, MARRIAGE_ID"),
                Index("idx_DiplomaticMarriage_kingdom_a_active",
                    "DiplomaticMarriage",
                    "KINGDOM_A_ID, STATUS, MARRIAGE_ID"),
                Index("idx_DiplomaticMarriage_kingdom_b_active",
                    "DiplomaticMarriage",
                    "KINGDOM_B_ID, STATUS, MARRIAGE_ID"),
                Index("idx_RulerHousehold_ruler_active",
                    "RulerHousehold",
                    "RULER_ACTOR_ID, STATUS, RELATIONSHIP_KIND, " +
                    "RELATIONSHIP_ID"),
                Index("idx_RulerHousehold_partner_active",
                    "RulerHousehold",
                    "PARTNER_ACTOR_ID, STATUS, RELATIONSHIP_ID"),
                Index("idx_RulerHousehold_recipient_active",
                    "RulerHousehold",
                    "RECIPIENT_KINGDOM_ID, STATUS, RELATIONSHIP_ID"),
                Index("idx_RulerHousehold_proposal",
                    "RulerHousehold", "SOURCE_PROPOSAL_ID, RELATIONSHIP_ID"),
                Index("uq_RulerHousehold_active_principal_ruler",
                    "RulerHousehold", "RULER_ACTOR_ID",
                    "STATUS=0 AND END_TIME<0 AND RELATIONSHIP_KIND='principal_wife'",
                    true),
                Index("uq_RulerHousehold_active_partner",
                    "RulerHousehold", "PARTNER_ACTOR_ID",
                    "STATUS=0 AND END_TIME<0", true),
                Index("idx_ActorArchive_lineage_kingdom_alive_birth",
                    "ActorArchive",
                    "LINEAGE_ID, KINGDOM_ID, IS_ALIVE, BIRTH_TIME, ID"),
                Index("uq_DiplomaticOperation_active_pair_type",
                    "DiplomaticOperation",
                    "SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID, OPERATION_TYPE",
                    "STATUS IN (0,1)", true),
                Index("uq_DiplomaticOperation_active_unordered_pair",
                    "DiplomaticOperation",
                    "MIN(SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID), " +
                    "MAX(SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID)",
                    "STATUS IN (0,1)", true),
                Index("idx_DiplomaticOperation_due",
                    "DiplomaticOperation",
                    "STATUS, DUE_TIME, OPERATION_ID"),
                Index("idx_DiplomaticOperation_pair_status_type",
                    "DiplomaticOperation",
                    "SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID, STATUS, " +
                    "OPERATION_TYPE, DUE_YEAR"),
                Index("idx_SpyNetwork_pair_active_accrual",
                    "SpyNetwork",
                    "SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID, ACTIVE, " +
                    "LAST_ACCRUAL_YEAR"),
                Index("uq_SpyNetwork_pair", "SpyNetwork",
                    "SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID", "", true),
                Index("idx_SpyNetworkClaimPurchase_pair_year",
                    "SpyNetworkClaimPurchase",
                    "SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID, PURCHASE_YEAR"),
                Index("uq_SpyNetworkClaimPurchase_key",
                    "SpyNetworkClaimPurchase",
                    "SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID, PURCHASE_KEY",
                    "", true),
                Index("uq_DiplomaticRelationModifier_active_source",
                    "DiplomaticRelationModifier", "SOURCE_TYPE, SOURCE_ID",
                    "ACTIVE=1", true),
                Index("idx_DiplomaticRelationModifier_pair_active",
                    "DiplomaticRelationModifier",
                    "KINGDOM_A_ID, KINGDOM_B_ID, ACTIVE, UNTIL_YEAR, " +
                    "MODIFIER_ID"),
                Index("idx_DiplomacyProposal_pair_type_rejection",
                    "DiplomacyProposal",
                    "MIN(REQUESTER_KINGDOM_ID, RESPONDER_KINGDOM_ID), " +
                    "MAX(REQUESTER_KINGDOM_ID, RESPONDER_KINGDOM_ID), " +
                    "PROPOSAL_TYPE, PLAYER_INITIATED, STATUS, RESPONSE_YEAR"),
                Index("uq_DiplomacyProposal_outstanding_pair",
                    "DiplomacyProposal",
                    "MIN(REQUESTER_KINGDOM_ID, RESPONDER_KINGDOM_ID), " +
                    "MAX(REQUESTER_KINGDOM_ID, RESPONDER_KINGDOM_ID)",
                    "STATUS IN ('pending','processing')", true),
                Index("idx_DiplomacyProposal_responder_status_created",
                    "DiplomacyProposal",
                    "RESPONDER_KINGDOM_ID, STATUS, CREATED_TIME, " +
                    "PROPOSAL_ID"),
                Index("idx_DiplomacyProposal_pending_due",
                    "DiplomacyProposal",
                    "STATUS, RESPONSE_DUE_TIME, PROPOSAL_ID"),
                Index("idx_DiplomacyProposal_processing_due",
                    "DiplomacyProposal",
                    "STATUS, RESPONSE_TIME, PROPOSAL_ID"),
                Index("idx_DiplomacyProposal_war_truce_coverage",
                    "DiplomacyProposal",
                    "WAR_ID, PROPOSAL_TYPE, STATUS, TREATY_UNTIL_YEAR, " +
                    "REQUESTER_KINGDOM_ID, RESPONDER_KINGDOM_ID")
            };
        }

        private static DiplomacyActionIndexSpec Index(string pName,
            string pTable, string pColumns, string pWhere = "",
            bool pUnique = false)
        {
            return new DiplomacyActionIndexSpec
            {
                Name = pName,
                Table = pTable,
                Columns = pColumns,
                Where = pWhere,
                Unique = pUnique
            };
        }
    }
}
