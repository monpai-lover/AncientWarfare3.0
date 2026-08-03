using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class CityReservePoolRules
    {
        public const int PeaceCityBudget = 1;
        public const int PreparationCityBudget = 4;
        public const int PeaceActorBudget = 8;
        public const int PreparationActorBudget = 32;
        public const int ReserveExhaustionContribution = 20;

        public static int ResolveAvailableManpower(
            int authenticResidents, int authenticMobilized,
            int activeCitySourcedMilitary)
        {
            int authentic = CityManpowerRules.AuthenticPopulation(
                authenticResidents, authenticMobilized);
            return CityManpowerRules.NoticeHeadroom(authentic,
                activeCitySourcedMilitary);
        }

        public static bool CanConfirmManpowerExhausted(
            bool ledgerReady, int availableManpower)
        {
            return ledgerReady && availableManpower <= 0;
        }

        public static int Capacity(int eligibleCivilians, int percent)
        {
            long eligible = Math.Max(0, eligibleCivilians);
            long share = Math.Max(0, Math.Min(100, percent));
            if (eligible <= 0L || share <= 0L) return 0;

            long capacity = eligible * share / 100L;
            // Keep a non-empty reserve in small settlements.  The law still
            // uses its exact percentage for normal-sized pools, but integer
            // truncation must not turn every 1-3 person pool into zero.
            if (capacity <= 0L) capacity = 1L;
            return (int)Math.Min(int.MaxValue, capacity);
        }

        public static int CapacityForPreparation(int eligibleCivilians,
            int lawPercent, bool preparation)
        {
            // Preparation changes when we scan and mobilize the pool, not how
            // large the law permits the pool to become.  Keep the same
            // 30/50/70/100% capacity in both phases.
            return Capacity(eligibleCivilians, lawPercent);
        }

        public static bool ShouldAddForLawChange(bool frozen,
            int oldPercent, int newPercent)
        {
            return frozen && Math.Max(0, newPercent) >
                   Math.Max(0, oldPercent);
        }

        public static int RequiredRemovalCount(int memberCount, int capacity)
        {
            return Math.Max(0, Math.Max(0, memberCount) -
                               Math.Max(0, capacity));
        }

        public static bool CanEnroll(bool alive, bool adult,
            bool localResident, bool baseEligible, bool frozen,
            int memberCount, int capacity)
        {
            return alive && adult && localResident && baseEligible &&
                   !frozen && Math.Max(0, memberCount) <
                   Math.Max(0, capacity);
        }

        public static int CityBudget(bool preparation)
        {
            return preparation ? PreparationCityBudget : PeaceCityBudget;
        }

        public static int ActorBudget(bool preparation)
        {
            return preparation ? int.MaxValue : PeaceActorBudget;
        }

        public static int FullReconciliationBudget(int residentCount,
            int indexedMemberCount)
        {
            long total = (long)Math.Max(0, residentCount) +
                         Math.Max(0, indexedMemberCount);
            return (int)Math.Min(int.MaxValue, Math.Max(1L, total));
        }

        public static bool CanRestoreRejectedCandidate(bool sameKingdom,
            bool sameCity, bool alive, bool reserveEligible,
            bool enlistedIntoTargetArmy)
        {
            return sameKingdom && sameCity && alive && reserveEligible &&
                   !enlistedIntoTargetArmy;
        }

        public static bool MatchesSourceCity(long sourceCityId,
            long candidateCityId)
        {
            return sourceCityId >= 0L && sourceCityId == candidateCityId;
        }

        public static bool ShouldReconcileJoiningKingdom(bool warActive,
            bool liveKingdom)
        {
            return warActive && liveKingdom;
        }

        public static long ResolveWarEmergencyId(bool frozen,
            long currentEmergencyId, long requestedEmergencyId)
        {
            if (frozen && currentEmergencyId >= 0L)
                return currentEmergencyId;
            return requestedEmergencyId;
        }

        public static bool CanMaintain(bool frozen, bool worldDayChanged)
        {
            return !frozen && worldDayChanged;
        }

        public static bool ShouldUnfreeze(int activeWarCount)
        {
            return activeWarCount <= 0;
        }

        public static bool TryTakeNextActorId(SortedSet<long> actorIds,
            out long actorId)
        {
            actorId = -1L;
            if (actorIds == null || actorIds.Count == 0) return false;
            actorId = actorIds.Min;
            actorIds.Remove(actorId);
            return true;
        }

        public static bool CanConsumeFromCity(bool kingdomFrozen,
            bool realmControlled, int population)
        {
            return kingdomFrozen && realmControlled &&
                   WartimeRecruitmentPopulationRules.RecruitmentCapacity(
                       population, 1) > 0;
        }

        public static bool CanConsumeDuringPreparation(bool activeNotice,
            bool realmControlled, int population)
        {
            return activeNotice && realmControlled &&
                   WartimeRecruitmentPopulationRules.RecruitmentCapacity(
                       population, 1) > 0;
        }

        public static bool CanConsumeForMobilization(
            ArmyMobilizationPhase phase, bool realmControlled,
            int population)
        {
            return ArmyMobilizationRules.CanConsume(phase) &&
                   realmControlled &&
                   WartimeRecruitmentPopulationRules.RecruitmentCapacity(
                       population, 1) > 0;
        }

        public static bool CanConfirmExhausted(
            bool reconciliationComplete, int availableActorCount)
        {
            return ArmyMobilizationRules.ShouldConfirmExhausted(
                reconciliationComplete, availableActorCount);
        }

        public static bool CanConfirmExhausted(bool kingdomFrozen,
            bool allIndexedCitiesChecked, int remainingIndexedActors)
        {
            return kingdomFrozen && allIndexedCitiesChecked &&
                   Math.Max(0, remainingIndexedActors) == 0;
        }

        public static bool HasRemainingUsableActors(int registeredActors)
        {
            return registeredActors > 0;
        }

        public static bool ShouldDeferPersistedMemberValidation(
            bool hasPersistedMember, bool restoreValidationReady)
        {
            return hasPersistedMember && !restoreValidationReady;
        }

        public static bool ShouldDeferCallbackInvalidation(
            bool hasPersistedMember, bool restoreInFlight)
        {
            return hasPersistedMember && restoreInFlight;
        }

        public static bool ShouldApplyReserveExhaustion(
            bool attackAssignment, int reinforcementShortage,
            bool kingdomFrozen, bool exhaustionConfirmed,
            bool alreadyApplied)
        {
            return attackAssignment && reinforcementShortage > 0 &&
                   kingdomFrozen && exhaustionConfirmed && !alreadyApplied;
        }

        public static int ComposeExhaustion(int baseExhaustion,
            int reserveContribution)
        {
            return Math.Max(0, Math.Min(100,
                Math.Max(0, baseExhaustion) +
                Math.Max(0, reserveContribution)));
        }

        public static int ApplyReserveExhaustionContribution(
            int existingContribution)
        {
            return Math.Max(Math.Max(0, existingContribution),
                ReserveExhaustionContribution);
        }

    }
}
