using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class KingdomWarDirectorWorkRules
    {
        public const int MaximumWarPlansPerWorkItem = 8;
        public const int MaximumSelectedWarPlans = 4;
        public const int MaximumArmiesPerWorkItem = 8;
        public const int MaximumFrontCitiesPerWorkItem = 32;
        public const int MaximumFrontParticipantsPerWorkItem = 16;
        public const int RefreshWorldDays = 30;

        public static long NextPeriodicWorldDay(long pKingdomId,
            long pCurrentWorldDay)
        {
            long current = Math.Max(0L, pCurrentWorldDay);
            long slot = PositiveModulo(pKingdomId, RefreshWorldDays);
            long currentSlot = current % RefreshWorldDays;
            long delta = (slot - currentSlot + RefreshWorldDays) %
                         RefreshWorldDays;
            if (delta == 0L) delta = RefreshWorldDays;
            return current + delta;
        }

        private static long PositiveModulo(long pValue, int pDivisor)
        {
            long value = pValue % pDivisor;
            return value < 0L ? value + pDivisor : value;
        }
    }

    public sealed class KingdomWarDirectorWorkQueue
    {
        private readonly Queue<long> _ready = new Queue<long>();
        private readonly HashSet<long> _queued = new HashSet<long>();
        private readonly SortedDictionary<long, SortedSet<long>>
            _periodicByDay =
                new SortedDictionary<long, SortedSet<long>>();
        private readonly Dictionary<long, long> _periodicDayByKingdom =
            new Dictionary<long, long>();

        public bool MarkDirty(long pKingdomId)
        {
            if (pKingdomId < 0L || !_queued.Add(pKingdomId)) return false;
            _ready.Enqueue(pKingdomId);
            return true;
        }

        public long SchedulePeriodic(long pKingdomId,
            long currentWorldDay)
        {
            if (pKingdomId < 0L) return -1L;
            long dueDay = KingdomWarDirectorWorkRules.
                NextPeriodicWorldDay(pKingdomId, currentWorldDay);
            RemovePeriodic(pKingdomId);
            if (!_periodicByDay.TryGetValue(dueDay,
                    out SortedSet<long> kingdoms))
            {
                kingdoms = new SortedSet<long>();
                _periodicByDay[dueDay] = kingdoms;
            }
            kingdoms.Add(pKingdomId);
            _periodicDayByKingdom[pKingdomId] = dueDay;
            return dueDay;
        }

        public bool TryTake(long currentWorldDay, out long pKingdomId)
        {
            PromoteOnePeriodic(Math.Max(0L, currentWorldDay));
            if (_ready.Count == 0)
            {
                pKingdomId = -1L;
                return false;
            }
            pKingdomId = _ready.Dequeue();
            _queued.Remove(pKingdomId);
            return true;
        }

        public void Clear()
        {
            _ready.Clear();
            _queued.Clear();
            _periodicByDay.Clear();
            _periodicDayByKingdom.Clear();
        }

        private void PromoteOnePeriodic(long pCurrentWorldDay)
        {
            long dueDay = -1L;
            SortedSet<long> dueKingdoms = null;
            foreach (KeyValuePair<long, SortedSet<long>> pair in
                     _periodicByDay)
            {
                if (pair.Key > pCurrentWorldDay) break;
                dueDay = pair.Key;
                dueKingdoms = pair.Value;
                break;
            }
            if (dueKingdoms == null || dueKingdoms.Count == 0) return;

            long kingdomId = -1L;
            using (SortedSet<long>.Enumerator enumerator =
                   dueKingdoms.GetEnumerator())
            {
                if (enumerator.MoveNext()) kingdomId = enumerator.Current;
            }
            if (kingdomId < 0L) return;
            dueKingdoms.Remove(kingdomId);
            if (dueKingdoms.Count == 0) _periodicByDay.Remove(dueDay);
            if (_periodicDayByKingdom.TryGetValue(kingdomId,
                    out long scheduledDay) && scheduledDay == dueDay)
                _periodicDayByKingdom.Remove(kingdomId);
            MarkDirty(kingdomId);
        }

        private void RemovePeriodic(long pKingdomId)
        {
            if (!_periodicDayByKingdom.TryGetValue(pKingdomId,
                    out long dueDay)) return;
            _periodicDayByKingdom.Remove(pKingdomId);
            if (!_periodicByDay.TryGetValue(dueDay,
                    out SortedSet<long> kingdoms)) return;
            kingdoms.Remove(pKingdomId);
            if (kingdoms.Count == 0) _periodicByDay.Remove(dueDay);
        }
    }

    public sealed class WarAllocationFacts
    {
        public WarAllocationFacts(long warId, bool capitalThreat,
            bool warGoalThreat, int signedWarScore, int requiredArmies,
            bool localTerritoryThreat = false)
        {
            WarId = warId;
            CapitalThreat = capitalThreat;
            WarGoalThreat = warGoalThreat;
            SignedWarScore = signedWarScore;
            RequiredArmies = Math.Max(0, requiredArmies);
            LocalTerritoryThreat = localTerritoryThreat;
        }

        public long WarId { get; }
        public bool CapitalThreat { get; }
        public bool WarGoalThreat { get; }
        public int SignedWarScore { get; }
        public int RequiredArmies { get; }
        public bool LocalTerritoryThreat { get; }
    }

    public sealed class ArmyAllocationFacts
    {
        public ArmyAllocationFacts(long armyId, int effectiveForce)
        {
            ArmyId = armyId;
            EffectiveForce = Math.Max(0, effectiveForce);
        }

        public long ArmyId { get; }
        public int EffectiveForce { get; }
    }

    public sealed class WarArmyAssignment
    {
        public WarArmyAssignment(long pArmyId, long pWarId,
            ArmyRtsRole pRole)
        {
            ArmyId = pArmyId;
            WarId = pWarId;
            Role = pRole;
        }

        public long ArmyId { get; }
        public long WarId { get; }
        public ArmyRtsRole Role { get; }
    }

    public sealed class FrontTargetFacts
    {
        public FrontTargetFacts(long cityId, bool frozenFriendly,
            bool formalWarGoal, bool enemyCapital,
            bool connectedCorridor, bool exposedSecondary,
            int distanceSquared, int enemyForce = 0,
            int targetX = int.MinValue, int targetY = int.MinValue)
            : this(cityId, frozenFriendly, formalWarGoal, enemyCapital,
                connectedCorridor, transportReachable: false,
                exposedSecondary, distanceSquared, enemyForce,
                targetX, targetY)
        {
        }

        public FrontTargetFacts(long cityId, bool frozenFriendly,
            bool formalWarGoal, bool enemyCapital,
            bool connectedCorridor, bool transportReachable,
            bool exposedSecondary, int distanceSquared, int enemyForce = 0,
            int targetX = int.MinValue, int targetY = int.MinValue)
            : this(cityId, frozenFriendly, formalWarGoal, enemyCapital,
                connectedCorridor, landReachable: connectedCorridor,
                transportReachable, exposedSecondary, distanceSquared,
                enemyForce, targetX, targetY)
        {
        }

        public FrontTargetFacts(long cityId, bool frozenFriendly,
            bool formalWarGoal, bool enemyCapital,
            bool connectedCorridor, bool landReachable,
            bool transportReachable, bool exposedSecondary,
            int distanceSquared, int enemyForce = 0,
            int targetX = int.MinValue, int targetY = int.MinValue,
            bool defensiveObjective = false)
        {
            CityId = cityId;
            FrozenFriendly = frozenFriendly;
            FormalWarGoal = formalWarGoal;
            EnemyCapital = enemyCapital;
            ConnectedCorridor = connectedCorridor;
            LandReachable = landReachable || connectedCorridor;
            TransportReachable = transportReachable;
            DefensiveObjective = defensiveObjective || frozenFriendly;
            ExposedSecondary = exposedSecondary;
            DistanceSquared = Math.Max(0, distanceSquared);
            EnemyForce = Math.Max(0, enemyForce);
            TargetX = targetX;
            TargetY = targetY;
        }

        public long CityId { get; }
        public bool FrozenFriendly { get; }
        public bool FormalWarGoal { get; }
        public bool EnemyCapital { get; }
        public bool ConnectedCorridor { get; }
        public bool LandReachable { get; }
        public bool TransportReachable { get; }
        public bool DefensiveObjective { get; }
        public bool OperationallyReachable =>
            LandReachable || TransportReachable;
        public bool ExposedSecondary { get; }
        public int DistanceSquared { get; }
        public int EnemyForce { get; }
        public int TargetX { get; }
        public int TargetY { get; }
        public bool HasPosition =>
            TargetX != int.MinValue && TargetY != int.MinValue;
    }

    public sealed class FrontArmyFacts
    {
        public FrontArmyFacts(long armyId, int effectiveForce,
            long currentTargetCityId, int requiredAttackRatioBasisPoints,
            int armyX = int.MinValue, int armyY = int.MinValue)
        {
            ArmyId = armyId;
            EffectiveForce = Math.Max(0, effectiveForce);
            CurrentTargetCityId = currentTargetCityId;
            RequiredAttackRatioBasisPoints = Math.Max(
                KingdomWarDirectorRules.MinimumAttackRatioBasisPoints,
                Math.Min(
                    KingdomWarDirectorRules.MaximumAttackRatioBasisPoints,
                    requiredAttackRatioBasisPoints));
            ArmyX = armyX;
            ArmyY = armyY;
        }

        public long ArmyId { get; }
        public int EffectiveForce { get; }
        public long CurrentTargetCityId { get; }
        public int RequiredAttackRatioBasisPoints { get; }
        public int ArmyX { get; }
        public int ArmyY { get; }
        public bool HasPosition =>
            ArmyX != int.MinValue && ArmyY != int.MinValue;
    }

    public sealed class FrontTargetAssignment
    {
        public FrontTargetAssignment(long armyId, long targetCityId,
            int friendlyForce, int requiredForce, bool forceReady,
            bool friendlyDefense = false)
        {
            ArmyId = armyId;
            TargetCityId = targetCityId;
            FriendlyForce = Math.Max(0, friendlyForce);
            RequiredForce = Math.Max(0, requiredForce);
            ForceReady = forceReady;
            FriendlyDefense = friendlyDefense;
        }

        public long ArmyId { get; }
        public long TargetCityId { get; }
        public int FriendlyForce { get; }
        public int RequiredForce { get; }
        public bool ForceReady { get; }
        public bool FriendlyDefense { get; }
    }

    public sealed class ArmyAttackThresholdFacts
    {
        public bool AggressiveRuler { get; set; }
        public bool ExpansionPhase { get; set; }
        public bool GoodSupply { get; set; }
        public bool LowSupply { get; set; }
        public bool CautiousRuler { get; set; }
        public bool CorruptPhase { get; set; }
        public bool Fatigued { get; set; }
        public bool LongDistance { get; set; }
        public bool PoorOrganization { get; set; }
    }

    public static class KingdomWarDirectorRules
    {
        public const int BaseAttackRatioBasisPoints = 125;
        public const int MinimumAttackRatioBasisPoints = 105;
        public const int MaximumAttackRatioBasisPoints = 160;

        public static IReadOnlyList<WarAllocationFacts> SelectWarPlans(
            IReadOnlyList<WarAllocationFacts> pWars, int maximumPlans)
        {
            int limit = Math.Max(0, maximumPlans);
            var wars = new List<WarAllocationFacts>();
            if (pWars != null)
                for (int i = 0; i < pWars.Count; i++)
                {
                    WarAllocationFacts war = pWars[i];
                    if (war != null && war.WarId >= 0 &&
                        war.RequiredArmies > 0) wars.Add(war);
                }
            wars.Sort(CompareWars);
            if (wars.Count > limit) wars.RemoveRange(limit,
                wars.Count - limit);
            return wars;
        }

        public static IReadOnlyList<WarAllocationFacts> MergeTopWarPlans(
            IReadOnlyList<WarAllocationFacts> pRetained,
            IReadOnlyList<WarAllocationFacts> pBatch, int maximumPlans)
        {
            var merged = new List<WarAllocationFacts>(
                (pRetained?.Count ?? 0) + (pBatch?.Count ?? 0));
            if (pRetained != null)
                for (int i = 0; i < pRetained.Count; i++)
                    merged.Add(pRetained[i]);
            if (pBatch != null)
                for (int i = 0; i < pBatch.Count; i++)
                    merged.Add(pBatch[i]);
            return SelectWarPlans(merged, maximumPlans);
        }

        public static IReadOnlyList<WarArmyAssignment> AllocateWars(
            IReadOnlyList<WarAllocationFacts> pWars,
            IReadOnlyList<ArmyAllocationFacts> pArmies)
        {
            var wars = new List<WarAllocationFacts>();
            if (pWars != null)
            {
                for (int i = 0; i < pWars.Count; i++)
                {
                    WarAllocationFacts war = pWars[i];
                    if (war != null && war.WarId >= 0 &&
                        war.RequiredArmies > 0) wars.Add(war);
                }
            }
            wars.Sort(CompareWars);

            var armies = new List<ArmyAllocationFacts>();
            if (pArmies != null)
            {
                for (int i = 0; i < pArmies.Count; i++)
                {
                    ArmyAllocationFacts army = pArmies[i];
                    if (army != null && army.ArmyId >= 0 &&
                        army.EffectiveForce > 0)
                        armies.Add(army);
                }
            }
            armies.Sort(CompareArmies);

            var result = new List<WarArmyAssignment>(armies.Count);
            if (wars.Count == 0 || armies.Count == 0) return result;
            var assignedCounts = new int[wars.Count];
            int armyIndex = 0;
            AssignRequiredArmies(wars, armies, assignedCounts, result,
                ref armyIndex, pEmergencyWars: true);
            AssignRequiredArmies(wars, armies, assignedCounts, result,
                ref armyIndex, pEmergencyWars: false);

            while (armyIndex < armies.Count)
            {
                for (int i = 0; i < wars.Count && armyIndex < armies.Count;
                     i++)
                {
                    ArmyAllocationFacts army = armies[armyIndex++];
                    result.Add(new WarArmyAssignment(army.ArmyId,
                        wars[i].WarId, ArmyRtsRole.Assault));
                }
            }
            return result;
        }

        private static void AssignRequiredArmies(
            IReadOnlyList<WarAllocationFacts> pWars,
            IReadOnlyList<ArmyAllocationFacts> pArmies,
            int[] pAssignedCounts, List<WarArmyAssignment> pResult,
            ref int pArmyIndex, bool pEmergencyWars)
        {
            bool assignedInRound = true;
            while (pArmyIndex < pArmies.Count && assignedInRound)
            {
                assignedInRound = false;
                for (int i = 0;
                     i < pWars.Count && pArmyIndex < pArmies.Count; i++)
                {
                    WarAllocationFacts war = pWars[i];
                    bool emergency = war.CapitalThreat ||
                                     war.LocalTerritoryThreat;
                    if (emergency != pEmergencyWars ||
                        pAssignedCounts[i] >= war.RequiredArmies) continue;
                    ArmyAllocationFacts army = pArmies[pArmyIndex++];
                    bool capitalDefense = war.CapitalThreat &&
                                          pAssignedCounts[i] == 0;
                    pResult.Add(new WarArmyAssignment(army.ArmyId, war.WarId,
                        capitalDefense
                            ? ArmyRtsRole.Defense
                            : ArmyRtsRole.Assault));
                    pAssignedCounts[i]++;
                    assignedInRound = true;
                }
            }
        }

        public static bool ShouldRequestDepletedArmyRecovery(
            int unitCount, bool captainAlive, bool royalGuard,
            bool dedicatedGarrison, bool canCommit = true)
        {
            return canCommit && captainAlive && !royalGuard &&
                   !dedicatedGarrison &&
                   unitCount < ArmyLogisticsRules.MinimumOperationalForce;
        }

        public static bool ShouldRequestMissingCaptainRecovery(
            int unitCount, bool captainAlive, bool royalGuard,
            bool dedicatedGarrison)
        {
            return unitCount > 0 && !captainAlive && !royalGuard &&
                   !dedicatedGarrison;
        }

        public static bool ShouldAllocateFieldArmy(int unitCount,
            bool captainAlive, bool royalGuard, bool dedicatedGarrison,
            bool specialArmy)
        {
            return ArmyLogisticsRules.HasMinimumOperationalForce(unitCount) &&
                   captainAlive && !royalGuard && !dedicatedGarrison &&
                   !specialArmy;
        }

        public static int SelectBestTargetIndex(
            IReadOnlyList<FrontTargetFacts> pTargets)
        {
            if (pTargets == null) return -1;
            int bestIndex = -1;
            int bestTier = int.MinValue;
            int bestDistance = int.MaxValue;
            long bestCityId = long.MaxValue;
            for (int i = 0; i < pTargets.Count; i++)
            {
                FrontTargetFacts target = pTargets[i];
                if (target == null || target.CityId < 0 ||
                    !target.OperationallyReachable) continue;
                int tier = TargetPriority(target);
                if (tier < bestTier ||
                    tier == bestTier &&
                    target.DistanceSquared > bestDistance ||
                    tier == bestTier &&
                    target.DistanceSquared == bestDistance &&
                    target.CityId >= bestCityId) continue;
                bestIndex = i;
                bestTier = tier;
                bestDistance = target.DistanceSquared;
                bestCityId = target.CityId;
            }
            return bestIndex;
        }

        public static IReadOnlyList<FrontTargetFacts> RetainFrontTargets(
            IReadOnlyList<FrontTargetFacts> pTargets, int maximumTargets)
        {
            int limit = Math.Max(0, maximumTargets);
            var targets = new List<FrontTargetFacts>();
            if (pTargets != null)
                for (int i = 0; i < pTargets.Count; i++)
                {
                    FrontTargetFacts target = pTargets[i];
                    if (target != null && target.CityId >= 0L &&
                        target.OperationallyReachable) targets.Add(target);
                }
            targets.Sort(CompareRetainedTargets);
            if (targets.Count > limit)
                targets.RemoveRange(limit, targets.Count - limit);
            return targets;
        }

        public static IReadOnlyList<FrontTargetAssignment>
            AssignFrontTargets(IReadOnlyList<FrontArmyFacts> pArmies,
                IReadOnlyList<FrontTargetFacts> pTargets,
                IReadOnlyDictionary<long, int>
                    pExistingAssaultReservations = null)
        {
            var armies = new List<FrontArmyFacts>();
            if (pArmies != null)
                for (int i = 0; i < pArmies.Count; i++)
                {
                    FrontArmyFacts army = pArmies[i];
                    if (army != null && army.ArmyId >= 0L &&
                        army.EffectiveForce > 0) armies.Add(army);
                }
            armies.Sort(CompareFrontArmies);

            var targets = new List<FrontTargetFacts>();
            if (pTargets != null)
                for (int i = 0; i < pTargets.Count; i++)
                {
                    FrontTargetFacts target = pTargets[i];
                    if (target != null && target.CityId >= 0L &&
                        target.OperationallyReachable) targets.Add(target);
                }
            bool connectedLandAvailable = false;
            for (int i = 0; i < targets.Count; i++)
                if (IsLandObjective(targets[i]))
                {
                    connectedLandAvailable = true;
                    break;
                }
            if (connectedLandAvailable)
                targets.RemoveAll(target => !IsLandObjective(target));

            var result = new List<FrontTargetAssignment>(armies.Count);
            var reservedForce = new Dictionary<long, int>();
            var reservedCount = new Dictionary<long, int>();
            if (pExistingAssaultReservations != null)
                foreach (KeyValuePair<long, int> pair in
                         pExistingAssaultReservations)
                    if (pair.Key >= 0L && pair.Value > 0)
                        reservedCount[pair.Key] = pair.Value;
            for (int i = 0; i < armies.Count; i++)
            {
                FrontArmyFacts army = armies[i];
                int targetIndex = SelectLeasedTarget(army, targets);
                if (targetIndex < 0)
                    targetIndex = SelectAssignmentTarget(army, targets,
                        reservedForce, reservedCount);
                if (targetIndex < 0) continue;
                FrontTargetFacts target = targets[targetIndex];
                reservedForce.TryGetValue(target.CityId,
                    out int previousForce);
                reservedCount.TryGetValue(target.CityId,
                    out int previousCount);
                int friendlyForce = SaturatingAdd(previousForce,
                    army.EffectiveForce);
                int assignmentRatio = AssignmentRatio(army, target);
                int requiredForce = RequiredForce(target.EnemyForce,
                    assignmentRatio);
                bool forceReady = CanLaunchAttack(friendlyForce,
                    target.EnemyForce, assignmentRatio,
                    survivalException: false);
                reservedForce[target.CityId] = friendlyForce;
                reservedCount[target.CityId] = previousCount + 1;
                result.Add(new FrontTargetAssignment(army.ArmyId,
                    target.CityId, friendlyForce, requiredForce,
                    forceReady, target.DefensiveObjective));
            }
            return result;
        }

        private static int SelectLeasedTarget(FrontArmyFacts pArmy,
            IReadOnlyList<FrontTargetFacts> pTargets)
        {
            if (pArmy == null || pArmy.CurrentTargetCityId < 0L ||
                pTargets == null) return -1;
            int currentIndex = -1;
            bool homelandEmergencyAvailable = false;
            for (int i = 0; i < pTargets.Count; i++)
            {
                FrontTargetFacts target = pTargets[i];
                if (target == null || !target.OperationallyReachable)
                    continue;
                if (target.FrozenFriendly)
                    homelandEmergencyAvailable = true;
                if (target.CityId == pArmy.CurrentTargetCityId)
                    currentIndex = i;
            }
            if (currentIndex < 0) return -1;
            return homelandEmergencyAvailable &&
                   !pTargets[currentIndex].FrozenFriendly
                ? -1
                : currentIndex;
        }

        public static int RequiredAttackRatioBasisPoints(
            ArmyAttackThresholdFacts pFacts)
        {
            if (pFacts == null) return BaseAttackRatioBasisPoints;
            int ratio = BaseAttackRatioBasisPoints;
            if (pFacts.AggressiveRuler) ratio -= 10;
            if (pFacts.ExpansionPhase) ratio -= 10;
            if (pFacts.GoodSupply) ratio -= 5;
            if (pFacts.LowSupply) ratio += 10;
            if (pFacts.CautiousRuler) ratio += 10;
            if (pFacts.CorruptPhase) ratio += 10;
            if (pFacts.Fatigued) ratio += 5;
            if (pFacts.LongDistance) ratio += 5;
            if (pFacts.PoorOrganization) ratio += 5;
            return Math.Max(MinimumAttackRatioBasisPoints,
                Math.Min(MaximumAttackRatioBasisPoints, ratio));
        }

        public static bool CanLaunchAttack(int friendlyForce,
            int enemyForce, int requiredRatioBasisPoints,
            bool survivalException)
        {
            int friendly = Math.Max(0, friendlyForce);
            int enemy = Math.Max(0, enemyForce);
            if (friendly <= 0) return false;
            if (survivalException || enemy == 0) return true;
            int ratio = Math.Max(MinimumAttackRatioBasisPoints,
                Math.Min(MaximumAttackRatioBasisPoints,
                    requiredRatioBasisPoints));
            return (long)friendly * 100L >= (long)enemy * ratio;
        }

        public static ArmyRtsRole ResolveMissionRole(
            ArmyRtsRole pAllocatedRole, bool hasStrategicTarget,
            bool forceReady, bool friendlyDefenseTarget = false)
        {
            if (pAllocatedRole == ArmyRtsRole.Defense ||
                friendlyDefenseTarget)
                return ArmyRtsRole.Defense;
            if (!hasStrategicTarget) return ArmyRtsRole.Reserve;
            return forceReady
                ? ArmyRtsRole.Assault
                : ArmyRtsRole.Reinforcement;
        }

        public static bool ShouldAdmitFriendlyDefenseTarget(
            bool frozenControlledByEnemy, bool activelyCapturedByEnemy)
        {
            return frozenControlledByEnemy || activelyCapturedByEnemy;
        }

        public static bool ShouldAdmitFrontTarget(
            ArmyRtsObjectiveState pState)
        {
            return pState == ArmyRtsObjectiveState.OpenAttack ||
                   pState == ArmyRtsObjectiveState.OpenDefense;
        }

        public static bool ShouldPublishFriendlyThreatTransition(
            long previousCapturerId, long currentCapturerId)
        {
            return previousCapturerId != currentCapturerId;
        }

        public static bool ShouldUsePlannedFrontTarget(
            bool friendlyTarget, bool friendlyTargetThreatened,
            bool ownerAtWarWithParticipant,
            bool controlledByParticipantSide)
        {
            return friendlyTarget
                ? friendlyTargetThreatened
                : ownerAtWarWithParticipant &&
                  !controlledByParticipantSide;
        }

        public static bool ShouldRetainMissionLease(
            bool samePhysicalObjective, bool currentMissionValid,
            bool currentTargetComplete, bool currentTargetCoolingDown,
            bool currentRetreat, bool currentHomelandEmergency,
            bool proposedHomelandEmergency, bool currentFrontHold)
        {
            if (samePhysicalObjective || !currentMissionValid ||
                currentTargetComplete || currentTargetCoolingDown)
                return false;
            if (currentFrontHold) return false;
            if (proposedHomelandEmergency && !currentHomelandEmergency)
                return false;
            if (currentRetreat) return true;
            return !proposedHomelandEmergency ||
                   currentHomelandEmergency;
        }

        private static int CompareWars(WarAllocationFacts pLeft,
            WarAllocationFacts pRight)
        {
            int priority = WarPriority(pRight).CompareTo(WarPriority(pLeft));
            return priority != 0
                ? priority
                : pLeft.WarId.CompareTo(pRight.WarId);
        }

        private static int CompareArmies(ArmyAllocationFacts pLeft,
            ArmyAllocationFacts pRight)
        {
            int force = pRight.EffectiveForce.CompareTo(pLeft.EffectiveForce);
            return force != 0
                ? force
                : pLeft.ArmyId.CompareTo(pRight.ArmyId);
        }

        private static int CompareFrontArmies(FrontArmyFacts pLeft,
            FrontArmyFacts pRight)
        {
            int force = pRight.EffectiveForce.CompareTo(
                pLeft.EffectiveForce);
            return force != 0
                ? force
                : pLeft.ArmyId.CompareTo(pRight.ArmyId);
        }

        private static int SelectAssignmentTarget(FrontArmyFacts pArmy,
            IReadOnlyList<FrontTargetFacts> pTargets,
            IReadOnlyDictionary<long, int> pReservedForce,
            IReadOnlyDictionary<long, int> pReservedCount)
        {
            int bestReady = -1;
            int bestGather = -1;
            int bestOverflow = -1;
            for (int i = 0; i < pTargets.Count; i++)
            {
                FrontTargetFacts target = pTargets[i];
                pReservedForce.TryGetValue(target.CityId,
                    out int reserved);
                pReservedCount.TryGetValue(target.CityId,
                    out int assignedArmies);
                if (bestOverflow < 0 || IsBetterOverflowTarget(pArmy,
                        target, pTargets[bestOverflow], assignedArmies,
                        ReservedCount(pReservedCount,
                            pTargets[bestOverflow].CityId)))
                    bestOverflow = i;
                if (assignedArmies >= ArmyRtsRules.AssaultReservationCap(
                        target.EnemyCapital, target.FormalWarGoal) &&
                    !target.FrozenFriendly) continue;
                int required = RequiredForce(target.EnemyForce,
                    AssignmentRatio(pArmy, target));
                if (reserved >= required && reserved > 0) continue;
                int projected = SaturatingAdd(reserved,
                    pArmy.EffectiveForce);
                bool ready = projected >= required;
                if (ready)
                {
                    if (bestReady < 0 || IsBetterAssignmentTarget(pArmy,
                            target, pTargets[bestReady], reserved,
                            ReservedForce(pReservedForce,
                                pTargets[bestReady].CityId)))
                        bestReady = i;
                    continue;
                }
                if (bestGather < 0 || IsBetterGatherTarget(pArmy, target,
                        pTargets[bestGather], reserved,
                        ReservedForce(pReservedForce,
                            pTargets[bestGather].CityId)))
                    bestGather = i;
            }
            if (bestReady >= 0 && bestGather >= 0)
                return pTargets[bestGather].FrozenFriendly &&
                       !pTargets[bestReady].FrozenFriendly
                    ? bestGather
                    : bestReady;
            if (bestReady >= 0) return bestReady;
            return bestGather >= 0 ? bestGather : bestOverflow;
        }

        private static bool IsBetterOverflowTarget(FrontArmyFacts pArmy,
            FrontTargetFacts pCandidate, FrontTargetFacts pCurrent,
            int pCandidateAssigned, int pCurrentAssigned)
        {
            int candidateTier = TargetPriority(pCandidate);
            int currentTier = TargetPriority(pCurrent);
            if (candidateTier != currentTier)
                return candidateTier > currentTier;
            if (pCandidateAssigned != pCurrentAssigned)
                return pCandidateAssigned < pCurrentAssigned;
            return IsBetterAssignmentTarget(pArmy, pCandidate, pCurrent,
                pCandidateAssigned, pCurrentAssigned);
        }

        private static bool IsBetterAssignmentTarget(FrontArmyFacts pArmy,
            FrontTargetFacts pCandidate, FrontTargetFacts pCurrent,
            int pCandidateReserved, int pCurrentReserved)
        {
            int candidatePriority = TargetPriority(pCandidate);
            int currentPriority = TargetPriority(pCurrent);
            if (candidatePriority != currentPriority)
                return candidatePriority > currentPriority;
            return CompareTargetTieBreak(pArmy, pCandidate, pCurrent);
        }

        private static bool IsBetterGatherTarget(FrontArmyFacts pArmy,
            FrontTargetFacts pCandidate, FrontTargetFacts pCurrent,
            int pCandidateReserved, int pCurrentReserved)
        {
            int candidateTier = TargetPriority(pCandidate);
            int currentTier = TargetPriority(pCurrent);
            if (candidateTier != currentTier)
                return candidateTier > currentTier;
            bool candidateStarted = pCandidateReserved > 0;
            bool currentStarted = pCurrentReserved > 0;
            if (candidateStarted != currentStarted) return candidateStarted;
            return IsBetterAssignmentTarget(pArmy, pCandidate, pCurrent,
                pCandidateReserved, pCurrentReserved);
        }

        private static bool CompareTargetTieBreak(FrontArmyFacts pArmy,
            FrontTargetFacts pCandidate, FrontTargetFacts pCurrent)
        {
            int candidateDistance = DistanceSquared(pArmy, pCandidate);
            int currentDistance = DistanceSquared(pArmy, pCurrent);
            if (candidateDistance != currentDistance)
                return candidateDistance < currentDistance;
            return pCandidate.CityId < pCurrent.CityId;
        }

        private static int CompareRetainedTargets(FrontTargetFacts pLeft,
            FrontTargetFacts pRight)
        {
            int priority = TargetPriority(pRight).CompareTo(
                TargetPriority(pLeft));
            if (priority != 0) return priority;
            int distance = pLeft.DistanceSquared.CompareTo(
                pRight.DistanceSquared);
            if (distance != 0) return distance;
            return pLeft.CityId.CompareTo(pRight.CityId);
        }

        private static int AssignmentRatio(FrontArmyFacts pArmy,
            FrontTargetFacts pTarget)
        {
            int ratio = pArmy.RequiredAttackRatioBasisPoints;
            if (DistanceSquared(pArmy, pTarget) >= 14_400) ratio += 5;
            return Math.Max(MinimumAttackRatioBasisPoints,
                Math.Min(MaximumAttackRatioBasisPoints, ratio));
        }

        private static int RequiredForce(int pEnemyForce,
            int pRequiredRatioBasisPoints)
        {
            int enemy = Math.Max(0, pEnemyForce);
            if (enemy == 0) return 1;
            int ratio = Math.Max(MinimumAttackRatioBasisPoints,
                Math.Min(MaximumAttackRatioBasisPoints,
                    pRequiredRatioBasisPoints));
            long required = ((long)enemy * ratio + 99L) / 100L;
            return required >= int.MaxValue ? int.MaxValue : (int)required;
        }

        private static int ReservedForce(
            IReadOnlyDictionary<long, int> pReservedForce, long pCityId)
        {
            return pReservedForce.TryGetValue(pCityId, out int value)
                ? value
                : 0;
        }

        private static int ReservedCount(
            IReadOnlyDictionary<long, int> pReservedCount, long pCityId)
        {
            return pReservedCount.TryGetValue(pCityId, out int value)
                ? value
                : 0;
        }

        private static int SaturatingAdd(int pLeft, int pRight)
        {
            long value = (long)Math.Max(0, pLeft) + Math.Max(0, pRight);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private static int WarPriority(WarAllocationFacts pWar)
        {
            int priority = pWar.CapitalThreat ? 100_000 : 0;
            if (pWar.LocalTerritoryThreat) priority += 80_000;
            if (pWar.WarGoalThreat) priority += 20_000;
            if (pWar.SignedWarScore < 0)
                priority += Math.Min(10_000, -pWar.SignedWarScore * 100);
            return priority;
        }

        private static int TargetPriority(FrontTargetFacts pTarget)
        {
            if (pTarget.FrozenFriendly) return 4;
            if (pTarget.ConnectedCorridor) return 3;
            if (pTarget.LandReachable) return 2;
            if (pTarget.TransportReachable) return 1;
            return 0;
        }

        private static bool IsLandObjective(FrontTargetFacts pTarget)
        {
            return pTarget != null &&
                   (pTarget.FrozenFriendly || pTarget.ConnectedCorridor);
        }

        private static int DistanceSquared(FrontArmyFacts pArmy,
            FrontTargetFacts pTarget)
        {
            if (pArmy == null || pTarget == null || !pArmy.HasPosition ||
                !pTarget.HasPosition) return pTarget?.DistanceSquared ??
                                            int.MaxValue;
            long x = (long)pArmy.ArmyX - pTarget.TargetX;
            long y = (long)pArmy.ArmyY - pTarget.TargetY;
            long distance = x * x + y * y;
            return distance >= int.MaxValue ? int.MaxValue : (int)distance;
        }
    }
}
