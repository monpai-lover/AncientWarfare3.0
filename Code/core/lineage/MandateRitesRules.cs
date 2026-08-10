using System;

namespace AncientWarfare3.core.lineage
{
    public enum MandateDeclarationSource
    {
        Ordinary,
        MandateWarVictory,
        MandateRebel,
        ForeignPseudoDynasty,
        AutonomousRestoration,
        FeudatoryRestoration,
        PlayerGrant
    }

    public static class MandateRitesRules
    {
        public const int OrdinaryRequirement = 1;
        public const int MaximumPermanentPoints = 10;

        public static int NormalizePermanentPoints(int pPoints)
        {
            return Math.Max(0, Math.Min(MaximumPermanentPoints, pPoints));
        }

        public static int TotalCompleteness(bool mandateRitesCompleted,
            bool usableCapitalTemple, int permanentPoints)
        {
            return (mandateRitesCompleted ? 1 : 0) +
                   (usableCapitalTemple ? 1 : 0) +
                   NormalizePermanentPoints(permanentPoints);
        }

        public static bool RequiresOrdinaryGate(MandateDeclarationSource pSource)
        {
            return pSource == MandateDeclarationSource.Ordinary;
        }

        public static bool CanDeclare(int pTotalCompleteness,
            MandateDeclarationSource pSource, out string pReason)
        {
            if (RequiresOrdinaryGate(pSource) &&
                Math.Max(0, pTotalCompleteness) < OrdinaryRequirement)
            {
                pReason = "ritual_completeness_missing";
                return false;
            }

            pReason = "";
            return true;
        }

        public static MandateDeclarationSource ResolveSource(string pDeclarationReason,
            string pOriginType, string pClaimantKind)
        {
            string reason = pDeclarationReason ?? "";
            string origin = pOriginType ?? "";
            string claimant = pClaimantKind ?? "";
            if (reason == "tianming_war")
                return MandateDeclarationSource.MandateWarVictory;
            if (reason == "tianmingrebel_war" || origin == "rebel" ||
                claimant == "rebel")
                return MandateDeclarationSource.MandateRebel;
            if (reason == "pseudo_foreign_war" || origin == "pseudo_foreign" ||
                claimant == "foreign_pseudo")
                return MandateDeclarationSource.ForeignPseudoDynasty;
            if (reason == "self_restoration" || origin == "self_restoration")
                return MandateDeclarationSource.AutonomousRestoration;
            if (reason == MandateFeudatoryCompletionRules.RestorationReason ||
                origin == MandateFeudatoryCompletionRules.RestorationOrigin ||
                claimant == MandateFeudatoryCompletionRules.RestorationClaimant)
                return MandateDeclarationSource.FeudatoryRestoration;
            if (reason == "player_grant" || origin == "player_grant" ||
                claimant == "player_grant")
                return MandateDeclarationSource.PlayerGrant;
            return MandateDeclarationSource.Ordinary;
        }

        public static bool CanPromoteToEmperor(bool promotingToEmperor,
            bool ancestralRitesCompleted, bool ritesMusicCompleted, out string pReason)
        {
            if (!promotingToEmperor)
            {
                pReason = "";
                return true;
            }
            if (!ancestralRitesCompleted)
            {
                pReason = "requires_ancestral_rites";
                return false;
            }
            if (!ritesMusicCompleted)
            {
                pReason = "requires_rites_music";
                return false;
            }

            pReason = "";
            return true;
        }
    }
}
