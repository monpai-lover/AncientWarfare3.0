using System;

namespace AncientWarfare3.core.lineage
{
    public readonly struct ZhuluAgeTargetFacts
    {
        public ZhuluAgeTargetFacts(long kingdomId, bool valid, bool isSelf,
            bool sameRoot, bool alreadyAtWar, bool diplomaticBlocked,
            bool sameAlliance, bool directlyAdjacent, long distanceSquared,
            long score)
        {
            KingdomId = kingdomId;
            Valid = valid;
            IsSelf = isSelf;
            SameRoot = sameRoot;
            AlreadyAtWar = alreadyAtWar;
            DiplomaticBlocked = diplomaticBlocked;
            SameAlliance = sameAlliance;
            DirectlyAdjacent = directlyAdjacent;
            DistanceSquared = Math.Max(0L, distanceSquared);
            Score = Math.Max(0L, score);
        }

        public long KingdomId { get; }
        public bool Valid { get; }
        public bool IsSelf { get; }
        public bool SameRoot { get; }
        public bool AlreadyAtWar { get; }
        public bool DiplomaticBlocked { get; }
        public bool SameAlliance { get; }
        public bool DirectlyAdjacent { get; }
        public long DistanceSquared { get; }
        public long Score { get; }
    }

    public static class ZhuluAgeRules
    {
        public const string AgeId = "age_zhulu";
        public const long CityWeight = 200L;
        public const long ZoneWeight = 2L;
        public const long PopulationWeight = 1L;
        public const long RecruitableWeight = 3L;

        public static bool ShouldUseWarOverride(bool explicitOverride,
            string currentAgeId)
        {
            return explicitOverride || string.Equals(currentAgeId, AgeId,
                StringComparison.Ordinal);
        }

        public static long DirectScore(int cityCount, int zoneCount,
            int population, int recruitableWarriors)
        {
            long result = 0L;
            result = AddSaturated(result,
                MultiplySaturated(cityCount, CityWeight));
            result = AddSaturated(result,
                MultiplySaturated(zoneCount, ZoneWeight));
            result = AddSaturated(result,
                MultiplySaturated(population, PopulationWeight));
            return AddSaturated(result,
                MultiplySaturated(recruitableWarriors,
                    RecruitableWeight));
        }

        public static long VassalContribution(long childScore)
        {
            return Math.Max(0L, childScore) / 2L;
        }

        public static bool HasMandateLead(long first, long second,
            int independentCount)
        {
            if (independentCount <= 0 || first < 0L || second < 0L)
                return false;
            if (independentCount == 1) return true;
            if (first == 0L) return false;
            return second <= long.MaxValue / 2L &&
                   first >= second * 2L;
        }

        public static bool IsEligibleTarget(ZhuluAgeTargetFacts facts)
        {
            return facts.Valid && !facts.IsSelf && !facts.SameRoot &&
                   !facts.AlreadyAtWar && !facts.DiplomaticBlocked &&
                   !facts.SameAlliance;
        }

        public static bool ShouldIncludeDistantTargets(bool isZhuluAge,
            MandatePhase phase, bool sourceCanUseMandateSystem)
        {
            return isZhuluAge || phase == MandatePhase.Chaos &&
                   sourceCanUseMandateSystem;
        }

        public static int WarCandidateLimit(bool isZhuluAge)
        {
            return isZhuluAge ? int.MaxValue : 24;
        }

        public static bool CanUseUnificationWarAi(bool isZhuluAge,
            bool normalWarAiSupported)
        {
            return isZhuluAge || normalWarAiSupported;
        }

        public static bool IsIndependentAiAttacker(bool isZhuluAge,
            bool hasVassalSuzerain, bool hasDiplomaticSuzerain)
        {
            return isZhuluAge
                ? !hasVassalSuzerain
                : !hasDiplomaticSuzerain;
        }

        public static bool IsEligibleDistantTarget(bool isZhuluAge,
            bool targetCanUseMandateSystem)
        {
            return isZhuluAge || targetCanUseMandateSystem;
        }

        public static bool ShouldLeaveAllianceForUnification(
            bool hasNonAllianceTarget, bool hasAllianceTarget)
        {
            return !hasNonAllianceTarget && hasAllianceTarget;
        }

        public static int CompareTargets(ZhuluAgeTargetFacts left,
            ZhuluAgeTargetFacts right)
        {
            int result = right.DirectlyAdjacent.CompareTo(
                left.DirectlyAdjacent);
            if (result != 0) return result;
            result = left.DistanceSquared.CompareTo(right.DistanceSquared);
            if (result != 0) return result;
            result = left.Score.CompareTo(right.Score);
            return result != 0
                ? result
                : left.KingdomId.CompareTo(right.KingdomId);
        }

        public static long AddScores(long left, long right)
        {
            return AddSaturated(left, right);
        }

        private static long MultiplySaturated(int value, long weight)
        {
            if (value <= 0 || weight <= 0L) return 0L;
            return value > long.MaxValue / weight
                ? long.MaxValue
                : value * weight;
        }

        private static long AddSaturated(long left, long right)
        {
            left = Math.Max(0L, left);
            right = Math.Max(0L, right);
            return left > long.MaxValue - right
                ? long.MaxValue
                : left + right;
        }
    }
}
