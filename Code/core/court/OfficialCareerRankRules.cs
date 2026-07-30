using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public static class OfficialCareerRankRules
    {
        public const int CivilTrack = 0;
        public const int MilitaryTrack = 1;
        public const int Unranked = 0;
        public const int MinimumRank = 1;
        public const int AutomaticRankCeiling = 16;
        public const int MaximumEntryRank = 14;
        public const int MaximumRank = 18;

        private static readonly string[] CivilNamedRankFallbacks =
        {
            "Wenlin Gentleman", "Rulin Gentleman", "Chengfeng Gentleman",
            "Attendant Gentleman", "Court Gentleman for Dispersed Service",
            "Court Audience Gentleman", "Consultation Gentleman",
            "Court Consultation Gentleman", "Grand Master for Dispersed Service",
            "Palace Grand Master", "Grand Master of the Palace Center",
            "Grand Master of Upright Counsel",
            "Grand Master of Silver Seal and Blue Ribbon",
            "Grand Master of Golden Seal and Purple Ribbon",
            "Grand Master of Splendid Happiness", "Specially Advanced",
            "Grand Commander Equal to the Three Excellencies",
            "Grand Commander Equal to the Three Excellencies"
        };

        private static readonly string[] MilitaryNamedRankFallbacks =
        {
            "Martial Cavalry Commandant", "Cloud Cavalry Commandant",
            "Flying Cavalry Commandant", "Valiant Cavalry Commandant",
            "Cavalry Commandant", "Upper Cavalry Commandant",
            "Light Chariot Commandant", "Upper Light Chariot Commandant",
            "Protector-General", "Upper Protector-General",
            "Pillar of State", "Upper Pillar of State"
        };

        public static int ClampRank(int pRank)
        {
            if (pRank <= Unranked) return Unranked;
            return Math.Max(MinimumRank, Math.Min(MaximumRank, pRank));
        }

        public static int EntryRankWhenInstitutionLocked()
        {
            return Unranked;
        }

        public static bool CanDisplayRankedCareer(bool hasNineRankSystem,
            int rank)
        {
            return hasNineRankSystem && ClampRank(rank) > Unranked;
        }

        public static string RankNameKey(int pRank)
        {
            if (ClampRank(pRank) == Unranked)
                return "aw_court_rank_unranked";
            return "aw_court_rank_" + ClampRank(pRank);
        }

        public static string NamedRankKey(int pTrack, int pRank)
        {
            int rank = ClampRank(pRank);
            if (rank == Unranked) return "aw_court_rank_unranked";
            if (pTrack == MilitaryTrack)
                return "aw_court_official_rank_military_turn_" +
                       MilitaryMeritTurn(rank);
            return "aw_court_official_rank_civil_" + rank;
        }

        public static string NamedRankFallbackEnglish(int pTrack, int pRank)
        {
            int rank = ClampRank(pRank);
            if (rank == Unranked) return "Unranked";
            return pTrack == MilitaryTrack
                ? MilitaryNamedRankFallbacks[MilitaryMeritTurn(rank) - 1]
                : CivilNamedRankFallbacks[rank - 1];
        }

        public static string RankFallbackEnglish(int pRank)
        {
            int rank = ClampRank(pRank);
            if (rank == Unranked) return "Unranked";
            int grade = 10 - (rank + 1) / 2;
            return (rank % 2 == 0 ? "Principal " : "Secondary ") +
                   Ordinal(grade) + " Rank";
        }

        public static int EntryRank(bool cityLeaderOrGeneral, bool schoolGuest,
            int age, bool royal, bool highPrestige)
        {
            int rank = cityLeaderOrGeneral ? 5 : schoolGuest ? 3 : 1;
            if (age >= 50) rank += 3;
            else if (age >= 40) rank += 2;
            else if (age >= 30) rank += 1;
            if (royal) rank += 3;
            if (highPrestige) rank += 2;
            return NormalizePrincipalEntryRank(rank);
        }

        public static int ApplyEntryRankBonus(int pEntryRank, int pBonus)
        {
            return NormalizePrincipalEntryRank(pEntryRank + Math.Max(0, pBonus));
        }

        public static int ResolveTrack(bool militaryOffice, bool activeGeneral)
        {
            return militaryOffice ? MilitaryTrack : CivilTrack;
        }

        public static string ResolveDisplayedOfficeId(string pOfficeId,
            bool activeGeneral, string generalOfficeId)
        {
            string officeId = (pOfficeId ?? "").Trim();
            if (officeId.Length > 0) return officeId;
            return activeGeneral ? (generalOfficeId ?? "").Trim() : "";
        }

        public static int ResolveDisplayedTrack(int pStoredTrack,
            bool usesGeneralFallback)
        {
            return usesGeneralFallback ? MilitaryTrack :
                pStoredTrack == MilitaryTrack ? MilitaryTrack : CivilTrack;
        }

        public static bool ShouldWriteAppointmentEdict(int previousRank,
            int nextRank)
        {
            return previousRank >= MinimumRank && nextRank >= MinimumRank &&
                   ClampRank(nextRank) > ClampRank(previousRank);
        }

        public static string TrackTitleKey(int pTrack)
        {
            return pTrack == MilitaryTrack
                ? "aw_court_official_track_military_title"
                : "aw_court_official_track_civil_title";
        }

        public static string TrackTitleFallbackEnglish(int pTrack)
        {
            return pTrack == MilitaryTrack
                ? "Military merit rank"
                : "Civil scattered rank";
        }

        public static string ComposeJointTitle(params string[] pParts)
        {
            var parts = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in pParts ?? Array.Empty<string>())
            {
                string part = (raw ?? "").Trim();
                if (part.Length == 0 || !seen.Add(part)) continue;
                parts.Add(part);
            }
            return string.Join(" · ", parts.ToArray());
        }

        public static string ComposeCareerTitle(string namedRank, string grade,
            string office, bool compact, string track = "", string merit = "",
            string nobleTitle = "")
        {
            return compact
                ? ComposeJointTitle(namedRank, grade, office)
                : ComposeJointTitle(namedRank, grade, office, track, merit,
                    nobleTitle);
        }

        public static string ComposeCardCareerLabel(string pRoleLine,
            string pCompactTitle, string pNamedRank,
            bool constrainedByTwoActions)
        {
            string roleLine = (pRoleLine ?? "").Trim();
            string title = (constrainedByTwoActions
                ? pNamedRank
                : pCompactTitle ?? "").Trim();
            if (roleLine.Length == 0) return title;
            if (title.Length == 0 || string.Equals(roleLine, title,
                    StringComparison.Ordinal))
                return roleLine;
            return roleLine + "\n" + title;
        }

        public static int CardCareerMinimumFontSize(
            bool constrainedByTwoActions)
        {
            return constrainedByTwoActions ? 4 : 6;
        }

        public static float UnitWindowCareerRowHeight()
        {
            return 26f;
        }

        public static int UnitWindowCareerMinimumFontSize()
        {
            return 4;
        }

        public static int TermLength(int age, int lastEvaluation, long actorId,
            int currentYear)
        {
            if (lastEvaluation == 0) return 3;
            int minimum;
            int maximum;
            if (age >= 60) { minimum = 3; maximum = 4; }
            else if (age >= 50) { minimum = 3; maximum = 5; }
            else if (age >= 40) { minimum = 4; maximum = 5; }
            else if (age >= 30) { minimum = 4; maximum = 6; }
            else { minimum = 5; maximum = 6; }

            int range = maximum - minimum + 1;
            int length = minimum + StablePercentage(actorId, currentYear,
                age + lastEvaluation * 31) % range;
            return lastEvaluation == 1 ? Math.Max(3, length - 1) : length;
        }

        public static int MeritCap(int pOfficeGrade)
        {
            if (pOfficeGrade == 10) return 9;
            if (pOfficeGrade == 20) return 6;
            if (pOfficeGrade == 30) return 3;
            return 1;
        }

        public static int EvaluationGrade(int mainAttribute, bool privileged,
            bool purpleRank, bool positiveGrowth, bool negativeGrowth, int roll)
        {
            int upper;
            int middle;
            if (privileged) { upper = 55; middle = 40; }
            else if (purpleRank) { upper = 40; middle = 50; }
            else if (mainAttribute >= 28 || positiveGrowth) { upper = 25; middle = 60; }
            else if (mainAttribute >= 17 && !negativeGrowth) { upper = 15; middle = 50; }
            else { upper = 0; middle = 30; }

            int normalizedRoll = NormalizePercentage(roll);
            if (normalizedRoll < upper)
                return mainAttribute >= 28 ? 0 : 1;
            if (normalizedRoll < upper + middle) return 2;
            return mainAttribute < 17 || negativeGrowth ? 4 : 3;
        }

        public static int RankDelta(int evaluationGrade, bool privileged,
            int roll)
        {
            if (evaluationGrade >= 4) return -2;
            if (evaluationGrade == 3) return -1;
            int normalizedRoll = NormalizePercentage(roll);
            if (privileged)
            {
                if (normalizedRoll < 20) return 1;
                return normalizedRoll < 70 ? 2 : 3;
            }
            if (evaluationGrade <= 0)
            {
                if (normalizedRoll < 35) return 1;
                return normalizedRoll < 80 ? 2 : 3;
            }
            if (evaluationGrade == 1)
                return normalizedRoll < 70 ? 1 : 2;
            return 1;
        }

        public static int ApplyAutomaticRankChange(int currentRank, int delta)
        {
            int current = ClampRank(currentRank);
            if (current == Unranked) return Unranked;
            if (current >= AutomaticRankCeiling && delta >= 0) return current;
            int next = current + delta;
            return Math.Max(MinimumRank, Math.Min(AutomaticRankCeiling, next));
        }

        public static int ApplyManualChange(int pCurrentRank, int pDelta,
            bool pHasMatureRankInstitution, bool pSpeciallyAuthorized)
        {
            int current = ClampRank(pCurrentRank);
            if (current == Unranked) return Unranked;
            int step = pDelta < 0 ? -1 : pDelta > 0 ? 1 : 0;
            if (step == 0) return current;
            if (step > 0 && current >= AutomaticRankCeiling &&
                (!pHasMatureRankInstitution || !pSpeciallyAuthorized))
                return current;
            return Math.Max(MinimumRank, ClampRank(current + step));
        }

        public static float InfluenceMultiplier(int pRank)
        {
            int rank = ClampRank(pRank);
            if (rank == Unranked) return 1f;
            if (rank <= 4) return 1.02f;
            if (rank <= 8) return 1.05f;
            if (rank <= 12) return 1.08f;
            if (rank <= 16) return 1.11f;
            return rank == 17 ? 1.14f : 1.17f;
        }

        public static int RequiredRankForOfficeGrade(int pOfficeGrade)
        {
            if (pOfficeGrade == 10) return 14;
            if (pOfficeGrade == 20) return 10;
            if (pOfficeGrade == 30) return 6;
            return MinimumRank;
        }

        public static bool IsRequiredServiceGrade(int servedOfficeGrade,
            int requiredOfficeGrade)
        {
            return (servedOfficeGrade == 10 ||
                    servedOfficeGrade == 20 ||
                    servedOfficeGrade == 30) &&
                   servedOfficeGrade == requiredOfficeGrade;
        }

        public static int ApplyOfficeRankFloor(int currentRank,
            int officeGrade, bool hasNineRankSystem)
        {
            if (!hasNineRankSystem) return ClampRank(currentRank);
            return Math.Max(ClampRank(currentRank),
                RequiredRankForOfficeGrade(officeGrade));
        }

        public static bool CanEnterOffice(int currentRank, int officeGrade,
            bool hasLowerService, bool hasMiddleService,
            bool hasPassingEvaluation)
        {
            if (officeGrade == 10)
                return hasMiddleService && hasPassingEvaluation;
            if (officeGrade == 20)
                return hasLowerService && hasPassingEvaluation;
            return officeGrade == 30;
        }

        public static int ResolveInitialAppointmentRank(int currentRank,
            int officeGrade, bool hasNineRankSystem,
            bool hasFormalQualification, int entryBonus)
        {
            int rank = ClampRank(currentRank);
            if (!hasNineRankSystem || !hasFormalQualification) return rank;
            int floor = RequiredRankForOfficeGrade(officeGrade);
            if (officeGrade == 30)
            {
                int bonus = Math.Max(0, Math.Min(2, entryBonus));
                floor = NormalizePrincipalEntryRank(floor + bonus);
            }
            return Math.Max(rank, floor);
        }

        public static int ResolveActingAppointmentRank(int currentRank,
            bool hasNineRankSystem)
        {
            return hasNineRankSystem ? ClampRank(currentRank) : Unranked;
        }

        public static int ResolveVacancyPromotionRank(int currentRank,
            int officeGrade, bool hasNineRankSystem,
            bool hasFormalQualification, bool vacancyPromotion,
            int entryBonus = 0)
        {
            int initial = ResolveInitialAppointmentRank(currentRank, officeGrade,
                hasNineRankSystem, hasFormalQualification, entryBonus);
            if (!vacancyPromotion || !hasNineRankSystem ||
                !hasFormalQualification ||
                officeGrade != 10 && officeGrade != 20 && officeGrade != 30)
                return initial;
            return Math.Max(initial, RequiredRankForOfficeGrade(officeGrade));
        }

        public static float OfficeRankMatchScore(int pRank, int pOfficeGrade)
        {
            if (ClampRank(pRank) == Unranked) return 0f;
            int difference = ClampRank(pRank) - RequiredRankForOfficeGrade(pOfficeGrade);
            if (difference == 0) return 8f;
            if (difference < 0) return Math.Max(-24f, difference * 3f);
            return Math.Max(2f, 8f - difference * 0.5f);
        }

        public static int DeterministicRoll(long pActorId, int pYear, int pSalt)
        {
            return StablePercentage(pActorId, pYear, pSalt);
        }

        public static float EvaluationMeritMultiplier(int pEvaluationGrade)
        {
            switch (pEvaluationGrade)
            {
                case 0: return 1.07f;
                case 1: return 1.05f;
                case 2: return 1.03f;
                case 3: return 0.95f;
                default: return 0.90f;
            }
        }

        public static float EvaluationMeritAdjustment(int pEvaluationGrade)
        {
            switch (pEvaluationGrade)
            {
                case 0: return 0.50f;
                case 1: return 0.35f;
                case 2: return 0.15f;
                case 3: return -0.15f;
                default: return -0.35f;
            }
        }

        public static float AnnualCivilMerit(float pTaxValue,
            float pFoodStability, float pUnrestRisk)
        {
            float value = 0.15f + Math.Max(0f, pTaxValue) * 0.002f +
                          Math.Max(0f, pFoodStability) * 0.004f -
                          Math.Max(0f, pUnrestRisk) * 0.002f;
            return Math.Max(0f, Math.Min(1f, value));
        }

        public static float AnnualMilitaryMerit(int pGeneralMerit, int pTroopPower)
        {
            float value = 0.10f + Math.Max(0, pGeneralMerit) * 0.005f +
                          Math.Max(0, pTroopPower) * 0.002f;
            return Math.Max(0f, Math.Min(1f, value));
        }

        public static float ApplyMerit(float pCurrent, float pDelta, int pCap)
        {
            return Math.Max(0f, Math.Min(Math.Max(0, pCap), pCurrent + pDelta));
        }

        private static int StablePercentage(long pActorId, int pYear, int pSalt)
        {
            unchecked
            {
                ulong value = (ulong)pActorId;
                value ^= (ulong)(uint)pYear * 0x9E3779B185EBCA87UL;
                value ^= (ulong)(uint)pSalt * 0xC2B2AE3D27D4EB4FUL;
                value ^= value >> 33;
                value *= 0xFF51AFD7ED558CCDUL;
                value ^= value >> 33;
                return (int)(value % 100UL);
            }
        }

        private static int NormalizePercentage(int pValue)
        {
            int value = pValue % 100;
            return value < 0 ? value + 100 : value;
        }

        private static int NormalizePrincipalEntryRank(int pRank)
        {
            int rank = Math.Min(MaximumEntryRank, ClampRank(pRank));
            if ((rank & 1) != 0) rank++;
            return Math.Min(MaximumEntryRank, rank);
        }

        private static int MilitaryMeritTurn(int pRank)
        {
            return Math.Max(1, Math.Min(12,
                (ClampRank(pRank) * 12 + MaximumRank - 1) / MaximumRank));
        }

        private static string Ordinal(int pValue)
        {
            switch (pValue)
            {
                case 1: return "First";
                case 2: return "Second";
                case 3: return "Third";
                case 4: return "Fourth";
                case 5: return "Fifth";
                case 6: return "Sixth";
                case 7: return "Seventh";
                case 8: return "Eighth";
                default: return "Ninth";
            }
        }
    }
}
