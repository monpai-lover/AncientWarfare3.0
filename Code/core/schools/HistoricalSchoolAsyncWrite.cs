using System;
using System.Data.SQLite;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.schools
{
    internal interface IHistoricalSchoolBackgroundWrite
    {
        HistoricalSchoolTeachingPersistenceOutcome Execute(
            SQLiteConnection pDb, SQLiteTransaction pTransaction);
    }

    internal interface IHistoricalSchoolAsyncWriteOperation
    {
        IHistoricalSchoolBackgroundWrite DetachBackgroundWrite();
    }

    internal sealed class HistoricalSchoolAsyncEnvelope :
        HistoricalWriteEnvelope, IHistoricalCustomWriteEnvelope
    {
        private readonly IHistoricalSchoolBackgroundWrite _backgroundWrite;

        public HistoricalSchoolAsyncEnvelope(long pSequence,
            string pOperationKey, AWAsyncStamp pStamp,
            IHistoricalSchoolBackgroundWrite pBackgroundWrite)
            : base(pSequence, pOperationKey, string.Empty,
                Array.Empty<HistoricalSqlValue>(), HistoricalWriteKind.Append,
                pStamp)
        {
            _backgroundWrite = pBackgroundWrite ??
                throw new ArgumentNullException(nameof(pBackgroundWrite));
        }

        object IHistoricalCustomWriteEnvelope.Execute(SQLiteConnection pConnection,
            SQLiteTransaction pTransaction)
        {
            HistoricalSchoolTeachingPersistenceOutcome outcome =
                _backgroundWrite.Execute(pConnection, pTransaction);
            if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                throw new InvalidOperationException(
                    "Historical school background write returned Unknown.");
            return outcome;
        }
    }
}
