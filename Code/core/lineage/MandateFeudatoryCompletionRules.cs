using System;

namespace AncientWarfare3.core.lineage
{
    public static class MandateFeudatoryCompletionRules
    {
        public const string CollapseReason = "mandate_collapse";
        public const string RestorationReason = "feudatory_restoration";
        public const string RestorationOrigin = "feudatory_restoration";
        public const string RestorationClaimant = "dynastic_restoration";
        public const int MaximumCollapseAttempts = 2;

        public static bool CanActivateCollapseFeudatory(MandatePhase pPhase,
            bool mandateActive, bool snapshotActive, bool parentAlive,
            bool princeValid, bool citiesValid)
        {
            return pPhase == MandatePhase.Chaos && !mandateActive &&
                   snapshotActive && parentAlive && princeValid && citiesValid;
        }

        public static bool ShouldRetryCollapse(int pAttempts)
        {
            return pAttempts < MaximumCollapseAttempts;
        }

        public static bool IsDynasticRestorationOrigin(string pReason,
            string pOrigin, string pClaimant)
        {
            return string.Equals(pReason, RestorationReason,
                       StringComparison.Ordinal) &&
                   string.Equals(pOrigin, RestorationOrigin,
                       StringComparison.Ordinal) &&
                   string.Equals(pClaimant, RestorationClaimant,
                       StringComparison.Ordinal);
        }

        public static bool ShouldInheritPreviousLegalCores(
            bool hadPreviousMandate, string pOrigin)
        {
            if (!hadPreviousMandate) return false;
            return string.Equals(pOrigin, RestorationOrigin,
                       StringComparison.Ordinal) ||
                   string.Equals(pOrigin, "self_restoration",
                       StringComparison.Ordinal);
        }

        public static int CourtCatalystDelta(float order, float livelihood,
            float aggression, float peace, int ministerialPower)
        {
            int delta = 0;
            if (order >= 0.70f && livelihood >= 0.60f) delta -= 2;
            if (aggression >= 0.70f && peace <= 0.30f) delta += 3;
            if (ministerialPower >= 80) delta += 3;
            else if (ministerialPower >= 60) delta += 1;
            return Math.Max(-3, Math.Min(6, delta));
        }

        public static int FeudatoryInstabilityCatalystDelta(int activeCount,
            int unstableCount)
        {
            int active = Math.Max(0, activeCount);
            if (active == 0) return 0;
            int unstable = Math.Max(0, Math.Min(active, unstableCount));
            if (unstable == 0) return -1;
            if (unstable == active) return 3;
            return unstable * 2 >= active ? 2 : 1;
        }
    }
}
