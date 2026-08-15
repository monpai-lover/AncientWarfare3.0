using System;

namespace AncientWarfare3.core.lineage
{
    public enum RulerRank
    {
        Bo = 0,
        Hou = 1,
        Gong = 2,
        King = 3,
        Emperor = 4
    }

    public static class RulerAppellationRules
    {
        public static string RouteRulerTitleKey(bool pBandit)
        {
            return pBandit ? "aw_bandit_ruler_title" : "";
        }

        public static string LivingRanked(string pStateName, RulerRank pTitle)
        {
            string state = Normalize(pStateName);
            string suffix = RankSuffix(pTitle);
            return pTitle == RulerRank.Emperor || suffix.Length == 0
                ? ""
                : state + suffix;
        }

        public static string LivingEmperor(string pStateName, string pEraName)
        {
            string state = Normalize(pStateName);
            if (state.Length > 0 && !state.StartsWith("大", StringComparison.Ordinal))
                state = "大" + state;
            return state + Normalize(pEraName) + "皇帝";
        }

        public static bool ShouldUseLivingEmperor(bool isEmpireRank,
            bool isMandate)
        {
            return isEmpireRank || isMandate;
        }

        public static string ResolveFamilyTreeRitualAppellation(bool isAlive,
            bool isCurrentRuler, string livingAppellation,
            string posthumousAppellation)
        {
            return isAlive && isCurrentRuler
                ? Normalize(livingAppellation)
                : Normalize(posthumousAppellation);
        }

        public static string DeadRanked(string pStateName, string pPosthumous,
            RulerRank pTitle)
        {
            string suffix = RankSuffix(pTitle);
            if (pTitle == RulerRank.Emperor || suffix.Length == 0) return "";
            return Normalize(pStateName) + TakeCharacters(pPosthumous, 1) + suffix;
        }

        public static string DeadEmperor(string pTemple, string pPosthumous,
            bool pMandate)
        {
            return Normalize(pTemple) + TakeCharacters(pPosthumous, pMandate ? 2 : 1) +
                   "皇帝";
        }

        public static string LivingRepublic()
        {
            return "元首";
        }

        public static string LivingMilitaryGovernorate()
        {
            return "将军";
        }

        public static string ResolveWindowRulerLabel(bool pIsRepublic,
            bool pIsEmpireRank, bool pIsMandate, string pLivingAppellation,
            string pRepublicLabel, string pMonarchyLabel,
            string pEmperorFallbackLabel)
        {
            return ResolveWindowRulerLabel(pIsMilitaryGovernorate: false,
                pIsRepublic, pIsEmpireRank, pIsMandate,
                pLivingAppellation, pRepublicLabel, pMonarchyLabel,
                pEmperorFallbackLabel, LivingMilitaryGovernorate());
        }

        public static string ResolveWindowRulerLabel(
            bool pIsMilitaryGovernorate, bool pIsRepublic,
            bool pIsEmpireRank, bool pIsMandate, string pLivingAppellation,
            string pRepublicLabel, string pMonarchyLabel,
            string pEmperorFallbackLabel, string pMilitaryGovernorLabel)
        {
            if (pIsMilitaryGovernorate)
            {
                string military = Normalize(pMilitaryGovernorLabel);
                return military.Length > 0
                    ? military
                    : LivingMilitaryGovernorate();
            }
            if (pIsRepublic) return Normalize(pRepublicLabel);
            if (!ShouldUseLivingEmperor(pIsEmpireRank, pIsMandate))
                return Normalize(pMonarchyLabel);
            string appellation = Normalize(pLivingAppellation);
            return appellation.Length > 0
                ? appellation
                : Normalize(pEmperorFallbackLabel);
        }

        public static string ResolveWindowRulerLabel(bool pIsRepublic,
            bool pIsMandate, string pLivingAppellation,
            string pRepublicLabel, string pMonarchyLabel,
            string pMandateFallbackLabel)
        {
            return ResolveWindowRulerLabel(pIsRepublic,
                pIsEmpireRank: false, pIsMandate, pLivingAppellation,
                pRepublicLabel, pMonarchyLabel, pMandateFallbackLabel);
        }

        public static string Retrospective(string pTemple)
        {
            return Normalize(pTemple);
        }

        public static bool ShouldProjectLiving(bool isXiaKingdom,
            bool usesXiaInstitutions, bool isRebel, bool isRepublic)
        {
            return ShouldProjectLiving(isXiaKingdom, usesXiaInstitutions,
                isRebel, isRepublic, isEmpireOrMandate: false);
        }

        public static bool ShouldProjectLiving(bool isXiaKingdom,
            bool usesXiaInstitutions, bool isRebel, bool isRepublic,
            bool isEmpireOrMandate)
        {
            return isXiaKingdom || usesXiaInstitutions || isRebel ||
                   isRepublic || isEmpireOrMandate;
        }

        public static string ResolveLiveProjection(string pLiveStateName,
            string pCachedStateName, string pCachedAppellation)
        {
            string live = Normalize(pLiveStateName);
            string cachedState = Normalize(pCachedStateName);
            string cachedAppellation = Normalize(pCachedAppellation);
            return live.Length > 0 &&
                   string.Equals(live, cachedState, StringComparison.Ordinal) &&
                   cachedAppellation.Length > 0
                ? cachedAppellation
                : live;
        }

        private static string RankSuffix(RulerRank pTitle)
        {
            return pTitle switch
            {
                RulerRank.Bo => "伯",
                RulerRank.Hou => "侯",
                RulerRank.Gong => "公",
                RulerRank.King => "王",
                RulerRank.Emperor => "皇帝",
                _ => ""
            };
        }

        private static string TakeCharacters(string pValue, int pCount)
        {
            string value = Normalize(pValue);
            return value.Length <= pCount ? value : value.Substring(0, pCount);
        }

        private static string Normalize(string pValue)
        {
            return (pValue ?? "").Trim();
        }
    }
}
