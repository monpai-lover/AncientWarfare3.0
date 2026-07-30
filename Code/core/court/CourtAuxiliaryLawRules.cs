using System;

namespace AncientWarfare3.core.court
{
    public enum CourtTermLaw
    {
        Lifetime,
        FixedThreeYears,
        DynamicThreeToSixYears,
        FixedNineYears
    }

    public enum CourtBorderCommandLaw
    {
        Discretionary,
        Petition,
        Centralized
    }

    public enum CourtAppointmentCultureLaw
    {
        MeritOnly,
        XiaPreference,
        XiaCentered
    }

    public enum CourtAuxiliaryLawKind
    {
        Term,
        BorderCommand,
        AppointmentCulture,
        Conscription
    }

    public enum CourtAuxiliaryLawChangeResult
    {
        Success,
        InvalidKingdom,
        InvalidChoice,
        Unchanged,
        InsufficientPoints,
        Cooldown,
        PersistenceFailed
    }

    public static class CourtAuxiliaryLawRules
    {
        public const float ChangeCost = 20f;
        public const int ChangeCooldownYears = 10;
        public const int AiEvaluationIntervalYears = 12;
        public const int AiMinimumImprovement = 15;
        public const int MaximumLifetimeMigrationsPerYear = 8;
        public const int MaximumPetitionCandidatesPerYear = 4;
        public const int MaximumVassalPetitionCandidatesPerYear = 2;
        public const int MaximumBorderGeneralCandidatesPerYear = 2;
        public const int MaximumVassalSlotsInspectedPerYear = 8;
        public const int BorderPetitionApprovalThreshold = 115;

        public const CourtTermLaw DefaultTermLaw =
            CourtTermLaw.DynamicThreeToSixYears;
        public const CourtBorderCommandLaw DefaultBorderCommandLaw =
            CourtBorderCommandLaw.Petition;
        public const CourtAppointmentCultureLaw DefaultAppointmentCultureLaw =
            CourtAppointmentCultureLaw.XiaPreference;

        public static int ResolveTermEndYear(CourtTermLaw pLaw,
            int pCurrentYear, int pDynamicLength)
        {
            if (pLaw == CourtTermLaw.Lifetime) return int.MaxValue;
            int years = pLaw switch
            {
                CourtTermLaw.FixedThreeYears => 3,
                CourtTermLaw.FixedNineYears => 9,
                _ => Clamp(pDynamicLength, 3, 6)
            };
            long end = (long)pCurrentYear + years;
            return end >= int.MaxValue ? int.MaxValue - 1 : (int)end;
        }

        public static int AppointmentCultureScore(
            CourtAppointmentCultureLaw pLaw, bool pActorIsXia)
        {
            return pLaw switch
            {
                CourtAppointmentCultureLaw.XiaPreference =>
                    pActorIsXia ? 8 : 0,
                CourtAppointmentCultureLaw.XiaCentered =>
                    pActorIsXia ? 16 : -8,
                _ => 0
            };
        }

        public static bool AllowsBorderPetitions(CourtBorderCommandLaw pLaw)
        {
            return pLaw != CourtBorderCommandLaw.Centralized;
        }

        public static int BorderPetitionScore(CourtBorderCommandLaw pLaw,
            float pOwnPower, float pTargetPower, int pOpinion,
            float pAggression, float pWar, float pPeace)
        {
            float own = Math.Max(0f, pOwnPower);
            float target = Math.Max(1f, pTargetPower);
            int ratio = Clamp(Round(own / target * 100f), 0, 180);
            int hostility = Clamp(-pOpinion / 4, -20, 25);
            float aggression = Clamp01(pAggression);
            float war = Clamp01(pWar);
            float peace = Clamp01(pPeace);
            int court = Round((aggression + war - peace - 0.5f) * 20f);
            int discretion = pLaw == CourtBorderCommandLaw.Discretionary
                ? 20
                : 0;
            return ratio + hostility + court + discretion;
        }

        public static bool ShouldApproveBorderPetition(
            CourtBorderCommandLaw pLaw, int pScore)
        {
            return AllowsBorderPetitions(pLaw) &&
                   pScore >= BorderPetitionApprovalThreshold;
        }

