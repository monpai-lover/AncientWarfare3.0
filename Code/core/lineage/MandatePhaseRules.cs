using System;

namespace AncientWarfare3.core.lineage
{
    public enum MandatePhase
    {
        Golden,
        Decline,
        Chaos,
        Renewal
    }

    public readonly struct MandatePhaseFacts
    {
        public MandatePhaseFacts(MandatePhase phase, int currentYear, int phaseSinceYear,
            bool hasMandateHistory, bool mandateActive, int mandateValue, int authority,
            bool activeClaimants, int catalystScore, int stableYears)
        {
            Phase = phase;
            CurrentYear = currentYear;
            PhaseSinceYear = phaseSinceYear;
            HasMandateHistory = hasMandateHistory;
            MandateActive = mandateActive;
            MandateValue = mandateValue;
            Authority = authority;
            ActiveClaimants = activeClaimants;
            CatalystScore = catalystScore;
            StableYears = stableYears;
        }

        public MandatePhase Phase { get; }
        public int CurrentYear { get; }
        public int PhaseSinceYear { get; }
        public bool HasMandateHistory { get; }
        public bool MandateActive { get; }
        public int MandateValue { get; }
        public int Authority { get; }
        public bool ActiveClaimants { get; }
        public int CatalystScore { get; }
        public int StableYears { get; }
    }

    public static class MandatePhaseRules
    {
        public const int MinimumPhaseYears = 8;
        public const int RenewalYears = 10;
        public const int StableRecoveryYears = 5;
        public const int ChaosCatalystThreshold = 90;

        public static bool CanForceChaos(bool hasMandateHistory)
        {
            return hasMandateHistory;
        }

        public static MandatePhase NormalizeLoadedPhase(MandatePhase phase,
            bool hasMandateHistory)
        {
            return phase == MandatePhase.Chaos && !hasMandateHistory
                ? MandatePhase.Golden
                : phase;
        }

        public static MandatePhase PhaseAfterMandateEstablished(
            bool pHadPreviousMandate)
        {
            _ = pHadPreviousMandate;
            return MandatePhase.Renewal;
        }

        public static string LocalizationKey(MandatePhase pPhase)
        {
            return pPhase switch
            {
                MandatePhase.Decline => "aw_mandate_phase_decline",
                MandatePhase.Chaos => "aw_mandate_phase_chaos",
                MandatePhase.Renewal => "aw_mandate_phase_renewal",
                _ => "aw_mandate_phase_golden"
            };
        }

        public static MandatePhase Evaluate(MandatePhaseFacts pFacts)
        {
            MandatePhase normalized = NormalizeLoadedPhase(pFacts.Phase,
                pFacts.HasMandateHistory);
            if (normalized != pFacts.Phase) return normalized;
            bool hardChaos = pFacts.HasMandateHistory &&
                             (!pFacts.MandateActive || pFacts.MandateValue <= 0 ||
                              pFacts.ActiveClaimants);
            if (hardChaos) return MandatePhase.Chaos;
            if (pFacts.Phase == MandatePhase.Chaos) return MandatePhase.Chaos;

            int phaseAge = Math.Max(0, pFacts.CurrentYear - pFacts.PhaseSinceYear);
            if (pFacts.Phase == MandatePhase.Renewal)
            {
                return phaseAge < RenewalYears
                    ? MandatePhase.Renewal
                    : ResolveRenewalExit(pFacts.MandateValue, pFacts.Authority,
                        pFacts.CatalystScore);
            }

            if (phaseAge < MinimumPhaseYears) return pFacts.Phase;
            if (pFacts.Phase == MandatePhase.Golden &&
                (pFacts.MandateValue < 40 || pFacts.CatalystScore >= 60))
                return MandatePhase.Decline;

            if (ShouldEnterChaosAfterCatalyst(pFacts.Phase,
                    pFacts.CurrentYear, pFacts.PhaseSinceYear,
                    pFacts.CatalystScore))
                return MandatePhase.Chaos;

            if (pFacts.Phase == MandatePhase.Decline &&
                pFacts.StableYears >= StableRecoveryYears &&
                pFacts.MandateValue >= 70 && pFacts.Authority >= 60 &&
                pFacts.CatalystScore <= 20)
                return MandatePhase.Golden;

            return pFacts.Phase;
        }

        public static bool ShouldEnterChaosAfterCatalyst(
            MandatePhase pPhase, int currentYear, int phaseSinceYear,
            int catalystScore)
        {
            return pPhase == MandatePhase.Decline &&
                   Math.Max(0, currentYear - phaseSinceYear) >=
                   MinimumPhaseYears &&
                   catalystScore >= ChaosCatalystThreshold;
        }

        public static MandatePhase ResolveRenewalExit(int pMandateValue, int pAuthority,
            int pCatalystScore)
        {
            return pMandateValue >= 40 && pAuthority >= 40 && pCatalystScore <= 40
                ? MandatePhase.Golden
                : MandatePhase.Decline;
        }

        public static bool IsRevivalTransition(MandatePhase pPrevious,
            MandatePhase pNext)
        {
            return pPrevious == MandatePhase.Decline &&
                   pNext == MandatePhase.Golden;
        }

        public static int AdjustCatalyst(int pCurrent, int pDelta)
        {
            return Math.Max(0, Math.Min(100, pCurrent + pDelta));
        }

        public static int CatalystDeltaForMandateChange(int pDelta)
        {
            return pDelta < 0
                ? Math.Min(20, Math.Abs(pDelta) * 2)
                : -Math.Min(10, pDelta);
        }

        public static int AnnualCatalystDecay(MandatePhase pPhase)
        {
            return pPhase switch
            {
                MandatePhase.Golden => 5,
                MandatePhase.Decline => 2,
                MandatePhase.Chaos => 1,
                MandatePhase.Renewal => 4,
                _ => 0
            };
        }

        public static float OccupationMultiplier(MandatePhase pPhase)
        {
            return pPhase switch
            {
                MandatePhase.Golden => 0.75f,
                MandatePhase.Decline => 1f,
                MandatePhase.Chaos => 1.5f,
                MandatePhase.Renewal => 0.65f,
                _ => 1f
            };
        }

        public static bool CanContestMandate(MandatePhase pPhase)
        {
            return pPhase == MandatePhase.Chaos;
        }

        public static bool CanLaunchAutonomousRestoration(MandatePhase pPhase)
        {
            return pPhase == MandatePhase.Chaos;
        }

        public static int MaxCentralization(MandatePhase pPhase)
        {
            return pPhase switch
            {
                MandatePhase.Golden => 3,
                MandatePhase.Decline => 2,
                MandatePhase.Chaos => 0,
                MandatePhase.Renewal => 1,
                _ => 0
            };
        }
    }
}
