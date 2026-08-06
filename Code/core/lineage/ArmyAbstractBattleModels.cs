using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyAbstractBattleOutcome
    {
        NoBattle = 0,
        AttackVictory = 1,
        DefenseVictory = 2
    }

    public sealed class ArmyAbstractBattleParticipant
    {
        public long ArmyId { get; set; } = -1L;
        public long ActorId { get; set; } = -1L;
        public int UnitCount { get; set; }
        public int CommanderStrength { get; set; }
        public bool IsAttacker { get; set; }
        public bool IsSynthetic { get; set; }
        public bool IsProtectedCivilAuthority { get; set; }
        public long OwningCityId { get; set; } = -1L;
    }

    public sealed class ArmyAbstractBattleFacts
    {
        public long WarId { get; set; } = -1L;
        public long TargetCityId { get; set; } = -1L;
        public long ResolutionSequence { get; set; }
        public IReadOnlyList<ArmyAbstractBattleParticipant> Attackers {
            get; set;
        } = Array.Empty<ArmyAbstractBattleParticipant>();
        public IReadOnlyList<ArmyAbstractBattleParticipant> Defenders {
            get; set;
        } = Array.Empty<ArmyAbstractBattleParticipant>();
    }

    public sealed class ArmyAbstractBattleResult
    {
        public ArmyAbstractBattleOutcome Outcome { get; internal set; }
        public int AttackValue { get; internal set; }
        public int DefenseValue { get; internal set; }
        public ulong Seed { get; internal set; }
        public long PrimaryAttackerArmyId { get; internal set; } = -1L;
    }
}
