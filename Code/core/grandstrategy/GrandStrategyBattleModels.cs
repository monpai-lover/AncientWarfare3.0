using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.grandstrategy
{
    public enum GrandStrategyBattlePhase
    {
        Engagement = 0,
        MainBattle = 1,
        Rout = 2,
        Pursuit = 3,
        Completed = 4
    }

    public sealed class GrandStrategyBattleRoundInput
    {
        public GrandStrategyBattleRoundInput(long worldSeed,
            int terrainModifier, int attackerTechnology,
            int defenderTechnology, int attackerTraining,
            int defenderTraining, int attackerEquipment,
            int defenderEquipment, int attackerCommanderBonus,
            int defenderCommanderBonus, int weatherModifier)
        {
            WorldSeed = worldSeed;
            TerrainModifier = terrainModifier;
            AttackerTechnology = attackerTechnology;
            DefenderTechnology = defenderTechnology;
            AttackerTraining = attackerTraining;
            DefenderTraining = defenderTraining;
            AttackerEquipment = attackerEquipment;
            DefenderEquipment = defenderEquipment;
            AttackerCommanderBonus = attackerCommanderBonus;
            DefenderCommanderBonus = defenderCommanderBonus;
            WeatherModifier = weatherModifier;
        }

        public long WorldSeed { get; }
        public int TerrainModifier { get; }
        public int AttackerTechnology { get; }
        public int DefenderTechnology { get; }
        public int AttackerTraining { get; }
        public int DefenderTraining { get; }
        public int AttackerEquipment { get; }
        public int DefenderEquipment { get; }
        public int AttackerCommanderBonus { get; }
        public int DefenderCommanderBonus { get; }
        public int WeatherModifier { get; }

        internal string Fingerprint => string.Join(":", WorldSeed,
            TerrainModifier, AttackerTechnology, DefenderTechnology,
            AttackerTraining, DefenderTraining, AttackerEquipment,
            DefenderEquipment, AttackerCommanderBonus,
            DefenderCommanderBonus, WeatherModifier);
    }

    public sealed class GrandStrategyBattleRoundResult
    {
        public int Round { get; internal set; }
        public int AttackerRoll { get; internal set; }
        public int DefenderRoll { get; internal set; }
        public int AttackerLosses { get; internal set; }
        public int DefenderLosses { get; internal set; }
    }

    public sealed class GrandStrategyBattleReport
    {
        public GrandStrategyBattleReport(long battleId)
        {
            BattleId = battleId;
        }

        public long BattleId { get; }
        public List<GrandStrategyBattleRoundResult> Rounds { get; } =
            new List<GrandStrategyBattleRoundResult>();
    }

    public sealed class GrandStrategyReinforcement
    {
        public GrandStrategyReinforcement(long armyId, int strength,
            bool isAttacker, int arriveRound)
        {
            ArmyId = armyId;
            Strength = Math.Max(0, strength);
            IsAttacker = isAttacker;
            ArriveRound = Math.Max(1, arriveRound);
        }

        public long ArmyId { get; }
        public int Strength { get; }
        public bool IsAttacker { get; }
        public int ArriveRound { get; }
    }

    public sealed class GrandStrategyBattleState
    {
        public GrandStrategyBattleState(long battleId, long warId,
            long attackerArmyId, long defenderArmyId,
            int attackerStrength, int defenderStrength, int frontage)
        {
            BattleId = battleId;
            WarId = warId;
            AttackerArmyId = attackerArmyId;
            DefenderArmyId = defenderArmyId;
            AttackerStrength = Math.Max(0, attackerStrength);
            DefenderStrength = Math.Max(0, defenderStrength);
            Frontage = Math.Max(1, frontage);
            Phase = GrandStrategyBattlePhase.MainBattle;
        }

        public long BattleId { get; }
        public long WarId { get; }
        public long AttackerArmyId { get; }
        public long DefenderArmyId { get; }
        public int AttackerStrength { get; internal set; }
        public int DefenderStrength { get; internal set; }
        public int Frontage { get; }
        public int Round { get; internal set; }
        public GrandStrategyBattlePhase Phase { get; internal set; }
        public List<GrandStrategyReinforcement> PendingReinforcements { get; } =
            new List<GrandStrategyReinforcement>();
        public GrandStrategyBattleReport Report { get; internal set; }
        internal HashSet<int> CommittedRounds { get; } = new HashSet<int>();
        internal string LastInputFingerprint { get; set; }
    }
}
