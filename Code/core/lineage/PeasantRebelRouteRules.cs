using System;

namespace AncientWarfare3.core.lineage
{
    public static class PeasantRebelRouteIds
    {
        public const string Founding = "founding";
        public const string Bandit = "bandit";
    }

    public static class PeasantRebelRouteRules
    {
        public static int FoundingChance(int leader, int city,
            int origin, int turmoil)
        {
            return Clamp(50 + Clamp(leader, -15, 15) +
                Clamp(city, -15, 15) + Clamp(origin, -20, 20) +
                Clamp(turmoil, 0, 10), 10, 90);
        }

        public static string SelectRoute(int roll, int chance)
        {
            return Clamp(roll, 0, 99) < Clamp(chance, 10, 90)
                ? PeasantRebelRouteIds.Founding
                : PeasantRebelRouteIds.Bandit;
        }

        public static bool CanDeclareWar(bool attackerBandit,
            bool defenderBandit, bool attackerIsOrigin)
        {
            if (attackerBandit) return false;
            return !defenderBandit || attackerIsOrigin;
        }

        public static bool CanAcquireCity(bool bandit,
            int currentCityCount, bool alreadyOwned)
        {
            return !bandit || alreadyOwned || currentCityCount == 0;
        }

        public static bool CanSwitchGovernment(string currentClass,
            string targetClass)
        {
            string current = (currentClass ?? "").Trim();
            string target = (targetClass ?? "").Trim();
            if (target == "peasant_bandit")
                return current == "peasant_rebel";
            if (current == "peasant_bandit")
                return target == "peasant_rebel";
            return current != target;
        }

        public static bool CanAcquireWhitelistedCity(bool bandit,
            bool alreadyOwned, bool whitelisted)
        {
            return !bandit || alreadyOwned || whitelisted;
        }

        public static bool ShouldBypassTruce(bool defenderBandit,
            bool attackerIsOrigin)
        {
            return defenderBandit && attackerIsOrigin;
        }

        public static bool ShouldRepairWalls(bool bandit,
            bool suppressionActive)
        {
            return bandit && !suppressionActive;
        }

        public static int RepairCount(int missing, int yearlyBudget)
        {
            return Math.Min(Math.Max(0, missing),
                Math.Max(0, yearlyBudget));
        }

        public static bool CanEvaluateWeakOriginTransition(
            int banditAgeYears, bool originWeak, bool turmoil,
            int cityFactor, int leaderFactor)
        {
            return banditAgeYears >= 3 && originWeak && turmoil &&
                   cityFactor >= 0 && leaderFactor >= 0;
        }

        public static int TransitionChance(bool quarterStrength,
            int hostileWarCount, bool capitalLost, int cityFactor,
            int leaderFactor)
        {
            int chance = 20 + (quarterStrength ? 20 : 0) +
                Math.Min(20, Math.Max(0, hostileWarCount - 1) * 10) +
                (capitalLost ? 15 : 0) + Clamp(cityFactor, 0, 15) +
                Clamp(leaderFactor, 0, 15);
            return Clamp(chance, 20, 90);
        }

        public static string ComposeName(string root, string route)
        {
            return (root ?? "").Trim() +
                   (route == PeasantRebelRouteIds.Bandit
                       ? "\u8d3c"
                       : "\u4e49\u519b");
        }

        public static string ResolvePersistedRoute(string storedRoute,
            bool currentPeasantRebel)
        {
            string route = (storedRoute ?? "").Trim();
            if (route == PeasantRebelRouteIds.Founding ||
                route == PeasantRebelRouteIds.Bandit) return route;
            return currentPeasantRebel
                ? PeasantRebelRouteIds.Founding
                : "";
        }

        public static bool CanMutateAuthority(bool replicaSession)
        {
            return !replicaSession;
        }

        public static int LeaderFactor(int warfare, int stewardship,
            int diplomacy, bool ambitious, bool peaceful)
        {
            int average = Clamp(
                (Math.Max(0, warfare) + Math.Max(0, stewardship) +
                 Math.Max(0, diplomacy)) / 3, 0, 20);
            int personality = (ambitious ? 5 : 0) -
                              (peaceful ? 5 : 0);
            return Clamp(average - 10 + personality, -15, 15);
        }

        public static int CityFactor(int population, int originMedian)
        {
            if (originMedian <= 0) return 0;
            double ratio = Math.Max(0, population) /
                           (double)originMedian;
            return Clamp((int)Math.Round((ratio - 1d) * 30d), -15, 15);
        }

        public static int OriginStrengthFactor(int originStrength,
            int rebelStrength)
        {
            if (originStrength <= 0) return 20;
            double ratio = Math.Max(0, rebelStrength) /
                           (double)originStrength;
            if (ratio <= 0.25d) return -20;
            if (ratio >= 1d) return 20;
            return Clamp((int)Math.Round(
                -20d + (ratio - 0.25d) / 0.75d * 40d), -20, 20);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
