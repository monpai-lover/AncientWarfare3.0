using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class StandingArmyRules
    {
        public const int MaxCandidateScan = 64;
        public const int MaxStandingScanPerPass = 64;
        public const int MaxAppointmentsPerPass = 2;
        public const int MaxReductionsPerPass = 2;
        public const int MaxReplacementsPerPass = 1;
        public const float PeacetimePatrolRetrySeconds = 0.15f;

        public static int PeacetimeCore(int pWarriorSlots)
        {
            return pWarriorSlots <= 0
                ? 0
                : Math.Max(1, (int)Math.Ceiling(pWarriorSlots * 0.30d));
        }

        public static bool ShouldMaintainPeacetime(bool militaryEmergency, bool temporaryLeviesActive)
        {
            return !militaryEmergency && !temporaryLeviesActive;
        }

        public static bool ShouldUsePeacetimePatrol(
            bool isCareerStandingSoldier,
            bool militaryEmergency,
            bool inCombat,
            bool cityAttackOrder)
        {
            return false;
        }

        public static bool ShouldReleaseLegacyPeacetimePatrol(
            string pJobId, string pTaskId)
        {
            return pJobId == "aw_standing_army_peacetime_job" ||
                   pTaskId == "aw_standing_army_peacetime_patrol";
        }

        public static bool ShouldRetryPeacetimePatrol(
            bool pHasPatrolTarget, bool pTargetIsCurrentTile)
        {
            return !pHasPatrolTarget || pTargetIsCurrentTile;
        }

        public static bool ShouldKeepPeacetimePatrolForAnchor(
            bool actorCityMatchesAnchor, bool armyAnchoredToActorCity,
            bool actorInsideCityCoreZone)
        {
            return actorCityMatchesAnchor && armyAnchoredToActorCity;
        }

        public static bool ShouldEnsureArmyMembership(
            bool isWarrior, bool hasArmyMembership)
        {
            return isWarrior && !hasArmyMembership;
        }

        public static float MilitaryScore(float damage, float warfare, float health, float armor, float speed)
        {
            return damage + warfare * 2f + health * 0.1f + armor * 2f + speed * 0.25f;
        }

        public static bool IsKingdomReady(IReadOnlyList<int> pRequired, IReadOnlyList<int> pFilled)
        {
            if (pRequired == null || pFilled == null || pRequired.Count != pFilled.Count) return false;

            bool hasPositiveCore = false;
            for (int i = 0; i < pRequired.Count; i++)
            {
                int required = Math.Max(0, pRequired[i]);
                if (required <= 0) continue;
                hasPositiveCore = true;
                if (Math.Min(required, Math.Max(0, pFilled[i])) < required) return false;
            }

            return hasPositiveCore;
        }

        public static bool ShouldAllowGuardMaintenance(bool hasExistingGuards, bool standingCoreReady,
            bool militaryEmergency)
        {
            return hasExistingGuards || standingCoreReady;
        }

        public static bool ShouldAllowGuardRecruitment(bool standingCoreReady, bool militaryEmergency)
        {
            return standingCoreReady;
        }
    }
}
