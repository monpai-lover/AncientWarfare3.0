using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AncientWarfare3.core.grandstrategy
{
    [DataContract]
    public sealed class GrandStrategySnapshot
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; }
        [DataMember(Order = 2)] public long WorldGeneration { get; set; }
        [DataMember(Order = 3)] public List<GrandStrategyLedgerSnapshot> Ledgers { get; set; } = new List<GrandStrategyLedgerSnapshot>();
        [DataMember(Order = 4)] public List<GrandStrategyArmySnapshot> Armies { get; set; } = new List<GrandStrategyArmySnapshot>();
        [DataMember(Order = 5)] public List<GrandStrategyBattleSnapshot> Battles { get; set; } = new List<GrandStrategyBattleSnapshot>();
        [DataMember(Order = 6)] public List<string> CommittedTransactions { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class GrandStrategyLedgerSnapshot
    {
        [DataMember(Order = 1)] public long KingdomId { get; set; }
        [DataMember(Order = 2)] public int AvailableManpower { get; set; }
        [DataMember(Order = 3)] public int RaisedManpower { get; set; }
        [DataMember(Order = 4)] public int WoundedManpower { get; set; }
        [DataMember(Order = 5)] public int DispersedManpower { get; set; }
        [DataMember(Order = 6)] public int PermanentDeaths { get; set; }
        [DataMember(Order = 7)] public int Prisoners { get; set; }
    }

    [DataContract]
    public sealed class GrandStrategyArmySnapshot
    {
        [DataMember(Order = 1)] public long Id { get; set; }
        [DataMember(Order = 2)] public long KingdomId { get; set; }
        [DataMember(Order = 3)] public long WarId { get; set; }
        [DataMember(Order = 4)] public int PositionTileId { get; set; }
        [DataMember(Order = 5)] public int Revision { get; set; }
        [DataMember(Order = 6)] public GrandStrategyArmyTask Task { get; set; }
        [DataMember(Order = 7)] public int[] Troops { get; set; } = new int[5];
    }

    [DataContract]
    public sealed class GrandStrategyBattleSnapshot
    {
        [DataMember(Order = 1)] public long BattleId { get; set; }
        [DataMember(Order = 2)] public long WarId { get; set; }
        [DataMember(Order = 3)] public long AttackerArmyId { get; set; }
        [DataMember(Order = 4)] public long DefenderArmyId { get; set; }
        [DataMember(Order = 5)] public int AttackerStrength { get; set; }
        [DataMember(Order = 6)] public int DefenderStrength { get; set; }
        [DataMember(Order = 7)] public int Frontage { get; set; }
        [DataMember(Order = 8)] public int Round { get; set; }
        [DataMember(Order = 9)] public GrandStrategyBattlePhase Phase { get; set; }
    }

    public static class GrandStrategyPersistence
    {
        public static GrandStrategySnapshot CreateSnapshot(int schemaVersion,
            long worldGeneration,
            IEnumerable<GrandStrategyKingdomLedger> ledgers,
            IEnumerable<GrandStrategyArmy> armies,
            IEnumerable<GrandStrategyBattleState> battles,
            IEnumerable<string> committedTransactions)
        {
            if (schemaVersion <= 0 || worldGeneration < 0)
                throw new ArgumentOutOfRangeException();
            var snapshot = new GrandStrategySnapshot
            {
                SchemaVersion = schemaVersion,
                WorldGeneration = worldGeneration
            };
            if (ledgers != null)
                foreach (GrandStrategyKingdomLedger ledger in ledgers)
                {
                    if (ledger == null) continue;
                    snapshot.Ledgers.Add(new GrandStrategyLedgerSnapshot
                    {
                        KingdomId = ledger.KingdomId,
                        AvailableManpower = ledger.AvailableManpower,
                        RaisedManpower = ledger.RaisedManpower,
                        WoundedManpower = ledger.WoundedManpower,
                        DispersedManpower = ledger.DispersedManpower,
                        PermanentDeaths = ledger.PermanentDeaths,
                        Prisoners = ledger.Prisoners
                    });
                }
            if (armies != null)
                foreach (GrandStrategyArmy army in armies)
                {
                    if (army == null || army.Disbanded) continue;
                    var troops = new int[5];
                    foreach (GrandStrategyTroopType type in Enum.GetValues(typeof(GrandStrategyTroopType)))
                        troops[(int)type] = army.Composition[type];
                    snapshot.Armies.Add(new GrandStrategyArmySnapshot
                    {
                        Id = army.Id,
                        KingdomId = army.KingdomId,
                        WarId = army.WarId,
                        PositionTileId = army.PositionTileId,
                        Revision = army.Revision,
                        Task = army.Task,
                        Troops = troops
                    });
                }
            if (battles != null)
                foreach (GrandStrategyBattleState battle in battles)
                {
                    if (battle == null) continue;
                    snapshot.Battles.Add(new GrandStrategyBattleSnapshot
                    {
                        BattleId = battle.BattleId,
                        WarId = battle.WarId,
                        AttackerArmyId = battle.AttackerArmyId,
                        DefenderArmyId = battle.DefenderArmyId,
                        AttackerStrength = battle.AttackerStrength,
                        DefenderStrength = battle.DefenderStrength,
                        Frontage = battle.Frontage,
                        Round = battle.Round,
                        Phase = battle.Phase
                    });
                }
            if (committedTransactions != null)
                foreach (string key in committedTransactions)
                    if (!string.IsNullOrWhiteSpace(key))
                        snapshot.CommittedTransactions.Add(key);
            return snapshot;
        }

        public static string Serialize(GrandStrategySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var serializer = new DataContractJsonSerializer(typeof(GrandStrategySnapshot));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, snapshot);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static GrandStrategySnapshot Deserialize(string json,
            long expectedWorldGeneration)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("grand_strategy_snapshot_missing");
            var serializer = new DataContractJsonSerializer(typeof(GrandStrategySnapshot));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var snapshot = serializer.ReadObject(stream) as GrandStrategySnapshot;
                if (snapshot == null || snapshot.SchemaVersion <= 0)
                    throw new InvalidOperationException("grand_strategy_snapshot_invalid");
                if (snapshot.WorldGeneration != expectedWorldGeneration)
                    throw new InvalidOperationException("grand_strategy_world_generation_mismatch");
                return snapshot;
            }
        }
    }
}
