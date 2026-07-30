using System;

namespace AncientWarfare3.core.lineage
{
    internal sealed class WarParticipantEntrySourceScope : IDisposable
    {
        [ThreadStatic]
        private static WarParticipantEntrySourceScope _current;

        private readonly WarParticipantEntrySourceScope _previous;
        private bool _disposed;

        private WarParticipantEntrySourceScope(long pWarId, long pKingdomId,
            WarParticipantEntrySourceKind pSourceKind,
            long pSourceKingdomId)
        {
            WarId = pWarId;
            KingdomId = pKingdomId;
            SourceKind = pSourceKind;
            SourceKingdomId = pSourceKingdomId;
            _previous = _current;
            _current = this;
        }

        public long WarId { get; }
        public long KingdomId { get; }
        public WarParticipantEntrySourceKind SourceKind { get; }
        public long SourceKingdomId { get; }

        public static WarParticipantEntrySourceScope Open(War pWar,
            Kingdom pKingdom, WarParticipantEntrySourceKind pSourceKind,
            Kingdom pSourceKingdom)
        {
            return new WarParticipantEntrySourceScope(
                pWar?.data?.id ?? -1L,
                pKingdom?.data?.id ?? -1L,
                pSourceKind,
                pSourceKingdom?.data?.id ?? -1L);
        }

        public static bool TryCurrent(War pWar, Kingdom pKingdom,
            out WarParticipantEntrySourceKind pSourceKind,
            out long pSourceKingdomId)
        {
            pSourceKind = WarParticipantEntrySourceKind.Unknown;
            pSourceKingdomId = -1L;
            WarParticipantEntrySourceScope current = _current;
            long warId = pWar?.data?.id ?? -1L;
            long kingdomId = pKingdom?.data?.id ?? -1L;
            if (current == null || warId < 0 || kingdomId < 0 ||
                current.WarId != warId || current.KingdomId != kingdomId ||
                current.SourceKind == WarParticipantEntrySourceKind.Unknown)
                return false;
            pSourceKind = current.SourceKind;
            pSourceKingdomId = current.SourceKingdomId;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!ReferenceEquals(_current, this)) return;
            WarParticipantEntrySourceScope next = _previous;
            while (next != null && next._disposed) next = next._previous;
            _current = next;
        }
    }
}
