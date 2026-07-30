namespace AncientWarfare3.core.lineage
{
    public static class TemporaryLevyDiagnosticRules
    {
        public static bool ShouldWriteRecoveryDiagnostic(
            bool diagnosticsEnabled, int pendingDemand)
        {
            return diagnosticsEnabled && pendingDemand > 0;
        }

        public static bool ShouldWriteRecoveryRequestDiagnostic(
            bool diagnosticsEnabled, int requestedDemand)
        {
            return diagnosticsEnabled && requestedDemand > 0;
        }

        public static string RecoveryCandidateBreakdown(int alreadyWarrior,
            int ineligible, int viable, int enlistFailures)
        {
            return "already_warrior=" + System.Math.Max(0, alreadyWarrior) +
                   " ineligible=" + System.Math.Max(0, ineligible) +
                   " viable=" + System.Math.Max(0, viable) +
                   " enlist_failed=" + System.Math.Max(0, enlistFailures);
        }

        public static string RecoveryIneligibilityBreakdown(int notResident,
            int notLivingAdult, int wrongProfession, int reservePolicy,
            int slavePolicy, int protectedIdentity, int nativeEligibility,
            int ageLimit, int capacity)
        {
            return "not_resident=" + System.Math.Max(0, notResident) +
                   " not_living_adult=" + System.Math.Max(0, notLivingAdult) +
                   " wrong_profession=" + System.Math.Max(0, wrongProfession) +
                   " reserve_policy=" + System.Math.Max(0, reservePolicy) +
                   " slave_policy=" + System.Math.Max(0, slavePolicy) +
                   " protected_identity=" + System.Math.Max(0, protectedIdentity) +
                   " native_eligibility=" + System.Math.Max(0, nativeEligibility) +
                   " age_limit=" + System.Math.Max(0, ageLimit) +
                   " capacity=" + System.Math.Max(0, capacity);
        }
    }
}
