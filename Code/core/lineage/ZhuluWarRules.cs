using System;

namespace AncientWarfare3.core.lineage
{
    public readonly struct ZhuluEligibilityFacts
    {
        public ZhuluEligibilityFacts(MandatePhase phase,
            bool attackerValid, bool defenderValid,
            bool attackerMandateEligible, bool defenderMandateEligible,
            bool attackerIsSubject, bool sameSubjectTree,
            bool diplomaticBlocked, bool sameAlliance,
            bool alreadyAtWar, bool ageOverride = false,
            bool hasMandateHistory = true)
        {
            Phase = phase;
            AttackerValid = attackerValid;
            DefenderValid = defenderValid;
            AttackerMandateEligible = attackerMandateEligible;
            DefenderMandateEligible = defenderMandateEligible;
            AttackerIsSubject = attackerIsSubject;
            SameSubjectTree = sameSubjectTree;
            DiplomaticBlocked = diplomaticBlocked;
            SameAlliance = sameAlliance;
            AlreadyAtWar = alreadyAtWar;
            AgeOverride = ageOverride;
            HasMandateHistory = hasMandateHistory;
        }

        public MandatePhase Phase { get; }
        public bool AttackerValid { get; }
        public bool DefenderValid { get; }
        public bool AttackerMandateEligible { get; }
        public bool DefenderMandateEligible { get; }
        public bool AttackerIsSubject { get; }
        public bool SameSubjectTree { get; }
        public bool DiplomaticBlocked { get; }
        public bool SameAlliance { get; }
        public bool AlreadyAtWar { get; }
        public bool AgeOverride { get; }
        public bool HasMandateHistory { get; }
    }

    public enum ZhuluWarOutcome
    {
        None,
        Attackers,
        Defenders,
        Ambiguous
    }

    public static class ZhuluWarRules
    {
        public const string WarTypeId = "zhulu_war";
        public const string GoalTypeId = "zhulu_annexation";
        public const string SettlementBlockedReason =
            "zhulu_requires_total_annexation";
        public const string HopeAgeId = "age_hope";
        public const string HopeAgeBlockedReason =
            "zhulu_blocked_in_hope_age";
        public const double DiplomaticDeclarationChance = .70d;

        public static bool CanCreateDeclaration(string currentAgeId)
        {
            return !string.Equals(currentAgeId, HopeAgeId,
                StringComparison.Ordinal);
        }

        public static bool ShouldIssueDiplomaticDeclaration(double pRoll)
        {
            return pRoll >= 0d && pRoll < DiplomaticDeclarationChance;
        }

        public static bool CanStart(ZhuluEligibilityFacts facts)
        {
            bool eraGate = facts.AgeOverride ||
                           facts.HasMandateHistory &&
                           facts.Phase == MandatePhase.Chaos &&
                           facts.AttackerMandateEligible &&
                           facts.DefenderMandateEligible;
            return eraGate && facts.AttackerValid && facts.DefenderValid &&
                   !facts.AttackerIsSubject && !facts.SameSubjectTree &&
                   !facts.DiplomaticBlocked && !facts.SameAlliance &&
                   !facts.AlreadyAtWar;
        }

        public static bool BlocksOrdinarySettlement(string warType,
            bool active)
        {
            return active && warType == WarTypeId;
        }

        public static bool ShouldUseVanillaTotalWar()
        {
            return false;
        }

        public static bool HasActiveClaimants(bool activeRebels,
            bool activeZhulu)
        {
            return activeRebels || activeZhulu;
        }

        public static ZhuluWarOutcome ResolveOutcome(bool attackerValid,
            int attackerCities, bool defenderValid, int defenderCities)
        {
            bool attackerAlive = attackerValid && attackerCities > 0;
            bool defenderAlive = defenderValid && defenderCities > 0;
            if (attackerAlive == defenderAlive)
            {
                return attackerAlive
                    ? ZhuluWarOutcome.None
                    : ZhuluWarOutcome.Ambiguous;
            }

            return attackerAlive
                ? ZhuluWarOutcome.Attackers
                : ZhuluWarOutcome.Defenders;
        }

        public static double ScoreTarget(float attackerPower,
            float defenderPower, bool directlyAdjacent,
            float capitalDistance)
        {
            float defender = System.Math.Max(1f, defenderPower);
            if (attackerPower < defender * .55f) return double.MinValue;
            double ratio = System.Math.Min(3d, attackerPower / defender);
            double distancePenalty = System.Math.Min(300d,
                System.Math.Max(0f, capitalDistance) * 2d);
            return 600d + (directlyAdjacent ? 200d : 0d) +
                   ratio * 100d - distancePenalty;
        }

        // Used only by the Zhulu-era fallback when the normal power gate
        // leaves a realm with no possible target. It keeps the attempt
        // strategically unattractive without making the realm permanently
        // inert; ordinary-era target scoring remains unchanged.
        public static double ScoreWeakFallbackTarget(float attackerPower,
            float defenderPower, bool directlyAdjacent,
            float capitalDistance)
        {
            double attacker = Math.Max(1d, attackerPower);
            double defender = Math.Max(1d, defenderPower);
            double ratio = Math.Min(1d, attacker / defender);
            double distancePenalty = Math.Min(300d,
                Math.Max(0f, capitalDistance) * 2d);
            return -1000d + (directlyAdjacent ? 200d : 0d) +
                   ratio * 100d - distancePenalty;
        }

        public static bool CanQueueSettlement(bool warValid, bool active,
            bool alreadyQueued)
        {
            return warValid && active && !alreadyQueued;
        }

        public static long ResolveCaptureRecipient(
            bool capturerOnAttackerSide, long mainAttackerId,
            long mainDefenderId)
        {
            return capturerOnAttackerSide
                ? mainAttackerId
                : mainDefenderId;
        }
    }
}
