using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class DiplomacyProposalRuntimeState<TCursor>
        where TCursor : class, IDisposable
    {
        public const int NoAnnualDecision = int.MinValue;

        private readonly Dictionary<long, TCursor> _settlementCursors =
            new Dictionary<long, TCursor>();
        private readonly Dictionary<long, TCursor> _recoveryCursors =
            new Dictionary<long, TCursor>();
        private readonly Dictionary<long, int> _settlementAssessmentYears =
            new Dictionary<long, int>();
        private readonly Dictionary<long, AnnualPreparationState>
            _annualPreparations =
                new Dictionary<long, AnnualPreparationState>();
        private readonly Dictionary<long, int> _annualDecisionYears =
            new Dictionary<long, int>();
        private readonly Dictionary<long, long> _annualDecisionLeases =
            new Dictionary<long, long>();

        public TCursor GetOrAddSettlementCursor(long pKingdomId,
            Func<TCursor> pFactory)
        {
            return GetOrAdd(_settlementCursors, pKingdomId, pFactory);
        }

        public TCursor GetOrAddRecoveryCursor(long pKingdomId,
            Func<TCursor> pFactory)
        {
            return GetOrAdd(_recoveryCursors, pKingdomId, pFactory);
        }

        public bool WasWarSettlementAssessed(long pKingdomId, int pYear)
        {
            return _settlementAssessmentYears.TryGetValue(pKingdomId,
                       out int assessedYear) && assessedYear == pYear;
        }

        public void MarkWarSettlementAssessed(long pKingdomId, int pYear)
        {
            _settlementAssessmentYears[pKingdomId] = pYear;
        }

        public bool GetOrRunAnnualPreparation(long pKingdomId, int pYear,
            Func<bool> pPrepare)
        {
            if (_annualPreparations.TryGetValue(pKingdomId,
                    out AnnualPreparationState state) &&
                state.Year == pYear)
                return state.CanPlan;
            bool canPlan = pPrepare != null && pPrepare();
            _annualPreparations[pKingdomId] =
                new AnnualPreparationState(pYear, canPlan);
            return canPlan;
        }

        public bool TryBeginAnnualDecision(long pKingdomId, int pYear)
        {
            if (_annualDecisionYears.TryGetValue(pKingdomId,
                    out int decisionYear) && decisionYear == pYear)
                return false;
            _annualDecisionYears[pKingdomId] = pYear;
            _annualDecisionLeases.Remove(pKingdomId);
            return true;
        }

        public bool TryReserveAnnualDecision(long pKingdomId, int pYear,
            long pLeaseId, out int pPreviousYear)
        {
            pPreviousYear = NoAnnualDecision;
            if (pLeaseId <= 0L ||
                _annualDecisionYears.TryGetValue(pKingdomId,
                    out int decisionYear) && decisionYear == pYear)
                return false;
            if (_annualDecisionYears.TryGetValue(pKingdomId,
                    out decisionYear))
                pPreviousYear = decisionYear;
            _annualDecisionYears[pKingdomId] = pYear;
            _annualDecisionLeases[pKingdomId] = pLeaseId;
            return true;
        }

        public bool TryRollbackAnnualDecision(long pKingdomId, int pYear,
            long pLeaseId, int pPreviousYear)
        {
            if (!_annualDecisionYears.TryGetValue(pKingdomId,
                    out int decisionYear) || decisionYear != pYear ||
                !_annualDecisionLeases.TryGetValue(pKingdomId,
                    out long leaseId) || leaseId != pLeaseId)
                return false;
            _annualDecisionLeases.Remove(pKingdomId);
            if (pPreviousYear == NoAnnualDecision)
                _annualDecisionYears.Remove(pKingdomId);
            else
                _annualDecisionYears[pKingdomId] = pPreviousYear;
            return true;
        }

        public bool TryCompleteAnnualDecision(long pKingdomId, int pYear,
            long pLeaseId)
        {
            if (!_annualDecisionYears.TryGetValue(pKingdomId,
                    out int decisionYear) || decisionYear != pYear ||
                !_annualDecisionLeases.TryGetValue(pKingdomId,
                    out long leaseId) || leaseId != pLeaseId)
                return false;
            _annualDecisionLeases.Remove(pKingdomId);
            return true;
        }

        public void RemoveKingdom(long pKingdomId)
        {
            RemoveCursor(_settlementCursors, pKingdomId);
            RemoveCursor(_recoveryCursors, pKingdomId);
            _settlementAssessmentYears.Remove(pKingdomId);
            _annualPreparations.Remove(pKingdomId);
            _annualDecisionYears.Remove(pKingdomId);
            _annualDecisionLeases.Remove(pKingdomId);
        }

        public void Clear()
        {
            DisposeAll(_settlementCursors);
            DisposeAll(_recoveryCursors);
            _settlementAssessmentYears.Clear();
            _annualPreparations.Clear();
            _annualDecisionYears.Clear();
            _annualDecisionLeases.Clear();
        }

        private static TCursor GetOrAdd(
            IDictionary<long, TCursor> pCursors, long pKingdomId,
            Func<TCursor> pFactory)
        {
            if (pCursors.TryGetValue(pKingdomId, out TCursor cursor))
                return cursor;
            cursor = pFactory();
            pCursors[pKingdomId] = cursor;
            return cursor;
        }

        private static void RemoveCursor(
            IDictionary<long, TCursor> pCursors, long pKingdomId)
        {
            if (!pCursors.TryGetValue(pKingdomId, out TCursor cursor))
                return;
            pCursors.Remove(pKingdomId);
            cursor?.Dispose();
        }

        private static void DisposeAll(
            IDictionary<long, TCursor> pCursors)
        {
            foreach (TCursor cursor in pCursors.Values) cursor?.Dispose();
            pCursors.Clear();
        }

        private readonly struct AnnualPreparationState
        {
            public AnnualPreparationState(int pYear, bool pCanPlan)
            {
                Year = pYear;
                CanPlan = pCanPlan;
            }

            public int Year { get; }
            public bool CanPlan { get; }
        }
    }
}
