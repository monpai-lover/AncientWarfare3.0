using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public static class WarRefugeeRules
    {
        public static int DeparturePermille(WarRefugeeThreatFacts pFacts,
            long pStableCityKey)
        {
            if (!pFacts.NearbyArmy && !pFacts.Siege &&
                !pFacts.CombatOrTransfer) return 0;
            int min = pFacts.Siege || pFacts.CombatOrTransfer ? 50 : 10;
            int max = pFacts.Siege || pFacts.CombatOrTransfer ? 100 : 30;
            if (pFacts.Famine) max = 150;
            return min + StableIndex(pStableCityKey, max - min + 1);
        }

        public static int DepartureQuota(int pPopulation, int pEligible,
            int pMinimumPopulation, int pCityBudget, int pWorldBudget,
            int pPermille)
        {
            int available = Math.Max(0, pPopulation - Math.Max(0, pMinimumPopulation));
            int calculated = (int)Math.Floor(Math.Max(0, pPopulation) *
                                              Math.Max(0, pPermille) / 1000d);
            return Math.Max(0, Math.Min(Math.Min(available, calculated),
                Math.Min(Math.Max(0, pCityBudget), Math.Max(0, pWorldBudget))));
        }

        public static bool IsEligibleCivilian(WarRefugeeActorFacts pFacts)
        {
            return pFacts.Alive && !pFacts.King && !pFacts.Heir &&
                   !pFacts.CentralOfficial && !pFacts.LocalOfficial &&
                   !pFacts.General && !pFacts.Warrior && !pFacts.RoyalGuard &&
                   !pFacts.JourneyActive;
        }

        public static bool CanReceive(WarRefugeeDestinationFacts pFacts,
            int pBatchSize)
        {
            return pFacts.Alive && pFacts.Relation != WarRefugeeRelation.Enemy &&
                   !pFacts.WarGoal && !pFacts.HostileArmy && !pFacts.Combat &&
                   !pFacts.Famine &&
                   pFacts.Food > 0 && pFacts.Housing >= pBatchSize &&
                   pFacts.Capacity >= pBatchSize;
        }

        public static int CompareDestinations(WarRefugeeDestinationFacts pLeft,
            WarRefugeeDestinationFacts pRight)
        {
            int relation = ((int)pRight.Relation).CompareTo((int)pLeft.Relation);
            if (relation != 0) return relation;
            int safety = SafetyScore(pRight).CompareTo(SafetyScore(pLeft));
            if (safety != 0) return safety;
            int distance = pLeft.Distance.CompareTo(pRight.Distance);
            return distance != 0 ? distance : pLeft.Id.CompareTo(pRight.Id);
        }

        public static long SelectLeader(IReadOnlyList<WarRefugeeLeaderCandidate> pCandidates)
        {
            if (pCandidates == null) return -1L;
            return pCandidates.Where(p => p.Alive && p.Adult)
                .Select(p => p.Id).DefaultIfEmpty(-1L).Min();
        }

        public static int AbstractArrivalMonth(int pDepartureMonth, int pDistance)
        {
            int duration = Math.Max(1, Math.Min(24,
                (int)Math.Ceiling(Math.Sqrt(Math.Max(0, pDistance)) / 7d)));
            return pDepartureMonth + duration;
        }

        public static bool ShouldUseAbstractJourney(bool pCrossSea,
            bool pReachable, int pRetries)
        {
            return pCrossSea || !pReachable && pRetries >= 3;
        }

        public static bool IsHouseholdArrived(int pActiveMembers, int pMembersInDestination)
        {
            return pActiveMembers > 0 && pMembersInDestination >= pActiveMembers;
        }

        public static bool ShouldAdvanceAfterNoProgress(int pRetries)
        {
            return pRetries >= 3;
        }

        public static int AvailableCapacityAfterReservations(int pReadableCapacity,
            int pActiveReservations)
        {
            return Math.Max(0, Math.Max(0, pReadableCapacity) -
                Math.Max(0, pActiveReservations));
        }

        public static bool ShouldReleaseReservation(WarRefugeeJourneyState pState)
        {
            return pState == WarRefugeeJourneyState.Arrived ||
                   pState == WarRefugeeJourneyState.Cancelled ||
                   pState == WarRefugeeJourneyState.Settled;
        }

        public static WarRefugeeJourneyState PostArrivalDecision(
            bool pOriginSafe, int pSafeMonths, bool pPreferReturn,
            bool pPreferSettlement)
        {
            if (!CanEvaluateReturn(pOriginSafe, pSafeMonths))
                return WarRefugeeJourneyState.Arrived;
            return pPreferReturn ? WarRefugeeJourneyState.Returning :
                pPreferSettlement ? WarRefugeeJourneyState.Settled :
                WarRefugeeJourneyState.Arrived;
        }

        public static bool IsHostNonXia(bool native, bool fullyXiaized)
        {
            return !native && !fullyXiaized;
        }

        public static bool IsThreateningMission(bool pAttack, bool pEnemy,
            long pMissionTargetCityId, long pCityId)
        {
            return pAttack && pEnemy && pMissionTargetCityId >= 0L &&
                   pMissionTargetCityId == pCityId;
        }

        public static int ReadableSpareCapacity(int pHousingCapacity,
            int pPopulation, int pAllReservations, int pOwnReservation)
        {
            int otherReservations = Math.Max(0, pAllReservations -
                Math.Max(0, pOwnReservation));
            return Math.Max(0, Math.Max(0, pHousingCapacity) -
                Math.Max(0, pPopulation) - otherReservations);
        }

        public static bool HasLeaderReachedDestination(int pDistanceSquared,
            int pArrivalRadiusSquared)
        {
            return pDistanceSquared >= 0 && pDistanceSquared <=
                   Math.Max(0, pArrivalRadiusSquared);
        }

        public static bool ShouldUseAbstractWithoutAdultLeader(int activeMembers,
            int livingAdults)
        {
            return activeMembers > 0 && livingAdults <= 0;
        }

        public static bool AcceptForeignHost(WarRefugeeRelation pRelation,
            int pFood, int pCapacity, int pBatchSize, long pOriginId,
            long pDestinationId)
        {
            if (pFood <= 0 || pCapacity < pBatchSize || pBatchSize <= 0)
                return false;
            if (pRelation == WarRefugeeRelation.Domestic ||
                pRelation == WarRefugeeRelation.ProtectedPartner) return true;
            if (pRelation == WarRefugeeRelation.Enemy) return false;
            int pressure = Math.Min(900, 350 + Math.Min(400,
                (pCapacity - pBatchSize) * 20));
            return StableIndex(pOriginId ^ (pDestinationId << 1) ^ pBatchSize,
                1000) < pressure;
        }

        public static bool ShouldSettleAfterOriginLoss(bool pOriginExists,
            int pMonthsSinceArrival)
        {
            return !pOriginExists && pMonthsSinceArrival >= 12;
        }

        public static bool ShouldSettleAfterProlongedDanger(bool pOriginSafe,
            int pMonthsSinceArrival)
        {
            return !pOriginSafe && pMonthsSinceArrival >= 60;
        }

        public static bool PreferReturn(WarRefugeeReturnFacts pFacts,
            long pJourneyId, int pCurrentMonth)
        {
            if (!pFacts.OriginSafe) return false;
            int home = pFacts.OriginProsperity +
                       Math.Min(60, pFacts.RelativesAtOrigin * 15) +
                       Math.Max(0, 30 - pFacts.ResidenceYears * 3);
            int host = pFacts.HostProsperity + (pFacts.HostSafe ? 15 : 0) +
                       pFacts.ResidenceYears * 5 +
                       (pFacts.LocalMarriage ? 30 : 0) +
                       (pFacts.HostBornChildren ? 30 : 0) +
                       (pFacts.EstablishedLivelihood ? 25 : 0);
            if (home != host) return home > host;
            return StableIndex(pJourneyId ^ ((long)pCurrentMonth << 24), 2) == 0;
        }

        public static bool CanEvaluateReturn(bool pOriginContinuouslySafe,
            int pSafeMonths)
        {
            return pOriginContinuouslySafe && pSafeMonths >= 12;
        }

        public static bool CanEvaluateAssimilation(bool pHostNonXia,
            int pResidenceYears, int pLastEvaluationYear, int pCurrentYear)
        {
            return pHostNonXia && pResidenceYears >= 5 &&
                   pCurrentYear > pLastEvaluationYear;
        }

        public static int AssimilationPermille(int pResidenceYears,
            bool pLocalMarriage, bool pHostBornChild, bool pEstablishedLivelihood)
        {
            int chance = Math.Max(0, pResidenceYears - 5) * 35;
            if (pLocalMarriage) chance += 100;
            if (pHostBornChild) chance += 100;
            if (pEstablishedLivelihood) chance += 125;
            return Math.Min(950, chance);
        }

        public static bool StableChance(long pActorId, int pYear,
            int pChancePermille)
        {
            int chance = Math.Max(0, Math.Min(1000, pChancePermille));
            return StableIndex(pActorId ^ ((long)pYear << 32), 1000) < chance;
        }

        private static int SafetyScore(WarRefugeeDestinationFacts pFacts)
        {
            return pFacts.Food + pFacts.Housing + pFacts.Capacity;
        }

        private static int StableIndex(long pKey, int pCount)
        {
            if (pCount <= 1) return 0;
            unchecked
            {
                long value = (pKey ^ (pKey >> 32)) * 1103515245L + 12345L;
                value = value == long.MinValue ? 0 : Math.Abs(value);
                return (int)(value % pCount);
            }
        }
    }
}
