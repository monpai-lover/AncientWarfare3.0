using System;
using System.Collections.Generic;
using System.IO;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.grandstrategy
{
    internal static class GrandStrategyRuntimeHost
    {
        private const string SnapshotFileName = "aw3_grand_strategy_armies.json";
        private static GrandStrategyRuntime _runtime;
        private static long _worldGeneration;
        private static readonly Dictionary<long, List<GrandStrategyArmy>>
            WarArmies = new Dictionary<long, List<GrandStrategyArmy>>();

        internal static bool Active =>
            GrandStrategyRuntimeRules.ShouldRun(
                AWPerformanceSettings.CurrentArmyMode);

        internal static GrandStrategyRuntime Runtime =>
            _runtime ??= new GrandStrategyRuntime(_worldGeneration);

        internal static void Initialize()
        {
            _runtime = new GrandStrategyRuntime(_worldGeneration);
            WarArmies.Clear();
        }

        internal static void OnWarStarted(War war)
        {
            if (!Active || war?.data == null || war.hasEnded()) return;
            long warId = war.data.id;
            if (WarArmies.ContainsKey(warId)) return;
            var raised = new List<GrandStrategyArmy>();
            RaiseSide(war, war.getAttackers(), raised);
            RaiseSide(war, war.getDefenders(), raised);
            WarArmies[warId] = raised;
        }

        internal static void OnWarEnded(War war)
        {
            if (war?.data == null || !WarArmies.TryGetValue(war.data.id,
                    out List<GrandStrategyArmy> armies)) return;
            for (int i = 0; i < armies.Count; i++)
            {
                GrandStrategyArmy army = armies[i];
                GrandStrategyKingdomLedger ledger = Runtime.EnsureLedger(
                    army.KingdomId, 0);
                // The service owns exactly-once disbanding through the army flag.
                var service = new GrandStrategyArmyService(
                    new GrandStrategyIdAllocator(_worldGeneration));
                service.DisbandForWarEnd(army, ledger);
            }
            WarArmies.Remove(war.data.id);
        }

        internal static void Tick()
        {
            Runtime.Tick(AWPerformanceSettings.CurrentArmyMode);
        }

        internal static void ClearWorld()
        {
            _worldGeneration++;
            _runtime?.ClearWorld();
            _runtime = new GrandStrategyRuntime(_worldGeneration);
            WarArmies.Clear();
        }

        internal static bool TryWriteSnapshot(string directory,
            out string error)
        {
            error = string.Empty;
            if (!Active || string.IsNullOrWhiteSpace(directory)) return false;
            try
            {
                // Runtime indexes will expose full snapshots as presentation and
                // multiplayer layers land; this version persists mode/world identity.
                GrandStrategySnapshot snapshot =
                    GrandStrategyPersistence.CreateSnapshot(1,
                        _worldGeneration,
                        Runtime.Ledgers,
                        Runtime.Armies,
                        Runtime.Battles,
                        Array.Empty<string>());
                string path = Path.Combine(Path.GetFullPath(directory),
                    SnapshotFileName);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary,
                    GrandStrategyPersistence.Serialize(snapshot));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static bool TryRestoreSnapshot(string directory,
            out string error)
        {
            error = string.Empty;
            if (!Active || string.IsNullOrWhiteSpace(directory)) return false;
            try
            {
                string path = Path.Combine(Path.GetFullPath(directory),
                    SnapshotFileName);
                if (!File.Exists(path))
                {
                    Initialize();
                    return false;
                }
                GrandStrategySnapshot snapshot =
                    GrandStrategyPersistence.Deserialize(
                        File.ReadAllText(path), _worldGeneration);
                var ledgers = new List<GrandStrategyKingdomLedger>();
                for (int i = 0; i < snapshot.Ledgers.Count; i++)
                {
                    GrandStrategyLedgerSnapshot item = snapshot.Ledgers[i];
                    ledgers.Add(GrandStrategyKingdomLedger.Restore(
                        item.KingdomId, item.AvailableManpower,
                        item.RaisedManpower, item.WoundedManpower,
                        item.DispersedManpower, item.PermanentDeaths,
                        item.Prisoners));
                }
                var armies = new List<GrandStrategyArmy>();
                for (int i = 0; i < snapshot.Armies.Count; i++)
                {
                    GrandStrategyArmySnapshot item = snapshot.Armies[i];
                    var composition = new GrandStrategyTroopComposition();
                    for (int type = 0; type < 5; type++)
                        composition[(GrandStrategyTroopType)type] =
                            item.Troops != null && type < item.Troops.Length
                                ? item.Troops[type] : 0;
                    var army = new GrandStrategyArmy(item.Id,
                        item.KingdomId, item.WarId, composition)
                    {
                        PositionTileId = item.PositionTileId,
                        Task = item.Task,
                        Revision = item.Revision
                    };
                    armies.Add(army);
                }
                var battles = new List<GrandStrategyBattleState>();
                for (int i = 0; i < snapshot.Battles.Count; i++)
                {
                    GrandStrategyBattleSnapshot item = snapshot.Battles[i];
                    var battle = new GrandStrategyBattleState(item.BattleId,
                        item.WarId, item.AttackerArmyId,
                        item.DefenderArmyId, item.AttackerStrength,
                        item.DefenderStrength, item.Frontage)
                    {
                        Round = item.Round,
                        Phase = item.Phase
                    };
                    battles.Add(battle);
                }
                _runtime = new GrandStrategyRuntime(snapshot.WorldGeneration);
                _runtime.Rebuild(ledgers, armies, battles);
                WarArmies.Clear();
                for (int i = 0; i < armies.Count; i++)
                {
                    GrandStrategyArmy army = armies[i];
                    if (!WarArmies.TryGetValue(army.WarId,
                            out List<GrandStrategyArmy> byWar))
                    {
                        byWar = new List<GrandStrategyArmy>();
                        WarArmies.Add(army.WarId, byWar);
                    }
                    byWar.Add(army);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Initialize();
                return false;
            }
        }

        internal static bool TryCommitOccupation(long siegeId, long warId,
            long cityId, long armyId)
        {
            if (!Active) return false;
            City city = FindCity(cityId);
            if (city?.data == null || !TryFindArmy(warId, armyId,
                    out GrandStrategyArmy army)) return false;
            Kingdom occupier = FindKingdom(army.KingdomId);
            return occupier?.data != null &&
                core.lineage.WarScoreService.
                    TryFreezeCityOccupation(city, occupier);
        }

        private static void RaiseSide(War war, IEnumerable<Kingdom> kingdoms,
            List<GrandStrategyArmy> destination)
        {
            if (kingdoms == null) return;
            foreach (Kingdom kingdom in kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                int population = Math.Max(0, kingdom.getPopulationPeople());
                int totalManpower = Math.Max(100, population / 5);
                GrandStrategyKingdomLedger ledger = Runtime.EnsureLedger(
                    kingdom.id, totalManpower);
                int raise = Math.Min(ledger.AvailableManpower,
                    Math.Max(50, totalManpower / 2));
                if (raise <= 0) continue;
                IReadOnlyList<GrandStrategyArmy> armies = Runtime.RaiseForWar(
                    kingdom.id, war.data.id, raise, technology: 1,
                    supplyLimit: 1000, maximumArmies: 8);
                int rallyTile = kingdom.capital?.getTile()?.data?.tile_id ?? -1;
                for (int a = 0; a < armies.Count; a++)
                {
                    armies[a].PositionTileId = rallyTile;
                    destination.Add(armies[a]);
                }
            }
        }

        private static bool TryFindArmy(long warId, long armyId,
            out GrandStrategyArmy army)
        {
            army = null;
            if (!WarArmies.TryGetValue(warId,
                    out List<GrandStrategyArmy> armies)) return false;
            for (int i = 0; i < armies.Count; i++)
                if (armies[i].Id == armyId)
                {
                    army = armies[i];
                    return true;
                }
            return false;
        }

        private static City FindCity(long id)
        {
            try { return World.world?.cities?.get(id); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long id)
        {
            try { return World.world?.kingdoms?.get(id); }
            catch { return null; }
        }
    }
}
