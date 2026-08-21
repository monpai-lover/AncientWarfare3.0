using System;

namespace AncientWarfare3.core.lineage
{
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
    }
}
