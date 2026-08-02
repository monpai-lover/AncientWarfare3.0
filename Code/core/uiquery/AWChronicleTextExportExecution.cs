using System;
using System.Data.SQLite;
using System.Threading;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.uiquery
{
    internal sealed class AWChronicleTextExportExecution
    {
        private readonly ChronicleTextExportRequest _request;
        private readonly DateTime _exportedAt;

        public AWChronicleTextExportExecution(
            ChronicleTextExportRequest pRequest, DateTime pExportedAt)
        {
            _request = pRequest ?? throw new ArgumentNullException(
                nameof(pRequest));
            _exportedAt = pExportedAt;
        }

        public object Execute(SQLiteConnection pConnection,
            CancellationToken pToken)
        {
            pToken.ThrowIfCancellationRequested();
            ChronicleTextExportSnapshot snapshot;
            using (HistoryQuery.EnterBackgroundRead(pConnection))
            {
                snapshot = ChronicleTextExportService.ReadSnapshot(
                    _request.Source, _request.ContextId);
            }
            pToken.ThrowIfCancellationRequested();
            string path = ChronicleTextExportRules.ResolveUniqueFilePath(
                _request, _exportedAt);
            string text = ChronicleTextExportRules.Format(_request, snapshot,
                _exportedAt);
            return ChronicleTextExportRules.Publish(path, text);
        }
    }
}
