using System;

namespace AncientWarfare3.core.lineage
{
    public static class DynasticReproductionRules
    {
        public const float PrioritizedReproductionWeight = 4f;

        public static bool NeedsCivilianReproductionWindow(
            bool dynasticIdentity, bool alive, bool adult,
            bool breedingAge, bool canProduceBabies,
            bool hasLivingSon)
        {
            return dynasticIdentity && alive && adult && breedingAge &&
                   canProduceBabies && !hasLivingSon;
        }

        public static bool ShouldProtectFromOrdinaryMilitaryService(
            bool actorNeedsWindow, bool hasLivingPartner,
            bool partnerNeedsWindow)
        {
            return actorNeedsWindow ||
                   hasLivingPartner && partnerNeedsWindow;
        }

        public static bool ShouldReleaseExistingMilitaryRole(
            bool isWarrior, bool isCurrentHeir,
            bool reproductionProtected,
            bool isCareerStandingSoldier,
            bool militaryEmergency,
            bool inCombat,
            bool cityAttackOrder)
        {
            return isWarrior &&
                   !isCareerStandingSoldier &&
                   !militaryEmergency && !inCombat && !cityAttackOrder &&
                   (isCurrentHeir || reproductionProtected);
        }

        public static bool ShouldAllowPeacetimeStandingReproduction(
            bool isCareerStandingSoldier,
            bool militaryEmergency,
            bool inCombat,
            bool cityAttackOrder)
        {
            return isCareerStandingSoldier &&
                   !militaryEmergency && !inCombat && !cityAttackOrder;
        }

        public static bool IsSexualReproductionTask(string pTaskId)
        {
            return !string.IsNullOrEmpty(pTaskId) &&
                   pTaskId.StartsWith("sexual_reproduction_",
                       StringComparison.Ordinal);
        }

        public static bool ShouldClearReproductionObservation(
            int firstObservedYear)
        {
            return firstObservedYear >= 0;
        }

        public static bool ShouldObserveReproductionTimeout(
            bool isWarrior, bool militaryEmergency,
            bool inCombat, bool cityAttackOrder)
        {
            return !isWarrior ||
                   !militaryEmergency && !inCombat && !cityAttackOrder;
        }

        public static bool ShouldPreservePeacetimeReproduction(
            bool shouldUsePeacetimePatrol, string taskId,
            int firstObservedYear, int currentYear)
        {
            return shouldUsePeacetimePatrol &&
                   IsSexualReproductionTask(taskId) &&
                   !ShouldRecoverStuckReproduction(
                       shouldUsePeacetimePatrol, taskId,
                       firstObservedYear, currentYear);
        }

        public static bool ShouldRecoverStuckReproduction(
            bool shouldUsePeacetimePatrol, string taskId,
            int firstObservedYear, int currentYear)
        {
            return shouldUsePeacetimePatrol &&
                   IsSexualReproductionTask(taskId) &&
                   firstObservedYear >= 0 &&
                   currentYear > firstObservedYear;
        }

        public static float ReproductionDecisionWeight(
            float originalWeight, bool usesDynasticSystem,
            bool hasNobleIdentity,
            bool isRuler, bool isCurrentHeir,
            bool isFeudatoryPrince, bool holdsMaleNobleTitle,
            bool hasLivingSon)
        {
            bool eligibleRole = hasNobleIdentity || isRuler || isCurrentHeir ||
                                isFeudatoryPrince ||
                                holdsMaleNobleTitle;
            return usesDynasticSystem && eligibleRole && !hasLivingSon
                ? Math.Max(originalWeight,
                    PrioritizedReproductionWeight)
                : originalWeight;
        }
    }
}
