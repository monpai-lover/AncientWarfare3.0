using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;

namespace AncientWarfare3.core.lineage
{
    public sealed class ConferredPosthumousCandidateRecord
    {
        public long ActorId;
        public string ActorName = "";
        public long LineageId = -1;
        public long ShiId = -1;
        public double BirthTime;
        public ConferredPosthumousRole Roles;
        public int NobleRank;
        public int HighestOfficeRank;
        public int TenureYears;
        public int CivilMerit;
        public int GeneralMerit;
    }

    public sealed class ConferredPosthumousTargetRecord
    {
        public long ActorId = -1;
        public string ActorName = "";
        public long LineageId = -1;
        public long ShiId = -1;
        public double BirthTime;
        public bool IsAlive;
        public ConferredPosthumousRole Roles;
    }

    public readonly struct ConferredPosthumousCooldownRecord
    {
        public readonly long RecordId;
        public readonly double DecidedTime;

        public ConferredPosthumousCooldownRecord(long pRecordId,
            double pDecidedTime)
        {
            RecordId = pRecordId;
            DecidedTime = pDecidedTime;
        }
    }

    public sealed class ConferredPosthumousExistingTitle
    {
        public long RecordId = -1;
        public string DisplayTitle = "";
        public string TitleKind = "";
        public double DecidedTime = -1d;
    }

    public sealed class ConferredPosthumousTitleQuery
    {
        private const string CandidateSql = @"
WITH role_rows(actor_id, role) AS (
    SELECT DISTINCT KING_ACTOR_ID, 8
      FROM KingdomReign
     WHERE KINGDOM_ID=@kingdom AND KING_ACTOR_ID>=0
    UNION ALL
    SELECT ID, 4
      FROM ActorArchive
     WHERE LINEAGE_ID=@lineage AND IS_ALIVE=0 AND @lineage>=0
    UNION ALL
    SELECT DISTINCT ACTOR_ID, 2
      FROM CourtOfficer
     WHERE KINGDOM_ID=@kingdom AND ACTOR_ID>=0
    UNION ALL
    SELECT DISTINCT ACTOR_ID, 1
      FROM GeneralState
     WHERE KINGDOM_ID=@kingdom AND ACTOR_ID>=0
), role_masks AS (
    SELECT actor_id,
           MAX(CASE WHEN role=8 THEN 8 ELSE 0 END) +
           MAX(CASE WHEN role=4 THEN 4 ELSE 0 END) +
           MAX(CASE WHEN role=2 THEN 2 ELSE 0 END) +
           MAX(CASE WHEN role=1 THEN 1 ELSE 0 END) AS roles
      FROM role_rows
     GROUP BY actor_id
)
SELECT archive.ID, IFNULL(archive.DISPLAY_NAME,''),
       IFNULL(archive.LINEAGE_ID,-1), IFNULL(archive.SHI_ID,-1),
       IFNULL(archive.BIRTH_TIME,0), role_masks.roles,
       IFNULL((SELECT MAX(grant.NOBLE_RANK) FROM Enfeoffment grant
                WHERE grant.KINGDOM_ID=@kingdom
                  AND grant.ACTOR_ID=archive.ID),0) AS noble_rank,
       MAX(
           IFNULL((SELECT MAX(officer.RANK_AT_APPOINTMENT)
                     FROM CourtOfficer officer
                    WHERE officer.KINGDOM_ID=@kingdom
                      AND officer.ACTOR_ID=archive.ID),0),
           IFNULL((SELECT MAX(career.RANK)
                     FROM OfficialCareerState career
                    WHERE career.KINGDOM_ID=@kingdom
                      AND career.ACTOR_ID=archive.ID),0)
       ) AS office_rank,
       IFNULL((SELECT CASE WHEN MIN(officer.APPOINTED_YEAR)>=0
                    THEN MAX(1,
                        MAX(CASE WHEN officer.ENDED_YEAR>=0
                                 THEN officer.ENDED_YEAR
                                 ELSE officer.APPOINTED_YEAR END) -
                        MIN(officer.APPOINTED_YEAR) + 1)
                    ELSE 0 END
                 FROM CourtOfficer officer
                WHERE officer.KINGDOM_ID=@kingdom
                  AND officer.ACTOR_ID=archive.ID),0) AS tenure_years,
       IFNULL((SELECT CAST(ROUND(MAX(career.MERIT)*100) AS INTEGER)
                 FROM OfficialCareerState career
                WHERE career.KINGDOM_ID=@kingdom
                  AND career.ACTOR_ID=archive.ID),0) AS civil_merit,
       IFNULL((SELECT MAX(general.MERIT_SCORE) FROM GeneralState general
                WHERE general.KINGDOM_ID=@kingdom
                  AND general.ACTOR_ID=archive.ID),0) AS general_merit
  FROM role_masks
  JOIN ActorArchive archive ON archive.ID=role_masks.actor_id
 WHERE archive.IS_ALIVE=0
   AND NOT EXISTS (
       SELECT 1 FROM PosthumousTitle title
        WHERE title.ACTOR_ID=archive.ID AND title.IS_RETROSPECTIVE=0
   )
 ORDER BY role_masks.roles DESC, noble_rank DESC, office_rank DESC,
          tenure_years DESC, civil_merit DESC, general_merit DESC,
          archive.BIRTH_TIME ASC, archive.ID ASC
 LIMIT @limit";

