using System;
using System.Data.SQLite;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.court
{
    internal sealed class CivilServiceRulerDeathWriteFacts
    {
        internal CivilServiceRulerDeathWriteFacts(long pSessionId,
            long pKingdomId, long pDueWorldDay, double pUpdatedTime)
        {
            SessionId = pSessionId;
            KingdomId = pKingdomId;
            DueWorldDay = pDueWorldDay;
            UpdatedTime = pUpdatedTime;
        }

        internal long SessionId { get; }
        internal long KingdomId { get; }
        internal long DueWorldDay { get; }
        internal double UpdatedTime { get; }
    }

    internal sealed class CivilServiceRulerDeathWriteResult
    {
        internal CivilServiceRulerDeathWriteResult(bool pAccepted)
        {
            Accepted = pAccepted;
        }

        internal bool Accepted { get; }
    }

#if !AW3_RULES_TESTS
    internal sealed class CivilServiceRulerDeathWriteEnvelope :
        HistoricalWriteEnvelope, IHistoricalCustomWriteEnvelope
    {
        private readonly CivilServiceRulerDeathWriteFacts _facts;

        internal CivilServiceRulerDeathWriteEnvelope(long pSequence,
            string pOperationKey, AWAsyncStamp pStamp,
            CivilServiceRulerDeathWriteFacts pFacts)
            : base(pSequence, pOperationKey, string.Empty,
                Array.Empty<HistoricalSqlValue>(), HistoricalWriteKind.State,
                pStamp, "civil-service-ruler-death")
        {
            _facts = pFacts ?? throw new ArgumentNullException(nameof(pFacts));
        }

        object IHistoricalCustomWriteEnvelope.Execute(
            SQLiteConnection pConnection, SQLiteTransaction pTransaction)
        {
            return CivilServiceRulerDeathPersistence.Execute(pConnection,
                pTransaction, _facts);
        }
    }
#endif

    internal static class CivilServiceRulerDeathPersistence
    {
        private const string Table = "CivilServiceExamSession";

        internal static CivilServiceRulerDeathWriteResult Execute(
            SQLiteConnection pConnection, SQLiteTransaction pTransaction,
            CivilServiceRulerDeathWriteFacts pFacts)
        {
            if (pConnection == null)
                throw new ArgumentNullException(nameof(pConnection));
            if (pTransaction == null)
                throw new ArgumentNullException(nameof(pTransaction));
            if (pFacts == null)
                throw new ArgumentNullException(nameof(pFacts));
            using (var command = new SQLiteCommand(pConnection)
                   { Transaction = pTransaction })
            {
                command.CommandText = "UPDATE " + Table +
                    " SET PLAYER_RANKING_PENDING=0," +
                    "NEXT_DUE_WORLD_DAY=@due,UPDATED_TIME=@time" +
                    " WHERE ID=@id AND KINGDOM_ID=@kingdom" +
                    " AND MODE='imperial_exam' AND STAGE='ranking'" +
                    " AND STATUS='ranking_pending'" +
                    " AND PLAYER_RANKING_PENDING=1";
                Bind(command, pFacts);
                if (command.ExecuteNonQuery() == 1)
                    return new CivilServiceRulerDeathWriteResult(true);
            }

            using var verify = new SQLiteCommand(pConnection)
            {
                Transaction = pTransaction,
                CommandText = "SELECT 1 FROM " + Table +
                    " WHERE ID=@id AND KINGDOM_ID=@kingdom" +
                    " AND MODE='imperial_exam' AND STAGE='ranking'" +
                    " AND STATUS='ranking_pending'" +
                    " AND PLAYER_RANKING_PENDING=0" +
                    " AND NEXT_DUE_WORLD_DAY=@due LIMIT 1"
            };
            Bind(verify, pFacts);
            return new CivilServiceRulerDeathWriteResult(
                verify.ExecuteScalar() != null);
        }

        private static void Bind(SQLiteCommand pCommand,
            CivilServiceRulerDeathWriteFacts pFacts)
        {
            pCommand.Parameters.AddWithValue("@id", pFacts.SessionId);
            pCommand.Parameters.AddWithValue("@kingdom", pFacts.KingdomId);
            pCommand.Parameters.AddWithValue("@due", pFacts.DueWorldDay);
            pCommand.Parameters.AddWithValue("@time", pFacts.UpdatedTime);
        }
    }
}
