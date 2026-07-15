using System;

namespace AncientWarfare3.core.schools
{
    public static class FormalAffiliationTransferRules
    {
        public static bool Allows(
            long pPermitActor,
            long pPermitKingdom,
            long pPermitCity,
            long pActor,
            long pKingdom,
            long pCity)
        {
            return pPermitActor >= 0 && pPermitActor == pActor &&
                   pPermitKingdom >= 0 && pPermitKingdom == pKingdom &&
                   pPermitCity >= 0 && pPermitCity == pCity;
        }

        public static bool AllowsKingdom(
            long pPermitActor,
            long pPermitKingdom,
            long pActor,
            long pKingdom)
        {
            return pPermitActor >= 0 && pPermitActor == pActor &&
                   pPermitKingdom >= 0 && pPermitKingdom == pKingdom;
        }
    }

    internal sealed class FormalAffiliationTransferScope : IDisposable
    {
        [ThreadStatic]
        private static FormalAffiliationTransferScope _current;

        private readonly FormalAffiliationTransferScope _previous;
        private bool _disposed;

        private FormalAffiliationTransferScope(
            long pActorId,
            long pKingdomId,
            long pCityId)
        {
            ActorId = pActorId;
            KingdomId = pKingdomId;
            CityId = pCityId;
            _previous = _current;
            _current = this;
        }

        public long ActorId { get; }
        public long KingdomId { get; }
        public long CityId { get; }

        public static FormalAffiliationTransferScope Open(
            long pActorId,
            long pKingdomId,
            long pCityId)
        {
            return new FormalAffiliationTransferScope(pActorId, pKingdomId, pCityId);
        }

        public static bool Allows(long pActorId, long pKingdomId, long pCityId)
        {
            FormalAffiliationTransferScope permit = _current;
            return permit != null && FormalAffiliationTransferRules.Allows(
                permit.ActorId,
                permit.KingdomId,
                permit.CityId,
                pActorId,
                pKingdomId,
                pCityId);
        }

        public static bool AllowsKingdom(long pActorId, long pKingdomId)
        {
            FormalAffiliationTransferScope permit = _current;
            return permit != null && FormalAffiliationTransferRules.AllowsKingdom(
                permit.ActorId,
                permit.KingdomId,
                pActorId,
                pKingdomId);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_current, this)) _current = _previous;
        }
    }
}