        private readonly SQLiteConnection _db;

        public ConferredPosthumousTitleQuery(SQLiteConnection pDb)
        {
            _db = pDb ?? throw new ArgumentNullException(nameof(pDb));
        }

        public List<ConferredPosthumousCandidateRecord> ReadCandidates(
            long pKingdomId, long pRoyalLineageId, int pLimit)
        {
            int limit = Math.Min(
                ConferredPosthumousTitleRules.MaximumCandidates,
                Math.Max(0, pLimit));
            var result = new List<ConferredPosthumousCandidateRecord>(limit);
            if (pKingdomId < 0 || limit == 0) return result;

            using var command = BuildCandidateCommand(CandidateSql,
                pKingdomId, pRoyalLineageId, limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new ConferredPosthumousCandidateRecord
                {
                    ActorId = ValueLong(reader, 0, -1L),
                    ActorName = ValueString(reader, 1),
                    LineageId = ValueLong(reader, 2, -1L),
                    ShiId = ValueLong(reader, 3, -1L),
                    BirthTime = ValueDouble(reader, 4, 0d),
                    Roles = (ConferredPosthumousRole)ValueInt(reader, 5, 0),
                    NobleRank = ValueInt(reader, 6, 0),
                    HighestOfficeRank = ValueInt(reader, 7, 0),
                    TenureYears = ValueInt(reader, 8, 0),
                    CivilMerit = ValueInt(reader, 9, 0),
                    GeneralMerit = ValueInt(reader, 10, 0)
                });
            }
            return result;
        }

        public double ReadLastConferredTime(long pKingdomId)
        {
            return ReadLastConferred(pKingdomId).DecidedTime;
        }

        public ConferredPosthumousCooldownRecord ReadLastConferred(
            long pKingdomId)
        {
            if (pKingdomId < 0)
                return new ConferredPosthumousCooldownRecord(-1L, -1d);
            using var command = new SQLiteCommand(
                "SELECT RECORD_ID,DECIDED_TIME FROM PosthumousTitle " +
                "WHERE KINGDOM_ID=@kingdom AND TITLE_KIND='conferred' " +
                "ORDER BY DECIDED_TIME DESC,RECORD_ID DESC LIMIT 1", _db);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read()
                ? new ConferredPosthumousCooldownRecord(
                    ValueLong(reader, 0, -1L),
                    ValueDouble(reader, 1, -1d))
                : new ConferredPosthumousCooldownRecord(-1L, -1d);
        }

