using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal sealed class RulerHouseholdQuery
    {
        private const int WorldTimePerYear = 60;
        private const int MaximumRowsPerRuler = 10;
        private const int MaximumMaintenanceRows = 16;
        private const int MaximumOfferCandidates = 32;
        private const string Projection =
            "RELATIONSHIP_ID,RULER_ACTOR_ID,PARTNER_ACTOR_ID," +
            "SOURCE_KINGDOM_ID,RECIPIENT_KINGDOM_ID,RELATIONSHIP_KIND," +
            "RANK_CODE,START_YEAR,START_TIME,END_TIME,STATUS," +
            "SOURCE_PROPOSAL_ID,OWNER_ROLE_AT_ENTRY,SOURCE_KIND," +
            "SOURCE_RELATION_ID,SOURCE_TRIBUTE_YEAR";

        private readonly SQLiteConnection _db;

        public RulerHouseholdQuery(SQLiteConnection pDb)
        {
            _db = pDb ?? throw new ArgumentNullException(nameof(pDb));
        }

        public IReadOnlyList<RulerHouseholdRecord> ReadActiveByRuler(
            long pRulerActorId, int pConsortCapacity)
        {
            int limit = Math.Min(MaximumRowsPerRuler,
                Math.Max(0, pConsortCapacity) + 1);
            return ReadMany("RULER_ACTOR_ID=@id", pRulerActorId,
                pAfterId: -1L, limit);
        }

        public IReadOnlyList<RulerHouseholdRecord> ReadActiveByRecipient(
            long pRecipientKingdomId, long pAfterRelationshipId,
            int pRequestedLimit)
        {
            int limit = Math.Min(MaximumMaintenanceRows,
                Math.Max(0, pRequestedLimit));
            return ReadMany("RECIPIENT_KINGDOM_ID=@id",
                pRecipientKingdomId, pAfterRelationshipId, limit);
        }

        public bool TryReadActiveByPartner(long pPartnerActorId,
            out RulerHouseholdRecord pRecord)
        {
            return TryReadOne("PARTNER_ACTOR_ID=@id AND STATUS=0 AND " +
                              "END_TIME<0", pPartnerActorId, out pRecord);
        }

        public int CountActiveConsorts(long pRulerActorId)
        {
            using var command = new SQLiteCommand(
                "SELECT COUNT(*) FROM RulerHousehold WHERE " +
                "RULER_ACTOR_ID=@id AND STATUS=0 AND END_TIME<0 AND " +
                "RELATIONSHIP_KIND='consort'", _db);
            command.Parameters.AddWithValue("@id", pRulerActorId);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public bool HasActivePrincipal(long pRulerActorId)
        {
            using var command = new SQLiteCommand(
                "SELECT 1 FROM RulerHousehold WHERE RULER_ACTOR_ID=@id " +
                "AND STATUS=0 AND END_TIME<0 AND " +
                "RELATIONSHIP_KIND='principal_wife' LIMIT 1", _db);
            command.Parameters.AddWithValue("@id", pRulerActorId);
            return command.ExecuteScalar() != null;
        }

        public bool TryReadByProposal(long pProposalId,
            out RulerHouseholdRecord pRecord)
        {
            return TryReadOne("SOURCE_PROPOSAL_ID=@id", pProposalId,
                out pRecord);
        }

        public bool HasTributaryOffering(long pRelationId,
            int pTributeYear)
        {
            using var command = new SQLiteCommand(
                "SELECT 1 FROM RulerHousehold WHERE " +
                "SOURCE_KIND='tributary_offering' AND " +
                "SOURCE_RELATION_ID=@relation AND SOURCE_TRIBUTE_YEAR=@year " +
                "LIMIT 1", _db);
            command.Parameters.AddWithValue("@relation", pRelationId);
            command.Parameters.AddWithValue("@year", pTributeYear);
            return command.ExecuteScalar() != null;
        }

        public IReadOnlyList<long> ReadActiveOwnerIdsByRecipient(
            long pKingdomId, int pLimit)
        {
            return ReadActiveOwnerIdsByRecipient(pKingdomId, -1L, pLimit);
        }

        public IReadOnlyList<long> ReadActiveOwnerIdsByRecipient(
            long pKingdomId, long pAfterOwnerId, int pLimit)
        {
            int limit = Math.Min(MaximumMaintenanceRows,
                Math.Max(0, pLimit));
            var result = new List<long>(limit);
            if (pKingdomId < 0L || limit == 0) return result;
            using var command = new SQLiteCommand(
                "SELECT DISTINCT RULER_ACTOR_ID FROM RulerHousehold WHERE " +
                "RECIPIENT_KINGDOM_ID=@kingdom AND STATUS=0 AND END_TIME<0 " +
                "AND RULER_ACTOR_ID>@after ORDER BY RULER_ACTOR_ID LIMIT @limit", _db);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@after", pAfterOwnerId);
            command.Parameters.AddWithValue("@limit", limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(reader.GetInt64(0));
            return result;
        }

        public IReadOnlyList<RulerHouseholdRecord> ReadActiveByOwner(
            long pOwnerActorId, int pLimit)
        {
            int limit = Math.Min(MaximumRowsPerRuler,
                Math.Max(0, pLimit));
            return ReadMany("RULER_ACTOR_ID=@id", pOwnerActorId,
                pAfterId: -1L, limit);
        }

        public IReadOnlyList<RulerHouseholdRecord>
            ReadActiveForRankNormalization(long pRulerActorId)
        {
            return ReadMany("RULER_ACTOR_ID=@id", pRulerActorId,
                pAfterId: -1L, MaximumMaintenanceRows);
        }

        public IReadOnlyList<long> ReadOfferCandidateIds(long pKingdomId,
            long pRulingLineageId, long pExcludedParentId,
            RulerHouseholdKind pKind,
            bool pIncludeSlaves, int pRequestedLimit)
        {
            int limit = Math.Min(MaximumOfferCandidates,
                Math.Max(0, pRequestedLimit));
            var result = new List<long>(limit);
            if (pKingdomId < 0L || limit == 0) return result;
            string candidateClassWhere = pKind ==
                RulerHouseholdKind.PrincipalWife
                ? "STATUS='noble' AND LINEAGE_ID>=0 AND SHI_ID>=0"
                : pIncludeSlaves
                    ? "(IFNULL(STATUS,'') NOT IN ('slave','slave_lineage') " +
                      "OR STATUS='slave_lineage')"
                    : "IFNULL(STATUS,'') NOT IN ('slave','slave_lineage')";
            using var command = new SQLiteCommand(
                "SELECT ID FROM ActorArchive INDEXED BY " +
                "idx_ActorArchive_kingdom_alive_birth WHERE " +
                "KINGDOM_ID=@kingdom AND IS_ALIVE=1 AND SEX=1 AND " +
                candidateClassWhere + " " +
                "AND BIRTH_TIME<=@youngest AND BIRTH_TIME>@oldest " +
                "AND (@excluded_parent<0 OR (" +
                "IFNULL(PARENT_ID_1,-1)<>@excluded_parent AND " +
                "IFNULL(PARENT_ID_2,-1)<>@excluded_parent)) " +
                "ORDER BY CASE " +
                "WHEN STATUS='noble' AND LINEAGE_ID=@ruling_lineage THEN 0 " +
                "WHEN STATUS='noble' THEN 1 ELSE 2 END," +
                "BIRTH_TIME DESC,ID LIMIT @limit",
                _db);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@ruling_lineage", pRulingLineageId);
            command.Parameters.AddWithValue("@excluded_parent",
                pExcludedParentId);
            double now = LineageService.CurTime();
            command.Parameters.AddWithValue("@youngest", now -
                RulerHouseholdRules.MinimumCandidateAge * WorldTimePerYear);
            command.Parameters.AddWithValue("@oldest", now -
                (RulerHouseholdRules.MaximumCandidateAge + 1) *
                WorldTimePerYear);
            command.Parameters.AddWithValue("@limit", limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(Convert.ToInt64(reader.GetValue(0)));
            return result;
        }

        private IReadOnlyList<RulerHouseholdRecord> ReadMany(
            string pIdentityWhere, long pIdentity, long pAfterId, int pLimit)
        {
            var result = new List<RulerHouseholdRecord>(pLimit);
            if (pIdentity < 0L || pLimit <= 0) return result;
            using var command = new SQLiteCommand(
                "SELECT " + Projection + " FROM RulerHousehold WHERE " +
                pIdentityWhere + " AND STATUS=0 AND END_TIME<0 AND " +
                "RELATIONSHIP_ID>@after ORDER BY RELATIONSHIP_ID LIMIT @limit",
                _db);
            command.Parameters.AddWithValue("@id", pIdentity);
            command.Parameters.AddWithValue("@after", pAfterId);
            command.Parameters.AddWithValue("@limit", pLimit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadRecord(reader));
            return result;
        }

        private bool TryReadOne(string pWhere, long pId,
            out RulerHouseholdRecord pRecord)
        {
            pRecord = null;
            if (pId < 0L) return false;
            using var command = new SQLiteCommand(
                "SELECT " + Projection + " FROM RulerHousehold WHERE " +
                pWhere + " ORDER BY RELATIONSHIP_ID DESC LIMIT 1", _db);
            command.Parameters.AddWithValue("@id", pId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            pRecord = ReadRecord(reader);
            return true;
        }

        private static RulerHouseholdRecord ReadRecord(SQLiteDataReader pReader)
        {
            return new RulerHouseholdRecord
            {
                RelationshipId = Convert.ToInt64(pReader.GetValue(0)),
                RulerActorId = Convert.ToInt64(pReader.GetValue(1)),
                PartnerActorId = Convert.ToInt64(pReader.GetValue(2)),
                SourceKingdomId = Convert.ToInt64(pReader.GetValue(3)),
                RecipientKingdomId = Convert.ToInt64(pReader.GetValue(4)),
                Kind = ParseKind(Convert.ToString(pReader.GetValue(5))),
                RankCode = Convert.ToString(pReader.GetValue(6)) ?? "",
                StartYear = Convert.ToInt32(pReader.GetValue(7)),
                StartTime = Convert.ToDouble(pReader.GetValue(8)),
                EndTime = Convert.ToDouble(pReader.GetValue(9)),
                Status = Convert.ToInt32(pReader.GetValue(10)),
                SourceProposalId = Convert.ToInt64(pReader.GetValue(11)),
                OwnerRoleAtEntry = pReader.IsDBNull(12) ? "" :
                    Convert.ToString(pReader.GetValue(12)) ?? "",
                SourceKind = pReader.IsDBNull(13) ? "" :
                    Convert.ToString(pReader.GetValue(13)) ?? "",
                SourceRelationId = pReader.IsDBNull(14) ? -1L :
                    Convert.ToInt64(pReader.GetValue(14)),
                SourceTributeYear = pReader.IsDBNull(15) ? -1 :
                    Convert.ToInt32(pReader.GetValue(15))
            };
        }

        internal static RulerHouseholdKind ParseKind(string pKind)
        {
            return string.Equals(pKind, "principal_wife",
                StringComparison.Ordinal)
                ? RulerHouseholdKind.PrincipalWife
                : RulerHouseholdKind.Consort;
        }
    }
}