        public static CourtAuxiliaryLawChangeResult ValidateChange(
            bool pValidKingdom, bool pValidChoice, bool pChanged,
            float pPoliticalPoints, int pCurrentYear, int pLastChangeYear,
            float pReserve)
        {
            if (!pValidKingdom)
                return CourtAuxiliaryLawChangeResult.InvalidKingdom;
            if (!pValidChoice)
                return CourtAuxiliaryLawChangeResult.InvalidChoice;
            if (!pChanged)
                return CourtAuxiliaryLawChangeResult.Unchanged;
            float points = Math.Max(0f, pPoliticalPoints);
            float reserve = Math.Max(0f, pReserve);
            if (points + 0.001f < ChangeCost + reserve)
                return CourtAuxiliaryLawChangeResult.InsufficientPoints;
            if (CooldownRemaining(pCurrentYear, pLastChangeYear) > 0)
                return CourtAuxiliaryLawChangeResult.Cooldown;
            return CourtAuxiliaryLawChangeResult.Success;
        }

        public static int CooldownRemaining(int pCurrentYear,
            int pLastChangeYear)
        {
            if (pLastChangeYear < 0) return 0;
            long elapsed = (long)pCurrentYear - pLastChangeYear;
            if (elapsed >= ChangeCooldownYears) return 0;
            if (elapsed <= 0) return ChangeCooldownYears;
            return ChangeCooldownYears - (int)elapsed;
        }

        public static bool ShouldEvaluateAi(int pCurrentYear,
            int pLastEvaluationYear)
        {
            return pLastEvaluationYear < 0 ||
                   (long)pCurrentYear - pLastEvaluationYear >=
                   AiEvaluationIntervalYears;
        }

        public static bool ShouldCommitAiEvaluation(bool pCandidateFound,
            CourtAuxiliaryLawChangeResult pResult)
        {
            if (!pCandidateFound) return true;
            return pResult == CourtAuxiliaryLawChangeResult.Success ||
                   pResult == CourtAuxiliaryLawChangeResult.Unchanged;
        }

        public static bool ShouldAdoptAiCandidate(int pCurrentScore,
            int pCandidateScore)
        {
            return pCandidateScore - pCurrentScore >= AiMinimumImprovement;
        }

        public static int OptionCount(CourtAuxiliaryLawKind pKind)
        {
            return pKind == CourtAuxiliaryLawKind.Term ||
                   pKind == CourtAuxiliaryLawKind.Conscription
                ? 4
                : 3;
        }

        public static int ScoreTermLaw(CourtTermLaw pLaw, float pEfficiency,
            float pConcentration, bool pCirculation)
        {
            float efficiency = Clamp(pEfficiency, 0f, 100f);
            float concentration = Clamp01(pConcentration);
            return pLaw switch
            {
                CourtTermLaw.FixedThreeYears =>
                    25 + Round((100f - efficiency) * 0.60f),
                CourtTermLaw.FixedNineYears =>
                    20 + Round(efficiency * 0.65f) +
                    (pCirculation ? 5 : 0),
                CourtTermLaw.Lifetime =>
                    10 + Round(efficiency * 0.35f) +
                    Round(concentration * 35f) +
                    (pCirculation ? -15 : 15),
                _ => 55
            };
        }

        public static int ScoreBorderCommandLaw(
            CourtBorderCommandLaw pLaw, int pCentralization,
            int pDirectVassals, bool pAtWar, float pAggression, float pPeace)
        {
            int centralization = Clamp(pCentralization, 0, 3);
            int directVassals = Math.Max(0, pDirectVassals);
            float aggression = Clamp01(pAggression);
            float peace = Clamp01(pPeace);
            return pLaw switch
            {
                CourtBorderCommandLaw.Discretionary =>
                    30 + Math.Min(25, directVassals * 5) +
                    Round(aggression * 35f) - Round(peace * 20f) -
                    (pAtWar ? 20 : 0) - centralization * 8,
                CourtBorderCommandLaw.Centralized =>
                    20 + centralization * 20 + (pAtWar ? 35 : 0) +
                    Round(peace * 10f),
                _ => 60
            };
        }

        public static int ScoreAppointmentCultureLaw(
            CourtAppointmentCultureLaw pLaw, bool pNativeXia,
            int pXiaizationLevel, float pOrder)
        {
            int xiaLevel = Clamp(pXiaizationLevel, 0, 4);
            float order = Clamp01(pOrder);
            return pLaw switch
            {
                CourtAppointmentCultureLaw.MeritOnly =>
                    40 + (pNativeXia ? 0 : 20) +
                    Math.Max(0, (2 - xiaLevel) * 10),
                CourtAppointmentCultureLaw.XiaCentered =>
                    20 + (pNativeXia ? 20 : 0) + xiaLevel * 10 +
                    Round(order * 15f),
                _ => 55
            };
        }

        private static int Round(float pValue)
        {
            return (int)Math.Round(pValue, MidpointRounding.AwayFromZero);
        }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }

        private static float Clamp(float pValue, float pMinimum,
            float pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }

        private static float Clamp01(float pValue)
        {
            return Clamp(pValue, 0f, 1f);
        }
    }
}
