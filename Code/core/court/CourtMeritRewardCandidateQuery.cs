using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.court
{
    internal enum CourtMeritRewardEligibilityReason
    {
        None = 0,
        HonorRankAdvance = 1,
        LandHighMerit = 2,
        LandMartialCourt = 3
    }

    internal readonly struct CourtMeritRewardDetachedCandidate
    {
        public CourtMeritRewardDetachedCandidate(long actorId,
            float civilMerit, int civilMeritCap, int lastRewardYear,
            bool archiveKnown, long archiveLineageId, int archiveSex,
            int currentNobleRank, string currentNobleStyle,
            bool generalProjectionKnown, bool generalActive,
            int militaryMerit, long fiefCityId, bool officerSource,
            bool generalSource, CourtMeritRewardKind rewardKind,
            CourtMeritRewardEligibilityReason eligibilityReason)
        {
            ActorId = actorId;
            CivilMerit = civilMerit;
            CivilMeritCap = civilMeritCap;
            LastRewardYear = lastRewardYear;
            ArchiveKnown = archiveKnown;
            ArchiveLineageId = archiveLineageId;
            ArchiveSex = archiveSex;
            CurrentNobleRank = currentNobleRank;
            CurrentNobleStyle = currentNobleStyle ?? "";
            GeneralProjectionKnown = generalProjectionKnown;
            GeneralActive = generalActive;
            MilitaryMerit = militaryMerit;
            FiefCityId = fiefCityId;
            OfficerSource = officerSource;
            GeneralSource = generalSource;
            RewardKind = rewardKind;
            EligibilityReason = eligibilityReason;
        }

        public long ActorId { get; }
        public float CivilMerit { get; }
        public int CivilMeritCap { get; }
        public int LastRewardYear { get; }
        public bool ArchiveKnown { get; }
        public long ArchiveLineageId { get; }
        public int ArchiveSex { get; }
        public int CurrentNobleRank { get; }
        public string CurrentNobleStyle { get; }
        public bool GeneralProjectionKnown { get; }
        public bool GeneralActive { get; }
        public int MilitaryMerit { get; }
        public long FiefCityId { get; }
        public bool OfficerSource { get; }
        public bool GeneralSource { get; }
        public CourtMeritRewardKind RewardKind { get; }
        public CourtMeritRewardEligibilityReason EligibilityReason { get; }
    }

    internal readonly struct CourtMeritRewardArchiveRepairCandidate
    {
        public CourtMeritRewardArchiveRepairCandidate(long actorId,
            bool officerSource, bool generalSource)
        {
            ActorId = actorId;
            OfficerSource = officerSource;
            GeneralSource = generalSource;
        }

        public long ActorId { get; }
        public bool OfficerSource { get; }
        public bool GeneralSource { get; }
        public bool MissingArchive => true;
    }

    internal static class CourtMeritRewardCandidateQuery
    {
        public static IReadOnlyList<CourtMeritRewardDetachedCandidate>
            LoadOfficerCandidates(SQLiteConnection pDb,
                string pOfficerTable, string pCareerStateTable,
                string pEnfeoffmentTable, string pActorArchiveTable,
                string pGeneralStateTable, long kingdomId,
                long rulerLineageId, int nonRoyalMaximumNobleRank,
                int realmMaximumNobleRank, bool hasGrantableLand,
                bool martialCourtSupportsLand, int currentYear,
                int cooldownYears, int maximumCandidates)
        {
            if (!IsIdentifier(pOfficerTable))
                return Array.Empty<CourtMeritRewardDetachedCandidate>();
            string sourceSql = "SELECT officer.ACTOR_ID," +
                               "1 AS OFFICER_SOURCE," +
                               "0 AS GENERAL_SOURCE FROM " +
                               pOfficerTable + " officer" +
                               " WHERE officer.KINGDOM_ID=@kingdom" +
                               " AND officer.ACTIVE=1" +
                               " GROUP BY officer.ACTOR_ID";
            return LoadCandidates(pDb, sourceSql, pCareerStateTable,
                pEnfeoffmentTable, pActorArchiveTable, pGeneralStateTable,
                kingdomId, rulerLineageId, nonRoyalMaximumNobleRank,
                realmMaximumNobleRank, hasGrantableLand,
                martialCourtSupportsLand, currentYear, cooldownYears,
                maximumCandidates);
        }

        public static IReadOnlyList<CourtMeritRewardDetachedCandidate>
            LoadGeneralCandidates(SQLiteConnection pDb,
                string pCareerStateTable, string pEnfeoffmentTable,
                string pActorArchiveTable, string pGeneralStateTable,
                long kingdomId, long rulerLineageId,
                int nonRoyalMaximumNobleRank,
                int realmMaximumNobleRank, bool hasGrantableLand,
                bool martialCourtSupportsLand, int currentYear,
                int cooldownYears, int maximumCandidates)
        {
            if (!IsIdentifier(pGeneralStateTable))
                return Array.Empty<CourtMeritRewardDetachedCandidate>();
            string sourceSql = "SELECT general_source.ACTOR_ID," +
                               "0 AS OFFICER_SOURCE," +
                               "1 AS GENERAL_SOURCE FROM " +
                               pGeneralStateTable + " general_source" +
                               " WHERE general_source.KINGDOM_ID=@kingdom" +
                               " AND general_source.ACTIVE=1" +
                               " GROUP BY general_source.ACTOR_ID";
            return LoadCandidates(pDb, sourceSql, pCareerStateTable,
                pEnfeoffmentTable, pActorArchiveTable, pGeneralStateTable,
                kingdomId, rulerLineageId, nonRoyalMaximumNobleRank,
                realmMaximumNobleRank, hasGrantableLand,
                martialCourtSupportsLand, currentYear, cooldownYears,
                maximumCandidates);
        }

        public static IReadOnlyList<CourtMeritRewardArchiveRepairCandidate>
            LoadMissingArchiveRepairs(SQLiteConnection pDb,
                string pOfficerTable, string pActorArchiveTable,
                string pGeneralStateTable, long kingdomId,
                long afterActorId,
                int maximumRepairs)
        {
            var result =
                new List<CourtMeritRewardArchiveRepairCandidate>();
            if (pDb == null || kingdomId < 0 || maximumRepairs <= 0 ||
                !IsIdentifier(pOfficerTable) ||
                !IsIdentifier(pActorArchiveTable) ||
                !IsIdentifier(pGeneralStateTable))
                return result;

            using var command = new SQLiteCommand(pDb);
            command.CommandText =
                "WITH sources AS (" +
                "SELECT officer.ACTOR_ID,1 AS OFFICER_SOURCE," +
                "0 AS GENERAL_SOURCE FROM " + pOfficerTable + " officer" +
                " WHERE officer.KINGDOM_ID=@kingdom AND officer.ACTIVE=1" +
                " UNION ALL " +
                "SELECT general_state.ACTOR_ID,0 AS OFFICER_SOURCE," +
                "1 AS GENERAL_SOURCE FROM " + pGeneralStateTable +
                " general_state WHERE general_state.KINGDOM_ID=@kingdom" +
                " AND general_state.ACTIVE=1)," +
                "missing AS (SELECT sources.ACTOR_ID," +
                "MAX(sources.OFFICER_SOURCE) AS OFFICER_SOURCE," +
                "MAX(sources.GENERAL_SOURCE) AS GENERAL_SOURCE" +
                " FROM sources" +
                " LEFT JOIN " + pActorArchiveTable +
                " archive ON archive.ID=sources.ACTOR_ID" +
                " WHERE archive.ID IS NULL GROUP BY sources.ACTOR_ID" +
                ") SELECT ACTOR_ID,OFFICER_SOURCE,GENERAL_SOURCE" +
                " FROM missing ORDER BY CASE WHEN ACTOR_ID>@after" +
                " THEN 0 ELSE 1 END,ACTOR_ID LIMIT @limit";
            command.Parameters.AddWithValue("@kingdom", kingdomId);
            command.Parameters.AddWithValue("@after", afterActorId);
            command.Parameters.AddWithValue("@limit", maximumRepairs);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new CourtMeritRewardArchiveRepairCandidate(
                    ValueLong(reader, 0, -1L),
                    ValueInt(reader, 1, 0) != 0,
                    ValueInt(reader, 2, 0) != 0));
            }
            return result;
        }

        public static bool NeedsGeneralProjectionRepair(SQLiteConnection pDb,
            string pGeneralStateTable, long actorId, long kingdomId,
            bool liveActive)
        {
            if (pDb == null || actorId < 0 || kingdomId < 0 ||
                !IsIdentifier(pGeneralStateTable))
                return false;

            using var command = new SQLiteCommand(pDb);
            command.CommandText =
                "SELECT KINGDOM_ID,ACTIVE FROM " + pGeneralStateTable +
                " WHERE ACTOR_ID=@actor LIMIT 1";
            command.Parameters.AddWithValue("@actor", actorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return liveActive;
            return ValueLong(reader, 0, -1L) != kingdomId ||
                   (ValueInt(reader, 1, 0) != 0) != liveActive;
        }

        private static IReadOnlyList<CourtMeritRewardDetachedCandidate>
            LoadCandidates(SQLiteConnection pDb, string pSourceSql,
                string pCareerStateTable, string pEnfeoffmentTable,
                string pActorArchiveTable, string pGeneralStateTable,
                long kingdomId, long rulerLineageId,
                int nonRoyalMaximumNobleRank,
                int realmMaximumNobleRank, bool hasGrantableLand,
                bool martialCourtSupportsLand, int currentYear,
                int cooldownYears, int maximumCandidates)
        {
            var result = new List<CourtMeritRewardDetachedCandidate>();
            if (pDb == null || kingdomId < 0 || maximumCandidates <= 0 ||
                string.IsNullOrEmpty(pSourceSql) ||
                !IsIdentifier(pCareerStateTable) ||
                !IsIdentifier(pEnfeoffmentTable) ||
                !IsIdentifier(pActorArchiveTable) ||
                !IsIdentifier(pGeneralStateTable) ||
                nonRoyalMaximumNobleRank <= 0 ||
                realmMaximumNobleRank <= 0)
                return result;

            string civilRaw =
                "CASE WHEN IFNULL(career.MERIT_CAP,0)>0 THEN " +
                "MAX(0.0,MIN(1.0,IFNULL(career.MERIT,0.0)/" +
                "career.MERIT_CAP))*80.0+" +
                "MIN(10.0,career.MERIT_CAP)*2.0 ELSE 0.0 END";
            string civilFloor = "CAST(raw.CIVIL_RAW AS INTEGER)";
            string civilFraction =
                "(raw.CIVIL_RAW-" + civilFloor + ")";
            string civilScore =
                "CASE WHEN " + civilFraction + "<0.5 THEN " + civilFloor +
                " WHEN " + civilFraction + ">0.5 THEN " + civilFloor +
                "+1 WHEN (" + civilFloor + "%2)=0 THEN " + civilFloor +
                " ELSE " + civilFloor + "+1 END";
            string combinedScore =
                "CASE WHEN rounded.CIVIL_SCORE>=55" +
                " AND rounded.MILITARY_MERIT>=55 THEN" +
                " MIN(100,MAX(rounded.CIVIL_SCORE," +
                "rounded.MILITARY_MERIT)+5) ELSE" +
                " MAX(rounded.CIVIL_SCORE,rounded.MILITARY_MERIT) END";

            using var command = new SQLiteCommand(pDb);
            command.CommandText =
                "WITH source AS (" + pSourceSql + ")," +
                "raw AS (SELECT source.ACTOR_ID," +
                "IFNULL(career.MERIT,0.0) AS CIVIL_MERIT," +
                "IFNULL(career.MERIT_CAP,0) AS CIVIL_MERIT_CAP," +
                "IFNULL(career.LAST_NOBLE_REWARD_YEAR,-1)" +
                " AS LAST_REWARD_YEAR,1 AS ARCHIVE_KNOWN," +
                "archive.LINEAGE_ID AS ARCHIVE_LINEAGE_ID," +
                "archive.SEX AS ARCHIVE_SEX," +
                "MAX(0,MIN(8,IFNULL((SELECT noble.NOBLE_RANK FROM " +
                pEnfeoffmentTable + " noble" +
                " WHERE noble.ACTOR_ID=source.ACTOR_ID" +
                " AND noble.ACTIVE=1 ORDER BY noble.NOBLE_RANK DESC," +
                "noble.GRANT_ID DESC LIMIT 1),0))) AS CURRENT_NOBLE_RANK," +
                "IFNULL((SELECT noble.TITLE_STYLE FROM " +
                pEnfeoffmentTable + " noble" +
                " WHERE noble.ACTOR_ID=source.ACTOR_ID" +
                " AND noble.ACTIVE=1 ORDER BY noble.NOBLE_RANK DESC," +
                "noble.GRANT_ID DESC LIMIT 1),'') AS CURRENT_NOBLE_STYLE," +
                "CASE WHEN general_state.ACTOR_ID IS NULL THEN 0 ELSE 1 END" +
                " AS GENERAL_KNOWN," +
                "CASE WHEN general_state.KINGDOM_ID=@kingdom" +
                " AND general_state.ACTIVE=1 THEN 1 ELSE 0 END" +
                " AS GENERAL_ACTIVE," +
                "CASE WHEN general_state.KINGDOM_ID=@kingdom" +
                " AND general_state.ACTIVE=1 THEN" +
                " MAX(0,MIN(100,IFNULL(general_state.MERIT_SCORE,0)))" +
                " ELSE 0 END AS MILITARY_MERIT," +
                "CASE WHEN general_state.KINGDOM_ID=@kingdom" +
                " AND general_state.ACTIVE=1 THEN" +
                " IFNULL(general_state.FIEF_CITY_ID,-1) ELSE -1 END" +
                " AS FIEF_CITY_ID,source.OFFICER_SOURCE," +
                "source.GENERAL_SOURCE," + civilRaw + " AS CIVIL_RAW" +
                " FROM source LEFT JOIN " + pCareerStateTable +
                " career ON career.ACTOR_ID=source.ACTOR_ID" +
                " AND career.KINGDOM_ID=@kingdom" +
                " INNER JOIN " + pActorArchiveTable +
                " archive ON archive.ID=source.ACTOR_ID" +
                " LEFT JOIN " + pGeneralStateTable +
                " general_state ON general_state.ACTOR_ID=source.ACTOR_ID)," +
                "rounded AS (SELECT raw.*," + civilScore +
                " AS CIVIL_SCORE FROM raw)," +
                "scored AS (SELECT rounded.*," + combinedScore +
                " AS COMBINED_SCORE FROM rounded)," +
                "classified AS (SELECT scored.*," +
                "CASE WHEN scored.GENERAL_ACTIVE=1" +
                " AND scored.FIEF_CITY_ID<0 AND @hasLand=1" +
                " AND scored.MILITARY_MERIT>=45" +
                " AND (scored.MILITARY_MERIT>=80 OR @martialCourt=1)" +
                " THEN 1 ELSE 0 END AS LAND_ELIGIBLE," +
                "CASE WHEN scored.ARCHIVE_SEX=0" +
                " AND scored.CURRENT_NOBLE_RANK<CASE" +
                " WHEN @rulerLineage>=0 AND" +
                " scored.ARCHIVE_LINEAGE_ID=@rulerLineage" +
                " THEN @realmRankCap ELSE @nonRoyalRankCap END" +
                " THEN 1 ELSE 0 END AS HONOR_ELIGIBLE FROM scored)" +
                " SELECT ACTOR_ID,CIVIL_MERIT,CIVIL_MERIT_CAP," +
                "LAST_REWARD_YEAR,ARCHIVE_KNOWN,ARCHIVE_LINEAGE_ID," +
                "ARCHIVE_SEX,CURRENT_NOBLE_RANK,CURRENT_NOBLE_STYLE," +
                "GENERAL_KNOWN,GENERAL_ACTIVE,MILITARY_MERIT," +
                "FIEF_CITY_ID,OFFICER_SOURCE,GENERAL_SOURCE," +
                "CASE WHEN LAND_ELIGIBLE=1 THEN 2" +
                " WHEN HONOR_ELIGIBLE=1 THEN 1 ELSE 0 END AS REWARD_KIND," +
                "CASE WHEN LAND_ELIGIBLE=1 AND MILITARY_MERIT>=80 THEN 2" +
                " WHEN LAND_ELIGIBLE=1 THEN 3" +
                " WHEN HONOR_ELIGIBLE=1 THEN 1 ELSE 0 END AS REASON" +
                " FROM classified WHERE COMBINED_SCORE>=55" +
                " AND (LAST_REWARD_YEAR<0 OR" +
                " CAST(@year AS INTEGER)-LAST_REWARD_YEAR>=@cooldown)" +
                " AND (LAND_ELIGIBLE=1 OR HONOR_ELIGIBLE=1)" +
                " ORDER BY COMBINED_SCORE DESC,ACTOR_ID ASC LIMIT @limit";
            command.Parameters.AddWithValue("@kingdom", kingdomId);
            command.Parameters.AddWithValue("@year", currentYear);
            command.Parameters.AddWithValue("@cooldown",
                Math.Max(0, cooldownYears));
            command.Parameters.AddWithValue("@rulerLineage", rulerLineageId);
            command.Parameters.AddWithValue("@nonRoyalRankCap",
                nonRoyalMaximumNobleRank);
            command.Parameters.AddWithValue("@realmRankCap",
                realmMaximumNobleRank);
            command.Parameters.AddWithValue("@hasLand",
                hasGrantableLand ? 1 : 0);
            command.Parameters.AddWithValue("@martialCourt",
                martialCourtSupportsLand ? 1 : 0);
            command.Parameters.AddWithValue("@limit", maximumCandidates);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadCandidate(reader));
            return result;
        }

        private static CourtMeritRewardDetachedCandidate ReadCandidate(
            SQLiteDataReader pReader)
        {
            return new CourtMeritRewardDetachedCandidate(
                ValueLong(pReader, 0, -1L),
                ValueFloat(pReader, 1, 0f),
                ValueInt(pReader, 2, 0),
                ValueInt(pReader, 3, -1),
                ValueInt(pReader, 4, 0) != 0,
                ValueLong(pReader, 5, -1L),
                ValueInt(pReader, 6, -1),
                ValueInt(pReader, 7, 0),
                ValueString(pReader, 8),
                ValueInt(pReader, 9, 0) != 0,
                ValueInt(pReader, 10, 0) != 0,
                ValueInt(pReader, 11, 0),
                ValueLong(pReader, 12, -1L),
                ValueInt(pReader, 13, 0) != 0,
                ValueInt(pReader, 14, 0) != 0,
                (CourtMeritRewardKind)ValueInt(pReader, 15, 0),
                (CourtMeritRewardEligibilityReason)ValueInt(pReader, 16, 0));
        }

        private static int ValueInt(SQLiteDataReader pReader, int pOrdinal,
            int pFallback)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pFallback
                : Convert.ToInt32(pReader.GetValue(pOrdinal));
        }

        private static long ValueLong(SQLiteDataReader pReader, int pOrdinal,
            long pFallback)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pFallback
                : Convert.ToInt64(pReader.GetValue(pOrdinal));
        }

        private static float ValueFloat(SQLiteDataReader pReader,
            int pOrdinal, float pFallback)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pFallback
                : Convert.ToSingle(pReader.GetValue(pOrdinal));
        }

        private static string ValueString(SQLiteDataReader pReader,
            int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal)
                ? ""
                : Convert.ToString(pReader.GetValue(pOrdinal)) ?? "";
        }

        private static bool IsIdentifier(string pValue)
        {
            if (string.IsNullOrWhiteSpace(pValue)) return false;
            for (int i = 0; i < pValue.Length; i++)
            {
                char value = pValue[i];
                if (char.IsLetterOrDigit(value) || value == '_') continue;
                return false;
            }
            return true;
        }
    }
}
