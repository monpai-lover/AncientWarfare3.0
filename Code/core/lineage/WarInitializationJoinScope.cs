using System;

namespace AncientWarfare3.core.lineage
{
    internal sealed class WarInitializationJoinScope : IDisposable
    {
        [ThreadStatic]
        private static WarInitializationJoinScope _current;

        private readonly WarInitializationJoinScope _previous;
        private bool _disposed;

        private WarInitializationJoinScope(long pWarId,
            long pAttackerKingdomId, long pDefenderKingdomId)
        {
            WarId = pWarId;
            AttackerKingdomId = pAttackerKingdomId;
            DefenderKingdomId = pDefenderKingdomId;
            _previous = _current;
            _current = this;
        }

        private long WarId { get; }
        private long AttackerKingdomId { get; }
        private long DefenderKingdomId { get; }

        public static WarInitializationJoinScope Open(War pWar,
            Kingdom pAttacker, Kingdom pDefender)
        {
            return new WarInitializationJoinScope(
                pWar?.data?.id ?? -1L,
                pAttacker?.data?.id ?? -1L,
                pDefender?.data?.id ?? -1L);
        }

        public static bool Contains(War pWar, Kingdom pKingdom)
        {
            WarInitializationJoinScope current = _current;
            long warId = pWar?.data?.id ?? -1L;
            long kingdomId = pKingdom?.data?.id ?? -1L;
            return current != null && warId >= 0 && kingdomId >= 0 &&
                   current.WarId == warId &&
                   (current.AttackerKingdomId == kingdomId ||
                    current.DefenderKingdomId == kingdomId);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!ReferenceEquals(_current, this)) return;
            WarInitializationJoinScope next = _previous;
            while (next != null && next._disposed) next = next._previous;
            _current = next;
        }
    }
}
