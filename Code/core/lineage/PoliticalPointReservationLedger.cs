using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class PoliticalPointReservationLedger
    {
        private readonly struct Reservation
        {
            public readonly long KingdomId;
            public readonly int Amount;

            public Reservation(long pKingdomId, int pAmount)
            {
                KingdomId = pKingdomId;
                Amount = pAmount;
            }
        }

        private readonly object _gate = new object();
        private readonly Dictionary<long, Reservation> _byId =
            new Dictionary<long, Reservation>();
        private readonly Dictionary<long, long> _byKingdom =
            new Dictionary<long, long>();
        private long _nextId = 1;

        public bool TryReserve(long kingdomId, int amount, int available,
            out long reservationId)
        {
            reservationId = -1;
            if (kingdomId < 0 || amount <= 0 || available < amount) return false;
            lock (_gate)
            {
                if (_byKingdom.ContainsKey(kingdomId)) return false;
                long id = _nextId++;
                _byId[id] = new Reservation(kingdomId, amount);
                _byKingdom[kingdomId] = id;
                reservationId = id;
                return true;
            }
        }

        public bool TryCommit(long reservationId, out long kingdomId,
            out int amount)
        {
            kingdomId = -1;
            amount = 0;
            lock (_gate)
            {
                if (!_byId.TryGetValue(reservationId, out Reservation reservation))
                    return false;
                _byId.Remove(reservationId);
                _byKingdom.Remove(reservation.KingdomId);
                kingdomId = reservation.KingdomId;
                amount = reservation.Amount;
                return true;
            }
        }

        public void Release(long reservationId)
        {
            lock (_gate)
            {
                if (!_byId.TryGetValue(reservationId, out Reservation reservation))
                    return;
                _byId.Remove(reservationId);
                _byKingdom.Remove(reservation.KingdomId);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _byId.Clear();
                _byKingdom.Clear();
            }
        }
    }
}
