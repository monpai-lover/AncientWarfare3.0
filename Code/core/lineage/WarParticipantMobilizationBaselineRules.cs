using System;

namespace AncientWarfare3.core.lineage
{
    public static class WarParticipantMobilizationBaselineRules
    {
        private const string PotentialKeyPrefix =
            "aw_war_mobilization_potential_";

        public static int NormalizePotential(int pPotential)
        {
            return Math.Max(1, pPotential);
        }

        public static int ResolveContribution(int pRecordedPotential,
            int pLivePotential)
        {
            return pRecordedPotential > 0
                ? NormalizePotential(pRecordedPotential)
                : NormalizePotential(pLivePotential);
        }

        public static string PotentialKey(long pKingdomId)
        {
            return PotentialKeyPrefix + pKingdomId;
        }
    }
}
