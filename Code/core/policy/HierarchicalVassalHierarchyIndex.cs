using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.policy
{
    internal sealed class HierarchicalVassalHierarchyIndex
    {
        private readonly Dictionary<long, long> _representatives;
        private readonly Dictionary<long, IReadOnlyList<long>>
            _directChildren;
        private readonly Dictionary<long, IReadOnlyList<long>>
            _representativeMembers;

        private HierarchicalVassalHierarchyIndex(long pFocusKingdomId,
            Dictionary<long, long> pRepresentatives,
            Dictionary<long, IReadOnlyList<long>> pDirectChildren,
            Dictionary<long, IReadOnlyList<long>> pRepresentativeMembers)
        {
            FocusKingdomId = pFocusKingdomId;
            _representatives = pRepresentatives;
            _directChildren = pDirectChildren;
            _representativeMembers = pRepresentativeMembers;
        }

        internal long FocusKingdomId { get; }

        internal bool IsRoot => FocusKingdomId < 0L;

        internal int KingdomCount => _representatives.Count;

        internal long ResolveRepresentative(long pKingdomId)
        {
            return _representatives.TryGetValue(pKingdomId,
                out long representative) ? representative : -1L;
        }

        internal IReadOnlyList<long> GetDirectChildren(long pKingdomId)
        {
            return _directChildren.TryGetValue(pKingdomId,
                out IReadOnlyList<long> children)
                ? children
                : Array.Empty<long>();
        }

        internal IReadOnlyList<long> GetRepresentativeMembers(
            long pRepresentativeId)
        {
            return _representativeMembers.TryGetValue(pRepresentativeId,
                out IReadOnlyList<long> members)
                ? members
                : Array.Empty<long>();
        }

        internal static HierarchicalVassalHierarchyIndex Build(
            IReadOnlyDictionary<long, long> pRawSuzerainIds,
            long pFocusKingdomId)
        {
            return Build(pRawSuzerainIds, pFocusKingdomId,
                CancellationToken.None);
        }

        internal static HierarchicalVassalHierarchyIndex Build(
            IReadOnlyDictionary<long, long> pRawSuzerainIds,
            long pFocusKingdomId, CancellationToken pCancellationToken)
        {
            pCancellationToken.ThrowIfCancellationRequested();
            var raw = new Dictionary<long, long>();
            if (pRawSuzerainIds != null)
            {
                foreach (KeyValuePair<long, long> pair in pRawSuzerainIds)
                {
                    pCancellationToken.ThrowIfCancellationRequested();
                    raw[pair.Key] = pair.Value;
                }
            }

            var roots = new Dictionary<long, long>();
            var states = new Dictionary<long, byte>();
            var invalid = new HashSet<long>();
            var stack = new List<long>();
            var stackPositions = new Dictionary<long, int>();
            foreach (long kingdomId in raw.Keys)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                ResolveRoot(kingdomId, raw, roots, states, invalid, stack,
                    stackPositions, pCancellationToken);
            }

            var directMutable = new Dictionary<long, List<long>>();
            foreach (KeyValuePair<long, long> pair in raw)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                long childId = pair.Key;
                long suzerainId = pair.Value;
                if (invalid.Contains(childId) ||
                    !raw.ContainsKey(suzerainId) ||
                    invalid.Contains(suzerainId)) continue;
                if (!directMutable.TryGetValue(suzerainId,
                        out List<long> children))
                {
                    children = new List<long>();
                    directMutable[suzerainId] = children;
                }
                children.Add(childId);
            }

            var direct = new Dictionary<long, IReadOnlyList<long>>();
            foreach (KeyValuePair<long, List<long>> pair in directMutable)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                pair.Value.Sort();
                direct[pair.Key] = pair.Value.AsReadOnly();
            }

            var representatives = new Dictionary<long, long>();
            if (pFocusKingdomId < 0L ||
                !raw.ContainsKey(pFocusKingdomId))
            {
                foreach (long kingdomId in raw.Keys)
                {
                    pCancellationToken.ThrowIfCancellationRequested();
                    representatives[kingdomId] = roots[kingdomId];
                }
                return new HierarchicalVassalHierarchyIndex(-1L,
                    representatives, direct,
                    BuildRepresentativeMembers(representatives,
                        pCancellationToken));
            }

            var focusedMemo = new Dictionary<long, long>();
            foreach (long kingdomId in raw.Keys)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                representatives[kingdomId] = ResolveFocusedRepresentative(
                    kingdomId, pFocusKingdomId, raw, invalid, focusedMemo,
                    new HashSet<long>(), pCancellationToken);
            }
            return new HierarchicalVassalHierarchyIndex(pFocusKingdomId,
                representatives, direct,
                BuildRepresentativeMembers(representatives,
                    pCancellationToken));
        }

        private static Dictionary<long, IReadOnlyList<long>>
            BuildRepresentativeMembers(
                IReadOnlyDictionary<long, long> pRepresentatives,
                CancellationToken pCancellationToken)
        {
            var mutable = new Dictionary<long, List<long>>();
            foreach (KeyValuePair<long, long> pair in pRepresentatives)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                if (pair.Value < 0L) continue;
                if (!mutable.TryGetValue(pair.Value,
                        out List<long> members))
                {
                    members = new List<long>();
                    mutable[pair.Value] = members;
                }
                members.Add(pair.Key);
            }
            var result = new Dictionary<long, IReadOnlyList<long>>();
            foreach (KeyValuePair<long, List<long>> pair in mutable)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                pair.Value.Sort();
                result[pair.Key] = pair.Value.AsReadOnly();
            }
            return result;
        }

        private static long ResolveRoot(long pKingdomId,
            IReadOnlyDictionary<long, long> pRaw,
            IDictionary<long, long> pRoots,
            IDictionary<long, byte> pStates,
            ISet<long> pInvalid,
            IList<long> pStack,
            IDictionary<long, int> pStackPositions,
            CancellationToken pCancellationToken)
        {
            pCancellationToken.ThrowIfCancellationRequested();
            if (pRoots.TryGetValue(pKingdomId, out long knownRoot))
                return knownRoot;
            if (pStates.TryGetValue(pKingdomId, out byte state) && state == 1)
            {
                int cycleStart = pStackPositions.TryGetValue(pKingdomId,
                    out int position) ? position : 0;
                for (int index = cycleStart; index < pStack.Count; index++)
                {
                    pCancellationToken.ThrowIfCancellationRequested();
                    pInvalid.Add(pStack[index]);
                }
                return pKingdomId;
            }

            pStates[pKingdomId] = 1;
            pStackPositions[pKingdomId] = pStack.Count;
            pStack.Add(pKingdomId);

            long root = pKingdomId;
            long suzerainId = pRaw.TryGetValue(pKingdomId,
                out long rawSuzerainId) ? rawSuzerainId : -1L;
            if (suzerainId >= 0L && pRaw.ContainsKey(suzerainId))
            {
                long parentRoot = ResolveRoot(suzerainId, pRaw, pRoots,
                    pStates, pInvalid, pStack, pStackPositions,
                    pCancellationToken);
                if (pInvalid.Contains(suzerainId))
                    pInvalid.Add(pKingdomId);
                else
                    root = parentRoot;
            }

            if (pInvalid.Contains(pKingdomId)) root = pKingdomId;
            pRoots[pKingdomId] = root;
            pStack.RemoveAt(pStack.Count - 1);
            pStackPositions.Remove(pKingdomId);
            pStates[pKingdomId] = 2;
            return root;
        }

        private static long ResolveFocusedRepresentative(long pKingdomId,
            long pFocusKingdomId,
            IReadOnlyDictionary<long, long> pRaw,
            ISet<long> pInvalid,
            IDictionary<long, long> pMemo,
            ISet<long> pVisiting,
            CancellationToken pCancellationToken)
        {
            pCancellationToken.ThrowIfCancellationRequested();
            if (pKingdomId == pFocusKingdomId) return pFocusKingdomId;
            if (pMemo.TryGetValue(pKingdomId, out long known)) return known;
            if (pInvalid.Contains(pKingdomId) || !pVisiting.Add(pKingdomId))
                return pMemo[pKingdomId] = -1L;

            long representative = -1L;
            long suzerainId = pRaw.TryGetValue(pKingdomId,
                out long rawSuzerainId) ? rawSuzerainId : -1L;
            if (suzerainId == pFocusKingdomId)
                representative = pKingdomId;
            else if (suzerainId >= 0L && pRaw.ContainsKey(suzerainId) &&
                     !pInvalid.Contains(suzerainId))
                representative = ResolveFocusedRepresentative(suzerainId,
                    pFocusKingdomId, pRaw, pInvalid, pMemo, pVisiting,
                    pCancellationToken);

            pVisiting.Remove(pKingdomId);
            pMemo[pKingdomId] = representative;
            return representative;
        }
    }
}
