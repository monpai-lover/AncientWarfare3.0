using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class ArmyAbstractBattleRules
    {
        public const int CommanderStrengthCap = 100;
        public const int StrongerRatioNumerator = 125;
        public const int StrongerRatioDenominator = 100;

        public static ArmyAbstractBattleResult Resolve(
            ArmyAbstractBattleFacts pFacts)
        {
            if (pFacts == null) return EmptyResult();
            int attack = Aggregate(pFacts.Attackers);
            int defense = Aggregate(pFacts.Defenders);
            ulong seed = StableSeed(pFacts);
            ArmyAbstractBattleOutcome outcome;
            if (attack == 0 && defense == 0)
                outcome = ArmyAbstractBattleOutcome.NoBattle;
            else if (defense == 0)
                outcome = ArmyAbstractBattleOutcome.AttackVictory;
            else if (attack == 0)
                outcome = ArmyAbstractBattleOutcome.DefenseVictory;
            else
                outcome = ResolveNonEmpty(attack, defense, seed);

            ArmyAbstractBattleParticipant primary =
                SelectPrimaryAttacker(pFacts.Attackers);
            return new ArmyAbstractBattleResult
            {
                Outcome = outcome,
                AttackValue = attack,
                DefenseValue = defense,
                Seed = seed,
                PrimaryAttackerArmyId = primary?.ArmyId ?? -1L
            };
        }

        public static ulong ParticipantHash(ArmyAbstractBattleFacts pFacts)
        {
            if (pFacts == null) return 0UL;
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                MixParticipants(ref hash, pFacts.Attackers, true);
                MixParticipants(ref hash, pFacts.Defenders, false);
                return hash;
            }
        }

        public static int Aggregate(
            IReadOnlyList<ArmyAbstractBattleParticipant> pParticipants)
        {
            if (pParticipants == null || pParticipants.Count == 0)
                return 0;
            long total = 0L;
            for (int i = 0; i < pParticipants.Count; i++)
            {
                ArmyAbstractBattleParticipant participant = pParticipants[i];
                if (participant == null) continue;
                total += AdjustedCardValue(participant.UnitCount,
                    participant.CommanderStrength);
                if (total >= int.MaxValue) return int.MaxValue;
            }
            return (int)Math.Max(0L, total);
        }

        public static int AdjustedCardValue(int pUnitCount,
            int pCommanderStrength)
        {
            int units = Math.Max(0, pUnitCount);
            if (units == 0) return 0;
            int commander = Math.Max(0, Math.Min(
                CommanderStrengthCap, pCommanderStrength));
            long value = (long)units * (100L + commander) / 100L;
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        public static ArmyAbstractBattleParticipant SelectPrimaryAttacker(
            IReadOnlyList<ArmyAbstractBattleParticipant> pAttackers)
        {
            ArmyAbstractBattleParticipant selected = null;
            if (pAttackers == null) return null;
            for (int i = 0; i < pAttackers.Count; i++)
            {
                ArmyAbstractBattleParticipant candidate = pAttackers[i];
                if (candidate == null || !candidate.IsAttacker ||
                    candidate.UnitCount <= 0) continue;
                if (selected == null ||
                    IsPrimaryBefore(candidate, selected)) selected = candidate;
            }
            return selected;
        }

        private static bool IsPrimaryBefore(
            ArmyAbstractBattleParticipant pCandidate,
            ArmyAbstractBattleParticipant pSelected)
        {
            int candidateValue = AdjustedCardValue(pCandidate.UnitCount,
                pCandidate.CommanderStrength);
            int selectedValue = AdjustedCardValue(pSelected.UnitCount,
                pSelected.CommanderStrength);
            return candidateValue > selectedValue ||
                   (candidateValue == selectedValue &&
                    pCandidate.ArmyId < pSelected.ArmyId);
        }

        private static ArmyAbstractBattleOutcome ResolveNonEmpty(int pAttack,
            int pDefense, ulong pSeed)
        {
            int stronger = Math.Max(pAttack, pDefense);
            int weaker = Math.Min(pAttack, pDefense);
            if ((long)stronger * StrongerRatioDenominator >=
                (long)weaker * StrongerRatioNumerator)
                return pAttack > pDefense
                    ? ArmyAbstractBattleOutcome.AttackVictory
                    : ArmyAbstractBattleOutcome.DefenseVictory;

            ulong total = (ulong)pAttack + (ulong)pDefense;
            ulong roll = total == 0UL ? 0UL : pSeed % total;
            return roll < (ulong)pAttack
                ? ArmyAbstractBattleOutcome.AttackVictory
                : ArmyAbstractBattleOutcome.DefenseVictory;
        }

        private static ulong StableSeed(ArmyAbstractBattleFacts pFacts)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                Mix(ref hash, pFacts.WarId);
                Mix(ref hash, pFacts.TargetCityId);
                Mix(ref hash, pFacts.ResolutionSequence);
                MixParticipants(ref hash, pFacts.Attackers, true);
                MixParticipants(ref hash, pFacts.Defenders, false);
                return hash;
            }
        }

        private static void MixParticipants(ref ulong pHash,
            IReadOnlyList<ArmyAbstractBattleParticipant> pParticipants,
            bool pAttacker)
        {
            var ordered = new List<ArmyAbstractBattleParticipant>();
            if (pParticipants != null)
                for (int i = 0; i < pParticipants.Count; i++)
                    if (pParticipants[i] != null) ordered.Add(pParticipants[i]);
            ordered.Sort((pLeft, pRight) =>
            {
                int id = pLeft.ArmyId.CompareTo(pRight.ArmyId);
                if (id != 0) return id;
                return pLeft.ActorId.CompareTo(pRight.ActorId);
            });
            Mix(ref pHash, pAttacker ? 1L : 2L);
            Mix(ref pHash, ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                ArmyAbstractBattleParticipant participant = ordered[i];
                Mix(ref pHash, participant.ArmyId);
                Mix(ref pHash, participant.ActorId);
                Mix(ref pHash, participant.UnitCount);
                Mix(ref pHash, participant.CommanderStrength);
                Mix(ref pHash, participant.OwningCityId);
                Mix(ref pHash, participant.IsSynthetic ? 1L : 0L);
                Mix(ref pHash, participant.IsProtectedCivilAuthority ? 1L : 0L);
            }
        }

        private static void Mix(ref ulong pHash, long pValue)
        {
            unchecked
            {
                ulong value = (ulong)pValue;
                for (int i = 0; i < 8; i++)
                {
                    pHash ^= (byte)(value & 0xffUL);
                    pHash *= 1099511628211UL;
                    value >>= 8;
                }
            }
        }

        private static ArmyAbstractBattleResult EmptyResult()
        {
            return new ArmyAbstractBattleResult
            {
                Outcome = ArmyAbstractBattleOutcome.NoBattle,
                AttackValue = 0,
                DefenseValue = 0,
                Seed = 0UL,
                PrimaryAttackerArmyId = -1L
            };
        }
    }
}
