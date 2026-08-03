using System;
using System.Data.SQLite;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal sealed class LineageBirthArchiveEnvelope :
        HistoricalWriteEnvelope, IHistoricalCustomWriteEnvelope
    {
        private const string OperationKeyPrefix =
            "lineage-birth:v1:child:";
        private readonly LineageBirthArchiveWrite _write;

        internal LineageBirthArchiveEnvelope(long pSequence,
            AWAsyncStamp pStamp, LineageBirthArchiveWrite pWrite)
            : base(pSequence, BuildOperationKey(pWrite), string.Empty,
                Array.Empty<HistoricalSqlValue>(), HistoricalWriteKind.State,
                pStamp)
        {
            _write = pWrite;
        }

        object IHistoricalCustomWriteEnvelope.Execute(
            SQLiteConnection pConnection, SQLiteTransaction pTransaction)
        {
            return LineageBirthArchivePersistence.Execute(pConnection,
                pTransaction, _write);
        }

        private static string BuildOperationKey(
            LineageBirthArchiveWrite pWrite)
        {
            if (pWrite == null)
                throw new ArgumentNullException(nameof(pWrite));
            return OperationKeyPrefix + pWrite.Child.id;
        }
    }
}
