using System;
using System.Data.SQLite;
using AncientWarfare3.content.schools;

namespace AncientWarfare3.core.schools
{
    internal enum HistoricalMasterLineageRowState
    {
        Missing = 0,
        Exact = 1,
        Conflict = 2
    }

    internal sealed class HistoricalMasterLineageCommitIdentity
    {
        public HistoricalMasterLineageCommitIdentity(long pActorId,
            string pCanonicalName, string pShiName, string pGivenName,
            string pFamilyName, HistoricalMasterFamilyEvidence pFamilyEvidence,
            long pHomeKingdomId, long pHometownCityId, double pCreatedTime)
        {
            ActorId = pActorId;
            CanonicalName = pCanonicalName ?? "";
            ShiName = pShiName ?? "";
            GivenName = pGivenName ?? "";
            FamilyName = pFamilyName ?? "";
            FamilyEvidence = pFamilyEvidence;
            HomeKingdomId = pHomeKingdomId;
            HometownCityId = pHometownCityId;
            CreatedTime = pCreatedTime;
        }

        public long ActorId { get; }
        public string CanonicalName { get; }
        public string ShiName { get; }
        public string GivenName { get; }
        public string FamilyName { get; }
        public HistoricalMasterFamilyEvidence FamilyEvidence { get; }
        public long HomeKingdomId { get; }
        public long HometownCityId { get; }
        public double CreatedTime { get; }
        public long LineageId { get; private set; } = -1L;
        public long ShiId { get; private set; } = -1L;
        public bool IdsFrozen => LineageId >= 0 && ShiId >= 0;
        public bool IsValid => ActorId >= 0 && HomeKingdomId >= 0 &&
                               HometownCityId >= 0 &&
                               !string.IsNullOrWhiteSpace(CanonicalName) &&
                               !string.IsNullOrWhiteSpace(ShiName) &&
                               !string.IsNullOrWhiteSpace(GivenName) &&
                               FamilyIsValid &&
                               CanonicalName == ShiName + GivenName &&
                               !double.IsNaN(CreatedTime) &&
                               !double.IsInfinity(CreatedTime) && CreatedTime >= 0d;

        private bool FamilyIsValid => FamilyEvidence ==
            HistoricalMasterFamilyEvidence.Unknown
                ? string.IsNullOrEmpty(FamilyName)
                : !string.IsNullOrWhiteSpace(FamilyName) &&
                  (FamilyEvidence == HistoricalMasterFamilyEvidence.KnownSame
                      ? FamilyName == ShiName
                      : FamilyEvidence == HistoricalMasterFamilyEvidence.KnownDistinct &&
                        FamilyName != ShiName);

        public void FreezeIds(long pLineageId, long pShiId)
        {
            if (pLineageId < 0 || pShiId < 0)
                throw new ArgumentOutOfRangeException(nameof(pLineageId));
            if (IdsFrozen && (LineageId != pLineageId || ShiId != pShiId))
                throw new InvalidOperationException("historical master lineage ids are frozen");
            LineageId = pLineageId;
            ShiId = pShiId;
        }
    }

    internal static class HistoricalMasterLineagePersistence
    {
        private const string LineageTable = "LineageGroup";
        private const string ShiTable = "ShiBranch";
        internal const string SourceType = "historical_master";

        internal static void FreezeIds(SQLiteConnection pDb,
            SQLiteTransaction pTransaction,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            if (pDb == null || pTransaction == null || pIdentity == null ||
                !pIdentity.IsValid)
                throw new ArgumentException("invalid historical master lineage identity");
            if (pIdentity.IdsFrozen) return;
            long lineageId = NextId(pDb, pTransaction, LineageTable, "LINEAGE_ID");
            long shiId = NextId(pDb, pTransaction, ShiTable, "SHI_ID");
            pIdentity.FreezeIds(lineageId, shiId);
        }

        internal static void Stage(SQLiteConnection pDb,
            SQLiteTransaction pTransaction,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            if (pDb == null || pTransaction == null || pIdentity == null ||
                !pIdentity.IsValid || !pIdentity.IdsFrozen)
                throw new ArgumentException("unfrozen historical master lineage identity");
            InsertLineage(pDb, pTransaction, pIdentity);
            InsertShi(pDb, pTransaction, pIdentity);
        }

        internal static void ReadStates(SQLiteConnection pDb,
            HistoricalMasterLineageCommitIdentity pIdentity,
            out HistoricalMasterLineageRowState pLineageState,
            out HistoricalMasterLineageRowState pShiState)
        {
            if (pDb == null || pIdentity == null || !pIdentity.IsValid ||
                !pIdentity.IdsFrozen)
            {
                pLineageState = HistoricalMasterLineageRowState.Conflict;
                pShiState = HistoricalMasterLineageRowState.Conflict;
                return;
            }
            pLineageState = ReadLineageState(pDb, pIdentity);
            pShiState = ReadShiState(pDb, pIdentity);
        }

