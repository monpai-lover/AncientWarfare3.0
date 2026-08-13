using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.grandstrategy
{
    public sealed class GrandStrategyArmyService
    {
        private readonly GrandStrategyIdAllocator _ids;

        public GrandStrategyArmyService(GrandStrategyIdAllocator ids)
        {
            _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        }

        public List<GrandStrategyArmy> RaiseForWar(
            GrandStrategyKingdomLedger ledger, long warId, int manpower,
            int technology, int supplyLimit, int maximumArmies)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (warId < 0 || manpower <= 0 || supplyLimit <= 0 || maximumArmies <= 0)
                throw new ArgumentOutOfRangeException();
            if (manpower > ledger.AvailableManpower)
                throw new InvalidOperationException("insufficient_manpower");
            if (!GrandStrategyLedgerRules.TryRaise(ledger, manpower,
                out string error)) throw new InvalidOperationException(error);
            int count = Math.Min(maximumArmies,
                Math.Max(1, (manpower + supplyLimit - 1) / supplyLimit));
            var result = new List<GrandStrategyArmy>(count);
            int baseSize = manpower / count;
            int remainder = manpower % count;
            for (int i = 0; i < count; i++)
            {
                int size = baseSize + (i < remainder ? 1 : 0);
                var army = new GrandStrategyArmy(_ids.NextArmyId(),
                    ledger.KingdomId, warId,
                    GrandStrategyTroopRules.Compose(size, technology));
                army.PositionTileId = -1;
                result.Add(army);
            }
            return result;
        }

        public GrandStrategyArmy Split(GrandStrategyArmy army, int strength)
        {
            if (army == null || army.Disbanded || strength <= 0 ||
                strength >= army.TotalStrength)
                throw new InvalidOperationException("invalid_split");
            var extracted = new GrandStrategyTroopComposition();
            int remaining = strength;
            foreach (GrandStrategyTroopType type in Enum.GetValues(typeof(GrandStrategyTroopType)))
            {
                int take = Math.Min(army.Composition[type], remaining);
                extracted[type] = take;
                army.Composition[type] -= take;
                remaining -= take;
            }
            var split = new GrandStrategyArmy(_ids.NextArmyId(), army.KingdomId,
                army.WarId, extracted) { PositionTileId = army.PositionTileId };
            army.Revision++;
            return split;
        }

        public bool DisbandForWarEnd(GrandStrategyArmy army,
            GrandStrategyKingdomLedger ledger)
        {
            if (army == null || ledger == null || army.Disbanded ||
                army.KingdomId != ledger.KingdomId) return false;
            ledger.AvailableManpower += army.TotalStrength;
            foreach (GrandStrategyTroopType type in Enum.GetValues(typeof(GrandStrategyTroopType)))
                army.Composition[type] = 0;
            army.Disbanded = true;
            army.Task = GrandStrategyArmyTask.Disband;
            army.Revision++;
            return true;
        }
    }
}
