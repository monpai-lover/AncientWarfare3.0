// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;

namespace AncientWarfare3.core.pathfinding
{
    public sealed class AWPathfindingConfig
    {
        public static AWPathfindingConfig Default { get; } = new AWPathfindingConfig();

        public int ShortRangeTiles { get; set; } = 24;
        public int LongRangeTiles { get; set; } = 96;
        public int SegmentTargetSteps { get; set; } = 24;
        public int RegionCorridorLookaheadTiles { get; set; } = 64;
        public int RegionRouteCacheSize { get; set; } = 512;
        public float LongRangeHeuristicWeight { get; set; } = 1.15f;
        public int MaxNodesShort { get; set; } = 3000;
        public int MaxNodesLong { get; set; } = 12000;
        public int MaxNodesLongFallback { get; set; } = 60000;
        public int FallbackCorridorMinDetour { get; set; } = 32;
        public float FallbackCorridorDetourScale { get; set; } = 0.75f;
        public int TransportCandidates { get; set; } = 2;
        public int TransportSearchRadius { get; set; } = 64;
        public float WalkSpeedScale { get; set; } = 0.4f;
        public float SwimSpeedScale { get; set; } = 0.25f;
        public float SailSpeedScale { get; set; } = 0.6f;
        public int MaxLabelsPerTile { get; set; } = 4;
        public float StaminaCostWeight { get; set; } = 0.08f;
        public float HealthCostWeight { get; set; } = 4f;
        public float LowHealthRiskCost { get; set; } = 160f;
        public float DeathRiskCost { get; set; } = 100000f;
        public float BlockRiskCost { get; set; } = 12f;
        public float FireRiskCost { get; set; } = 30f;
        public float OceanRiskCost { get; set; } = 4f;
        public float LavaRiskCost { get; set; } = 120f;
        public float TerrainDamageRiskCost { get; set; } = 60f;
        public float WaterStaminaDrainPerSecond { get; set; } = 10f;
        public float DrowningDamagePerSecond { get; set; } = 2f;
        public float DamageUnitsTicksPerSecond { get; set; } = 3.333f;
        public float ExhaustedSwimSpeedScale { get; set; } = 0.4f;

        public static AWPathfindingConfig CreateArmyRouteConfig(
            int worldTileCount)
        {
            int tiles = Math.Max(1, worldTileCount);
            return new AWPathfindingConfig
            {
                ShortRangeTiles = 24,
                LongRangeTiles = 96,
                SegmentTargetSteps = 24,
                RegionCorridorLookaheadTiles = 64,
                RegionRouteCacheSize = 512,
                LongRangeHeuristicWeight = 1.15f,
                MaxNodesShort = 6000,
                MaxNodesLong = 60000,
                MaxNodesLongFallback = Math.Max(120000,
                    Math.Min(1000000, tiles + 4096)),
                FallbackCorridorMinDetour = Math.Max(128,
                    (int)Math.Ceiling(Math.Sqrt(tiles) * 2d)),
                FallbackCorridorDetourScale = 1.5f,
                MaxLabelsPerTile = 1
            };
        }

        public static int WorkerCount(int pProcessorCount)
        {
            return Math.Max(1, Math.Min(8, pProcessorCount - 1));
        }
    }
}
