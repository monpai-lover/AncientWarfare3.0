using System;

namespace AncientWarfare3.core.performance
{
    public enum AWWartimeFrameBudgetTier
    {
        Configured,
        Moderate,
        Severe
    }

    public readonly struct AWWartimeFrameBudgetState
    {
        public AWWartimeFrameBudgetState(AWWartimeFrameBudgetTier pTier,
            int pPressureFrames, int pRecoveryFrames,
            float pEffectiveTargetFps, string pReason)
        {
            Tier = pTier;
            PressureFrames = Math.Max(0, pPressureFrames);
            RecoveryFrames = Math.Max(0, pRecoveryFrames);
            EffectiveTargetFps = pEffectiveTargetFps;
            Reason = pReason ?? "configured";
        }

        public AWWartimeFrameBudgetTier Tier { get; }
        public int PressureFrames { get; }
        public int RecoveryFrames { get; }
        public float EffectiveTargetFps { get; }
        public string Reason { get; }
    }

    public static class AWWartimeFrameBudgetRules
    {
        public const int EnterStableFrames = 30;
        public const int RecoveryStableFrames = 90;
        public const double ModerateEnterRatio = 0.60d;
        public const double SevereEnterRatio = 0.90d;
        public const double ModerateExitRatio = 0.45d;
        public const double SevereExitRatio = 0.75d;
        public const float ModerateTargetFps = 40f;
        public const float SevereTargetFps = 35f;

        public static AWWartimeFrameBudgetState Advance(
            AWWartimeFrameBudgetState pState, bool pWartimeWorkActive,
            double pAdmissionCredits, double pMaximumCredits,
            float pConfiguredTargetFps)
        {
            float configured = Math.Max(1f, pConfiguredTargetFps);
            if (!pWartimeWorkActive)
                return Create(AWWartimeFrameBudgetTier.Configured,
                    0, 0, configured);

            double maximum = Math.Max(0d, pMaximumCredits);
            double ratio = maximum <= 0d
                ? 0d
                : Math.Max(0d, pAdmissionCredits) / maximum;
            AWWartimeFrameBudgetTier tier = pState.Tier;
            int pressure = pState.PressureFrames;
            int recovery = pState.RecoveryFrames;

            switch (tier)
            {
                case AWWartimeFrameBudgetTier.Severe:
                    pressure = 0;
                    recovery = ratio < SevereExitRatio
                        ? recovery + 1
                        : 0;
                    if (recovery >= RecoveryStableFrames)
                        return Create(AWWartimeFrameBudgetTier.Moderate,
                            0, 0, configured);
                    break;
                case AWWartimeFrameBudgetTier.Moderate:
                    if (ratio >= SevereEnterRatio)
                    {
                        pressure++;
                        recovery = 0;
                        if (pressure >= EnterStableFrames)
                            return Create(
                                AWWartimeFrameBudgetTier.Severe,
                                0, 0, configured);
                    }
                    else
                    {
                        pressure = 0;
                        recovery = ratio < ModerateExitRatio
                            ? recovery + 1
                            : 0;
                        if (recovery >= RecoveryStableFrames)
                            return Create(
                                AWWartimeFrameBudgetTier.Configured,
                                0, 0, configured);
                    }
                    break;
                default:
                    recovery = 0;
                    if (ratio >= ModerateEnterRatio)
                    {
                        pressure++;
                        if (pressure >= EnterStableFrames)
                        {
                            AWWartimeFrameBudgetTier entered =
                                ratio >= SevereEnterRatio
                                    ? AWWartimeFrameBudgetTier.Severe
                                    : AWWartimeFrameBudgetTier.Moderate;
                            return Create(entered, 0, 0, configured);
                        }
                    }
                    else
                    {
                        pressure = 0;
                    }
                    break;
            }

            return Create(tier, pressure, recovery, configured);
        }

        private static AWWartimeFrameBudgetState Create(
            AWWartimeFrameBudgetTier pTier, int pPressureFrames,
            int pRecoveryFrames, float pConfiguredTargetFps)
        {
            float effective;
            string reason;
            switch (pTier)
            {
                case AWWartimeFrameBudgetTier.Severe:
                    effective = Math.Min(pConfiguredTargetFps,
                        SevereTargetFps);
                    reason = "severe_backlog";
                    break;
                case AWWartimeFrameBudgetTier.Moderate:
                    effective = Math.Min(pConfiguredTargetFps,
                        ModerateTargetFps);
                    reason = "moderate_backlog";
                    break;
                default:
                    effective = pConfiguredTargetFps;
                    reason = "configured";
                    break;
            }
            return new AWWartimeFrameBudgetState(pTier,
                pPressureFrames, pRecoveryFrames, effective, reason);
        }
    }
}
