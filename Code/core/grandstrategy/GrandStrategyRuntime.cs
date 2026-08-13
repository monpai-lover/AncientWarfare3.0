using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.grandstrategy
{
    public sealed class GrandStrategyRuntime
    {
        private readonly Dictionary<long, GrandStrategyKingdomLedger> _ledgers =
            new Dictionary<long, GrandStrategyKingdomLedger>();
        private readonly Dictionary<long, GrandStrategyArmy> _armies =
            new Dictionary<long, GrandStrategyArmy>();
        private readonly Dictionary<long, GrandStrategyBattleState> _battles =
            new Dictionary<long, GrandStrategyBattleState>();
        private readonly GrandStrategyArmyService _armyService;
        private readonly GrandStrategyBattleService _battleService =
            new GrandStrategyBattleService();
        private readonly GrandStrategyIdAllocator _ids;

        public GrandStrategyRuntime(long worldGeneration)
        {
            _ids = new GrandStrategyIdAllocator(worldGeneration);
            _armyService = new GrandStrategyArmyService(_ids);
        }

        public int LedgerCount => _ledgers.Count;
        public int ArmyCount => _armies.Count;
        public int BattleCount => _battles.Count;
        public IReadOnlyCollection<GrandStrategyKingdomLedger> Ledgers =>
            _ledgers.Values;
        public IReadOnlyCollection<GrandStrategyArmy> Armies =>
            _armies.Values;
        public IReadOnlyCollection<GrandStrategyBattleState> Battles =>
            _battles.Values;

        public GrandStrategyKingdomLedger EnsureLedger(long kingdomId,
            int initialManpower)
        {
            if (_ledgers.TryGetValue(kingdomId, out GrandStrategyKingdomLedger existing))
                return existing;
            var ledger = new GrandStrategyKingdomLedger(kingdomId,
                initialManpower);
            _ledgers.Add(kingdomId, ledger);
            return ledger;
        }

        public IReadOnlyList<GrandStrategyArmy> RaiseForWar(long kingdomId,
            long warId, int manpower, int technology, int supplyLimit,
            int maximumArmies)
        {
            GrandStrategyKingdomLedger ledger = _ledgers[kingdomId];
            List<GrandStrategyArmy> raised = _armyService.RaiseForWar(ledger,
                warId, manpower, technology, supplyLimit, maximumArmies);
            for (int i = 0; i < raised.Count; i++) _armies.Add(raised[i].Id, raised[i]);
            return raised;
        }

        public bool TryAddBattle(GrandStrategyBattleState battle)
        {
            if (battle == null || _battles.ContainsKey(battle.BattleId)) return false;
            _battles.Add(battle.BattleId, battle);
            return true;
        }

        public void Tick(GrandStrategyArmyMode mode)
        {
            if (!GrandStrategyRuntimeRules.ShouldRun(mode)) return;
            foreach (GrandStrategyBattleState battle in _battles.Values)
            {
                if (battle.Phase == GrandStrategyBattlePhase.Completed) continue;
                // Monthly round scheduling is driven by the host integration;
                // this method only guarantees that inactive modes do nothing.
            }
        }

        public void ClearWorld()
        {
            _battles.Clear();
            _armies.Clear();
            _ledgers.Clear();
        }

        public void Rebuild(IEnumerable<GrandStrategyKingdomLedger> ledgers,
            IEnumerable<GrandStrategyArmy> armies,
            IEnumerable<GrandStrategyBattleState> battles)
        {
            ClearWorld();
            if (ledgers != null)
                foreach (GrandStrategyKingdomLedger ledger in ledgers)
                    if (ledger != null) _ledgers[ledger.KingdomId] = ledger;
            if (armies != null)
                foreach (GrandStrategyArmy army in armies)
                    if (army != null && !army.Disbanded) _armies[army.Id] = army;
            if (battles != null)
                foreach (GrandStrategyBattleState battle in battles)
                    if (battle != null && battle.Phase != GrandStrategyBattlePhase.Completed)
                        _battles[battle.BattleId] = battle;
        }

        public bool TryGetLedger(long kingdomId,
            out GrandStrategyKingdomLedger ledger)
        {
            return _ledgers.TryGetValue(kingdomId, out ledger);
        }
    }
}
