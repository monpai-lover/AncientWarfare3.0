using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public sealed class BanditIslandCandidateFact
    {
        public BanditIslandCandidateFact(long islandId, bool eligible,
            int safetyScore, int routeCost, int buildableArea)
        {
            IslandId = islandId;
            Eligible = eligible;
            SafetyScore = safetyScore;
            RouteCost = routeCost;
            BuildableArea = buildableArea;
        }

        public long IslandId { get; }
        public bool Eligible { get; }
        public int SafetyScore { get; }
        public int RouteCost { get; }
        public int BuildableArea { get; }
    }

    public enum BanditStrongholdKind
    {
        Land,
        Island
    }

    public enum BanditMigrationStage
    {
        None,
        Evaluating,
        Boarding,
        Voyaging,
        Founding,
        Completed,
        Failed
    }

    public static class PeasantRebelBanditIslandRules
    {
        public static bool ShouldStartEvacuation(bool suppressionWar,
            bool hostileAttackActive, int banditStrength,
            int hostileStrength, int population, int threatCycles)
        {
            bool weak = population < 4 ||
                hostileStrength > 0 &&
                banditStrength * 100 < hostileStrength * 60;
            return suppressionWar &&
                (hostileAttackActive || population < 4) && weak &&
                threatCycles >= 1;
        }

        public static int NextThreatCycles(bool threatPresent, int current)
        {
            return threatPresent ? Math.Min(2, Math.Max(0, current) + 1) : 0;
        }

        public static bool IsEligibleIsland(bool hasCity,
            bool hasStronghold, int buildableArea,
            bool hasCoastalLanding, bool occupiedByHostile)
        {
            return !hasCity && !hasStronghold && buildableArea > 0 &&
                hasCoastalLanding && !occupiedByHostile;
        }

        public static bool IsEligiblePiracyTarget(bool coastal,
            bool reachable, bool allied, bool stronghold,
            int stealableFood)
        {
            return coastal && reachable && !allied && !stronghold &&
                stealableFood > 0;
        }

        public static bool CanTransition(BanditMigrationStage current,
            BanditMigrationStage next, int manifestCount)
        {
            if (next == BanditMigrationStage.Failed)
                return current != BanditMigrationStage.None &&
                    current != BanditMigrationStage.Completed;
            if (next == BanditMigrationStage.None)
                return current == BanditMigrationStage.Failed ||
                    current == BanditMigrationStage.Completed;
            if (next >= BanditMigrationStage.Boarding &&
                next <= BanditMigrationStage.Founding && manifestCount <= 0)
                return false;
            return current switch
            {
                BanditMigrationStage.None =>
                    next == BanditMigrationStage.Evaluating,
                BanditMigrationStage.Evaluating =>
                    next == BanditMigrationStage.Boarding,
                BanditMigrationStage.Boarding =>
                    next == BanditMigrationStage.Voyaging,
                BanditMigrationStage.Voyaging =>
                    next == BanditMigrationStage.Founding,
                BanditMigrationStage.Founding =>
                    next == BanditMigrationStage.Completed,
                _ => false
            };
        }

        public static IReadOnlyList<BanditIslandCandidateFact> RankIslands(
            IEnumerable<BanditIslandCandidateFact> candidates)
        {
            return (candidates ?? Enumerable.Empty<
                    BanditIslandCandidateFact>())
                .Where(candidate => candidate != null && candidate.Eligible)
                .OrderByDescending(candidate => candidate.SafetyScore)
                .ThenBy(candidate => candidate.RouteCost)
                .ThenByDescending(candidate => candidate.BuildableArea)
                .ThenBy(candidate => candidate.IslandId)
                .ToList();
        }
    }
}
