using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class ArmyLegacyRetreatIndex
    {
        private sealed class State
        {
            internal long TargetCityId;
            internal int Baseline;
            internal int Living;
            internal bool Suppressed;
        }

        private readonly Dictionary<long, State> _states =
            new Dictionary<long, State>();
        private readonly Dictionary<long, int> _pendingCasualties =
            new Dictionary<long, int>();

        public void BeginTarget(long armyId, long targetCityId,
            int baselineUnits)
        {
            if (armyId < 0L || targetCityId < 0L || baselineUnits < 0)
                return;
            if (_states.TryGetValue(armyId, out State current))
            {
                if (current.TargetCityId == targetCityId) return;
                current.TargetCityId = targetCityId;
                return;
            }
            _pendingCasualties.TryGetValue(armyId, out int pending);
            _pendingCasualties.Remove(armyId);
            _states[armyId] = new State
            {
                TargetCityId = targetCityId,
                Baseline = baselineUnits,
                Living = Math.Max(0, baselineUnits - pending)
            };
        }

        public void RecordCasualty(long armyId)
        {
            if (_states.TryGetValue(armyId, out State state))
            {
                state.Suppressed = false;
                if (state.Living > 0) state.Living--;
                return;
            }
            _pendingCasualties.TryGetValue(armyId, out int pending);
            if (pending < int.MaxValue)
                _pendingCasualties[armyId] = pending + 1;
        }

        public bool TryGet(long armyId, long targetCityId,
            out int pBaseline, out int pLiving)
        {
            pBaseline = 0;
            pLiving = 0;
            if (!_states.TryGetValue(armyId, out State state) ||
                state.TargetCityId != targetCityId ||
                state.Suppressed) return false;
            pBaseline = state.Baseline;
            pLiving = state.Living;
            return true;
        }

        public long RecordRecovery(long armyId)
        {
            _pendingCasualties.Remove(armyId);
            if (_states.TryGetValue(armyId, out State state))
            {
                state.Suppressed = true;
                return state.TargetCityId;
            }
            return -1L;
        }

        public void Remove(long armyId)
        {
            _states.Remove(armyId);
            _pendingCasualties.Remove(armyId);
        }

        public void Clear()
        {
            _states.Clear();
            _pendingCasualties.Clear();
        }
    }

    public struct ArmyLegacyRetreatPersistenceFlow
    {
        private bool _hasSuppressedTarget;
        private long _suppressedTargetCityId;

        public ArmyLegacyRetreatPersistenceFlow(
            long persistedSuppressedTargetCityId)
        {
            _hasSuppressedTarget = persistedSuppressedTargetCityId >= 0L;
            _suppressedTargetCityId = persistedSuppressedTargetCityId;
        }

        public long SuppressedTargetCityId => _hasSuppressedTarget
            ? _suppressedTargetCityId
            : -1L;

        public bool ShouldSuppressTarget(long targetCityId)
        {
            return targetCityId >= 0L && _hasSuppressedTarget &&
                   _suppressedTargetCityId == targetCityId;
        }

        public bool TryBeginTarget(long targetCityId)
        {
            if (targetCityId < 0L || ShouldSuppressTarget(targetCityId))
                return false;
            _hasSuppressedTarget = false;
            _suppressedTargetCityId = -1L;
            return true;
        }

        public void RecordRecovery(long targetCityId)
        {
            _hasSuppressedTarget = targetCityId >= 0L;
            _suppressedTargetCityId = targetCityId;
        }

        public void RecordCasualty()
        {
            _hasSuppressedTarget = false;
            _suppressedTargetCityId = -1L;
        }
    }

    public readonly struct ArmySafeCityCandidate
    {
        public ArmySafeCityCandidate(long pCityId, long distanceSquared,
            bool friendly, bool underAttack, bool enemyFrozenControlled,
            bool reachable, bool sameIsland, bool coolingDown = false,
            bool excluded = false)
        {
            CityId = pCityId;
            DistanceSquared = Math.Max(0L, distanceSquared);
            Friendly = friendly;
            UnderAttack = underAttack;
            EnemyFrozenControlled = enemyFrozenControlled;
            Reachable = reachable;
            SameIsland = sameIsland;
            CoolingDown = coolingDown;
            Excluded = excluded;
        }

        public long CityId { get; }
        public long DistanceSquared { get; }
        public bool Friendly { get; }
        public bool UnderAttack { get; }
        public bool EnemyFrozenControlled { get; }
        public bool Reachable { get; }
        public bool SameIsland { get; }
        public bool CoolingDown { get; }
        public bool Excluded { get; }
    }

    public sealed class ArmySafeCityIndex
    {
        internal sealed class Bucket
        {
            internal readonly List<long> CityIds = new List<long>();
            internal readonly Dictionary<long, int> Positions =
                new Dictionary<long, int>();
            internal int Version;
        }

        private readonly Dictionary<long, Bucket> _byKingdom =
            new Dictionary<long, Bucket>();
        private readonly Dictionary<long, long> _kingdomByCity =
            new Dictionary<long, long>();

        public void SetCity(long cityId, long kingdomId, bool safe)
        {
            if (cityId < 0L) return;
            if (_kingdomByCity.TryGetValue(cityId, out long previousKingdom))
            {
                if (safe && previousKingdom == kingdomId) return;
                Remove(previousKingdom, cityId);
            }
            if (!safe || kingdomId < 0L) return;
            Bucket bucket = GetOrCreate(kingdomId);
            bucket.Positions[cityId] = bucket.CityIds.Count;
            bucket.CityIds.Add(cityId);
            bucket.Version++;
            _kingdomByCity[cityId] = kingdomId;
        }

        public bool Contains(long kingdomId, long cityId)
        {
            return _byKingdom.TryGetValue(kingdomId, out Bucket bucket) &&
                   bucket.Positions.ContainsKey(cityId);
        }

        public ArmySafeCityIdCursor CreateCursor(long kingdomId)
        {
            _byKingdom.TryGetValue(kingdomId, out Bucket bucket);
            return new ArmySafeCityIdCursor(bucket);
        }

        public void RemoveKingdom(long kingdomId)
        {
            if (!_byKingdom.TryGetValue(kingdomId, out Bucket bucket)) return;
            bucket.Version++;
            for (int i = 0; i < bucket.CityIds.Count; i++)
                _kingdomByCity.Remove(bucket.CityIds[i]);
            _byKingdom.Remove(kingdomId);
        }

        public void Clear()
        {
            _byKingdom.Clear();
            _kingdomByCity.Clear();
        }

        private Bucket GetOrCreate(long pKingdomId)
        {
            if (!_byKingdom.TryGetValue(pKingdomId, out Bucket bucket))
            {
                bucket = new Bucket();
                _byKingdom[pKingdomId] = bucket;
            }
            return bucket;
        }

        private void Remove(long pKingdomId, long pCityId)
        {
            if (!_byKingdom.TryGetValue(pKingdomId, out Bucket bucket) ||
                !bucket.Positions.TryGetValue(pCityId, out int position))
                return;
            int lastPosition = bucket.CityIds.Count - 1;
            long lastCityId = bucket.CityIds[lastPosition];
            bucket.CityIds[position] = lastCityId;
            bucket.CityIds.RemoveAt(lastPosition);
            bucket.Positions.Remove(pCityId);
            if (position < bucket.CityIds.Count)
                bucket.Positions[lastCityId] = position;
            bucket.Version++;
            _kingdomByCity.Remove(pCityId);
            if (bucket.CityIds.Count == 0) _byKingdom.Remove(pKingdomId);
        }
    }

    public sealed class ArmySafeCityIdCursor
    {
        private readonly ArmySafeCityIndex.Bucket _bucket;
        private readonly int _version;
        private int _position;

        internal ArmySafeCityIdCursor(ArmySafeCityIndex.Bucket pBucket)
        {
            _bucket = pBucket;
            _version = pBucket?.Version ?? 0;
        }

        public bool IsStale => _bucket != null &&
                               _bucket.Version != _version;

        public bool IsComplete => _bucket == null || IsStale ||
                                  _position >= _bucket.CityIds.Count;

        public IReadOnlyList<long> Take(int pMaximum)
        {
            int limit = Math.Max(0, pMaximum);
            if (_bucket == null || IsStale || limit == 0)
                return Array.Empty<long>();
            int end = Math.Min(_bucket.CityIds.Count, _position + limit);
            var result = new List<long>(end - _position);
            while (_position < end)
                result.Add(_bucket.CityIds[_position++]);
            return result;
        }
    }

    public enum ArmyRetreatSelectionOutcome
    {
        Pending,
        Assigned,
        Recover
    }

    public readonly struct ArmyRetreatCandidateFlowResult
    {
        public ArmyRetreatCandidateFlowResult(
            ArmyRetreatSelectionOutcome pOutcome, long pCityId)
        {
            Outcome = pOutcome;
            CityId = pCityId;
        }

        public ArmyRetreatSelectionOutcome Outcome { get; }
        public long CityId { get; }
    }

    public sealed class ArmyRetreatCandidateFlow
    {
        public ArmyRetreatCandidateFlowResult ObserveBatch(
            IReadOnlyList<ArmySafeCityCandidate> pCandidates,
            bool cursorComplete)
        {
            long selected = ArmyRetreatRules.SelectNearestSafeCityId(
                pCandidates);
            ArmyRetreatSelectionOutcome outcome = cursorComplete
                ? selected >= 0L
                    ? ArmyRetreatSelectionOutcome.Assigned
                    : ArmyRetreatSelectionOutcome.Recover
                : ArmyRetreatSelectionOutcome.Pending;
            return new ArmyRetreatCandidateFlowResult(outcome, selected);
        }
    }

    public sealed class ArmyRetreatSelectionState
    {
        public const int MaximumPendingAttempts = 3;

        public int PendingAttempts { get; private set; }
        public ArmyRetreatSelectionOutcome Outcome { get; private set; } =
            ArmyRetreatSelectionOutcome.Pending;

        public ArmyRetreatSelectionOutcome RecordPending()
        {
            if (Outcome != ArmyRetreatSelectionOutcome.Pending)
                return Outcome;
            if (PendingAttempts < MaximumPendingAttempts)
            {
                PendingAttempts++;
                return Outcome;
            }
            Outcome = ArmyRetreatSelectionOutcome.Recover;
            return Outcome;
        }

        public ArmyRetreatSelectionOutcome RecordAssigned()
        {
            Outcome = ArmyRetreatSelectionOutcome.Assigned;
            return Outcome;
        }
    }

    public enum ArmyLegacyRetreatMovementOutcome
    {
        Pending,
        Complete,
        Recover
    }

    public sealed class ArmyLegacyRetreatMovementFlow
    {
        public const int MaximumPendingAttempts = 3;

        public int PendingAttempts { get; private set; }
        public ArmyLegacyRetreatMovementOutcome Outcome { get; private set; } =
            ArmyLegacyRetreatMovementOutcome.Pending;

        public ArmyLegacyRetreatMovementOutcome RecordAttempt(bool succeeded)
        {
            if (Outcome != ArmyLegacyRetreatMovementOutcome.Pending)
                return Outcome;
            if (succeeded)
            {
                Outcome = ArmyLegacyRetreatMovementOutcome.Complete;
                return Outcome;
            }
            if (PendingAttempts < MaximumPendingAttempts)
            {
                PendingAttempts++;
                return Outcome;
            }
            Outcome = ArmyLegacyRetreatMovementOutcome.Recover;
            return Outcome;
        }
    }

    public static class ArmyRetreatRules
    {
        public const int RetreatCooldownYears = 1;
        public const int FormalWarLossPercent = 20;

        public static int CalculateFormalWarLossThreshold(int pBaselineUnits)
        {
            if (pBaselineUnits <= 0) return 0;
            return (int)(((long)pBaselineUnits * FormalWarLossPercent + 99L) / 100L);
        }

        public static bool ShouldRetreatAfterFormalWarLosses(
            int pBaselineUnits, int pCumulativeLosses)
        {
            if (pBaselineUnits <= 0) return false;
            return Math.Max(0, pCumulativeLosses) >=
                   CalculateFormalWarLossThreshold(pBaselineUnits);
        }

        public static bool ShouldResetFormalWarBaseline(
            int pStoredBaselineUnits, int pCurrentUnits)
        {
            return false;
        }

        public static int KeepFormalWarBaseline(
            int pStoredBaselineUnits, int pCurrentUnits)
        {
            return Math.Max(0, pStoredBaselineUnits);
        }

        public static bool ShouldEvaluateLegacyRetreat(ArmyRtsMode pMode,
            bool actorIsCaptain)
        {
            return actorIsCaptain && pMode != ArmyRtsMode.On;
        }

        public static long SelectNearestSafeCityId(
            IReadOnlyList<ArmySafeCityCandidate> pCandidates)
        {
            if (pCandidates == null) return -1L;
            long selected = -1L;
            long distance = long.MaxValue;
            for (int i = 0; i < pCandidates.Count; i++)
            {
                ArmySafeCityCandidate candidate = pCandidates[i];
                if (candidate.CityId < 0L || !candidate.Friendly ||
                    candidate.UnderAttack || candidate.EnemyFrozenControlled ||
                    !candidate.Reachable || !candidate.SameIsland ||
                    candidate.CoolingDown || candidate.Excluded) continue;
                if (candidate.DistanceSquared > distance ||
                    candidate.DistanceSquared == distance &&
                    candidate.CityId >= selected) continue;
                selected = candidate.CityId;
                distance = candidate.DistanceSquared;
            }
            return selected;
        }

        public static int SelectRetreatOriginTileId(int captainTileId,
            int formationAnchorTileId, int currentCityTileId,
            int missionAnchorTileId)
        {
            if (captainTileId >= 0) return captainTileId;
            if (formationAnchorTileId >= 0) return formationAnchorTileId;
            if (currentCityTileId >= 0) return currentCityTileId;
            return missionAnchorTileId >= 0 ? missionAnchorTileId : -1;
        }

        public static bool ShouldRetreat(string pRole, int pBaselineUnits, int pCurrentUnits,
            bool pCaptainAlive, bool pIsAttacking, bool pCooldownActive)
        {
            if (!pIsAttacking) return false;
            if (pCooldownActive) return false;
            if (pRole == AWArmyRole.RoyalGuard) return false;
            if (pCurrentUnits < 0) pCurrentUnits = 0;

            int losses = Math.Max(0, pBaselineUnits - pCurrentUnits);
            return ShouldRetreatAfterFormalWarLosses(pBaselineUnits, losses);
        }

        public static bool ShouldSkipAttackWhileRetreating(int pRetreatUntilYear, int pCurrentYear)
        {
            return pRetreatUntilYear > pCurrentYear;
        }

        public static bool ShouldStandAndFightWhenNoSafeCity(
            ArmyRtsRole pRole, ArmyRtsPosture pPosture,
            bool hasSafeRetreatCity, bool explicitPlayerRetreat)
        {
            return !hasSafeRetreatCity;
        }

        public static bool ProtectUncontestedOccupation(bool attackerIsDominant, bool activeCaptureUnits,
            bool noDefenders, bool hostileRivalActive, bool ownershipChanged)
        {
            return attackerIsDominant && activeCaptureUnits && noDefenders &&
                   !hostileRivalActive && !ownershipChanged;
        }

    }
}
