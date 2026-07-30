namespace AncientWarfare3.core.lineage
{
    public enum ArmyRecruitmentDisposition
    {
        Reject = 0,
        Replenish = 1,
        Create = 2
    }

    public static class ArmyEstablishmentRules
    {
        public const int MaximumFieldArmies = 12;
        public const int MergeThresholdPercent = 35;
        public const int MemberAssignmentBatchSize = 16;
        public const int MaximumMergePairsPerWorkItem = 1;

        public static ArmyRecruitmentDisposition DecideRecruitment(
            int fieldArmyCount, bool hasReplenishmentArmy)
        {
            if (hasReplenishmentArmy)
                return ArmyRecruitmentDisposition.Replenish;
            return fieldArmyCount < MaximumFieldArmies
                ? ArmyRecruitmentDisposition.Create
                : ArmyRecruitmentDisposition.Reject;
        }

        public static ArmyRecruitmentDisposition DecideFieldCreation(
            int fieldArmyCount, bool hasIndexedExistingArmy,
            bool candidateIsFieldArmy, bool exemptSpecialCreation)
        {
            if (!candidateIsFieldArmy || exemptSpecialCreation)
                return ArmyRecruitmentDisposition.Create;
            return DecideRecruitment(fieldArmyCount,
                hasIndexedExistingArmy);
        }

        public static bool ShouldPublishCompletedCreation(bool createdArmy,
            bool memberAssigned)
        {
            return createdArmy && memberAssigned;
        }

        public static bool ShouldMaintainExcessFieldArmies(
            int fieldArmyCount)
        {
            return fieldArmyCount > MaximumFieldArmies;
        }

        public static bool ShouldScheduleMaintenanceMerge(
            bool kingdomOverHardCap, bool kingdomAtWar,
            int sourceLiving, bool sourceCaptainAlive,
            bool sourceHasActiveMission = false)
        {
            return kingdomOverHardCap && !kingdomAtWar &&
                   IsMaintenanceMergeSource(sourceLiving,
                       sourceCaptainAlive, sourceHasActiveMission);
        }

        public static bool IsMaintenanceMergeSource(int sourceLiving,
            bool sourceCaptainAlive, bool sourceHasActiveMission)
        {
            return sourceLiving > 0 && !sourceCaptainAlive &&
                   !sourceHasActiveMission;
        }

        public static bool ShouldUseAsReplenishmentTarget(
            int sourceLiving, int sourceTargetStrength)
        {
            return sourceLiving > 0 && sourceTargetStrength > 0 &&
                   sourceLiving < sourceTargetStrength;
        }

        public static bool ShouldCountTowardsFieldArmyLimit(
            int sourceLiving)
        {
            return sourceLiving > 0;
        }

        public static bool ShouldContinueMergeBatch(int remainingLiving,
            int movedThisBatch)
        {
            return remainingLiving > 0 && movedThisBatch > 0;
        }

        public static bool IsFieldArmyClassification(bool hasData,
            bool alive, bool hasKingdom, bool markedSpecial, bool sortie,
            bool controlledCreationShell, bool restorationArmy)
        {
            return hasData && alive && hasKingdom && !markedSpecial &&
                   !sortie && !controlledCreationShell &&
                   !restorationArmy;
        }

        public static bool ShouldMerge(int sourceLiving,
            int sourceTargetStrength, bool sameFront, bool compatible,
            bool sourceRequiredDefense, bool targetCoversDefense,
            bool sourceHasActiveMission = false)
        {
            if (!CanContinueMerge(sameFront, compatible,
                    sourceRequiredDefense, targetCoversDefense,
                    sourceHasActiveMission) ||
                sourceTargetStrength <= 0)
                return false;
            long percent = (long)System.Math.Max(0, sourceLiving) * 100L;
            return percent < (long)sourceTargetStrength *
                   MergeThresholdPercent;
        }

        public static bool CanContinueMerge(bool sameFront,
            bool compatible, bool sourceRequiredDefense,
            bool targetCoversDefense,
            bool sourceHasActiveMission = false)
        {
            return !sourceHasActiveMission && sameFront && compatible &&
                   (!sourceRequiredDefense || targetCoversDefense);
        }

        public static bool ShouldMergeForMaintenance(int sourceLiving,
            int sourceTargetStrength, bool sameFront, bool compatible,
            bool sourceRequiredDefense, bool targetCoversDefense,
            bool kingdomOverHardCap,
            bool sourceHasActiveMission = false)
        {
            if (!CanContinueMerge(sameFront, compatible,
                    sourceRequiredDefense, targetCoversDefense,
                    sourceHasActiveMission))
                return false;
            return kingdomOverHardCap || ShouldMerge(sourceLiving,
                sourceTargetStrength, sameFront, compatible,
                sourceRequiredDefense, targetCoversDefense,
                sourceHasActiveMission);
        }
    }
}
