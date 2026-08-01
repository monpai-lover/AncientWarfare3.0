using System;

namespace AncientWarfare3.core.court
{
    public static class CourtInstitutionId
    {
        public const string Zhou = "zhou";
        public const string Han = "han";
        public const string Tang = "tang";
        public const string Song = "song";
        public const string WesternPrimitive = "western_primitive";
        public const string WesternBase = "western_base";
        public const string WesternElective = "western_elective";
        public const string WesternFeudal = "western_feudal";
        public const string WesternRoyalDirect = "western_royal_direct";
    }

    public static class CourtInstitutionRules
    {
        public const int HanPreferredYear = 30;
        public const int TangPreferredYear = 90;
        public const int SongPreferredYear = 180;

        public static string Resolve(bool pHasOfficialCourt,
            bool pHasThreeDepartments, bool pHasSongCourt)
        {
            if (pHasOfficialCourt && pHasThreeDepartments && pHasSongCourt)
                return CourtInstitutionId.Song;
            if (pHasOfficialCourt && pHasThreeDepartments)
                return CourtInstitutionId.Tang;
            if (pHasOfficialCourt) return CourtInstitutionId.Han;
            return CourtInstitutionId.Zhou;
        }

        public static bool IsKnown(string pInstitution)
        {
            return pInstitution == CourtInstitutionId.Zhou ||
                   pInstitution == CourtInstitutionId.Han ||
                   pInstitution == CourtInstitutionId.Tang ||
                   pInstitution == CourtInstitutionId.Song ||
                   pInstitution == CourtInstitutionId.WesternPrimitive ||
                   pInstitution == CourtInstitutionId.WesternBase ||
                   pInstitution == CourtInstitutionId.WesternElective ||
                   pInstitution == CourtInstitutionId.WesternFeudal ||
                   pInstitution == CourtInstitutionId.WesternRoyalDirect;
        }

        public static int Rank(string pInstitution)
        {
            switch (pInstitution ?? "")
            {
                case CourtInstitutionId.Han: return 1;
                case CourtInstitutionId.Tang: return 2;
                case CourtInstitutionId.Song: return 3;
                case CourtInstitutionId.WesternBase: return 1;
                case CourtInstitutionId.WesternElective:
                case CourtInstitutionId.WesternFeudal: return 2;
                case CourtInstitutionId.WesternRoyalDirect: return 3;
                default: return 0;
            }
        }

        public static bool IsUpgrade(string pPrevious, string pNext)
        {
            return Rank(pNext) > Rank(pPrevious);
        }

        public static int ResearchEraScore(string pTechId, int pCurrentYear)
        {
            switch (pTechId ?? "")
            {
                case "aw_tech_official_court":
                    return EraScore(pCurrentYear, HanPreferredYear, -80, 45);
                case "aw_tech_three_departments":
                    return EraScore(pCurrentYear, TangPreferredYear, -260, 75);
                case "aw_tech_song_court":
                    return EraScore(pCurrentYear, SongPreferredYear, -520, 110);
                default:
                    return 0;
            }
        }

        public static string InstitutionLocalizationKey(string pInstitution)
        {
            return "aw_court_institution_" + (IsKnown(pInstitution)
                ? pInstitution
                : CourtInstitutionId.Zhou);
        }

        public static string TierForInstitution(string pInstitution)
        {
            switch (pInstitution ?? "")
            {
                case CourtInstitutionId.Han:
                    return CourtTier.SanGongJiuQing;
                case CourtInstitutionId.Tang:
                case CourtInstitutionId.Song:
                    return CourtTier.SanShengLiuBu;
                case CourtInstitutionId.WesternPrimitive:
                case CourtInstitutionId.WesternBase:
                case CourtInstitutionId.WesternElective:
                case CourtInstitutionId.WesternFeudal:
                case CourtInstitutionId.WesternRoyalDirect:
                    return pInstitution;
                default:
                    return CourtTier.EasternZhou;
            }
        }

        public static string InstitutionForTier(string pTier)
        {
            switch (pTier ?? "")
            {
                case CourtTier.SanGongJiuQing:
                    return CourtInstitutionId.Han;
                case CourtTier.SanShengLiuBu:
                    return CourtInstitutionId.Tang;
                case CourtInstitutionId.WesternPrimitive:
                case CourtInstitutionId.WesternBase:
                case CourtInstitutionId.WesternElective:
                case CourtInstitutionId.WesternFeudal:
                case CourtInstitutionId.WesternRoyalDirect:
                    return pTier;
                default:
                    return CourtInstitutionId.Zhou;
            }
        }

        public static string OfficeLocalizationKey(string pInstitution,
            string pOfficeId)
        {
            string office = pOfficeId ?? "";
            if (string.IsNullOrEmpty(office)) return "aw_court_office_";
            if (office.StartsWith("west_", StringComparison.Ordinal))
                return "aw_court_office_" + office;
            string institution = IsKnown(pInstitution)
                ? pInstitution
                : CourtInstitutionId.Zhou;
            return HasInstitutionOfficeName(institution, office)
                ? "aw_court_office_" + institution + "_" + office
                : "aw_court_office_" + office;
        }

        private static int EraScore(int pYear, int pPreferredYear,
            int pEarlyScore, int pMatureScore)
        {
            if (pYear >= pPreferredYear) return pMatureScore;
            int approachWindow = Math.Max(10, pPreferredYear / 3);
            return pYear >= pPreferredYear - approachWindow
                ? pEarlyScore / 4
                : pEarlyScore;
        }

        private static bool HasInstitutionOfficeName(string pInstitution,
            string pOffice)
        {
            switch (pInstitution)
            {
                case CourtInstitutionId.Han:
                    return IsHanOffice(pOffice);
                case CourtInstitutionId.Tang:
                case CourtInstitutionId.Song:
                    return IsDepartmentOffice(pOffice);
                default:
                    return false;
            }
        }

        private static bool IsHanOffice(string pOffice)
        {
            switch (pOffice)
            {
                case CourtOfficeId.Chancellor:
                case CourtOfficeId.Censor:
                case CourtOfficeId.Marshal:
                case CourtOfficeId.Justice:
                case CourtOfficeId.Steward:
                case CourtOfficeId.Erudite:
                case CourtOfficeId.ImperialPhysician:
                case CourtOfficeId.ImperialAstrologer:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsDepartmentOffice(string pOffice)
        {
            switch (pOffice)
            {
                case CourtOfficeId.Zhongshu:
                case CourtOfficeId.Menxia:
                case CourtOfficeId.Shangshu:
                case CourtOfficeId.Libu:
                case CourtOfficeId.Hubu:
                case CourtOfficeId.Ribu:
                case CourtOfficeId.Bingbu:
                case CourtOfficeId.Xingbu:
                case CourtOfficeId.Gongbu:
                case CourtOfficeId.ImperialPhysician:
                case CourtOfficeId.ImperialAstrologer:
                    return true;
                default:
                    return false;
            }
        }
    }
}
