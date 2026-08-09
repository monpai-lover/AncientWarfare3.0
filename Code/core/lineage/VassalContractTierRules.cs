namespace AncientWarfare3.core.lineage
{
    public static class VassalContractTierRules
    {
        public const int Inner = 0;
        public const int Outer = 1;
        public const int Jimi = 2;
        public const int Tributary = 3;

        public static int NormalizeTier(int pTier)
        {
            return pTier >= Inner && pTier <= Tributary ? pTier : Outer;
        }

        public static VassalEffectiveTerms TermsFor(int pTier)
        {
            switch (NormalizeTier(pTier))
            {
                case Inner:
                    return new VassalEffectiveTerms(25, 25, 100);
                case Jimi:
                    return new VassalEffectiveTerms(85, 0, 0);
                case Tributary:
                    return new VassalEffectiveTerms(100, 10, 0);
                default:
                    return new VassalEffectiveTerms(50, 10, 50);
            }
        }

        public static VassalEffectiveTerms TermsFor(int pTier,
            VassalSubjectKind pSubjectKind)
        {
            if (pSubjectKind == VassalSubjectKind.MilitaryGovernorate)
                return new VassalEffectiveTerms(50, 0, 100);
            return TermsFor(pTier);
        }

        public static bool IsLooseTributary(int pTier)
        {
            return NormalizeTier(pTier) == Tributary;
        }

        public static bool CountsAsVassal(int pTier)
        {
            return !IsLooseTributary(pTier);
        }

        public static bool CanJoinSuzerainWar(int pTier)
        {
            return !IsLooseTributary(pTier);
        }

        public static bool CanBeAnnexed(int pTier)
        {
            return !IsLooseTributary(pTier);
        }

        public static bool UsesSuzerainMapColor(int pTier)
        {
            return !IsLooseTributary(pTier);
        }

        public static bool CanInitiateForcedTributary(int attackerTitleRank)
        {
            return attackerTitleRank == 3 || attackerTitleRank == 4;
        }

        public static bool CanForceTributary(bool pParticipantsValid,
            bool pTargetIndependent, bool pTargetAlreadyTributary,
            bool pDirectlyAdjacent, float pAttackerPower, float pDefenderPower)
        {
            if (!pParticipantsValid || !pTargetIndependent || pTargetAlreadyTributary ||
                !pDirectlyAdjacent || float.IsNaN(pAttackerPower) || float.IsNaN(pDefenderPower))
                return false;
            float attacker = pAttackerPower < 0f ? 0f : pAttackerPower;
            float defender = pDefenderPower < 1f ? 1f : pDefenderPower;
            return attacker >= defender * 1.25f;
        }
    }
}
