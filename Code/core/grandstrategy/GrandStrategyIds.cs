using System;

namespace AncientWarfare3.core.grandstrategy
{
    public sealed class GrandStrategyIdAllocator
    {
        private readonly long _worldGeneration;
        private long _nextArmy;
        private long _nextBattle;
        private long _nextSiege;
        private long _nextReport;

        public GrandStrategyIdAllocator(long worldGeneration)
        {
            if (worldGeneration < 0) throw new ArgumentOutOfRangeException(nameof(worldGeneration));
            _worldGeneration = worldGeneration;
        }

        public long WorldGeneration => _worldGeneration;
        public long NextArmyId() => Next(ref _nextArmy, 1);
        public long NextBattleId() => Next(ref _nextBattle, 2);
        public long NextSiegeId() => Next(ref _nextSiege, 3);
        public long NextReportId() => Next(ref _nextReport, 4);

        private long Next(ref long counter, long kind)
        {
            if (counter == long.MaxValue) throw new InvalidOperationException("grand_strategy_id_exhausted");
            counter++;
            unchecked
            {
                long prefix = (_worldGeneration & 0x1FFFFF) << 40;
                long value = prefix | ((kind & 0xF) << 36) | (counter & 0xFFFFFFFFFL);
                return value < 0 ? counter : value;
            }
        }
    }
}