        public IReadOnlyDictionary<long, double> ReadLastConferredTimes()
        {
            var result = new Dictionary<long, double>();
            using var command = new SQLiteCommand(
                "SELECT KINGDOM_ID,MAX(DECIDED_TIME) " +
                "FROM PosthumousTitle WHERE TITLE_KIND='conferred' " +
                "GROUP BY KINGDOM_ID", _db);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long kingdomId = ValueLong(reader, 0, -1L);
                if (kingdomId < 0) continue;
                result[kingdomId] = ValueDouble(reader, 1, -1d);
            }
            return result;
        }

        public bool TryReadTarget(long pKingdomId, long pRoyalLineageId,
            long pActorId, out ConferredPosthumousTargetRecord pTarget)
        {
            pTarget = null;
            if (pKingdomId < 0 || pActorId < 0) return false;
            using var command = new SQLiteCommand(@"
SELECT archive.ID,IFNULL(archive.DISPLAY_NAME,''),
       IFNULL(archive.LINEAGE_ID,-1),IFNULL(archive.SHI_ID,-1),
       IFNULL(archive.BIRTH_TIME,0),archive.IS_ALIVE,
       (CASE WHEN EXISTS(
            SELECT 1 FROM KingdomReign reign
             WHERE reign.KINGDOM_ID=@kingdom
               AND reign.KING_ACTOR_ID=archive.ID) THEN 8 ELSE 0 END) +
       (CASE WHEN archive.LINEAGE_ID=@lineage AND @lineage>=0
             THEN 4 ELSE 0 END) +
       (CASE WHEN EXISTS(
            SELECT 1 FROM CourtOfficer officer
             WHERE officer.KINGDOM_ID=@kingdom
               AND officer.ACTOR_ID=archive.ID) THEN 2 ELSE 0 END) +
       (CASE WHEN EXISTS(
            SELECT 1 FROM GeneralState general
             WHERE general.KINGDOM_ID=@kingdom
               AND general.ACTOR_ID=archive.ID) THEN 1 ELSE 0 END) AS roles
  FROM ActorArchive archive
 WHERE archive.ID=@actor
 LIMIT 1", _db);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@lineage", pRoyalLineageId);
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            pTarget = new ConferredPosthumousTargetRecord
            {
                ActorId = ValueLong(reader, 0, -1L),
                ActorName = ValueString(reader, 1),
                LineageId = ValueLong(reader, 2, -1L),
                ShiId = ValueLong(reader, 3, -1L),
                BirthTime = ValueDouble(reader, 4, 0d),
                IsAlive = ValueInt(reader, 5, 1) != 0,
                Roles = (ConferredPosthumousRole)ValueInt(reader, 6, 0)
            };
            return true;
        }

        public bool HasFormalTitle(long pActorId)
        {
            if (pActorId < 0) return false;
            using var command = new SQLiteCommand(
                "SELECT 1 FROM PosthumousTitle WHERE ACTOR_ID=@actor " +
                "AND IS_RETROSPECTIVE=0 LIMIT 1", _db);
            command.Parameters.AddWithValue("@actor", pActorId);
            return command.ExecuteScalar() != null;
        }

        public bool TryReadFormalTitle(long pActorId,
            out ConferredPosthumousExistingTitle pTitle)
        {
            pTitle = null;
            if (pActorId < 0) return false;
            using var command = new SQLiteCommand(
                "SELECT RECORD_ID,IFNULL(FULL_TITLE,'')," +
                "IFNULL(TITLE_KIND,''),IFNULL(DECIDED_TIME,-1) " +
                "FROM PosthumousTitle WHERE ACTOR_ID=@actor " +
                "AND IS_RETROSPECTIVE=0 " +
                "ORDER BY DECIDED_TIME DESC,RECORD_ID DESC LIMIT 1", _db);
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            pTitle = new ConferredPosthumousExistingTitle
            {
                RecordId = ValueLong(reader, 0, -1L),
                DisplayTitle = ValueString(reader, 1),
                TitleKind = ValueString(reader, 2),
                DecidedTime = ValueDouble(reader, 3, -1d)
            };
            return true;
        }

        public string ExplainCandidatePlan(long pKingdomId,
            long pRoyalLineageId, int pLimit)
        {
            int limit = Math.Min(
                ConferredPosthumousTitleRules.MaximumCandidates,
                Math.Max(1, pLimit));
            using var command = BuildCandidateCommand(
                "EXPLAIN QUERY PLAN " + CandidateSql,
                pKingdomId, pRoyalLineageId, limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            var result = new StringBuilder();
            while (reader.Read())
            {
                if (reader.FieldCount > 3 && !reader.IsDBNull(3))
                    result.AppendLine(Convert.ToString(reader.GetValue(3)));
            }
            return result.ToString();
        }

        private SQLiteCommand BuildCandidateCommand(string pSql,
            long pKingdomId, long pRoyalLineageId, int pLimit)
        {
            var command = new SQLiteCommand(pSql, _db);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@lineage", pRoyalLineageId);
            command.Parameters.AddWithValue("@limit", pLimit);
            return command;
        }

        private static string ValueString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex)
                ? ""
                : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }

        private static long ValueLong(SQLiteDataReader pReader, int pIndex,
            long pFallback)
        {
            return pReader.IsDBNull(pIndex)
                ? pFallback
                : Convert.ToInt64(pReader.GetValue(pIndex));
        }

        private static int ValueInt(SQLiteDataReader pReader, int pIndex,
            int pFallback)
        {
            return pReader.IsDBNull(pIndex)
                ? pFallback
                : Convert.ToInt32(pReader.GetValue(pIndex));
        }

        private static double ValueDouble(SQLiteDataReader pReader, int pIndex,
            double pFallback)
        {
            return pReader.IsDBNull(pIndex)
                ? pFallback
                : Convert.ToDouble(pReader.GetValue(pIndex));
        }
    }
}
