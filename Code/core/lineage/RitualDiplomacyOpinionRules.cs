using System;

namespace AncientWarfare3.core.lineage
{
    public readonly struct RitualDiplomacyFacts
    {
        public RitualDiplomacyFacts(long pRealmId, long pRootSuzerainId,
            long pTributarySuzerainId, int pXiaLevel, int pRitesScore,
            int pTitleRank, bool pIsMandate)
        {
            RealmId = pRealmId;
            RootSuzerainId = pRootSuzerainId;
            TributarySuzerainId = pTributarySuzerainId;
            XiaLevel = pXiaLevel;
            RitesScore = pRitesScore;
            TitleRank = pTitleRank;
            IsMandate = pIsMandate;
        }

        public long RealmId { get; }
        public long RootSuzerainId { get; }
        public long TributarySuzerainId { get; }
        public int XiaLevel { get; }
        public int RitesScore { get; }
        public int TitleRank { get; }
        public bool IsMandate { get; }
    }

    public static class RitualDiplomacyOpinionRules
    {
        public const int XiaRitesLevel = 3;
        public const int EmperorTitleRank = 4;

        public static int Zhengshuo(RitualDiplomacyFacts pMain,
            RitualDiplomacyFacts pTarget)
        {
            if (pMain.RealmId < 0 || pTarget.RealmId < 0 ||
                pMain.RealmId == pTarget.RealmId) return 0;
            if (pMain.RootSuzerainId == pTarget.RealmId ||
                pMain.TributarySuzerainId == pTarget.RealmId) return 20;
            if (pMain.RootSuzerainId >= 0 &&
                pMain.RootSuzerainId == pTarget.RootSuzerainId &&
                pMain.RootSuzerainId != pMain.RealmId &&
                pTarget.RootSuzerainId != pTarget.RealmId) return 8;
            return 0;
        }

        public static int Rites(RitualDiplomacyFacts pMain,
            RitualDiplomacyFacts pTarget)
        {
            int value = 0;
            bool mainXiaRites = pMain.XiaLevel >= XiaRitesLevel;
            bool targetXiaRites = pTarget.XiaLevel >= XiaRitesLevel;
            if (mainXiaRites && targetXiaRites) value += 8;
            else if (mainXiaRites && !targetXiaRites) value -= 6;

            int ritesGap = Math.Max(-3, Math.Min(3,
                pTarget.RitesScore - pMain.RitesScore));
            value += ritesGap * 4;
            return Math.Max(-10, Math.Min(12, value));
        }

        public static int Usurpation(RitualDiplomacyFacts pMain,
            RitualDiplomacyFacts pTarget)
        {
            if (pTarget.TitleRank < EmperorTitleRank || pTarget.IsMandate) return 0;
            if (pMain.IsMandate) return -30;
            return pMain.XiaLevel >= XiaRitesLevel ? -15 : 0;
        }
    }
}
