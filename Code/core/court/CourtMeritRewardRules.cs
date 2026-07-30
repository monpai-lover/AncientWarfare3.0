using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal enum CourtMeritRewardKind
    {
        None,
        Honor,
        Land
    }

    internal readonly struct CourtMeritRewardCandidateFacts
    {
        public CourtMeritRewardCandidateFacts(long actorId, bool eligible,
            bool royal, bool general, float civilMerit, int civilMeritCap,
            int militaryMerit, int currentNobleRank, bool hasFief,
            int lastRewardYear, bool canReceiveHonor = true)
        {
            ActorId = actorId;
            Eligible = eligible;
            Royal = royal;
            General = general;
            CivilMerit = civilMerit;
            CivilMeritCap = civilMeritCap;
            MilitaryMerit = militaryMerit;
            CurrentNobleRank = currentNobleRank;
            HasFief = hasFief;
            LastRewardYear = lastRewardYear;
            CanReceiveHonor = canReceiveHonor;
        }

        public long ActorId { get; }
        public bool Eligible { get; }
        public bool Royal { get; }
        public bool General { get; }
        public float CivilMerit { get; }
        public int CivilMeritCap { get; }
        public int MilitaryMerit { get; }
        public int CurrentNobleRank { get; }
        public bool HasFief { get; }
        public int LastRewardYear { get; }
        public bool CanReceiveHonor { get; }
    }

    internal readonly struct CourtMeritRewardCooldownProjection
    {
        public CourtMeritRewardCooldownProjection(bool shouldWrite,
            int kingdomLastRewardYear, int actorLastRewardYear)
        {
            ShouldWrite = shouldWrite;
            KingdomLastRewardYear = kingdomLastRewardYear;
            ActorLastRewardYear = actorLastRewardYear;
        }

        public bool ShouldWrite { get; }
        public int KingdomLastRewardYear { get; }
        public int ActorLastRewardYear { get; }
    }

    internal static class CourtMeritRewardRules
    {
        public const int EvaluationIntervalYears = 4;
        public const int ActorRewardCooldownYears = 12;
        public const int MaximumOfficerCandidates = 24;
        public const int MaximumGeneralCandidates = 12;
        public const int MinimumRewardScore = 55;

        public static int Score(CourtMeritRewardCandidateFacts pFacts)
        {
            if (!pFacts.Eligible || pFacts.ActorId < 0) return int.MinValue;
            int civil = 0;
            if (pFacts.CivilMeritCap > 0f)
            {
                float ratio = Math.Max(0f, Math.Min(1f,
                    pFacts.CivilMerit / pFacts.CivilMeritCap));
                civil = (int)Math.Round(ratio * 80f +
                    Math.Min(10f, pFacts.CivilMeritCap) * 2f);
            }
            int military = Math.Max(0, Math.Min(100,
                pFacts.MilitaryMerit));
            int score = Math.Max(civil, military);
            if (civil >= MinimumRewardScore &&
                military >= MinimumRewardScore)
                score = Math.Min(100, score + 5);
            return score;
        }

        public static int TargetNobleRank(
            CourtMeritRewardCandidateFacts pFacts, int pKingdomTitle)
        {
            int score = Score(pFacts);
            if (score < MinimumRewardScore) return pFacts.CurrentNobleRank;
            int earned = score >= 90
                ? NobleRankRules.RankCountyDuke
                : score >= 75
                    ? NobleRankRules.RankCountyCount
                    : NobleRankRules.RankCountyMale;
            int current = NobleRankRules.ClampRank(pFacts.CurrentNobleRank);
            int target = Math.Max(earned, current + 1);
            int realmCap = NobleRankRules.MaximumGrantableRank(pKingdomTitle);
            if (!pFacts.Royal)
                realmCap = Math.Min(realmCap,
                    NobleRankRules.RankStateDuke);
            return Math.Max(current, Math.Min(target, realmCap));
        }

        public static CourtMeritRewardKind RewardKind(
            CourtMeritRewardCandidateFacts pFacts, int pCurrentYear,
            int pKingdomTitle, bool pHasGrantableLand, float pCourtWar,
            float pCourtAggression)
        {
            if (!pFacts.Eligible || Score(pFacts) < MinimumRewardScore ||
                pFacts.LastRewardYear >= 0 &&
                (long)pCurrentYear - pFacts.LastRewardYear <
                ActorRewardCooldownYears)
                return CourtMeritRewardKind.None;

            bool martialLandGrant = pFacts.General && !pFacts.HasFief &&
                pHasGrantableLand && pFacts.MilitaryMerit >= 45 &&
                (pFacts.MilitaryMerit >= 80 ||
                 pCourtWar + pCourtAggression >= 1.1f);
            if (martialLandGrant) return CourtMeritRewardKind.Land;
            return pFacts.CanReceiveHonor &&
                   TargetNobleRank(pFacts, pKingdomTitle) >
                   pFacts.CurrentNobleRank
                ? CourtMeritRewardKind.Honor
                : CourtMeritRewardKind.None;
        }

        public static CourtMeritRewardCandidateFacts SelectBest(
            IReadOnlyList<CourtMeritRewardCandidateFacts> pCandidates,
            int pCurrentYear, int pKingdomTitle, bool pHasGrantableLand,
            float pCourtWar, float pCourtAggression)
        {
            var best = new CourtMeritRewardCandidateFacts(-1L, false, false,
                false, 0f, 0, 0, 0, false, -1, false);
            int bestScore = int.MinValue;
            if (pCandidates == null) return best;
            for (int i = 0; i < pCandidates.Count; i++)
            {
                CourtMeritRewardCandidateFacts candidate = pCandidates[i];
                if (RewardKind(candidate, pCurrentYear, pKingdomTitle,
                        pHasGrantableLand, pCourtWar, pCourtAggression) ==
                    CourtMeritRewardKind.None)
                    continue;
                int score = Score(candidate);
                if (best.ActorId >= 0 &&
                    (score < bestScore || score == bestScore &&
                     candidate.ActorId >= best.ActorId))
                    continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        public static CourtMeritRewardCooldownProjection ResolveCooldownCommit(
            bool grantSucceeded, int pCurrentYear,
            int pKingdomLastRewardYear, int pActorLastRewardYear)
        {
            return grantSucceeded
                ? new CourtMeritRewardCooldownProjection(true, pCurrentYear,
                    pCurrentYear)
                : new CourtMeritRewardCooldownProjection(false,
                    pKingdomLastRewardYear, pActorLastRewardYear);
        }

        public static bool TryCommitSelectedReward(CourtMeritRewardKind pKind,
            Func<bool> pTryLand, Func<bool> pTryHonor)
        {
            return pKind switch
            {
                CourtMeritRewardKind.Land => pTryLand != null && pTryLand(),
                CourtMeritRewardKind.Honor => pTryHonor != null && pTryHonor(),
                _ => false
            };
        }

        public static bool ShouldWriteHotCooldown(bool pDetachedPersisted)
        {
            return pDetachedPersisted;
        }
    }
}
