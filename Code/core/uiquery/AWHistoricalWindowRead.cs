using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.uiquery
{
    internal enum AWHistoricalWindowSource
    {
        Person,
        Kingdom,
        City
    }

    internal sealed class AWHistoricalWindowReadResult
    {
        private AWHistoricalWindowReadResult(
            AWHistoricalWindowSource pSource, long pContextId,
            List<HistoryEntry> pEntries, List<DynastyView> pDynasties,
            List<ReignPeriod> pPeriods)
        {
            Source = pSource;
            ContextId = pContextId;
            Entries = pEntries ?? new List<HistoryEntry>();
            Dynasties = pDynasties ?? new List<DynastyView>();
            Periods = pPeriods ?? new List<ReignPeriod>();
        }

        public AWHistoricalWindowSource Source { get; }
        public long ContextId { get; }
        public List<HistoryEntry> Entries { get; }
        public List<DynastyView> Dynasties { get; }
        public List<ReignPeriod> Periods { get; }

        public static AWHistoricalWindowReadResult ForPerson(long pContextId,
            List<HistoryEntry> pEntries)
        {
            return new AWHistoricalWindowReadResult(
                AWHistoricalWindowSource.Person, pContextId, pEntries,
                null, null);
        }

        public static AWHistoricalWindowReadResult ForKingdom(long pContextId,
            List<DynastyView> pDynasties)
        {
            return new AWHistoricalWindowReadResult(
                AWHistoricalWindowSource.Kingdom, pContextId, null,
                pDynasties, null);
        }

        public static AWHistoricalWindowReadResult ForCity(long pContextId,
            List<ReignPeriod> pPeriods)
        {
            return new AWHistoricalWindowReadResult(
                AWHistoricalWindowSource.City, pContextId, null, null,
                pPeriods);
        }
    }

    internal sealed class AWHistoricalWindowReadExecution
    {
        private readonly AWHistoricalWindowSource _source;
        private readonly long _contextId;

        public AWHistoricalWindowReadExecution(
            AWHistoricalWindowSource pSource, long pContextId)
        {
            _source = pSource;
            _contextId = pContextId;
        }

        public object Execute(SQLiteConnection pConnection,
            CancellationToken pToken)
        {
            pToken.ThrowIfCancellationRequested();
            AWHistoricalWindowReadResult result;
            using (HistoryQuery.EnterBackgroundRead(pConnection))
            {
                switch (_source)
                {
                    case AWHistoricalWindowSource.Kingdom:
                        result = AWHistoricalWindowReadResult.ForKingdom(
                            _contextId,
                            HistoryQuery.GetKingdomDynasties(_contextId));
                        break;
                    case AWHistoricalWindowSource.City:
                        result = AWHistoricalWindowReadResult.ForCity(
                            _contextId,
                            HistoryQuery.GetCityPeriods(_contextId));
                        break;
                    default:
                        result = AWHistoricalWindowReadResult.ForPerson(
                            _contextId,
                            HistoryQuery.ReadPerson(_contextId));
                        break;
                }
            }
            pToken.ThrowIfCancellationRequested();
            return result;
        }
    }
}
