using System;

namespace AncientWarfare3.core.lineage
{
    public readonly struct MandateMilitaryQuality : IEquatable<MandateMilitaryQuality>
    {
        public MandateMilitaryQuality(float pHealth, float pDamage,
            float pWarfare)
        {
            Health = pHealth;
            Damage = pDamage;
            Warfare = pWarfare;
        }

        public float Health { get; }
        public float Damage { get; }
        public float Warfare { get; }

        public bool Equals(MandateMilitaryQuality pOther)
        {
            return Health.Equals(pOther.Health) && Damage.Equals(pOther.Damage) &&
                   Warfare.Equals(pOther.Warfare);
        }

        public override bool Equals(object pObject)
        {
            return pObject is MandateMilitaryQuality other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Health.GetHashCode();
                hash = hash * 397 ^ Damage.GetHashCode();
                return hash * 397 ^ Warfare.GetHashCode();
            }
        }
    }

    public static class MandateMilitaryPhaseRules
    {
        public const string GoldenStatusId = "aw_mandate_army_golden";
        public const string DeclineStatusId = "aw_mandate_army_decline";
        public const string ChaosStatusId = "aw_mandate_army_chaos";
        public const string RenewalStatusId = "aw_mandate_army_renewal";

        public static float QuantityMultiplier(MandatePhase pPhase)
        {
            return pPhase switch
            {
                MandatePhase.Renewal => 1.25f,
                MandatePhase.Golden => 1.15f,
                MandatePhase.Decline => 0.90f,
                MandatePhase.Chaos => 0.75f,
                _ => 1f
            };
        }

        public static int EffectiveWarriorSlots(int baseSlots,
            bool activeMandate, MandatePhase pPhase)
        {
            int slots = Math.Max(0, baseSlots);
            if (!activeMandate || slots == 0) return slots;
            int adjusted = (int)Math.Round(slots * QuantityMultiplier(pPhase),
                MidpointRounding.AwayFromZero);
            return Math.Max(1, adjusted);
        }

        public static string ExpectedStatusId(bool activeMandate,
            bool warrior, MandatePhase pPhase)
        {
            if (!activeMandate || !warrior) return "";
            return pPhase switch
            {
                MandatePhase.Renewal => RenewalStatusId,
                MandatePhase.Golden => GoldenStatusId,
                MandatePhase.Decline => DeclineStatusId,
                MandatePhase.Chaos => ChaosStatusId,
                _ => ""
            };
        }

        public static MandateMilitaryQuality Quality(MandatePhase pPhase)
        {
            return pPhase switch
            {
                MandatePhase.Renewal => new MandateMilitaryQuality(10f, 2f, 2f),
                MandatePhase.Golden => new MandateMilitaryQuality(5f, 1f, 1f),
                MandatePhase.Decline => new MandateMilitaryQuality(-5f, -1f, -1f),
                MandatePhase.Chaos => new MandateMilitaryQuality(-10f, -2f, -2f),
                _ => default
            };
        }

        public static bool IsPhaseStatus(string pStatusId)
        {
            return pStatusId == GoldenStatusId || pStatusId == DeclineStatusId ||
                   pStatusId == ChaosStatusId || pStatusId == RenewalStatusId;
        }

        public static bool NeedsReconcile(bool hasExpectedStatus,
            int activePhaseStatusCount)
        {
            int expectedCount = hasExpectedStatus ? 1 : 0;
            return Math.Max(0, activePhaseStatusCount) != expectedCount;
        }
    }
}
