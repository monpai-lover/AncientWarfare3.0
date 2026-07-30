using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal enum NobleTitleStyle
    {
        None = 0,
        Male = 1,
        Princess = 2,
        SeniorPrincess = 3,
        GrandPrincess = 4
    }

    internal enum FemaleRoyalRelation
    {
        None = 0,
        Daughter = 1,
        Sister = 2,
        PaternalAunt = 3
    }

    internal readonly struct NobleRankCandidate
    {
        public readonly long ActorId;
        public readonly bool Eligible;
        public readonly double BirthTime;

        public NobleRankCandidate(long pActorId, bool pEligible,
            double pBirthTime)
        {
            ActorId = pActorId;
            Eligible = pEligible;
            BirthTime = pBirthTime;
        }
    }

    internal static class NobleRankRules
    {
        public const int RankNone = 0;
        public const int RankCountyMale = 1;
        public const int RankCountyViscount = 2;
        public const int RankCountyCount = 3;
        public const int RankCountyMarquis = 4;
        public const int RankCountyDuke = 5;
        public const int RankCommanderyDuke = 6;
        public const int RankStateDuke = 7;
        public const int RankPrince = 8;
        public const int MaximumRank = RankPrince;

        public static int RankForRoyalKinDistance(int pDistance)
        {
            return pDistance switch
            {
                1 or 2 => RankPrince,
                3 => RankCommanderyDuke,
                4 => RankCountyDuke,
                5 => RankCountyMarquis,
                _ => RankNone
            };
        }

        public static NobleTitleStyle FemaleStyleForRelation(
            FemaleRoyalRelation pRelation)
        {
            return pRelation switch
            {
                FemaleRoyalRelation.Daughter => NobleTitleStyle.Princess,
                FemaleRoyalRelation.Sister => NobleTitleStyle.SeniorPrincess,
                FemaleRoyalRelation.PaternalAunt =>
                    NobleTitleStyle.GrandPrincess,
                _ => NobleTitleStyle.None
            };
        }

        public static long SelectEldestEligibleId(
            IReadOnlyList<NobleRankCandidate> pCandidates)
        {
            if (pCandidates == null) return -1L;
            long selectedId = -1L;
            double selectedBirth = double.MaxValue;
            for (int i = 0; i < pCandidates.Count; i++)
            {
                NobleRankCandidate candidate = pCandidates[i];
                if (!candidate.Eligible || candidate.ActorId < 0) continue;
                double birth = double.IsNaN(candidate.BirthTime)
                    ? double.MaxValue
                    : candidate.BirthTime;
                if (selectedId >= 0 &&
                    (birth > selectedBirth ||
                     birth.Equals(selectedBirth) &&
                     candidate.ActorId >= selectedId))
                    continue;
                selectedId = candidate.ActorId;
                selectedBirth = birth;
            }
            return selectedId;
        }

        public static int ResultingInheritedRank(int pCurrentRank,
            int pInheritedRank)
        {
            return Math.Max(ClampRank(pCurrentRank),
                ClampRank(pInheritedRank));
        }

        public static string TitleKey(int pRank, NobleTitleStyle pStyle)
        {
            if (pStyle == NobleTitleStyle.Princess)
                return "aw_noble_style_princess";
            if (pStyle == NobleTitleStyle.SeniorPrincess)
                return "aw_noble_style_senior_princess";
            if (pStyle == NobleTitleStyle.GrandPrincess)
                return "aw_noble_style_grand_princess";
            return ClampRank(pRank) switch
            {
                RankCountyMale => "aw_noble_rank_county_male",
                RankCountyViscount => "aw_noble_rank_county_viscount",
                RankCountyCount => "aw_noble_rank_county_count",
                RankCountyMarquis => "aw_noble_rank_county_marquis",
                RankCountyDuke => "aw_noble_rank_county_duke",
                RankCommanderyDuke => "aw_noble_rank_commandery_duke",
                RankStateDuke => "aw_noble_rank_state_duke",
                RankPrince => "aw_noble_rank_prince",
                _ => ""
            };
        }

        public static string TitleFallback(int pRank, NobleTitleStyle pStyle)
        {
            if (pStyle == NobleTitleStyle.Princess)
                return "\u516C\u4E3B";
            if (pStyle == NobleTitleStyle.SeniorPrincess)
                return "\u957F\u516C\u4E3B";
            if (pStyle == NobleTitleStyle.GrandPrincess)
                return "\u5927\u957F\u516C\u4E3B";
            return ClampRank(pRank) switch
            {
                RankCountyMale => "\u53BF\u7537",
                RankCountyViscount => "\u53BF\u5B50",
                RankCountyCount => "\u53BF\u4F2F",
                RankCountyMarquis => "\u53BF\u4FAF",
                RankCountyDuke => "\u53BF\u516C",
                RankCommanderyDuke => "\u90E1\u516C",
                RankStateDuke => "\u56FD\u516C",
                RankPrince => "\u738B",
                _ => ""
            };
        }

        public static int ClampRank(int pRank)
        {
            return Math.Max(RankNone, Math.Min(MaximumRank, pRank));
        }

        public static int ManualGrantRank(bool pMale, int pSelectedRank)
        {
            return pMale ? ClampRank(pSelectedRank) : RankNone;
        }

        public static int MaximumGrantableRank(int pKingdomTitle)
        {
            return pKingdomTitle switch
            {
                <= 0 => RankCountyViscount,
                1 => RankCountyCount,
                2 => RankCountyMarquis,
                3 => RankStateDuke,
                _ => RankPrince
            };
        }

        public static bool CanGrantRank(int pKingdomTitle, int pRank)
        {
            return pRank >= RankCountyMale &&
                   pRank <= MaximumGrantableRank(pKingdomTitle);
        }

        public static bool ShouldGrantFormalRoyalTitle(bool adult,
            bool hasFormalTitle)
        {
            return adult && !hasFormalTitle;
        }

        public static bool ShouldReuseGreatGrantAvailability(
            int currentYear, int cachedYear, long currentRulerId,
            long cachedRulerId)
        {
            return currentYear >= 0 && currentYear == cachedYear &&
                   currentRulerId >= 0 && currentRulerId == cachedRulerId;
        }
    }
}
