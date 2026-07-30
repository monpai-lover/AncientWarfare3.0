using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal static class RitualDiplomacyOpinionCallbacks
    {
        public static int Zhengshuo(Kingdom pMain, Kingdom pTarget)
        {
            return RitualDiplomacyOpinionRules.Zhengshuo(ReadFacts(pMain), ReadFacts(pTarget));
        }

        public static int Rites(Kingdom pMain, Kingdom pTarget)
        {
            return RitualDiplomacyOpinionRules.Rites(ReadFacts(pMain), ReadFacts(pTarget));
        }

        public static int Usurpation(Kingdom pMain, Kingdom pTarget)
        {
            return RitualDiplomacyOpinionRules.Usurpation(ReadFacts(pMain), ReadFacts(pTarget));
        }

        public static int CourtOpenness(Kingdom pMain, Kingdom pTarget)
        {
            CourtInstitutionEffects effects =
                CourtInstitutionEffectService.Read(pMain);
            return CourtInstitutionEffectRules.CrossCultureOpinion(
                effects.CrossCultureOpinionBonus,
                ReadCultureId(pMain), ReadCultureId(pTarget));
        }

        public static int SuccessionSplit(Kingdom pMain, Kingdom pTarget)
        {
            return SuccessionDisputeService.ReadOpposedCourtOpinion(
                pMain, pTarget);
        }

        private static long ReadCultureId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1L;
            pKingdom.data.get(LineageKeys.DIPLOMACY_CULTURE_ID,
                out long cultureId, -1L);
            return cultureId;
        }

        private static RitualDiplomacyFacts ReadFacts(Kingdom pKingdom)
        {
            if (pKingdom?.data == null)
                return new RitualDiplomacyFacts(-1L, -1L, -1L, 0, 0, 0, false);

            long realmId = pKingdom.id;
            pKingdom.data.get(LineageKeys.DIPLOMACY_ROOT_SUZERAIN_ID,
                out long rootSuzerainId, realmId);
            pKingdom.data.get(LineageKeys.TRIBUTARY_SUZERAIN_ID,
                out long tributarySuzerainId, -1L);
            pKingdom.data.get(LineageKeys.DIPLOMACY_XIA_LEVEL, out int xiaLevel, 0);
            pKingdom.data.get(LineageKeys.DIPLOMACY_RITES_SCORE, out int ritesScore, 0);
            pKingdom.data.get(LineageKeys.DIPLOMACY_TITLE_RANK, out int titleRank, 0);
            pKingdom.data.get(LineageKeys.DIPLOMACY_IS_MANDATE, out bool isMandate, false);
            return new RitualDiplomacyFacts(realmId, rootSuzerainId,
                tributarySuzerainId, xiaLevel, ritesScore, titleRank, isMandate);
        }
    }
}
