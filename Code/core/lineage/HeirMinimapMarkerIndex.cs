using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Keeps the minimap renderer proportional to registered heirs rather
    /// than every kingdom. The full scan is restricted to a world change.
    /// </summary>
    internal static class HeirMinimapMarkerIndex
    {
        private static readonly List<long> CandidateKingdomIds =
            new List<long>();
        private static readonly HashSet<long> CandidateKingdomIdSet =
            new HashSet<long>();
        private static object _indexedWorld;

        internal static IReadOnlyList<long> GetCandidateKingdomIds()
        {
            object currentWorld = World.world;
            if (!ReferenceEquals(_indexedWorld, currentWorld))
                RebuildForWorld(currentWorld);
            return CandidateKingdomIds;
        }

        internal static void Refresh(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !ReferenceEquals(_indexedWorld, World.world)) return;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                out long heirId, -1L);
            if (heirId >= 0L && !pKingdom.isRekt() && pKingdom.isCiv() &&
                pKingdom.hasCities())
                Add(pKingdom.id);
            else
                Remove(pKingdom.id);
        }

        internal static void Remove(long pKingdomId)
        {
            if (!CandidateKingdomIdSet.Remove(pKingdomId)) return;
            CandidateKingdomIds.Remove(pKingdomId);
        }

        internal static void Reset()
        {
            CandidateKingdomIds.Clear();
            CandidateKingdomIdSet.Clear();
            _indexedWorld = null;
        }

        private static void RebuildForWorld(object pWorld)
        {
            CandidateKingdomIds.Clear();
            CandidateKingdomIdSet.Clear();
            _indexedWorld = pWorld;
            MapBox currentWorld = pWorld as MapBox;
            if (currentWorld?.kingdoms == null) return;
            foreach (Kingdom kingdom in currentWorld.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() ||
                    !kingdom.isCiv() || !kingdom.hasCities()) continue;
                kingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                    out long heirId, -1L);
                if (heirId >= 0L) Add(kingdom.id);
            }
        }

        private static void Add(long pKingdomId)
        {
            if (CandidateKingdomIdSet.Add(pKingdomId))
                CandidateKingdomIds.Add(pKingdomId);
        }
    }
}
