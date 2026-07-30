using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.db
{
    internal delegate bool HistoricalOrderingBarrier(TimeSpan pTimeout,
        out string pError);

    internal delegate bool HistoricalEventIdReservation(string pTable,
        out long pEventId, out string pError);

    internal delegate bool HistoricalAppendAttempt(out long pEventId,
        out string pError);

    internal static class HistoricalSynchronousWriteCoordinator
    {
        public static bool TryExecute<TResult>(TimeSpan pTimeout,
            IReadOnlyList<string> pTables,
            HistoricalOrderingBarrier pBarrier,
            HistoricalEventIdReservation pReserve,
            Func<IReadOnlyList<long>, TResult> pTransaction,
            out TResult pResult, out string pError)
        {
            pResult = default;
            pError = string.Empty;
            if (pTransaction == null)
            {
                pError = "historical synchronous transaction is unavailable";
                return false;
            }

            int tableCount = pTables?.Count ?? 0;
            var eventIds = tableCount == 0
                ? Array.Empty<long>()
                : new long[tableCount];
            if (tableCount > 0)
            {
                if (pBarrier == null)
                {
                    pError = "historical write ordering barrier is unavailable";
                    return false;
                }
                if (!pBarrier(pTimeout, out pError))
                {
                    pError ??= string.Empty;
                    return false;
                }
                if (pReserve == null)
                {
                    pError = "historical event id allocator is unavailable";
                    return false;
                }
                for (int index = 0; index < tableCount; index++)
                {
                    if (!pReserve(pTables[index], out eventIds[index],
                            out pError))
                    {
                        pError ??= string.Empty;
                        return false;
                    }
                }
            }

            try
            {
                pResult = pTransaction(eventIds);
                pError = string.Empty;
                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }

        public static bool TryAppendOrExecute(TimeSpan pTimeout,
            HistoricalAppendAttempt pAppend,
            HistoricalOrderingBarrier pBarrier, Action<long> pFallback,
            out long pEventId, out string pError)
        {
            pEventId = 0L;
            pError = string.Empty;
            if (pAppend == null)
            {
                pError = "historical append is unavailable";
                return false;
            }

            try
            {
                if (pAppend(out pEventId, out pError))
                {
                    pError = string.Empty;
                    return true;
                }
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
            pError ??= string.Empty;
            if (pEventId <= 0L || pFallback == null) return false;
            if (pBarrier == null)
            {
                pError = "historical write ordering barrier is unavailable";
                return false;
            }
            if (!pBarrier(pTimeout, out pError))
            {
                pError ??= string.Empty;
                return false;
            }

            try
            {
                pFallback(pEventId);
                pError = string.Empty;
                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }
    }
}
