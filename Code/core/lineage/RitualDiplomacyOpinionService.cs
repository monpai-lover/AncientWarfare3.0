using System;

namespace AncientWarfare3.core.lineage
{
    internal static class RitualDiplomacyOpinionService
    {
        public static void RegisterAssets()
        {
            AddOpinion("aw_opinion_zhengshuo", "opinion_aw_zhengshuo", "",
                RitualDiplomacyOpinionCallbacks.Zhengshuo);
            AddOpinion("aw_opinion_rites", "opinion_aw_rites", "opinion_aw_rites_negative",
                RitualDiplomacyOpinionCallbacks.Rites);
            AddOpinion("aw_opinion_usurpation", "opinion_aw_usurpation",
                "opinion_aw_usurpation_negative", RitualDiplomacyOpinionCallbacks.Usurpation);
            AddOpinion("aw_opinion_court_openness",
                "opinion_aw_court_openness", "",
                RitualDiplomacyOpinionCallbacks.CourtOpenness);
            AddOpinion("aw_opinion_succession_split",
                "opinion_aw_succession_split",
                "opinion_aw_succession_split",
                RitualDiplomacyOpinionCallbacks.SuccessionSplit);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral()) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.DIPLOMACY_SNAPSHOT_LAST_YEAR,
                out int lastYear, int.MinValue);
            if (lastYear == year) return;

            Kingdom root = VassalService.GetRootSuzerain(pKingdom) ?? pKingdom;
            int xiaLevel = XiaizationService.GetLevel(pKingdom);
            int ritesScore = Math.Max(0, Math.Min(3,
                MandateRitesService.ReadSnapshot(pKingdom).total_points));
            int titleRank = (int)KingdomTitleService.GetTitle(pKingdom);
            bool isMandate = MandateService.IsRuntimeMandateKingdom(pKingdom);

            pKingdom.data.set(LineageKeys.DIPLOMACY_ROOT_SUZERAIN_ID, root.id);
            pKingdom.data.set(LineageKeys.DIPLOMACY_XIA_LEVEL, xiaLevel);
            pKingdom.data.set(LineageKeys.DIPLOMACY_RITES_SCORE, ritesScore);
            pKingdom.data.set(LineageKeys.DIPLOMACY_TITLE_RANK, titleRank);
            pKingdom.data.set(LineageKeys.DIPLOMACY_IS_MANDATE, isMandate);
            pKingdom.data.set(LineageKeys.DIPLOMACY_CULTURE_ID,
                pKingdom.culture?.id ?? -1L);
            pKingdom.data.set(LineageKeys.DIPLOMACY_SNAPSHOT_LAST_YEAR, year);
        }

        private static void AddOpinion(string pId, string pPositiveKey,
            string pNegativeKey, OpinionDelegateCalc pCalc)
        {
            OpinionAsset asset = null;
            try { asset = AssetManager.opinion_library.get(pId); }
            catch { }

            bool add = asset == null;
            if (asset == null) asset = new OpinionAsset { id = pId };
            asset.translation_key = pPositiveKey;
            asset.translation_key_negative = pNegativeKey;
            asset.calc = pCalc;
            if (add) AssetManager.opinion_library.add(asset);
        }
    }
}
