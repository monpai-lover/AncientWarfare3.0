using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.grandstrategy
{
    public sealed class GrandStrategyActiveSiege
    {
        internal GrandStrategyActiveSiege(GrandStrategySiegeState state,
            long warId, long armyId)
        {
            State = state;
            WarId = warId;
            ArmyId = armyId;
        }

        public GrandStrategySiegeState State { get; internal set; }
        public long SiegeId => State.SiegeId;
        public long CityId => State.CityId;
        public long WarId { get; }
        public long ArmyId { get; }
        public bool OccupationCommitted { get; internal set; }
    }

    public sealed class GrandStrategySiegeService
    {
        private readonly Func<long, long, long, long, bool> _occupationBridge;
        private readonly Dictionary<long, GrandStrategyActiveSiege> _active =
            new Dictionary<long, GrandStrategyActiveSiege>();

        public GrandStrategySiegeService(
            Func<long, long, long, long, bool> occupationBridge)
        {
            _occupationBridge = occupationBridge ??
                throw new ArgumentNullException(nameof(occupationBridge));
        }

        public GrandStrategyActiveSiege Start(long siegeId, long warId,
            long cityId, long armyId, int defense, int maximumProgress)
        {
            if (_active.ContainsKey(siegeId)) return _active[siegeId];
            var siege = new GrandStrategyActiveSiege(
                new GrandStrategySiegeState(siegeId, cityId, defense,
                    maximumProgress), warId, armyId);
            _active.Add(siegeId, siege);
            return siege;
        }

        public GrandStrategySiegeState ResolveMonthlyRound(long siegeId,
            int engineers, int equipment, int officerSkill, int manpower,
            double supply, int technology, bool assault, int roll)
        {
            if (!_active.TryGetValue(siegeId,
                out GrandStrategyActiveSiege siege))
                throw new InvalidOperationException("siege_missing");
            if (siege.State.Complete) return siege.State;
            siege.State = GrandStrategySiegeRules.ResolveRound(siege.State,
                engineers, equipment, officerSkill, manpower, supply,
                technology, assault, roll);
            return siege.State;
        }

        public bool CommitOccupationOnce(long siegeId)
        {
            if (!_active.TryGetValue(siegeId,
                out GrandStrategyActiveSiege siege) || !siege.State.Complete)
                return false;
            if (siege.OccupationCommitted) return true;
            if (!_occupationBridge(siege.SiegeId, siege.WarId,
                    siege.CityId, siege.ArmyId)) return false;
            siege.OccupationCommitted = true;
            return true;
        }

        public void ClearWorld()
        {
            _active.Clear();
        }
    }
}
