using System;

namespace AncientWarfare3.core.lineage
{
    public static class MandateMilitaryStrengthRules
    {
        public const int MaximumArmyTarget = 900;
        public const int MaximumNativeCandidatesPerCycle = 96;

        public static int ResolveTarget(int configuredTarget)
        {
            return Math.Max(0, Math.Min(MaximumArmyTarget, configuredTarget));
        }

        public static bool ShouldRecruit(bool activeMandate, bool atWar,
            int currentWarriors, int configuredTarget)
        {
            int target = ResolveTarget(configuredTarget);
            return activeMandate && atWar && target > 0 &&
                   Math.Max(0, currentWarriors) < target;
        }

        public static bool CanUseNativeCandidate(bool valid, bool alive,
            bool adult, bool sameKingdom, bool isKing, bool isHeir,
            bool isCityLeader, bool isRoyalGuard, bool isWarrior,
            bool hasArmy)
        {
            return valid && alive && adult && sameKingdom && !isKing &&
                   !isHeir && !isCityLeader && !isRoyalGuard && !isWarrior &&
                   !hasArmy;
        }

        public static int RemainingTarget(int currentWarriors,
            int configuredTarget)
        {
            return Math.Max(0, ResolveTarget(configuredTarget) -
                Math.Max(0, currentWarriors));
        }
    }
}
