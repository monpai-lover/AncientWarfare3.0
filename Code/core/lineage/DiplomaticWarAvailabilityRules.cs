using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct DiplomaticWarAvailabilityCandidate
    {
        public DiplomaticWarAvailabilityCandidate(bool pAvailable,
            string pFailureReason)
        {
            Available = pAvailable;
            FailureReason = pFailureReason ?? "";
        }

        public bool Available { get; }
        public string FailureReason { get; }
    }

    public readonly struct DiplomaticWarAvailabilityResult
    {
        public DiplomaticWarAvailabilityResult(bool pAvailable,
            string pFailureReason)
        {
            Available = pAvailable;
            FailureReason = pFailureReason ?? "";
        }

        public bool Available { get; }
        public string FailureReason { get; }
    }

    public readonly struct DiplomaticWarSubmissionResolution
    {
        public DiplomaticWarSubmissionResolution(bool pCanSubmit,
            string pFailureReason)
        {
            CanSubmit = pCanSubmit;
            FailureReason = pFailureReason ?? "";
        }

        public bool CanSubmit { get; }
        public string FailureReason { get; }
    }

    public static class DiplomaticWarSubmissionRules
    {
        public static DiplomaticWarSubmissionResolution Resolve(
            bool pTargetCityIdentityValid,
            bool pCanonicalOptionMatched,
            bool pAuthoritativeGoalAllowed,
            string pAuthoritativeFailureReason)
        {
            if (!pTargetCityIdentityValid)
                return new DiplomaticWarSubmissionResolution(false,
                    "target_city_changed");
            if (pCanonicalOptionMatched)
                return new DiplomaticWarSubmissionResolution(true, "");
            if (!pAuthoritativeGoalAllowed)
            {
                return new DiplomaticWarSubmissionResolution(false,
                    string.IsNullOrWhiteSpace(pAuthoritativeFailureReason)
                        ? "unavailable"
                        : pAuthoritativeFailureReason);
            }
            return new DiplomaticWarSubmissionResolution(false,
                "war_target_option_changed");
        }
    }

    public static class DiplomaticWarAvailabilityRules
    {
        public static DiplomaticWarAvailabilityResult Resolve(
            bool pHasPendingForPair,
            IReadOnlyList<DiplomaticWarAvailabilityCandidate> pCandidates)
        {
            if (pHasPendingForPair)
            {
                return new DiplomaticWarAvailabilityResult(false,
                    "war_preparation");
            }

            if (pCandidates == null || pCandidates.Count == 0)
            {
                return new DiplomaticWarAvailabilityResult(false,
                    "no_war_reasons");
            }

            for (int i = 0; i < pCandidates.Count; i++)
            {
                if (pCandidates[i].Available)
                    return new DiplomaticWarAvailabilityResult(true, "");
            }

            for (int i = 0; i < pCandidates.Count; i++)
            {
                string reason = pCandidates[i].FailureReason;
                if (!string.IsNullOrWhiteSpace(reason))
                    return new DiplomaticWarAvailabilityResult(false, reason);
            }

            return new DiplomaticWarAvailabilityResult(false, "unavailable");
        }

        public static int ResolveSelectedGoalIndex(
            IReadOnlyList<DiplomaticWarAvailabilityCandidate> pCandidates,
            int pPreferredIndex)
        {
            if (pCandidates == null || pCandidates.Count == 0) return -1;

            if (pPreferredIndex >= 0 &&
                pPreferredIndex < pCandidates.Count &&
                pCandidates[pPreferredIndex].Available)
            {
                return pPreferredIndex;
            }

            for (int i = 0; i < pCandidates.Count; i++)
            {
                if (pCandidates[i].Available) return i;
            }

            return -1;
        }
    }
}
