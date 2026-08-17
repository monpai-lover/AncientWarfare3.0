using System;
using AncientWarfare3.core.court;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct ArmyAbstractBattleCardIdentity :
        IEquatable<ArmyAbstractBattleCardIdentity>,
        IComparable<ArmyAbstractBattleCardIdentity>
    {
        private readonly byte _kind;

        public ArmyAbstractBattleCardIdentity(bool pIsArmy, long pId)
        {
            _kind = pId >= 0L ? (byte)(pIsArmy ? 1 : 2) : (byte)0;
            Id = pId;
        }

        public long Id { get; }
        public bool IsArmy => _kind == 1;
        public bool IsValid => _kind != 0;

        public int CompareTo(ArmyAbstractBattleCardIdentity pOther)
        {
            int kind = _kind.CompareTo(pOther._kind);
            return kind != 0 ? kind : Id.CompareTo(pOther.Id);
        }

        public bool Equals(ArmyAbstractBattleCardIdentity pOther)
        {
            return _kind == pOther._kind && Id == pOther.Id;
        }

        public override bool Equals(object pObject)
        {
            return pObject is ArmyAbstractBattleCardIdentity other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked { return (_kind * 397) ^ Id.GetHashCode(); }
        }
    }

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
        public CustomCourtEffectModifier MoraleModifier { get; set; } =
            CustomCourtEffectModifier.Identity;
        public bool IsAttacker { get; set; }
        public bool IsSynthetic { get; set; }
        public bool IsProtectedCivilAuthority { get; set; }
        public long OwningCityId { get; set; } = -1L;

        // Army identity is authoritative; an actor ID is only a fallback for
        // snapshots that do not have a live army identity. The namespaces are
        // intentionally distinct so IDs from the two stores cannot collide.
        public ArmyAbstractBattleCardIdentity CardIdentity
        {
            get { return ArmyId >= 0L
                ? new ArmyAbstractBattleCardIdentity(true, ArmyId)
                : new ArmyAbstractBattleCardIdentity(false, ActorId); }
        }

        public bool HasCardIdentity { get { return CardIdentity.IsValid; } }
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
