using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public sealed class BanditRaidCandidate
    {
        public BanditRaidCandidate(long cityId, int routeDistance,
            int stealableFood, bool reachable, bool allied,
            bool stronghold)
        {
            CityId = cityId;
            RouteDistance = Math.Max(0, routeDistance);
            StealableFood = Math.Max(0, stealableFood);
            Reachable = reachable;
            Allied = allied;
            Stronghold = stronghold;
        }

        public long CityId { get; }
        public int RouteDistance { get; }
        public int StealableFood { get; }
        public bool Reachable { get; }
        public bool Allied { get; }
        public bool Stronghold { get; }
    }

    public static class PeasantRebelBanditRaidRules
    {
        public static bool NeedsRaid(int food, int population)
        {
            return population > 0 && Math.Max(0, food) < population * 2;
        }

        public static int PartySize(int availableWarriors)
        {
            if (availableWarriors <= 0) return 0;
            return Math.Min(8, availableWarriors);
        }

        public static bool CanJoinRaid(bool alive, bool warrior,
            bool ruler, bool heir, bool carryingResources)
        {
            return alive && warrior && !ruler && !heir &&
                   !carryingResources;
        }

        public static IReadOnlyDictionary<long, int> DistributeCargo(
            IEnumerable<long> actorIds, int amount)
        {
            long[] ordered = (actorIds ?? Enumerable.Empty<long>())
                .Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
            var result = new Dictionary<long, int>();
            int total = Math.Max(0, amount);
            if (ordered.Length == 0 || total == 0) return result;
            int share = total / ordered.Length;
            int remainder = total % ordered.Length;
            for (int index = 0; index < ordered.Length; index++)
            {
                int actorShare = share + (index < remainder ? 1 : 0);
                if (actorShare > 0) result[ordered[index]] = actorShare;
            }
            return result;
        }

        public static int StealableFood(int strongholdFood,
            int strongholdPopulation, int targetFood, int targetPopulation)
        {
            int need = Math.Max(0, Math.Max(0, strongholdPopulation) * 5 -
                                   Math.Max(0, strongholdFood));
            int share = Math.Max(0, targetFood) / 4;
            int surplus = Math.Max(0, Math.Max(0, targetFood) -
                                      Math.Max(0, targetPopulation) * 2);
            return Math.Min(need, Math.Min(share, surplus));
        }

        public static bool CooldownExpired(int currentYear,
            int cooldownUntilYear)
        {
            return currentYear >= cooldownUntilYear;
        }

        public static int SuppressionExpiryYear(int currentYear)
        {
            return currentYear + 3;
        }

        public static IReadOnlyList<BanditRaidCandidate> RankTargets(
            IEnumerable<BanditRaidCandidate> candidates)
        {
            return (candidates ?? Enumerable.Empty<BanditRaidCandidate>())
                .Where(candidate => candidate != null &&
                    candidate.CityId > 0 && candidate.Reachable &&
                    !candidate.Allied && !candidate.Stronghold &&
                    candidate.StealableFood > 0)
                .OrderBy(candidate => candidate.RouteDistance)
                .ThenByDescending(candidate => candidate.StealableFood)
                .ThenBy(candidate => candidate.CityId)
                .ToList();
        }
    }
}
