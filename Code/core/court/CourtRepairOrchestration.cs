using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    internal enum CourtRepairFailureStage
    {
        Check,
        Repair,
        Archive,
        General,
        GeneralCursorCleanup,
        CourtCursorCleanup
    }

    internal readonly struct CourtRepairScanResult
    {
        public CourtRepairScanResult(int inspected, int repairAttempts,
            int nextCursor)
        {
            Inspected = inspected;
            RepairAttempts = repairAttempts;
            NextCursor = nextCursor;
        }

        public int Inspected { get; }
        public int RepairAttempts { get; }
        public int NextCursor { get; }
    }

    internal sealed class CourtRepairCursorStore<TCursor>
    {
        private readonly Dictionary<long, TCursor> _byKingdom = new();

        public bool TryGet(long pKingdomId, out TCursor pCursor)
        {
            return _byKingdom.TryGetValue(pKingdomId, out pCursor);
        }

        public void Set(long pKingdomId, TCursor pCursor)
        {
            _byKingdom[pKingdomId] = pCursor;
        }

        public void Remove(long pKingdomId)
        {
            _byKingdom.Remove(pKingdomId);
        }

        public void Clear()
        {
            _byKingdom.Clear();
        }
    }

    internal static class CourtMeritRepairCursorRules
    {
        public static int Normalize(int pCursor, int pCount)
        {
            if (pCount <= 0) return 0;
            int cursor = pCursor % pCount;
            return cursor < 0 ? cursor + pCount : cursor;
        }

        public static int InspectionCount(int pCount,
            int pMaximumInspections)
        {
            return Math.Min(Math.Max(0, pCount),
                Math.Max(0, pMaximumInspections));
        }

        public static int Advance(int pCursor, int pInspected, int pCount)
        {
            if (pCount <= 0) return 0;
            return Normalize(Normalize(pCursor, pCount) +
                             Math.Max(0, pInspected), pCount);
        }
    }

    internal static class CourtRepairOrchestration
    {
        internal const int MaximumInspections = 32;
        internal const int MaximumRepairAttempts = 4;

        public static CourtRepairScanResult ScanBounded<T>(
            IReadOnlyList<T> pCandidates, int rawCursor,
            int maximumInspections, int maximumRepairAttempts,
            Func<T, bool> pNeedsRepair, Action<T> pRepair,
            Action<T, CourtRepairFailureStage, Exception> pOnFailure,
            Action<int> pPersistCursor)
        {
            int count = pCandidates?.Count ?? 0;
            int cursor = CourtMeritRepairCursorRules.Normalize(rawCursor,
                count);
            int inspectionLimit = CourtMeritRepairCursorRules
                .InspectionCount(count, Math.Min(MaximumInspections,
                    Math.Max(0, maximumInspections)));
            int repairLimit = Math.Min(MaximumRepairAttempts,
                Math.Max(0, maximumRepairAttempts));
            int inspected = 0;
            int repairAttempts = 0;
            int nextCursor = cursor;

            try
            {
                while (inspected < inspectionLimit &&
                       repairAttempts < repairLimit)
                {
                    T candidate;
                    try
                    {
                        candidate = pCandidates[(cursor + inspected) % count];
                    }
                    catch (Exception error)
                    {
                        inspected++;
                        ReportFailure(default, CourtRepairFailureStage.Check,
                            error, pOnFailure);
                        continue;
                    }
                    inspected++;

                    bool needsRepair;
                    try
                    {
                        needsRepair = pNeedsRepair != null &&
                                      pNeedsRepair(candidate);
                    }
                    catch (Exception error)
                    {
                        ReportFailure(candidate,
                            CourtRepairFailureStage.Check, error, pOnFailure);
                        continue;
                    }
                    if (!needsRepair) continue;

                    repairAttempts++;
                    try { pRepair?.Invoke(candidate); }
                    catch (Exception error)
                    {
                        ReportFailure(candidate,
                            CourtRepairFailureStage.Repair, error, pOnFailure);
                    }
                }
            }
            finally
            {
                nextCursor = CourtMeritRepairCursorRules.Advance(cursor,
                    inspected, count);
                pPersistCursor(nextCursor);
            }

            return new CourtRepairScanResult(inspected, repairAttempts,
                nextCursor);
        }

        public static bool TryRepairIndependent<T>(T actor,
            bool repairArchive, bool repairGeneral, ref int pRepairBudget,
            Action<T> pRepairArchive, Action<T> pRepairGeneral,
            Action<T, CourtRepairFailureStage, Exception> pOnFailure)
        {
            if (pRepairBudget <= 0 || !repairArchive && !repairGeneral)
                return false;
            pRepairBudget--;

            if (repairArchive)
            {
                try { pRepairArchive?.Invoke(actor); }
                catch (Exception error)
                {
                    ReportFailure(actor, CourtRepairFailureStage.Archive,
                        error, pOnFailure);
                }
            }
            if (repairGeneral)
            {
                try { pRepairGeneral?.Invoke(actor); }
                catch (Exception error)
                {
                    ReportFailure(actor, CourtRepairFailureStage.General,
                        error, pOnFailure);
                }
            }
            return true;
        }

        public static void ClearKingdomCursors(long pKingdomId,
            Action<long> pRemoveGeneralCursor,
            Action<long> pRemoveCourtCursor,
            Action<CourtRepairFailureStage, Exception> pOnFailure)
        {
            if (pKingdomId < 0) return;
            try { pRemoveGeneralCursor?.Invoke(pKingdomId); }
            catch (Exception error)
            {
                ReportCleanupFailure(
                    CourtRepairFailureStage.GeneralCursorCleanup, error,
                    pOnFailure);
            }
            try { pRemoveCourtCursor?.Invoke(pKingdomId); }
            catch (Exception error)
            {
                ReportCleanupFailure(
                    CourtRepairFailureStage.CourtCursorCleanup, error,
                    pOnFailure);
            }
        }

        private static void ReportFailure<T>(T candidate,
            CourtRepairFailureStage pStage, Exception pError,
            Action<T, CourtRepairFailureStage, Exception> pOnFailure)
        {
            try { pOnFailure?.Invoke(candidate, pStage, pError); }
            catch { }
        }

        private static void ReportCleanupFailure(
            CourtRepairFailureStage pStage, Exception pError,
            Action<CourtRepairFailureStage, Exception> pOnFailure)
        {
            try { pOnFailure?.Invoke(pStage, pError); }
            catch { }
        }
    }
}
