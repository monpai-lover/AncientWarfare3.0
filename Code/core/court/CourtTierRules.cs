using System;

namespace AncientWarfare3.core.court
{
    // 官场历史层级(按科技解锁):东周六卿 → 三公九卿 → 三省六部。
    // 每级有自己的中央官职集合;层级提升是一次朝廷改制事件。
    public static class CourtTierRules
    {
        public static string ResolveTier(bool hasOfficialCourt, bool hasThreeDepartments)
        {
            if (hasOfficialCourt && hasThreeDepartments) return CourtTier.SanShengLiuBu;
            if (hasOfficialCourt) return CourtTier.SanGongJiuQing;
            return CourtTier.EasternZhou;
        }

        public static int TierRank(string tier)
        {
            switch (tier ?? "")
            {
                case CourtTier.SanShengLiuBu: return 2;
                case CourtTier.SanGongJiuQing: return 1;
                default: return 0;
            }
        }

        public static bool IsUpgrade(string previousTier, string nextTier)
        {
            return TierRank(nextTier) > TierRank(previousTier);
        }

        public static string[] CentralOfficesForTier(string tier)
        {
            switch (tier ?? "")
            {
                case CourtTier.SanShengLiuBu:
                    return new[]
                    {
                        CourtOfficeId.Zhongshu, CourtOfficeId.Menxia, CourtOfficeId.Shangshu,
                        CourtOfficeId.Libu, CourtOfficeId.Hubu, CourtOfficeId.Ribu,
                        CourtOfficeId.Bingbu, CourtOfficeId.Xingbu, CourtOfficeId.Gongbu,
                        CourtOfficeId.ImperialPhysician, CourtOfficeId.ImperialAstrologer
                    };
                case CourtTier.SanGongJiuQing:
                    return new[]
                    {
                        CourtOfficeId.Chancellor, CourtOfficeId.Marshal, CourtOfficeId.Censor,
                        CourtOfficeId.Justice, CourtOfficeId.Steward, CourtOfficeId.Erudite,
                        CourtOfficeId.ImperialPhysician, CourtOfficeId.ImperialAstrologer
                    };
                case CourtTier.EasternZhou:
                    return new[]
                    {
                        CourtOfficeId.TaiZai, CourtOfficeId.SiTu,
                        CourtOfficeId.ZongBo, CourtOfficeId.SiMa,
                        CourtOfficeId.SiKou, CourtOfficeId.SiKong
                    };
                default:
                    return Array.Empty<string>();
            }
        }

        public static string PreferredSchoolForOffice(string officeId)
        {
            switch (officeId ?? "")
            {
                case CourtOfficeId.TaiZai: return CourtSchoolId.Ru;
                case CourtOfficeId.SiTu: return CourtSchoolId.Agrarian;
                case CourtOfficeId.ZongBo: return CourtSchoolId.Ru;
                case CourtOfficeId.SiMa: return CourtSchoolId.Military;
                case CourtOfficeId.SiKou: return CourtSchoolId.Legalist;
                case CourtOfficeId.SiKong: return CourtSchoolId.Mohist;
                case CourtOfficeId.Chancellor: return CourtSchoolId.Ru;
                case CourtOfficeId.Censor: return CourtSchoolId.Legalist;
                case CourtOfficeId.Marshal: return CourtSchoolId.Military;
                case CourtOfficeId.Justice: return CourtSchoolId.Legalist;
                case CourtOfficeId.Steward: return CourtSchoolId.Agrarian;
                case CourtOfficeId.Erudite: return CourtSchoolId.Ru;
                case CourtOfficeId.Zhongshu: return CourtSchoolId.Ru;
                case CourtOfficeId.Menxia: return CourtSchoolId.Legalist;
                case CourtOfficeId.Shangshu: return CourtSchoolId.Ru;
                case CourtOfficeId.Libu: return CourtSchoolId.Ru;
                case CourtOfficeId.Hubu: return CourtSchoolId.Agrarian;
                case CourtOfficeId.Ribu: return CourtSchoolId.Ru;
                case CourtOfficeId.Bingbu: return CourtSchoolId.Military;
                case CourtOfficeId.Xingbu: return CourtSchoolId.Legalist;
                case CourtOfficeId.Gongbu: return CourtSchoolId.Mohist;
                case CourtOfficeId.ImperialPhysician: return CourtSchoolId.Medical;
                case CourtOfficeId.ImperialAstrologer: return CourtSchoolId.YinYang;
                default: return CourtSchoolId.None;
            }
        }
    }
}
