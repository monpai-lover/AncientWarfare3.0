namespace AncientWarfare3.core.lineage
{
    public static class TemporaryLevyRules
    {
        public const int MaxWorkItemsPerKingdomYear = 16;
        public const int MaxCandidatesPerWorkItem = 16;
        public const int MaxRecruitsPerWorkItem = 8;
        public const int MaxCandidatesPerKingdomYear = 256;
        public const int MaxRecruitsPerKingdomYear = 128;
        public const int DemobilizationBatchSize = 8;
        public const float MaximumEnlistmentAge = 65f;
        public const int ReinforcementsPerCasualty = 2;
        public const int MaxPendingCasualtyReinforcements = 256;
        // A queued request can reach 256 members. At eight recruits per
        // deferred work item, its final batch must remain reachable.
        public const int MaxCasualtyReinforcementWorkItems =
            (MaxPendingCasualtyReinforcements +
             MaxRecruitsPerWorkItem - 1) / MaxRecruitsPerWorkItem;

        public static bool ShouldRunRecruitmentWorkItem(bool emergencyActive,
            int completedWorkItems, int scannedCandidates, int recruitedActors)
        {
            return emergencyActive &&
                   completedWorkItems < MaxWorkItemsPerKingdomYear &&
                   scannedCandidates < MaxCandidatesPerKingdomYear &&
                   recruitedActors < MaxRecruitsPerKingdomYear;
        }

        public static int ClampRestoredCounter(int pValue, int pMaximum)
        {
            return System.Math.Max(0, System.Math.Min(System.Math.Max(0, pMaximum), pValue));
        }

        public static int AddCasualtyReinforcementDemand(int currentDemand)
        {
            return System.Math.Min(MaxPendingCasualtyReinforcements,
                System.Math.Max(0, currentDemand) + ReinforcementsPerCasualty);
        }

        public static int AddReplenishmentDemand(int currentDemand,
            int missingStrength)
        {
            return System.Math.Min(MaxPendingCasualtyReinforcements,
                System.Math.Max(0, currentDemand) +
                System.Math.Max(0, missingStrength));
        }

        public static int MergeDirectedReplenishmentDemand(
            int currentDemand, int currentMissingStrength)
        {
            return System.Math.Min(MaxPendingCasualtyReinforcements,
                System.Math.Max(System.Math.Max(0, currentDemand),
                    System.Math.Max(0, currentMissingStrength)));
        }

        public static int MoveInvalidDirectedReplenishmentDemand(
            int pendingDemand, int invalidDirectedDemand)
        {
            return AddReplenishmentDemand(pendingDemand,
                invalidDirectedDemand);
        }

        public static bool ShouldDirectReplenishmentToRequestedArmy(
            long targetArmyId, bool targetArmyEligible,
            int pendingDemand)
        {
            return targetArmyId >= 0L && targetArmyEligible &&
                   pendingDemand > 0;
        }

        public static bool ShouldResetCasualtyReinforcementProgress(
            bool pPlanCreated)
        {
            return pPlanCreated;
        }

        public static int CasualtyReinforcementBatchLimit(int pendingDemand)
        {
            return System.Math.Min(MaxRecruitsPerWorkItem,
                System.Math.Max(0, pendingDemand));
        }

        public static int ImmediateDirectedRecoveryBatchBudget(
            int pendingDemand)
        {
            int boundedDemand = System.Math.Min(
                MaxPendingCasualtyReinforcements,
                System.Math.Max(0, pendingDemand));
            if (boundedDemand <= 0) return 0;
            return System.Math.Min(MaxCasualtyReinforcementWorkItems,
                (boundedDemand + MaxRecruitsPerWorkItem - 1) /
                MaxRecruitsPerWorkItem);
        }

        public static bool ShouldContinueImmediateDirectedRecovery(
            bool targetArmyActive, int pendingDemand,
            bool candidateCoverageComplete, int batchesProcessed,
            int batchBudget)
        {
            return targetArmyActive && pendingDemand > 0 &&
                   !candidateCoverageComplete && batchesProcessed >= 0 &&
                   batchesProcessed < System.Math.Max(0, batchBudget);
        }

        public static int ForcedEstablishmentSlotLimit(
            int currentMilitary, int normalWarriorSlots,
            int batchRecruitmentLimit)
        {
            long current = System.Math.Max(0, currentMilitary);
            long normal = System.Math.Max(0, normalWarriorSlots);
            long requested = System.Math.Max(0, batchRecruitmentLimit);
            return (int)System.Math.Min(int.MaxValue,
                System.Math.Max(normal, current + requested));
        }

        public static bool ShouldForceEmergencyRecoverySlots(
            bool forceEstablishment, bool directedReplenishment)
        {
            return forceEstablishment || directedReplenishment;
        }

        public static bool ShouldUseInitialEmergencyMobilizationSlots(
            bool emergencyActive)
        {
            return emergencyActive;
        }

        public static int ResolvePreparationCityOrdinal(int batchOrdinal,
            int preferredCityCount)
        {
            return batchOrdinal >= 0 && batchOrdinal < preferredCityCount
                ? batchOrdinal
                : -1;
        }

        public static bool ShouldWaitForPreparationTargets(bool activeNotice,
            bool preferredTargetsReady)
        {
            return activeNotice && !preferredTargetsReady;
        }

        public static int ToMonthKey(int year, int month)
        {
            int normalizedMonth = System.Math.Max(1,
                System.Math.Min(12, month));
            return year * 12 + normalizedMonth - 1;
        }

        public static bool ShouldProcessPreparationMonth(int currentMonthKey,
            int lastProcessedMonthKey)
        {
            return currentMonthKey != lastProcessedMonthKey;
        }

        public static bool ShouldStartPreparationMonth(bool emergencyActive,
            bool activeNotice, int currentMonthKey,
            int lastProcessedMonthKey)
        {
            return emergencyActive && activeNotice &&
                   currentMonthKey != lastProcessedMonthKey;
        }

        public static bool ShouldContinuePreparationMonth(
            bool emergencyActive, bool activeNotice, int completedCities,
            int totalCities)
        {
            return emergencyActive && activeNotice &&
                   System.Math.Max(0, completedCities) <
                   System.Math.Max(0, totalCities);
        }

        public static bool ShouldClearPreparationState(
            bool runtimePlanPresent, bool persistedInProgress,
            int persistedMonthKey)
        {
            return runtimePlanPresent || persistedInProgress ||
                   persistedMonthKey != int.MinValue;
        }

        public static bool ShouldRunAnnualRecruitment(bool emergencyActive,
            bool activeNotice)
        {
            return emergencyActive && !activeNotice;
        }

        public static bool ShouldUseDirectedReplenishmentAnchor(
            int completedWorkItems)
        {
            return true;
        }

        public static bool IsReplenishmentDemandExhausted(
            bool directedReplenishment, bool realmReserveExhausted,
            bool targetReserveExhausted)
        {
            return directedReplenishment
                ? targetReserveExhausted
                : realmReserveExhausted;
        }

        public static bool ShouldKeepCasualtyReinforcementCity(
            bool cityScanComplete)
        {
            return ShouldKeepCasualtyReinforcementCity(cityScanComplete,
                recruitedActors: 0);
        }

        public static bool ShouldKeepCasualtyReinforcementCity(
            bool cityScanComplete, int recruitedActors)
        {
            return !cityScanComplete && recruitedActors > 0;
        }

        public static bool ShouldKeepPreparationRecruitmentCity(
            bool cityScanComplete, int recruitedActors)
        {
            return !cityScanComplete && recruitedActors > 0;
        }

        public static bool ShouldDirectCasualtyRecoveryToArmy(
            bool targetArmyExists, bool targetArmySpecial)
        {
            return targetArmyExists && !targetArmySpecial;
        }

        public static bool ShouldRequestZeroArmyRecovery(
            bool emergencyActive, int usableFieldArmies,
            bool recoveryPending)
        {
            return emergencyActive && usableFieldArmies <= 0 &&
                   !recoveryPending;
        }

        public static bool ShouldRecoverOrphanWarrior(bool emergencyActive,
            bool isWarrior, bool hasArmy, bool localResident,
            bool protectedIdentity, bool activeGarrison)
        {
            return emergencyActive && isWarrior && !hasArmy &&
                   localResident && !protectedIdentity &&
                   !activeGarrison;
        }

        public static bool ShouldNotifyRtsRosterChanged(
            bool actorArmyChanged, bool armyListChanged)
        {
            return actorArmyChanged || armyListChanged;
        }

        public static bool ShouldNotifyWarDirectorOfRosterMutation(
            bool memberAssigned, bool emergencyActive)
        {
            return memberAssigned && emergencyActive;
        }

        public static int RecruitmentPriority(bool enslaved,
            bool retiredReserve)
        {
            if (enslaved) return 0;
            return retiredReserve ? 1 : 2;
        }

        public static bool ShouldCommissionReplacementCaptain(
            bool memberAssigned, bool captainOperational)
        {
            return ShouldCommissionReplacementCaptain(memberAssigned,
                captainOperational, captainExists: false,
                captainAlive: false, captainIsMember: false);
        }

        public static bool ShouldCommissionReplacementCaptain(
            bool memberAssigned, bool captainOperational,
            bool captainExists, bool captainAlive,
            bool captainIsMember)
        {
            bool stableCaptain = ArmyCaptainContinuityRules.
                ShouldPreserveAssignedCaptain(captainExists, captainAlive,
                    captainIsMember);
            return memberAssigned && !captainOperational &&
                   !stableCaptain;
        }

        public static bool ShouldAssignArmyAnchor(bool createdArmy,
            long currentAnchorCityId)
        {
            return createdArmy || currentAnchorCityId < 0L;
        }

        public static bool ShouldContinueCasualtyReinforcement(
            bool emergencyActive, int pendingDemand,
            int completedWorkItems)
        {
            return emergencyActive && pendingDemand > 0 &&
                   completedWorkItems < MaxCasualtyReinforcementWorkItems;
        }

        public static bool ShouldContinueCasualtyRecoveryUntilCoverage(
            bool emergencyActive, int pendingDemand,
            bool candidateCoverageComplete)
        {
            return emergencyActive && pendingDemand > 0 &&
                   !candidateCoverageComplete;
        }

        public static bool CanSupplyCasualtyRecoveryCity(int population,
            int currentMilitary, int normalWarriorSlots,
            bool forceEstablishment)
        {
            if (WartimeRecruitmentPopulationRules.RecruitmentCapacity(
                    population, 1) <= 0)
                return false;
            return forceEstablishment ||
                   System.Math.Max(0, currentMilitary) <
                   System.Math.Max(0, normalWarriorSlots);
        }

        public static bool CanEnlist(bool originalEligible, bool protectedIdentity, float age,
            int currentWarriors, int warriorSlots)
        {
            return originalEligible && !protectedIdentity && age < MaximumEnlistmentAge &&
                   warriorSlots > 0 && currentWarriors < warriorSlots;
        }

        public static bool CanRegisterReserve(bool originalEligible,
            bool protectedIdentity, float age)
        {
            return originalEligible && !protectedIdentity && age >= 18f &&
                   age < MaximumEnlistmentAge;
        }

        public static bool ShouldRemainMobilized(int pActiveNotices, int pActiveWars)
        {
            return pActiveNotices > 0 || pActiveWars > 0;
        }

        public static bool CanLaunchEmergencyArmy(bool vanillaReady,
            bool militaryEmergency, int armyCount, float warriorSlots,
            int standingCoreCount)
        {
            if (vanillaReady) return true;
            if (!militaryEmergency || warriorSlots <= 0f ||
                standingCoreCount <= 0) return false;
            int maximumPossible = System.Math.Max(1,
                (int)System.Math.Ceiling(warriorSlots));
            int required = System.Math.Min(maximumPossible,
                System.Math.Max(1, standingCoreCount));
            return System.Math.Max(0, armyCount) >= required;
        }
    }
}
