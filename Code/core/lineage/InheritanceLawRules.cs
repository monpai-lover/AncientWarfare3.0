using System;

namespace AncientWarfare3.core.lineage
{
    public enum InheritanceLaw
    {
        Primogeniture = 0,
        MilitaryAcclaim = 1,
        CivilAcclaim = 2
    }

    public enum InheritanceLawChangeResult
    {
        Success,
        InvalidKingdom,
        NoChange,
        Cooldown,
        InsufficientPoliticalPoints,
        Unavailable,
        PersistenceFailed
    }

    public readonly struct InheritanceLawSnapshot
    {
        public InheritanceLawSnapshot(MandatePhase pPhase,
            bool hasLivingDirectSon, bool stableDynasty, bool atWar,
            int activeArmyCount, int activeGeneralCount,
            int activeOfficerCount, int institutionMaturity,
            int militaryDirection, int civilDirection)
            : this(pPhase, hasLivingDirectSon, stableDynasty, atWar,
                activeArmyCount, activeGeneralCount, activeOfficerCount,
                institutionMaturity, militaryDirection, civilDirection,
                rulerCourtInfluence: 0)
        {
        }

        public InheritanceLawSnapshot(MandatePhase pPhase,
            bool hasLivingDirectSon, bool stableDynasty, bool atWar,
            int activeArmyCount, int activeGeneralCount,
            int activeOfficerCount, int institutionMaturity,
            int militaryDirection, int civilDirection,
            int rulerCourtInfluence)
        {
            Phase = pPhase;
            HasLivingDirectSon = hasLivingDirectSon;
            StableDynasty = stableDynasty;
            AtWar = atWar;
            ActiveArmyCount = Math.Max(0, activeArmyCount);
            ActiveGeneralCount = Math.Max(0, activeGeneralCount);
            ActiveOfficerCount = Math.Max(0, activeOfficerCount);
            InstitutionMaturity = Math.Max(0, institutionMaturity);
            MilitaryDirection = Clamp(militaryDirection, 0, 20);
            CivilDirection = Clamp(civilDirection, 0, 20);
            RulerCourtInfluence = Clamp(rulerCourtInfluence, -60, 70);
        }

        public MandatePhase Phase { get; }
        public bool HasLivingDirectSon { get; }
        public bool StableDynasty { get; }
        public bool AtWar { get; }
        public int ActiveArmyCount { get; }
        public int ActiveGeneralCount { get; }
        public int ActiveOfficerCount { get; }
        public int InstitutionMaturity { get; }
        public int MilitaryDirection { get; }
        public int CivilDirection { get; }
        public int RulerCourtInfluence { get; }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }
    }

    public static class InheritanceLawRules
    {
        public const int DefaultWindowWidth = 560;
        public const int DefaultWindowHeight = 360;
        public const float MinimumContentViewportHeight = 286f;
        public const int EvaluationIntervalYears = 6;
        public const int AdoptionLead = 15;
        public const int LockCost = 30;
        public const int LockCooldownYears = 10;
        public const int DesignatedHeirBaseline = 20;
        public const int DecisiveCandidateLead = 20;

        public static bool RequiresVerticalScroll(float viewportHeight)
        {
            return viewportHeight < MinimumContentViewportHeight;
        }

        public static InheritanceLaw Normalize(int pValue)
        {
            return pValue == (int)InheritanceLaw.MilitaryAcclaim
                ? InheritanceLaw.MilitaryAcclaim
                : pValue == (int)InheritanceLaw.CivilAcclaim
                    ? InheritanceLaw.CivilAcclaim
                    : InheritanceLaw.Primogeniture;
        }

        public static bool CanUseMilitary(int adultRoyalCount,
            int activeGeneralCount, int activeArmyCount)
        {
            return adultRoyalCount > 0 && activeGeneralCount > 0 &&
                   activeArmyCount > 0;
        }

        public static bool CanUseCivil(int adultRoyalCount,
            bool officialCourtCompleted, bool threeDepartmentsCompleted,
            bool finiteTermLaw, int activeOfficerCount)
        {
            return adultRoyalCount > 0 && officialCourtCompleted &&
                   threeDepartmentsCompleted && finiteTermLaw &&
                   activeOfficerCount >= 3;
        }

        public static InheritanceLaw ResolveEffective(
            InheritanceLaw? pLockedLaw, bool militaryUnlocked,
            bool civilUnlocked)
        {
            if (!pLockedLaw.HasValue) return InheritanceLaw.Primogeniture;
            InheritanceLaw law = pLockedLaw.Value;
            if (law == InheritanceLaw.MilitaryAcclaim && !militaryUnlocked)
                return InheritanceLaw.Primogeniture;
            if (law == InheritanceLaw.CivilAcclaim && !civilUnlocked)
                return InheritanceLaw.Primogeniture;
            return law;
        }

        public static bool ShouldRestorePrimogeniture(
            InheritanceLaw pCurrent, bool hasLivingLegitimateDirectSon)
        {
            return hasLivingLegitimateDirectSon &&
                   pCurrent != InheritanceLaw.Primogeniture;
        }

        public static bool EstablishesHereditaryBranch(
            string pSuccessionMode)
        {
            return string.Equals(pSuccessionMode, "military_acclaim",
                       StringComparison.Ordinal) ||
                   string.Equals(pSuccessionMode, "civil_acclaim",
                       StringComparison.Ordinal);
        }

        public static InheritanceLaw ResolveAvailableLaw(
            InheritanceLaw pCurrent, bool primogenitureAvailable,
            int primogenitureScore, bool militaryAvailable,
            int militaryScore, bool civilAvailable, int civilScore)
        {
            InheritanceLaw best = pCurrent;
            int bestScore = int.MinValue;
            bool found = false;
            ConsiderAvailable(InheritanceLaw.Primogeniture,
                primogenitureAvailable, primogenitureScore, pCurrent,
                ref best, ref bestScore, ref found);
            ConsiderAvailable(InheritanceLaw.MilitaryAcclaim,
                militaryAvailable, militaryScore, pCurrent,
                ref best, ref bestScore, ref found);
            ConsiderAvailable(InheritanceLaw.CivilAcclaim,
                civilAvailable, civilScore, pCurrent,
                ref best, ref bestScore, ref found);
            return found ? best : pCurrent;
        }

        public static int Score(InheritanceLaw pLaw,
            InheritanceLawSnapshot pSnapshot)
        {
            switch (pLaw)
            {
                case InheritanceLaw.MilitaryAcclaim:
                    int military = 25;
                    if (pSnapshot.Phase == MandatePhase.Chaos) military += 30;
                    else if (pSnapshot.Phase == MandatePhase.Decline) military += 15;
                    if (pSnapshot.AtWar) military += 10;
                    military += Math.Min(25,
                        pSnapshot.ActiveArmyCount * 5 +
                        pSnapshot.ActiveGeneralCount * 2);
                    military += pSnapshot.MilitaryDirection;
                    return military;

                case InheritanceLaw.CivilAcclaim:
                    int civil = 25;
                    if (pSnapshot.Phase == MandatePhase.Golden) civil += 15;
                    else if (pSnapshot.Phase == MandatePhase.Renewal) civil += 10;
                    civil += Math.Min(30,
                        pSnapshot.ActiveOfficerCount * 3 +
                        pSnapshot.InstitutionMaturity * 2);
                    civil += pSnapshot.CivilDirection;
                    return civil;

                default:
                    int hereditary = 50 +
                                      pSnapshot.RulerCourtInfluence;
                    if (pSnapshot.HasLivingDirectSon) hereditary += 15;
                    if (pSnapshot.Phase == MandatePhase.Golden ||
                        pSnapshot.Phase == MandatePhase.Renewal)
                        hereditary += 10;
                    if (pSnapshot.StableDynasty) hereditary += 10;
                    return hereditary;
            }
        }

        public static bool ShouldEvaluate(int currentYear,
            int lastEvaluationYear, long kingdomId)
        {
            if (lastEvaluationYear >= 0 &&
                currentYear - lastEvaluationYear < EvaluationIntervalYears)
                return false;

            int slot = (int)(kingdomId % EvaluationIntervalYears);
            if (slot < 0) slot += EvaluationIntervalYears;
            int yearSlot = currentYear % EvaluationIntervalYears;
            if (yearSlot < 0) yearSlot += EvaluationIntervalYears;
            return slot == yearSlot;
        }

        public static InheritanceLaw SelectAutomatic(
            InheritanceLaw pCurrent, int primogenitureScore,
            int militaryScore, int civilScore, bool militaryUnlocked,
            bool civilUnlocked)
        {
            int currentScore = ScoreFor(pCurrent, primogenitureScore,
                militaryScore, civilScore);
            InheritanceLaw bestLaw = pCurrent;
            int bestScore = currentScore;

            Consider(InheritanceLaw.Primogeniture, primogenitureScore,
                ref bestLaw, ref bestScore);
            if (militaryUnlocked)
                Consider(InheritanceLaw.MilitaryAcclaim, militaryScore,
                    ref bestLaw, ref bestScore);
            if (civilUnlocked)
                Consider(InheritanceLaw.CivilAcclaim, civilScore,
                    ref bestLaw, ref bestScore);

            return bestLaw != pCurrent && bestScore - currentScore >= AdoptionLead
                ? bestLaw
                : pCurrent;
        }

        public static InheritanceLawChangeResult ValidateChange(
            int currentYear, int lastChangeYear, int politicalPoints,
            InheritanceLaw? currentLock, InheritanceLaw? requestedLock,
            bool militaryUnlocked, bool civilUnlocked)
        {
            if (currentLock == requestedLock)
                return InheritanceLawChangeResult.NoChange;
            if (lastChangeYear >= 0 &&
                currentYear - lastChangeYear < LockCooldownYears)
                return InheritanceLawChangeResult.Cooldown;

            if (requestedLock == InheritanceLaw.MilitaryAcclaim &&
                !militaryUnlocked)
                return InheritanceLawChangeResult.Unavailable;
            if (requestedLock == InheritanceLaw.CivilAcclaim &&
                !civilUnlocked)
                return InheritanceLawChangeResult.Unavailable;
            if (politicalPoints < ChangeCost(requestedLock))
                return InheritanceLawChangeResult.InsufficientPoliticalPoints;
            return InheritanceLawChangeResult.Success;
        }

        public static int ChangeCost(InheritanceLaw? pRequestedLock)
        {
            return pRequestedLock.HasValue ? LockCost : 0;
        }

        public static int ResolveRulerCourtInfluence(int pRulerAbility,
            int pMinisterialPower, int pStrongestRivalAristocraticPower,
            bool pRoyalGuardPresent)
        {
            int ability = ClampValue(pRulerAbility - 10, -15, 20);
            int ministerial = ClampValue(10 - ClampValue(pMinisterialPower, 0, 100) / 2,
                -40, 10);
            int aristocratic = ClampValue(10 - Math.Max(0,
                    pStrongestRivalAristocraticPower) / 5,
                -30, 10);
            int guard = pRoyalGuardPresent ? 10 : 0;
            return ClampValue(ability + ministerial + aristocratic + guard,
                -60, 40);
        }

        public static int AggregateCandidateSupport(long pCandidateId,
            long pDesignatedHeirId, long pOrthodoxCandidateId,
            int pOrthodoxInfluence, long pMilitaryCandidateId,
            int pMilitaryInfluence, long pCivilCandidateId,
            int pCivilInfluence)
        {
            if (pCandidateId < 0) return int.MinValue;
            long support = pCandidateId == pDesignatedHeirId
                ? DesignatedHeirBaseline
                : 0L;
            if (pCandidateId == pOrthodoxCandidateId)
                support += pOrthodoxInfluence;
            if (pCandidateId == pMilitaryCandidateId)
                support += pMilitaryInfluence;
            if (pCandidateId == pCivilCandidateId)
                support += pCivilInfluence;
            return support <= int.MinValue
                ? int.MinValue
                : support >= int.MaxValue
                    ? int.MaxValue
                    : (int)support;
        }

        public static bool ShouldStartSuccessionDispute(long pLeaderActorId,
            long pDesignatedHeirId, int pLeaderSupport,
            int pRunnerUpSupport, bool pHasSupportCity,
            bool pHasActiveDispute)
        {
            return pLeaderActorId >= 0 && pDesignatedHeirId >= 0 &&
                   pLeaderActorId != pDesignatedHeirId && pHasSupportCity &&
                   !pHasActiveDispute &&
                   HasDecisiveCandidateLead(pLeaderSupport,
                       pRunnerUpSupport);
        }

        public static bool HasDecisiveCandidateLead(int pLeaderSupport,
            int pRunnerUpSupport)
        {
            return (long)pLeaderSupport - pRunnerUpSupport >=
                   DecisiveCandidateLead;
        }

        private static int ScoreFor(InheritanceLaw pLaw,
            int pPrimogenitureScore, int pMilitaryScore, int pCivilScore)
        {
            return pLaw == InheritanceLaw.MilitaryAcclaim
                ? pMilitaryScore
                : pLaw == InheritanceLaw.CivilAcclaim
                    ? pCivilScore
                    : pPrimogenitureScore;
        }

        private static void Consider(InheritanceLaw pLaw, int pScore,
            ref InheritanceLaw pBestLaw, ref int pBestScore)
        {
            if (pScore <= pBestScore) return;
            pBestLaw = pLaw;
            pBestScore = pScore;
        }

        private static void ConsiderAvailable(InheritanceLaw pLaw,
            bool pAvailable, int pScore, InheritanceLaw pCurrent,
            ref InheritanceLaw pBestLaw, ref int pBestScore,
            ref bool pFound)
        {
            if (!pAvailable) return;
            if (pFound && (pScore < pBestScore ||
                           pScore == pBestScore && pBestLaw == pCurrent ||
                           pScore == pBestScore && pLaw != pCurrent &&
                           pLaw > pBestLaw)) return;
            pBestLaw = pLaw;
            pBestScore = pScore;
            pFound = true;
        }

        private static int ClampValue(int pValue, int pMinimum,
            int pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }
    }
}
