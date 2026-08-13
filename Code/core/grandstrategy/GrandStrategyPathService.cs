using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.grandstrategy
{
    public sealed class GrandStrategyRoute
    {
        internal GrandStrategyRoute(long armyId, int targetTileId,
            IReadOnlyList<int> tileIds, double estimatedArrival,
            int supplyCost)
        {
            ArmyId = armyId;
            TargetTileId = targetTileId;
            TileIds = tileIds;
            EstimatedArrival = Math.Max(0, estimatedArrival);
            SupplyCost = Math.Max(0, supplyCost);
        }

        public long ArmyId { get; }
        public int TargetTileId { get; }
        public IReadOnlyList<int> TileIds { get; }
        public int Cursor { get; internal set; }
        public double EstimatedArrival { get; }
        public int SupplyCost { get; }
        public bool Complete => Cursor >= TileIds.Count - 1;
    }

    public sealed class GrandStrategyPathService
    {
        private readonly Dictionary<long, GrandStrategyRoute> _active =
            new Dictionary<long, GrandStrategyRoute>();

        public int ActiveRequestCount => _active.Count;

        public bool TrySubmit(GrandStrategyArmy army, int targetTileId,
            IReadOnlyList<int> tileIds, double estimatedArrival,
            int supplyCost)
        {
            if (army == null || army.Disbanded || targetTileId < 0 ||
                tileIds == null || tileIds.Count < 2 ||
                tileIds[0] != army.PositionTileId ||
                tileIds[tileIds.Count - 1] != targetTileId ||
                _active.ContainsKey(army.Id) ||
                !GrandStrategyPathRules.CanIssueOrder(
                    army.Task == GrandStrategyArmyTask.Retreat
                        ? GrandStrategyMovementState.Retreat
                        : GrandStrategyMovementState.Land)) return false;
            _active.Add(army.Id, new GrandStrategyRoute(army.Id,
                targetTileId, tileIds, estimatedArrival, supplyCost));
            army.Task = GrandStrategyArmyTask.March;
            army.Revision++;
            return true;
        }

        public bool TryAdvance(GrandStrategyArmy army)
        {
            if (army == null || !_active.TryGetValue(army.Id,
                out GrandStrategyRoute route) || route.Complete) return false;
            route.Cursor++;
            army.PositionTileId = route.TileIds[route.Cursor];
            army.Revision++;
            if (route.Complete) _active.Remove(army.Id);
            return true;
        }

        public bool Cancel(long armyId)
        {
            return _active.Remove(armyId);
        }

        public void ClearWorld()
        {
            _active.Clear();
        }
    }
}
