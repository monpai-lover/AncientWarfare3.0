using System;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class CourtAuxiliaryLawService
    {
        public static CourtTermLaw GetTermLaw(Kingdom pKingdom)
        {
            int value = ReadValue(pKingdom, LineageKeys.COURT_TERM_LAW,
                (int)CourtAuxiliaryLawRules.DefaultTermLaw);
            return value switch
            {
                (int)CourtTermLaw.Lifetime => CourtTermLaw.Lifetime,
                (int)CourtTermLaw.FixedThreeYears =>
                    CourtTermLaw.FixedThreeYears,
                (int)CourtTermLaw.FixedNineYears =>
                    CourtTermLaw.FixedNineYears,
                _ => CourtAuxiliaryLawRules.DefaultTermLaw
            };
        }

        public static CourtBorderCommandLaw GetBorderCommandLaw(
            Kingdom pKingdom)
        {
            int value = ReadValue(pKingdom,
                LineageKeys.COURT_BORDER_COMMAND_LAW,
                (int)CourtAuxiliaryLawRules.DefaultBorderCommandLaw);
            return value switch
            {
                (int)CourtBorderCommandLaw.Discretionary =>
                    CourtBorderCommandLaw.Discretionary,
                (int)CourtBorderCommandLaw.Centralized =>
                    CourtBorderCommandLaw.Centralized,
                _ => CourtAuxiliaryLawRules.DefaultBorderCommandLaw
            };
        }

        public static CourtAppointmentCultureLaw GetAppointmentCultureLaw(
            Kingdom pKingdom)
        {
            int value = ReadValue(pKingdom,
                LineageKeys.COURT_APPOINTMENT_CULTURE_LAW,
                (int)CourtAuxiliaryLawRules.DefaultAppointmentCultureLaw);
            return value switch
            {
                (int)CourtAppointmentCultureLaw.MeritOnly =>
                    CourtAppointmentCultureLaw.MeritOnly,
                (int)CourtAppointmentCultureLaw.XiaCentered =>
                    CourtAppointmentCultureLaw.XiaCentered,
                _ => CourtAuxiliaryLawRules.DefaultAppointmentCultureLaw
            };
        }

        public static CourtConscriptionLaw GetConscriptionLaw(
            Kingdom pKingdom)
        {
            int value = ReadValue(pKingdom,
                LineageKeys.COURT_CONSCRIPTION_LAW,
                (int)CourtConscriptionLawRules.DefaultLaw);
            return value switch
            {
                (int)CourtConscriptionLaw.Limited =>
                    CourtConscriptionLaw.Limited,
                (int)CourtConscriptionLaw.Expanded =>
                    CourtConscriptionLaw.Expanded,
                (int)CourtConscriptionLaw.FullMobilization =>
                    CourtConscriptionLaw.FullMobilization,
                _ => CourtConscriptionLawRules.DefaultLaw
            };
        }

        public static CourtFemaleSuccessionLaw GetFemaleSuccessionLaw(
            Kingdom pKingdom)
        {
            bool nativeXia = LineageService.IsXiaKingdom(pKingdom);
            int value = ReadValue(pKingdom,
                LineageKeys.COURT_FEMALE_SUCCESSION_LAW,
                (int)CourtAuxiliaryLawRules.DefaultFemaleSuccessionLaw(
                    nativeXia));
            return value == (int)CourtFemaleSuccessionLaw.Permitted
                ? CourtFemaleSuccessionLaw.Permitted
                : CourtFemaleSuccessionLaw.Forbidden;
        }

        public static bool AllowsFemaleSuccession(Kingdom pKingdom)
        {
            return CourtAuxiliaryLawRules.AllowsFemaleSuccession(
                LineageService.IsXiaKingdom(pKingdom),
                GetFemaleSuccessionLaw(pKingdom));
        }

        public static int GetLastChangeYear(Kingdom pKingdom,
            CourtAuxiliaryLawKind pKind)
        {
            string key = LastChangeKey(pKind);
            return string.IsNullOrEmpty(key) ? -1 : ReadValue(pKingdom, key, -1);
        }

        public static int GetCooldownRemaining(Kingdom pKingdom,
            CourtAuxiliaryLawKind pKind)
        {
            return CourtAuxiliaryLawRules.CooldownRemaining(CurrentYear(),
                GetLastChangeYear(pKingdom, pKind));
        }

        public static CourtAuxiliaryLawChangeResult TryChangeLaw(
            Kingdom pKingdom, CourtAuxiliaryLawKind pKind, int pDesiredValue,
            bool pAiInitiated = false)
        {
            bool validKingdom = IsValidKingdom(pKingdom);
            bool validChoice = IsValidChoice(pKind, pDesiredValue);
            int previousValue = validChoice
                ? CurrentValue(pKingdom, pKind)
                : -1;
            int previousYear = GetLastChangeYear(pKingdom, pKind);
            int currentYear = CurrentYear();
            float previousPoints = KingdomPolicyService.GetPoliticalPoints(
                pKingdom);
            float reserve =
                pAiInitiated ? PoliticalPointSpendingRules.CourtReserve : 0f;
            CourtAuxiliaryLawChangeResult validation =
                CourtAuxiliaryLawRules.ValidateChange(validKingdom,
                    validChoice, previousValue != pDesiredValue,
                    previousPoints, currentYear, previousYear, reserve);
            if (validation != CourtAuxiliaryLawChangeResult.Success)
                return validation;

            string valueKey = ValueKey(pKind);
            string yearKey = LastChangeKey(pKind);
            if (!KingdomPolicyService.TrySpendPoliticalPoints(pKingdom,
                    CourtAuxiliaryLawRules.ChangeCost,
                    pAiInitiated ? PoliticalPointSpendingRules.CourtReserve : 0f))
                return CourtAuxiliaryLawChangeResult.InsufficientPoints;

            try
            {
                pKingdom.data.set(valueKey, pDesiredValue);
                pKingdom.data.set(yearKey, currentYear);
            }
            catch (Exception error)
            {
                try
                {
                    pKingdom.data.set(valueKey, previousValue);
                    pKingdom.data.set(yearKey, previousYear);
                    KingdomPolicyService.RestorePoliticalPoints(pKingdom,
                        previousPoints);
                }
                catch (Exception restoreError)
                {
                    ModClass.LogWarning(
                        "Auxiliary law rollback failed: " +
                        restoreError.Message);
                }
                ModClass.LogWarning("Auxiliary law change failed: " +
                                    error.Message);
                return CourtAuxiliaryLawChangeResult.PersistenceFailed;
            }

            if (pKind == CourtAuxiliaryLawKind.Conscription)
                CityReservePoolService.OnConscriptionLawChanged(
                    pKingdom, (CourtConscriptionLaw)previousValue,
                    (CourtConscriptionLaw)pDesiredValue);

            if (pKind == CourtAuxiliaryLawKind.FemaleSuccession)
            {
                try
                {
                    HeirService.RefreshHeir(pKingdom);
                }
                catch (Exception error)
                {
                    HeirService.MarkSelectionDirty(pKingdom);
                    ModClass.LogWarning("Female succession refresh failed: " +
                                        error.Message);
                }
            }

            try
            {
                ChronicleEvents.OnCourtAuxiliaryLawChanged(pKingdom,
                    pKingdom.king, pKind, previousValue, pDesiredValue);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Auxiliary law history failed: " +
                                    error.Message);
            }
            return CourtAuxiliaryLawChangeResult.Success;
        }

        public static int ResolveTermEndYear(Kingdom pKingdom, int pAge,
            int pLastEvaluation, long pActorId, int pCurrentYear)
        {
            int dynamicLength = OfficialCareerRankRules.TermLength(pAge,
                pLastEvaluation, pActorId, pCurrentYear);
            return CourtAuxiliaryLawRules.ResolveTermEndYear(
                GetTermLaw(pKingdom), pCurrentYear, dynamicLength);
        }

        public static int AppointmentCultureScore(Kingdom pKingdom,
            Actor pActor)
        {
            return CourtAuxiliaryLawRules.AppointmentCultureScore(
                GetAppointmentCultureLaw(pKingdom),
                LineageService.IsXia(pActor));
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!IsValidKingdom(pKingdom)) return;
            try
            {
                if (KingdomPolicyService.IsPolicyAIEnabled(pKingdom))
                {
                    int year = CurrentYear();
                    pKingdom.data.get(
                        LineageKeys.COURT_AUXILIARY_LAW_AI_LAST_EVALUATION_YEAR,
                        out int lastEvaluationYear, -1);
                    if (CourtAuxiliaryLawRules.ShouldEvaluateAi(year,
                            lastEvaluationYear))
                    {
                        CourtAuxiliaryLawAiEvaluation evaluation =
                            TryEvaluateAi(pKingdom);
                        if (CourtAuxiliaryLawRules.ShouldCommitAiEvaluation(
                                evaluation.CandidateFound,
                                evaluation.Result))
                            pKingdom.data.set(
                                LineageKeys.COURT_AUXILIARY_LAW_AI_LAST_EVALUATION_YEAR,
                                year);
                    }
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Auxiliary law AI failed: " +
                                    error.Message);
            }

            try
            {
                CourtBorderPetitionService.OnKingdomYear(pKingdom);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Border petition work failed: " +
                                    error.Message);
            }
        }

        private static CourtAuxiliaryLawAiEvaluation TryEvaluateAi(
            Kingdom pKingdom)
        {
            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
            int centralization = ReadValue(pKingdom,
                LineageKeys.CENTRALIZATION_LEVEL, 0);
            int directVassals = VassalService.GetDirectVassalCount(pKingdom);
            bool atWar = HasEnemies(pKingdom);
            bool nativeXia = LineageService.IsXiaKingdom(pKingdom);
            int xiaLevel = nativeXia
                ? 4
                : ReadValue(pKingdom, LineageKeys.XIAIZATION_LEVEL, 0);

            bool found = false;
            CourtAuxiliaryLawKind bestKind = CourtAuxiliaryLawKind.Term;
            int bestValue = -1;
            int bestImprovement = 0;

            if (GetCooldownRemaining(pKingdom,
                    CourtAuxiliaryLawKind.Term) == 0)
            {
                CourtTermLaw current = GetTermLaw(pKingdom);
                int currentScore = CourtAuxiliaryLawRules.ScoreTermLaw(current,
                    court.efficiency, court.concentration,
                    CourtService.HasOfficialCourt(pKingdom));
                for (int value = 0; value <=
                     (int)CourtTermLaw.FixedNineYears; value++)
                {
                    if (value == (int)current) continue;
                    int candidateScore = CourtAuxiliaryLawRules.ScoreTermLaw(
                        (CourtTermLaw)value, court.efficiency,
                        court.concentration,
                        CourtService.HasOfficialCourt(pKingdom));
                    Consider(CourtAuxiliaryLawKind.Term, value, currentScore,
                        candidateScore, ref found, ref bestKind,
                        ref bestValue, ref bestImprovement);
                }
            }

            if (GetCooldownRemaining(pKingdom,
                    CourtAuxiliaryLawKind.BorderCommand) == 0)
            {
                CourtBorderCommandLaw current = GetBorderCommandLaw(pKingdom);
                int currentScore =
                    CourtAuxiliaryLawRules.ScoreBorderCommandLaw(current,
                        centralization, directVassals, atWar,
                        court.aggression, court.peace);
                for (int value = 0; value <=
                     (int)CourtBorderCommandLaw.Centralized; value++)
                {
                    if (value == (int)current) continue;
                    int candidateScore =
                        CourtAuxiliaryLawRules.ScoreBorderCommandLaw(
                            (CourtBorderCommandLaw)value, centralization,
                            directVassals, atWar, court.aggression,
                            court.peace);
                    Consider(CourtAuxiliaryLawKind.BorderCommand, value,
                        currentScore, candidateScore, ref found,
                        ref bestKind, ref bestValue, ref bestImprovement);
                }
            }

            if (GetCooldownRemaining(pKingdom,
                    CourtAuxiliaryLawKind.AppointmentCulture) == 0)
            {
                CourtAppointmentCultureLaw current =
                    GetAppointmentCultureLaw(pKingdom);
                int currentScore =
                    CourtAuxiliaryLawRules.ScoreAppointmentCultureLaw(current,
                        nativeXia, xiaLevel, court.order);
                for (int value = 0; value <=
                     (int)CourtAppointmentCultureLaw.XiaCentered; value++)
                {
                    if (value == (int)current) continue;
                    int candidateScore =
                        CourtAuxiliaryLawRules.ScoreAppointmentCultureLaw(
                            (CourtAppointmentCultureLaw)value, nativeXia,
                            xiaLevel, court.order);
                    Consider(CourtAuxiliaryLawKind.AppointmentCulture, value,
                        currentScore, candidateScore, ref found,
                        ref bestKind, ref bestValue, ref bestImprovement);
                }
            }

            if (GetCooldownRemaining(pKingdom,
                    CourtAuxiliaryLawKind.Conscription) == 0)
            {
                CourtConscriptionLaw current = GetConscriptionLaw(pKingdom);
                ResolveConscriptionThreats(pKingdom,
                    out bool existentialDefense, out bool capitalThreat,
                    out bool severeDisadvantage);
                int currentScore = CourtConscriptionLawRules.Score(current,
                    court.dominant_school, court.livelihood, court.peace,
                    court.war, court.aggression, existentialDefense,
                    capitalThreat, severeDisadvantage);
                for (int value = 0; value <=
                     (int)CourtConscriptionLaw.FullMobilization; value++)
                {
                    if (value == (int)current) continue;
                    int candidateScore = CourtConscriptionLawRules.Score(
                        (CourtConscriptionLaw)value, court.dominant_school,
                        court.livelihood, court.peace, court.war,
                        court.aggression, existentialDefense, capitalThreat,
                        severeDisadvantage);
                    Consider(CourtAuxiliaryLawKind.Conscription, value,
                        currentScore, candidateScore, ref found,
                        ref bestKind, ref bestValue, ref bestImprovement);
                }
            }

            CourtAuxiliaryLawChangeResult result = found
                ? TryChangeLaw(pKingdom, bestKind, bestValue,
                    pAiInitiated: true)
                : CourtAuxiliaryLawChangeResult.Unchanged;
            return new CourtAuxiliaryLawAiEvaluation(found, result);
        }

        private readonly struct CourtAuxiliaryLawAiEvaluation
        {
            public CourtAuxiliaryLawAiEvaluation(bool pCandidateFound,
                CourtAuxiliaryLawChangeResult pResult)
            {
                CandidateFound = pCandidateFound;
                Result = pResult;
            }

            public bool CandidateFound { get; }
            public CourtAuxiliaryLawChangeResult Result { get; }
        }

        private static void Consider(CourtAuxiliaryLawKind pKind, int pValue,
            int pCurrentScore, int pCandidateScore, ref bool pFound,
            ref CourtAuxiliaryLawKind pBestKind, ref int pBestValue,
            ref int pBestImprovement)
        {
            if (!CourtAuxiliaryLawRules.ShouldAdoptAiCandidate(pCurrentScore,
                    pCandidateScore)) return;
            int improvement = pCandidateScore - pCurrentScore;
            if (pFound && improvement <= pBestImprovement) return;
            pFound = true;
            pBestKind = pKind;
            pBestValue = pValue;
            pBestImprovement = improvement;
        }

        private static bool IsValidKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static bool IsValidChoice(CourtAuxiliaryLawKind pKind,
            int pValue)
        {
            return pKind switch
            {
                CourtAuxiliaryLawKind.Term => pValue >= 0 &&
                    pValue <= (int)CourtTermLaw.FixedNineYears,
                CourtAuxiliaryLawKind.BorderCommand => pValue >= 0 &&
                    pValue <= (int)CourtBorderCommandLaw.Centralized,
                CourtAuxiliaryLawKind.AppointmentCulture => pValue >= 0 &&
                    pValue <= (int)CourtAppointmentCultureLaw.XiaCentered,
                CourtAuxiliaryLawKind.Conscription => pValue >= 0 &&
                    pValue <= (int)CourtConscriptionLaw.FullMobilization,
                CourtAuxiliaryLawKind.FemaleSuccession => pValue >= 0 &&
                    pValue <= (int)CourtFemaleSuccessionLaw.Permitted,
                _ => false
            };
        }

        private static int CurrentValue(Kingdom pKingdom,
            CourtAuxiliaryLawKind pKind)
        {
            return pKind switch
            {
                CourtAuxiliaryLawKind.Term => (int)GetTermLaw(pKingdom),
                CourtAuxiliaryLawKind.BorderCommand =>
                    (int)GetBorderCommandLaw(pKingdom),
                CourtAuxiliaryLawKind.AppointmentCulture =>
                    (int)GetAppointmentCultureLaw(pKingdom),
                CourtAuxiliaryLawKind.Conscription =>
                    (int)GetConscriptionLaw(pKingdom),
                CourtAuxiliaryLawKind.FemaleSuccession =>
                    (int)GetFemaleSuccessionLaw(pKingdom),
                _ => -1
            };
        }

        private static string ValueKey(CourtAuxiliaryLawKind pKind)
        {
            return pKind switch
            {
                CourtAuxiliaryLawKind.Term => LineageKeys.COURT_TERM_LAW,
                CourtAuxiliaryLawKind.BorderCommand =>
                    LineageKeys.COURT_BORDER_COMMAND_LAW,
                CourtAuxiliaryLawKind.AppointmentCulture =>
                    LineageKeys.COURT_APPOINTMENT_CULTURE_LAW,
                CourtAuxiliaryLawKind.Conscription =>
                    LineageKeys.COURT_CONSCRIPTION_LAW,
                CourtAuxiliaryLawKind.FemaleSuccession =>
                    LineageKeys.COURT_FEMALE_SUCCESSION_LAW,
                _ => ""
            };
        }

        private static string LastChangeKey(CourtAuxiliaryLawKind pKind)
        {
            return pKind switch
            {
                CourtAuxiliaryLawKind.Term =>
                    LineageKeys.COURT_TERM_LAW_LAST_CHANGE_YEAR,
                CourtAuxiliaryLawKind.BorderCommand =>
                    LineageKeys.COURT_BORDER_COMMAND_LAW_LAST_CHANGE_YEAR,
                CourtAuxiliaryLawKind.AppointmentCulture =>
                    LineageKeys.COURT_APPOINTMENT_CULTURE_LAW_LAST_CHANGE_YEAR,
                CourtAuxiliaryLawKind.Conscription =>
                    LineageKeys.COURT_CONSCRIPTION_LAW_LAST_CHANGE_YEAR,
                CourtAuxiliaryLawKind.FemaleSuccession =>
                    LineageKeys.COURT_FEMALE_SUCCESSION_LAW_LAST_CHANGE_YEAR,
                _ => ""
            };
        }

        private static void ResolveConscriptionThreats(Kingdom pKingdom,
            out bool pExistentialDefense, out bool pCapitalThreat,
            out bool pSevereDisadvantage)
        {
            pExistentialDefense = MilitaryEmergencyService.HasAny(pKingdom);
            pCapitalThreat = false;
            long enemyPotential = 0L;
            try
            {
                if (World.world?.wars != null)
                    foreach (War war in World.world.wars)
                    {
                        if (war?.data == null || war.hasEnded() ||
                            !war.hasKingdom(pKingdom)) continue;
                        if (WarMilitaryFactsService.Build(pKingdom, war, 0)
                            .CapitalThreatened)
                            pCapitalThreat = true;
                    }

                using ListPool<Kingdom> enemies =
                    pKingdom.getEnemiesKingdoms();
                foreach (Kingdom enemy in enemies)
                {
                    if (enemy?.data == null || enemy.isRekt()) continue;
                    enemyPotential += WartimeMilitaryPotentialService
                        .CountPotentialWarriors(enemy);
                    if (enemyPotential >= int.MaxValue)
                    {
                        enemyPotential = int.MaxValue;
                        break;
                    }
                }
            }
            catch { }
            long ownPotential = WartimeMilitaryPotentialService
                .CountPotentialWarriors(pKingdom);
            pSevereDisadvantage = enemyPotential > 0L &&
                                   ownPotential * 2L < enemyPotential;
        }

        private static int ReadValue(Kingdom pKingdom, string pKey,
            int pFallback)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pKey))
                return pFallback;
            pKingdom.data.get(pKey, out int value, pFallback);
            return value;
        }

        private static bool HasEnemies(Kingdom pKingdom)
        {
            try { return pKingdom?.hasEnemies() == true; }
            catch { return false; }
        }

        private static int CurrentYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }
    }
}
