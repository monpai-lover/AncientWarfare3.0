using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum WarAiPeopleRelation
    {
        Unknown,
        SameCulture,
        SameSpecies,
        Foreign
    }

    public readonly struct WarAiGoalCandidate
    {
        public WarAiGoalCandidate(string pGoalType, int pBaseScore,
            int urgency = 0)
        {
            GoalType = pGoalType ?? "";
            BaseScore = pBaseScore;
            Urgency = Clamp(urgency, -100, 100);
        }

        public string GoalType { get; }
        public int BaseScore { get; }
        public int Urgency { get; }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }
    }

    public readonly struct WarAiGoalContext
    {
        public WarAiGoalContext(bool directlyAdjacent,
            bool attackerIsSubject, bool targetIsIndependent,
            bool diplomaticBlocked, float attackerToTargetPowerRatio,
            int targetCityCount, int attackerCentralization,
            float attackerExpansionism, float courtWar, float courtPeace,
            int currentSubjectCount = 0, int subjectSoftCap = 0,
            bool independenceTargetIsSuzerain = false,
            bool opposedSuccessionBranches = false,
            int attackerTitleRank = 2, int targetTitleRank = 0)
        {
            DirectlyAdjacent = directlyAdjacent;
            AttackerIsSubject = attackerIsSubject;
            TargetIsIndependent = targetIsIndependent;
            DiplomaticBlocked = diplomaticBlocked;
            AttackerToTargetPowerRatio = Math.Max(0f,
                attackerToTargetPowerRatio);
            TargetCityCount = Math.Max(0, targetCityCount);
            AttackerCentralization = Math.Max(0,
                Math.Min(3, attackerCentralization));
            AttackerExpansionism = Clamp01(attackerExpansionism);
            CourtWar = Clamp01(courtWar);
            CourtPeace = Clamp01(courtPeace);
            CurrentSubjectCount = Math.Max(0, currentSubjectCount);
            SubjectSoftCap = Math.Max(0, subjectSoftCap);
            IndependenceTargetIsSuzerain = independenceTargetIsSuzerain;
            OpposedSuccessionBranches = opposedSuccessionBranches;
            AttackerTitleRank = attackerTitleRank;
            TargetTitleRank = targetTitleRank;
        }

        public bool DirectlyAdjacent { get; }
        public bool AttackerIsSubject { get; }
        public bool TargetIsIndependent { get; }
        public bool DiplomaticBlocked { get; }
        public float AttackerToTargetPowerRatio { get; }
        public int TargetCityCount { get; }
        public int AttackerCentralization { get; }
        public float AttackerExpansionism { get; }
        public float CourtWar { get; }
        public float CourtPeace { get; }
        public int CurrentSubjectCount { get; }
        public int SubjectSoftCap { get; }
        public bool IndependenceTargetIsSuzerain { get; }
        public bool OpposedSuccessionBranches { get; }
        public int AttackerTitleRank { get; }
        public int TargetTitleRank { get; }

        private static float Clamp01(float pValue)
        {
            return Math.Max(0f, Math.Min(1f, pValue));
        }
    }

    public static class WarAiGoalSelectionRules
    {
        public const int SuccessionReunificationPreference = 60;

        public static bool CanAiForceVassal(int attackerTitleRank,
            int targetTitleRank)
        {
            return attackerTitleRank == 2 && targetTitleRank == 0;
        }

        public static WarAiPeopleRelation ResolvePeopleRelation(
            string pAttackerSpecies, string pDefenderSpecies,
            long pAttackerCultureId, long pDefenderCultureId)
        {
            return ResolvePeopleRelation(pAttackerSpecies, pDefenderSpecies,
                pAttackerCultureId, pDefenderCultureId,
                attackerIsNativeXia: false, defenderIsNativeXia: false);
        }

        public static WarAiPeopleRelation ResolvePeopleRelation(
            string pAttackerSpecies, string pDefenderSpecies,
            long pAttackerCultureId, long pDefenderCultureId,
            bool attackerIsNativeXia, bool defenderIsNativeXia)
        {
            bool culturesKnown = pAttackerCultureId >= 0 &&
                                 pDefenderCultureId >= 0;
            if (culturesKnown && pAttackerCultureId == pDefenderCultureId)
                return WarAiPeopleRelation.SameCulture;
            if (attackerIsNativeXia && defenderIsNativeXia)
                return WarAiPeopleRelation.SameSpecies;
            if (attackerIsNativeXia != defenderIsNativeXia)
                return WarAiPeopleRelation.Foreign;

            bool speciesKnown = !string.IsNullOrWhiteSpace(
                                    pAttackerSpecies) &&
                                !string.IsNullOrWhiteSpace(
                                    pDefenderSpecies);
            if (speciesKnown && string.Equals(pAttackerSpecies,
                    pDefenderSpecies, StringComparison.Ordinal))
                return WarAiPeopleRelation.SameSpecies;
            if (speciesKnown && culturesKnown)
                return WarAiPeopleRelation.Foreign;
            return WarAiPeopleRelation.Unknown;
        }

        public static int StrategicScore(string pGoalType, int pBaseScore,
            WarAiPeopleRelation pRelation)
        {
            return StrategicScore(pGoalType, pBaseScore, pRelation,
                pUrgency: 0);
        }

        public static int StrategicScore(string pGoalType, int pBaseScore,
            WarAiPeopleRelation pRelation, int pUrgency)
        {
            string goal = pGoalType ?? "";
            long score = (long)pBaseScore + Clamp(pUrgency, -100, 100) +
                         GoalAdjustment(goal) +
                         RelationAdjustment(goal, pRelation) +
                         TerritorialPreference(goal);
            return ClampScore(score);
        }

        public static int StrategicScore(string pGoalType, int pBaseScore,
            WarAiPeopleRelation pRelation, WarAiGoalContext pContext,
            int pUrgency = 0)
        {
            string goal = pGoalType ?? "";
            if (!IsEligible(goal, pContext)) return int.MinValue;

            int score = StrategicScore(goal, pBaseScore, pRelation,
                pUrgency);
            score = ClampScore((long)score +
                               SuccessionAdjustment(goal, pContext));
            if (!IsSubjugation(goal)) return score;

            int adjustment = goal == "force_tributary"
                ? TributaryAdjustment(pContext)
                : VassalAdjustment(pContext);
            return ClampScore((long)score + adjustment);
        }

        public static string SelectBestGoal(
            IReadOnlyList<WarAiGoalCandidate> pCandidates,
            WarAiPeopleRelation pRelation)
        {
            return SelectBestGoalInternal(pCandidates, pRelation,
                hasContext: false, default);
        }

        public static string SelectBestGoal(
            IReadOnlyList<WarAiGoalCandidate> pCandidates,
            WarAiPeopleRelation pRelation, WarAiGoalContext pContext)
        {
            return SelectBestGoalInternal(pCandidates, pRelation,
                hasContext: true, pContext);
        }

        public static bool ShouldPreferTerritorialPreparation(
            WarAiPeopleRelation pRelation, WarAiGoalContext pContext,
            int prospectiveClaimBaseScore,
            WarAiGoalCandidate pImmediateSubjugation)
        {
            if (!IsSubjugation(pImmediateSubjugation.GoalType)) return false;
            int territorialScore = StrategicScore("press_claim_city",
                prospectiveClaimBaseScore, pRelation, pContext,
                pUrgency: 20);
            int subjugationScore = StrategicScore(
                pImmediateSubjugation.GoalType,
                pImmediateSubjugation.BaseScore, pRelation, pContext,
                pImmediateSubjugation.Urgency);
            return territorialScore != int.MinValue &&
                   territorialScore >= subjugationScore;
        }

        public static int ObjectiveUrgency(string pGoalType, int pBaseScore)
        {
            switch (pGoalType ?? "")
            {
                case "independence": return 60;
                case "reunify_succession": return 50;
                case "take_mandate": return 40;
                case "mandate_conquest": return 35;
                case "take_core_city": return 35;
                case "press_claim_city": return 20;
                case "restore_kingdom":
                    return Math.Max(5, Math.Min(40, pBaseScore / 5));
                case "force_vassal": return 25;
                case "force_tributary": return 15;
                default: return 0;
            }
        }

        private static string SelectBestGoalInternal(
            IReadOnlyList<WarAiGoalCandidate> pCandidates,
            WarAiPeopleRelation pRelation, bool hasContext,
            WarAiGoalContext pContext)
        {
            if (pCandidates == null) return "";
            string bestLegalGoal = "";
            int bestLegalScore = int.MinValue;
            string bestNoCbGoal = "";
            int bestNoCbScore = int.MinValue;

            for (int i = 0; i < pCandidates.Count; i++)
            {
                WarAiGoalCandidate candidate = pCandidates[i];
                int score = hasContext
                    ? StrategicScore(candidate.GoalType,
                        candidate.BaseScore, pRelation, pContext,
                        candidate.Urgency)
                    : StrategicScore(candidate.GoalType,
                        candidate.BaseScore, pRelation,
                        candidate.Urgency);
                if (score == int.MinValue) continue;
                if (IsNoCb(candidate.GoalType))
                {
                    if (score <= bestNoCbScore) continue;
                    bestNoCbScore = score;
                    bestNoCbGoal = candidate.GoalType;
                    continue;
                }

                if (score < bestLegalScore ||
                    score == bestLegalScore &&
                    (!IsTerritorialGoal(candidate.GoalType) ||
                     IsTerritorialGoal(bestLegalGoal))) continue;
                bestLegalScore = score;
                bestLegalGoal = candidate.GoalType;
            }

            return string.IsNullOrEmpty(bestLegalGoal)
                ? bestNoCbGoal
                : bestLegalGoal;
        }

        private static bool IsEligible(string pGoalType,
            WarAiGoalContext pContext)
        {
            if (pContext.AttackerIsSubject)
                return pGoalType == "independence" &&
                       pContext.IndependenceTargetIsSuzerain;
            if (pGoalType == "independence") return false;
            if (!IsSubjugation(pGoalType)) return true;
            if (!pContext.DirectlyAdjacent ||
                !pContext.TargetIsIndependent ||
                pContext.DiplomaticBlocked ||
                pContext.AttackerToTargetPowerRatio < 1.25f)
                return false;
            if (pGoalType != "force_vassal") return true;
            return CanAiForceVassal(pContext.AttackerTitleRank,
                       pContext.TargetTitleRank) &&
                   pContext.SubjectSoftCap > 0 &&
                   pContext.CurrentSubjectCount < pContext.SubjectSoftCap;
        }

        private static int GoalAdjustment(string pGoalType)
        {
            switch (pGoalType)
            {
                case "independence": return 30;
                case "reunify_succession": return 25;
                case "take_mandate": return 25;
                case "mandate_conquest": return 20;
                case "take_core_city": return 15;
                case "restore_kingdom": return 10;
                case "press_claim_city": return 10;
                case "force_vassal": return 5;
                case "no_cb":
                case "no_cb_punitive":
                    return -25;
                default:
                    return 0;
            }
        }

        private static int SuccessionAdjustment(string pGoalType,
            WarAiGoalContext pContext)
        {
            return pContext.OpposedSuccessionBranches &&
                   pGoalType == "reunify_succession"
                ? SuccessionReunificationPreference
                : 0;
        }

        private static int RelationAdjustment(string pGoalType,
            WarAiPeopleRelation pRelation)
        {
            switch (pGoalType)
            {
                case "take_core_city":
                case "press_claim_city":
                    if (pRelation == WarAiPeopleRelation.SameCulture)
                        return 25;
                    if (pRelation == WarAiPeopleRelation.SameSpecies)
                        return 15;
                    return pRelation == WarAiPeopleRelation.Foreign ? -5 : 0;
                case "restore_kingdom":
                    if (pRelation == WarAiPeopleRelation.SameCulture)
                        return 10;
                    if (pRelation == WarAiPeopleRelation.SameSpecies)
                        return 5;
                    return 0;
                case "force_vassal":
                    if (pRelation == WarAiPeopleRelation.Foreign) return 25;
                    if (pRelation == WarAiPeopleRelation.SameSpecies) return -45;
                    if (pRelation == WarAiPeopleRelation.SameCulture) return -55;
                    return 0;
                case "force_tributary":
                    if (pRelation == WarAiPeopleRelation.Foreign) return 30;
                    if (pRelation == WarAiPeopleRelation.SameSpecies) return -30;
                    if (pRelation == WarAiPeopleRelation.SameCulture) return -40;
                    return 0;
                default:
                    return 0;
            }
        }

        private static int VassalAdjustment(WarAiGoalContext pContext)
        {
            int spareCapacity = Math.Max(0, pContext.SubjectSoftCap -
                                             pContext.CurrentSubjectCount);
            int capacity = pContext.SubjectSoftCap <= 0
                ? 0
                : Round(20f * spareCapacity / pContext.SubjectSoftCap);
            int targetSize = Math.Max(0, 12 -
                Math.Min(4, pContext.TargetCityCount) * 3);
            int power = Round(Math.Min(20f, Math.Max(0f,
                pContext.AttackerToTargetPowerRatio - 1.25f) * 16f));
            return capacity + pContext.AttackerCentralization * 4 +
                   targetSize + Round(pContext.AttackerExpansionism * 15f) +
                   Round(pContext.CourtWar * 10f) -
                   Round(pContext.CourtPeace * 5f) + power;
        }

        private static int TributaryAdjustment(WarAiGoalContext pContext)
        {
            int subjectPressure = pContext.SubjectSoftCap <= 0
                ? 0
                : Round(12f * Math.Min(pContext.CurrentSubjectCount,
                    pContext.SubjectSoftCap) / pContext.SubjectSoftCap);
            int moderatePower = pContext.AttackerToTargetPowerRatio <= 2.4f
                ? 8
                : 0;
            return (3 - pContext.AttackerCentralization) * 9 +
                   Math.Min(12, pContext.TargetCityCount * 3) +
                   Round(pContext.CourtPeace * 10f) -
                   Round(pContext.CourtWar * 5f) + moderatePower +
                   subjectPressure;
        }

        private static bool IsSubjugation(string pGoalType)
        {
            return pGoalType == "force_vassal" ||
                   pGoalType == "force_tributary";
        }

        private static int TerritorialPreference(string pGoalType)
        {
            return IsTerritorialGoal(pGoalType)
                ? WarVictoryExhaustionRules.TerritorialGoalPreference
                : 0;
        }

        private static bool IsTerritorialGoal(string pGoalType)
        {
            return pGoalType == "take_core_city" ||
                   pGoalType == "press_claim_city";
        }

        private static bool IsNoCb(string pGoalType)
        {
            return pGoalType == "no_cb" || pGoalType == "no_cb_punitive";
        }

        private static int Round(float pValue)
        {
            return (int)Math.Round(pValue, MidpointRounding.AwayFromZero);
        }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }

        private static int ClampScore(long pValue)
        {
            if (pValue <= int.MinValue + 1L) return int.MinValue + 1;
            if (pValue >= int.MaxValue) return int.MaxValue;
            return (int)pValue;
        }

        public static bool ShouldLaunchDedicatedSubjugationWar(
            WarAiPeopleRelation pRelation, bool directlyAdjacent,
            bool attackerIsSubject, bool targetIsIndependent,
            bool diplomaticBlocked, float powerRatio)
        {
            return pRelation == WarAiPeopleRelation.Foreign &&
                   directlyAdjacent && !attackerIsSubject &&
                   targetIsIndependent && !diplomaticBlocked &&
                   powerRatio >= 1.25f;
        }

        public static bool ShouldLaunchDedicatedVassalWar(
            WarAiPeopleRelation pRelation, bool directlyAdjacent,
            bool attackerIsSubject, bool targetIsIndependent,
            bool diplomaticBlocked, float powerRatio,
            int currentSubjectCount, int subjectSoftCap)
        {
            return ShouldLaunchDedicatedSubjugationWar(pRelation,
                       directlyAdjacent, attackerIsSubject,
                       targetIsIndependent, diplomaticBlocked, powerRatio) &&
                   subjectSoftCap > 0 &&
                   currentSubjectCount < subjectSoftCap;
        }

        public static bool CanRedirectVanillaWarIntent(
            bool validCivilizedRealm, bool hasKing, bool hasEnemies,
            bool attackerIsSubject)
        {
            return validCivilizedRealm && hasKing && !hasEnemies &&
                   !attackerIsSubject;
        }
    }
}
