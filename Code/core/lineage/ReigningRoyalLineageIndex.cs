using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ReigningRoyalLineageIndex
    {
        private const int RebuildKingdomsPerCycle = 16;
        private static readonly ReigningRoyalLineageIndexState State =
            new ReigningRoyalLineageIndexState();
        private static Kingdom[] _rebuildKingdoms;
        private static int _rebuildCursor;

        internal static bool HasReigningKing(long pLineageId)
        {
            return State.IsReady && State.HasReigningKing(pLineageId);
        }

        internal static bool IsRoyalLineageOf(Kingdom pKingdom,
            long pLineageId)
        {
            if (!State.IsReady || pKingdom?.data == null ||
                pLineageId < 0L ||
                !State.HasReigningKing(pLineageId) ||
                !State.Contains(pLineageId, pKingdom.id)) return false;
            return ReadRoyalLineage(pKingdom) == pLineageId;
        }

        internal static void OnKingDying(Kingdom pKingdom, Actor pKing)
        {
            OnKingRemoved(pKingdom, pKing);
        }

        internal static void OnKingRemoved(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null ||
                pKingdom.king != pKing) return;
            State.RemoveKingdom(pKingdom.id);
        }

        internal static void OnKingInstalled(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null ||
                pKingdom.king != pKing || !pKing.isAlive() ||
                pKing.isRekt()) return;
            long lineageId = ReadLineage(pKing);
            if (lineageId < 0L)
            {
                State.RemoveKingdom(pKingdom.id);
                return;
            }
            State.Register(lineageId, pKingdom.id);
        }

        internal static void ProcessAuthorityCycle()
        {
            if (State.IsReady || !Config.game_loaded ||
                SmoothLoader.isLoading()) return;
            if (_rebuildKingdoms == null && !BeginRebuild()) return;

            int end = System.Math.Min(_rebuildKingdoms.Length,
                _rebuildCursor + RebuildKingdomsPerCycle);
            for (; _rebuildCursor < end; _rebuildCursor++)
            {
                Kingdom kingdom = _rebuildKingdoms[_rebuildCursor];
                Actor king = kingdom?.king;
                if (kingdom?.data == null || kingdom.isRekt() ||
                    king?.data == null || !king.isAlive() ||
                    king.isRekt()) continue;
                long lineageId = ReadLineage(king);
                if (lineageId >= 0L)
                    State.Register(lineageId, kingdom.id);
            }

            if (_rebuildCursor < _rebuildKingdoms.Length) return;
            _rebuildKingdoms = null;
            _rebuildCursor = 0;
            State.CompleteRebuild();
        }

        internal static void Reset()
        {
            State.Clear();
            _rebuildKingdoms = null;
            _rebuildCursor = 0;
        }

        private static bool BeginRebuild()
        {
            if (World.world?.kingdoms == null) return false;
            var kingdoms = new List<Kingdom>();
            foreach (Kingdom kingdom in World.world.kingdoms)
                kingdoms.Add(kingdom);
            _rebuildKingdoms = kingdoms.ToArray();
            _rebuildCursor = 0;
            State.BeginRebuild();
            return true;
        }

        private static long ReadRoyalLineage(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long lineageId, -1L);
            return lineageId >= 0L ? lineageId : ReadLineage(pKingdom.king);
        }

        private static long ReadLineage(Actor pActor)
        {
            if (pActor?.data == null) return -1L;
            pActor.data.get(LineageKeys.LINEAGE_ID,
                out long lineageId, -1L);
            return lineageId;
        }
    }
}