        private static void InsertLineage(SQLiteConnection pDb,
            SQLiteTransaction pTransaction,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + LineageTable +
                " (LINEAGE_ID,FAMILY_NAME,FOUNDER_ACTOR_ID,FOUNDER_NAME,CREATED_TIME," +
                "ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID,IS_EXTINCT) VALUES " +
                "(@id,@family,@actor,@name,@time,@kingdom,@city,0)";
            command.Parameters.AddWithValue("@id", pIdentity.LineageId);
            command.Parameters.AddWithValue("@family", pIdentity.FamilyName);
            command.Parameters.AddWithValue("@actor", pIdentity.ActorId);
            command.Parameters.AddWithValue("@name", pIdentity.CanonicalName);
            command.Parameters.AddWithValue("@time", pIdentity.CreatedTime);
            command.Parameters.AddWithValue("@kingdom", pIdentity.HomeKingdomId);
            command.Parameters.AddWithValue("@city", pIdentity.HometownCityId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("historical lineage insert failed");
        }

        private static void InsertShi(SQLiteConnection pDb,
            SQLiteTransaction pTransaction,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + ShiTable +
                " (SHI_ID,LINEAGE_ID,CLAN_NAME,FOUNDER_ACTOR_ID,SOURCE_TYPE," +
                "ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID,ORIGIN_ORIGINAL_CLAN_ID," +
                "CREATED_TIME,IS_EXTINCT) VALUES " +
                "(@id,@lineage,@clan,@actor,@source,@kingdom,@city,-1,@time,0)";
            command.Parameters.AddWithValue("@id", pIdentity.ShiId);
            command.Parameters.AddWithValue("@lineage", pIdentity.LineageId);
            command.Parameters.AddWithValue("@clan", pIdentity.ShiName);
            command.Parameters.AddWithValue("@actor", pIdentity.ActorId);
            command.Parameters.AddWithValue("@source", SourceType);
            command.Parameters.AddWithValue("@kingdom", pIdentity.HomeKingdomId);
            command.Parameters.AddWithValue("@city", pIdentity.HometownCityId);
            command.Parameters.AddWithValue("@time", pIdentity.CreatedTime);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("historical shi insert failed");
        }

        private static HistoricalMasterLineageRowState ReadLineageState(
            SQLiteConnection pDb, HistoricalMasterLineageCommitIdentity pIdentity)
        {
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT LINEAGE_ID,FAMILY_NAME,FOUNDER_ACTOR_ID," +
                "FOUNDER_NAME,CREATED_TIME,ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID,IS_EXTINCT" +
                " FROM " + LineageTable +
                " WHERE LINEAGE_ID=@id OR FOUNDER_ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@id", pIdentity.LineageId);
            command.Parameters.AddWithValue("@actor", pIdentity.ActorId);
            int count = 0;
            bool exact = false;
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                count++;
                exact |= Long(reader, 0, -1L) == pIdentity.LineageId &&
                         Text(reader, 1) == pIdentity.FamilyName &&
                         Long(reader, 2, -1L) == pIdentity.ActorId &&
                         Text(reader, 3) == pIdentity.CanonicalName &&
                         Double(reader, 4, -1d).Equals(pIdentity.CreatedTime) &&
                         Long(reader, 5, -1L) == pIdentity.HomeKingdomId &&
                         Long(reader, 6, -1L) == pIdentity.HometownCityId &&
                         Int(reader, 7, -1) == 0;
            }
            if (count == 0) return HistoricalMasterLineageRowState.Missing;
            return count == 1 && exact
                ? HistoricalMasterLineageRowState.Exact
                : HistoricalMasterLineageRowState.Conflict;
        }

        private static HistoricalMasterLineageRowState ReadShiState(
            SQLiteConnection pDb, HistoricalMasterLineageCommitIdentity pIdentity)
        {
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT SHI_ID,LINEAGE_ID,CLAN_NAME,FOUNDER_ACTOR_ID," +
                "SOURCE_TYPE,ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID,ORIGIN_ORIGINAL_CLAN_ID," +
                "CREATED_TIME,IS_EXTINCT FROM " + ShiTable +
                " WHERE SHI_ID=@id OR FOUNDER_ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@id", pIdentity.ShiId);
            command.Parameters.AddWithValue("@actor", pIdentity.ActorId);
            int count = 0;
            bool exact = false;
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                count++;
                exact |= Long(reader, 0, -1L) == pIdentity.ShiId &&
                         Long(reader, 1, -1L) == pIdentity.LineageId &&
                         Text(reader, 2) == pIdentity.ShiName &&
                         Long(reader, 3, -1L) == pIdentity.ActorId &&
                         Text(reader, 4) == SourceType &&
                         Long(reader, 5, -1L) == pIdentity.HomeKingdomId &&
                         Long(reader, 6, -1L) == pIdentity.HometownCityId &&
                         Long(reader, 7, long.MinValue) == -1L &&
                         Double(reader, 8, -1d).Equals(pIdentity.CreatedTime) &&
                         Int(reader, 9, -1) == 0;
            }
            if (count == 0) return HistoricalMasterLineageRowState.Missing;
            return count == 1 && exact
                ? HistoricalMasterLineageRowState.Exact
                : HistoricalMasterLineageRowState.Conflict;
        }

        private static long NextId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, string pColumn)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(" + pColumn + "),0)+1 FROM " +
                                  pTable;
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static long Long(SQLiteDataReader pReader, int pOrdinal, long pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToInt64(pReader.GetValue(pOrdinal));
        }

        private static int Int(SQLiteDataReader pReader, int pOrdinal, int pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToInt32(pReader.GetValue(pOrdinal));
        }

        private static double Double(SQLiteDataReader pReader, int pOrdinal,
            double pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToDouble(pReader.GetValue(pOrdinal));
        }

        private static string Text(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal)
                ? ""
                : pReader.GetValue(pOrdinal)?.ToString() ?? "";
        }
    }
}
