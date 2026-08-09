using System;
using System.Data.SQLite;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal sealed class SuccessionDisputeWriteFacts
    {
        internal long OriginalKingdomId;
        internal long PredecessorActorId;
        internal long SuccessorActorId;
        internal long ClaimantActorId;
        internal string OriginalStateName = string.Empty;
        internal string OriginalQualifier = string.Empty;
        internal string RivalQualifier = string.Empty;
        internal int AccessionLaw;
        internal string SuccessorMode = string.Empty;
        internal string ClaimantMode = string.Empty;
        internal int SuccessorSupport;
        internal int ClaimantSupport;
        internal double PreparedTime;
        internal int PreparedYear;
        internal int DeadlineYear;
        internal int Status;
        internal long OriginalLineageId = -1L;
        internal long OriginalShiId = -1L;
        internal int ClaimGenerationBoundary;
        internal long[] SupportCityIds = Array.Empty<long>();
    }

    internal sealed class SuccessionDisputeWriteResult
    {
        internal SuccessionDisputeWriteResult(long pDisputeId,
            long[] pSupportCityIds)
        {
            DisputeId = pDisputeId;
            SupportCityIds = pSupportCityIds == null
                ? Array.Empty<long>()
                : (long[])pSupportCityIds.Clone();
        }

        internal long DisputeId { get; }
        internal long[] SupportCityIds { get; }
    }

#if !AW3_RULES_TESTS
    internal sealed class SuccessionDisputeWriteEnvelope :
        HistoricalWriteEnvelope, IHistoricalCustomWriteEnvelope
    {
        private readonly SuccessionDisputeWriteFacts _facts;

        internal SuccessionDisputeWriteEnvelope(long pSequence,
            string pOperationKey, AWAsyncStamp pStamp,
            SuccessionDisputeWriteFacts pFacts)
            : base(pSequence, pOperationKey, string.Empty,
                Array.Empty<HistoricalSqlValue>(), HistoricalWriteKind.Append,
                pStamp, "succession-dispute")
        {
            _facts = SuccessionDisputePersistence.Copy(pFacts);
        }

        object IHistoricalCustomWriteEnvelope.Execute(
            SQLiteConnection pConnection, SQLiteTransaction pTransaction)
        {
            return SuccessionDisputePersistence.Execute(pConnection,
                pTransaction, _facts);
        }
    }
