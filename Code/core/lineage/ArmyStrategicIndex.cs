using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class ArmyStrategicIdentityIndex
    {
        private readonly Dictionary<long, long> _kingdomByArmy =
            new Dictionary<long, long>();
        private readonly Dictionary<long, SortedSet<long>> _armiesByKingdom =
            new Dictionary<long, SortedSet<long>>();

        public bool Register(long pArmyId, long pKingdomId)
        {
            if (pArmyId < 0 || pKingdomId < 0) return false;
            if (_kingdomByArmy.TryGetValue(pArmyId, out long previousId))
            {
                if (previousId == pKingdomId) return false;
                RemoveFromKingdom(pArmyId, previousId);
            }

            if (!_armiesByKingdom.TryGetValue(pKingdomId,
                    out SortedSet<long> armies))
            {
                armies = new SortedSet<long>();
                _armiesByKingdom[pKingdomId] = armies;
            }
            armies.Add(pArmyId);
            _kingdomByArmy[pArmyId] = pKingdomId;
            return true;
        }

        public bool Remove(long pArmyId)
        {
            if (!_kingdomByArmy.TryGetValue(pArmyId, out long kingdomId))
                return false;
            _kingdomByArmy.Remove(pArmyId);
            RemoveFromKingdom(pArmyId, kingdomId);
            return true;
        }

        public bool TryGetKingdomId(long pArmyId, out long pKingdomId)
        {
            return _kingdomByArmy.TryGetValue(pArmyId, out pKingdomId);
        }

        public IReadOnlyList<long> GetArmyIds(long pKingdomId)
        {
            if (!_armiesByKingdom.TryGetValue(pKingdomId,
                    out SortedSet<long> armies) || armies.Count == 0)
                return Array.Empty<long>();
            var result = new long[armies.Count];
            armies.CopyTo(result);
            return result;
        }

        public int CopyArmyIdsAfter(long pKingdomId,
            long pAfterArmyId, int pMaximum, List<long> pDestination,
            out bool pComplete)
        {
            pComplete = true;
            if (pDestination == null) return 0;
            pDestination.Clear();
            int maximum = Math.Max(0, pMaximum);
            if (maximum == 0)
            {
                pComplete = false;
                return 0;
            }
            if (!_armiesByKingdom.TryGetValue(pKingdomId,
                    out SortedSet<long> armies) || armies.Count == 0)
                return 0;
            if (pAfterArmyId == long.MaxValue) return 0;
            long minimum = Math.Max(0L, pAfterArmyId + 1L);
            foreach (long armyId in armies.GetViewBetween(minimum,
                         long.MaxValue))
            {
                if (pDestination.Count >= maximum)
                {
                    pComplete = false;
                    break;
                }
                pDestination.Add(armyId);
            }
            return pDestination.Count;
        }

        public ArmyStrategicIdCursor CreateCursor(long pKingdomId)
        {
            return new ArmyStrategicIdCursor(GetArmyIds(pKingdomId));
        }

        public void Clear()
        {
            _kingdomByArmy.Clear();
            _armiesByKingdom.Clear();
        }

        private void RemoveFromKingdom(long pArmyId, long pKingdomId)
        {
            if (!_armiesByKingdom.TryGetValue(pKingdomId,
                    out SortedSet<long> armies)) return;
            armies.Remove(pArmyId);
            if (armies.Count == 0) _armiesByKingdom.Remove(pKingdomId);
        }
    }

    public sealed class ArmyStrategicIdCursor
    {
        private readonly IReadOnlyList<long> _armyIds;
        private int _position;

        public ArmyStrategicIdCursor(IReadOnlyList<long> pArmyIds)
        {
            _armyIds = pArmyIds ?? Array.Empty<long>();
        }

        public bool IsComplete => _position >= _armyIds.Count;
        public int Remaining => Math.Max(0, _armyIds.Count - _position);

        public IReadOnlyList<long> Take(int pLimit)
        {
            int count = Math.Min(Math.Max(0, pLimit), Remaining);
            if (count == 0) return Array.Empty<long>();
            var result = new long[count];
            for (int i = 0; i < count; i++)
                result[i] = _armyIds[_position + i];
            _position += count;
            return result;
        }
    }

    public sealed class ArmyFieldIdentityIndex
    {
        private readonly ArmyStrategicIdentityIndex _identities =
            new ArmyStrategicIdentityIndex();

        public bool Register(long pArmyId, long pKingdomId)
        {
            return _identities.Register(pArmyId, pKingdomId);
        }

        public bool Remove(long pArmyId)
        {
            return _identities.Remove(pArmyId);
        }

        public int Count(long pKingdomId)
        {
            return _identities.GetArmyIds(pKingdomId).Count;
        }

        public ArmyStrategicIdCursor CreateCursor(long pKingdomId)
        {
            return _identities.CreateCursor(pKingdomId);
        }

        public void Clear()
        {
            _identities.Clear();
        }
    }

    public sealed class ArmyFieldUsabilityScan
    {
        private readonly ArmyStrategicIdCursor _cursor;
        private readonly int _maximumValidFieldArmies;
        private int _validFieldArmies;
        private int _pendingObservations;

        public ArmyFieldUsabilityScan(ArmyStrategicIdCursor pCursor,
            int maximumValidFieldArmies)
        {
            _cursor = pCursor ?? new ArmyStrategicIdCursor(
                Array.Empty<long>());
            _maximumValidFieldArmies = Math.Max(0,
                maximumValidFieldArmies);
        }

        public bool FoundUsable { get; private set; }
        public bool IsComplete => FoundUsable ||
                                  _validFieldArmies >=
                                  _maximumValidFieldArmies ||
                                  _cursor.IsComplete &&
                                  _pendingObservations == 0;

        public IReadOnlyList<long> TakeNextRawBatch(int pMaximum)
        {
            if (IsComplete || _pendingObservations > 0)
                return Array.Empty<long>();
            int limit = Math.Min(Math.Max(0, pMaximum),
                _maximumValidFieldArmies);
            IReadOnlyList<long> result = _cursor.Take(limit);
            _pendingObservations = result.Count;
            return result;
        }

        public void Observe(bool validFieldArmy, bool usable)
        {
            if (_pendingObservations <= 0 || IsComplete) return;
            _pendingObservations--;
            if (!validFieldArmy) return;
            _validFieldArmies++;
            if (usable) FoundUsable = true;
        }
    }
}