#endif

    internal static class SuccessionDisputePersistence
    {
        private const string DisputeTable = "SuccessionDispute";
        private const string CityTable = "SuccessionDisputeCity";

        internal static SuccessionDisputeWriteResult Execute(
            SQLiteConnection pConnection, SQLiteTransaction pTransaction,
            SuccessionDisputeWriteFacts pFacts)
        {
            if (pConnection == null)
                throw new ArgumentNullException(nameof(pConnection));
            if (pTransaction == null)
                throw new ArgumentNullException(nameof(pTransaction));
            if (pFacts == null)
                throw new ArgumentNullException(nameof(pFacts));

            long existingId = FindExisting(pConnection, pTransaction,
                pFacts);
            if (existingId >= 0L)
                return new SuccessionDisputeWriteResult(existingId,
                    pFacts.SupportCityIds);

            long disputeId = NextId(pConnection, pTransaction,
                DisputeTable, "DISPUTE_ID");
            InsertDispute(pConnection, pTransaction, disputeId, pFacts);
            long entryId = NextId(pConnection, pTransaction, CityTable,
                "ENTRY_ID");
            long[] cityIds = pFacts.SupportCityIds ?? Array.Empty<long>();
            for (int i = 0; i < cityIds.Length; i++)
                InsertCity(pConnection, pTransaction, entryId++, disputeId,
                    cityIds[i], i, pFacts);
            return new SuccessionDisputeWriteResult(disputeId, cityIds);
        }

        internal static SuccessionDisputeWriteFacts Copy(
            SuccessionDisputeWriteFacts pFacts)
        {
            if (pFacts == null)
                throw new ArgumentNullException(nameof(pFacts));
            return new SuccessionDisputeWriteFacts
            {
                OriginalKingdomId = pFacts.OriginalKingdomId,
                PredecessorActorId = pFacts.PredecessorActorId,
                SuccessorActorId = pFacts.SuccessorActorId,
                ClaimantActorId = pFacts.ClaimantActorId,
                OriginalStateName = pFacts.OriginalStateName ?? string.Empty,
                OriginalQualifier = pFacts.OriginalQualifier ?? string.Empty,
                RivalQualifier = pFacts.RivalQualifier ?? string.Empty,
                AccessionLaw = pFacts.AccessionLaw,
                SuccessorMode = pFacts.SuccessorMode ?? string.Empty,
                ClaimantMode = pFacts.ClaimantMode ?? string.Empty,
                SuccessorSupport = pFacts.SuccessorSupport,
                ClaimantSupport = pFacts.ClaimantSupport,
                PreparedTime = pFacts.PreparedTime,
                PreparedYear = pFacts.PreparedYear,
                DeadlineYear = pFacts.DeadlineYear,
                Status = pFacts.Status,
                OriginalLineageId = pFacts.OriginalLineageId,
                OriginalShiId = pFacts.OriginalShiId,
                ClaimGenerationBoundary = pFacts.ClaimGenerationBoundary,
                SupportCityIds = pFacts.SupportCityIds == null
                    ? Array.Empty<long>()
                    : (long[])pFacts.SupportCityIds.Clone()
            };
        }

        private static long FindExisting(SQLiteConnection pConnection,
            SQLiteTransaction pTransaction,
            SuccessionDisputeWriteFacts pFacts)
        {
            using var command = new SQLiteCommand(pConnection)
            {
                Transaction = pTransaction,
                CommandText = "SELECT DISPUTE_ID FROM " + DisputeTable +
                    " WHERE ORIGINAL_KINGDOM_ID=@kingdom" +
                    " AND PREDECESSOR_ACTOR_ID=@predecessor" +
                    " AND SUCCESSOR_ACTOR_ID=@successor" +
                    " AND CLAIMANT_ACTOR_ID=@claimant AND END_TIME=-1" +
                    " ORDER BY DISPUTE_ID DESC LIMIT 1"
            };
            command.Parameters.AddWithValue("@kingdom",
                pFacts.OriginalKingdomId);
            command.Parameters.AddWithValue("@predecessor",
                pFacts.PredecessorActorId);
            command.Parameters.AddWithValue("@successor",
                pFacts.SuccessorActorId);
            command.Parameters.AddWithValue("@claimant",
                pFacts.ClaimantActorId);
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? -1L
                : Convert.ToInt64(value);
        }

        private static void InsertDispute(SQLiteConnection pConnection,
            SQLiteTransaction pTransaction, long pDisputeId,
            SuccessionDisputeWriteFacts pFacts)
        {
            using var command = new SQLiteCommand(pConnection)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO " + DisputeTable +
                    "(DISPUTE_ID,ORIGINAL_KINGDOM_ID,RIVAL_KINGDOM_ID," +
                    "PREDECESSOR_ACTOR_ID,SUCCESSOR_ACTOR_ID," +
                    "CLAIMANT_ACTOR_ID,ORIGINAL_STATE_NAME," +
                    "ORIGINAL_QUALIFIER,RIVAL_QUALIFIER,ACCESSION_LAW," +
                    "SUCCESSOR_MODE,CLAIMANT_MODE,SUCCESSOR_SUPPORT," +
                    "CLAIMANT_SUPPORT,WAR_ID,PREPARED_TIME,START_TIME," +
                    "PREPARED_YEAR,DEADLINE_YEAR,STATUS,END_TIME," +
                    "END_REASON,ORIGINAL_LINEAGE_ID,ORIGINAL_SHI_ID," +
                    "CLAIM_GENERATION_BOUNDARY) VALUES(" +
                    "@id,@kingdom,-1,@predecessor,@successor,@claimant," +
                    "@state,@original_qualifier,@rival_qualifier,@law," +
                    "@successor_mode,@claimant_mode,@successor_support," +
                    "@claimant_support,-1,@time,-1,@year,@deadline," +
                    "@status,-1,'',@lineage,@shi,@claim_generation)"
            };
            command.Parameters.AddWithValue("@id", pDisputeId);
            command.Parameters.AddWithValue("@kingdom",
                pFacts.OriginalKingdomId);
            command.Parameters.AddWithValue("@predecessor",
                pFacts.PredecessorActorId);
            command.Parameters.AddWithValue("@successor",
                pFacts.SuccessorActorId);
            command.Parameters.AddWithValue("@claimant",
                pFacts.ClaimantActorId);
            command.Parameters.AddWithValue("@state",
                pFacts.OriginalStateName ?? string.Empty);
            command.Parameters.AddWithValue("@original_qualifier",
                pFacts.OriginalQualifier ?? string.Empty);
            command.Parameters.AddWithValue("@rival_qualifier",
                pFacts.RivalQualifier ?? string.Empty);
            command.Parameters.AddWithValue("@law", pFacts.AccessionLaw);
            command.Parameters.AddWithValue("@successor_mode",
                pFacts.SuccessorMode ?? string.Empty);
            command.Parameters.AddWithValue("@claimant_mode",
                pFacts.ClaimantMode ?? string.Empty);
            command.Parameters.AddWithValue("@successor_support",
                pFacts.SuccessorSupport);
            command.Parameters.AddWithValue("@claimant_support",
                pFacts.ClaimantSupport);
            command.Parameters.AddWithValue("@time", pFacts.PreparedTime);
            command.Parameters.AddWithValue("@year", pFacts.PreparedYear);
            command.Parameters.AddWithValue("@deadline",
                pFacts.DeadlineYear);
            command.Parameters.AddWithValue("@status", pFacts.Status);
            command.Parameters.AddWithValue("@lineage",
                pFacts.OriginalLineageId);
            command.Parameters.AddWithValue("@shi", pFacts.OriginalShiId);
            command.Parameters.AddWithValue("@claim_generation",
                pFacts.ClaimGenerationBoundary);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "succession dispute insert returned no row");
        }

        private static void InsertCity(SQLiteConnection pConnection,
            SQLiteTransaction pTransaction, long pEntryId, long pDisputeId,
            long pCityId, int pOrdinal,
            SuccessionDisputeWriteFacts pFacts)
        {
            using var command = new SQLiteCommand(pConnection)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO " + CityTable +
                    "(ENTRY_ID,DISPUTE_ID,CITY_ID,ORIGINAL_KINGDOM_ID," +
                    "SIDE,ORDINAL,ACTIVE,ASSIGNED_TIME,END_TIME," +
                    "END_REASON) VALUES(@entry,@dispute,@city,@kingdom," +
                    "1,@ordinal,1,@time,-1,'')"
            };
            command.Parameters.AddWithValue("@entry", pEntryId);
            command.Parameters.AddWithValue("@dispute", pDisputeId);
            command.Parameters.AddWithValue("@city", pCityId);
            command.Parameters.AddWithValue("@kingdom",
                pFacts.OriginalKingdomId);
            command.Parameters.AddWithValue("@ordinal", pOrdinal);
            command.Parameters.AddWithValue("@time", pFacts.PreparedTime);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "succession dispute city insert returned no row");
        }

        private static long NextId(SQLiteConnection pConnection,
            SQLiteTransaction pTransaction, string pTable, string pColumn)
        {
            using var command = new SQLiteCommand(pConnection)
            {
                Transaction = pTransaction,
                CommandText = "SELECT COALESCE(MAX(" + pColumn +
                              "),0)+1 FROM " + pTable
            };
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }
}
