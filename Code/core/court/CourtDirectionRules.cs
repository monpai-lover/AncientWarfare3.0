using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public readonly struct CourtDirectionVector
    {
        public readonly float Livelihood;
        public readonly float Aggression;
        public readonly float Peace;

        public CourtDirectionVector(float livelihood, float aggression, float peace)
        {
            Livelihood = livelihood;
            Aggression = aggression;
            Peace = peace;
        }
    }

    public readonly struct CourtInfluenceContribution
    {
        public readonly long ActorId;
        public readonly string SchoolId;
        public readonly float Weight;
        public readonly int RoleRank;
        public readonly bool IsKing;

        public CourtInfluenceContribution(long actorId, string schoolId, float weight,
            int roleRank, bool isKing)
        {
            ActorId = actorId;
            SchoolId = schoolId ?? "";
            Weight = Math.Max(0f, weight);
            RoleRank = roleRank;
            IsKing = isKing;
        }
    }

    public sealed class CourtDirectionSnapshot
    {
        public float Livelihood;
        public float Aggression;
        public float Peace;
        public float KingShare;
        public List<long> CountedActorIds = new List<long>();
    }

    public static class CourtDirectionRules
    {
        private const float KingWeight = 0.25f;
        private const float CourtWeight = 0.75f;

        public static CourtDirectionSnapshot Aggregate(
            IEnumerable<CourtInfluenceContribution> pContributions)
        {
            var result = new CourtDirectionSnapshot();
            List<CourtInfluenceContribution> unique = Deduplicate(pContributions);
            result.CountedActorIds.AddRange(unique.Select(p => p.ActorId));

            CourtDirectionVector royal = WeightedAverage(unique.Where(p => p.IsKing));
            CourtDirectionVector court = WeightedAverage(unique.Where(p => !p.IsKing));
            bool hasKing = unique.Any(p => p.IsKing && p.Weight > 0f);
            bool hasCourt = unique.Any(p => !p.IsKing && p.Weight > 0f);
            if (!hasKing) royal = Neutral();
            if (!hasCourt) court = Neutral();

            result.KingShare = hasKing ? KingWeight : 0f;
            result.Livelihood = Clamp01(royal.Livelihood * KingWeight + court.Livelihood * CourtWeight);
            result.Aggression = Clamp01(royal.Aggression * KingWeight + court.Aggression * CourtWeight);
            result.Peace = Clamp01(royal.Peace * KingWeight + court.Peace * CourtWeight);
            return result;
        }

        public static CourtDirectionVector SchoolVector(string pSchoolId)
        {
            return pSchoolId switch
            {
                CourtSchoolId.Ru => new CourtDirectionVector(0.70f, 0.15f, 0.65f),
                CourtSchoolId.Legalist => new CourtDirectionVector(0.35f, 0.80f, 0.15f),
                CourtSchoolId.Dao => new CourtDirectionVector(0.55f, 0.00f, 0.90f),
                CourtSchoolId.Mohist => new CourtDirectionVector(0.75f, 0.15f, 0.75f),
                CourtSchoolId.Military => new CourtDirectionVector(0.20f, 1.00f, 0.05f),
                CourtSchoolId.Diplomat => new CourtDirectionVector(0.35f, 0.20f, 1.00f),
                CourtSchoolId.Agrarian => new CourtDirectionVector(1.00f, 0.05f, 0.45f),
                CourtSchoolId.YinYang => new CourtDirectionVector(0.55f, 0.35f, 0.55f),
                CourtSchoolId.Logician => new CourtDirectionVector(0.45f, 0.25f, 0.65f),
                CourtSchoolId.Medical => new CourtDirectionVector(1.00f, 0.00f, 0.70f),
                CourtSchoolId.Syncretist => new CourtDirectionVector(0.55f, 0.50f, 0.55f),
                CourtSchoolId.Merchant => new CourtDirectionVector(0.90f, 0.15f, 0.65f),
                CourtSchoolId.Craftsman => new CourtDirectionVector(0.90f, 0.25f, 0.35f),
                CourtSchoolId.Historian => new CourtDirectionVector(0.65f, 0.10f, 0.70f),
                _ => Neutral()
            };
        }

        public static float OffensiveWarMultiplier(float aggression, float peace, float livelihood,
            bool protectedWar)
        {
            if (protectedWar) return 1f;
            return Clamp(1f + (aggression - 0.5f) * 0.45f - (peace - 0.5f) * 0.35f -
                         (livelihood - 0.5f) * 0.15f, 0.5f, 1.5f);
        }

        public static float VoluntaryDiplomacyMultiplier(float peace)
        {
            return Clamp(1f + (peace - 0.5f) * 0.7f, 0.5f, 1.5f);
        }

        public static float ForcedVassalMultiplier(float aggression)
        {
            return Clamp(1f + (aggression - 0.5f) * 0.7f, 0.5f, 1.5f);
        }

        public static int LivelihoodResearchBonus(float livelihood, bool livelihoodNode)
        {
            return livelihoodNode ? (int)Math.Round(Clamp01(livelihood) * 70f) : 0;
        }

        public static float WhitePeaceChance(int warYears, float attackerToDefenderPower,
            float averagePeace, float averageAggression)
        {
            if (warYears < 10) return 0f;
            float duration = Math.Min(0.20f, (warYears - 10) * 0.01f);
            float stalemate = attackerToDefenderPower >= 0.8f && attackerToDefenderPower <= 1.25f ? 0.10f : 0f;
            float losing = attackerToDefenderPower < 0.8f ? 0.15f : 0f;
            float chance = Clamp01(averagePeace) * 0.25f + duration + stalemate + losing -
                           Clamp01(averageAggression) * 0.20f;
            return Clamp(chance, 0f, 0.45f);
        }

        private static List<CourtInfluenceContribution> Deduplicate(
            IEnumerable<CourtInfluenceContribution> pContributions)
        {
            var result = new List<CourtInfluenceContribution>();
            if (pContributions == null) return result;
            foreach (IGrouping<long, CourtInfluenceContribution> group in
                     pContributions.Where(p => p.ActorId >= 0).GroupBy(p => p.ActorId))
            {
                CourtInfluenceContribution primary = group
                    .OrderBy(p => p.RoleRank)
                    .ThenByDescending(p => p.Weight)
                    .First();
                float extra = group.Where(p => !SameContribution(p, primary)).Sum(p => p.Weight) * 0.15f;
                float weight = primary.Weight + Math.Min(primary.Weight * 0.15f, extra);
                result.Add(new CourtInfluenceContribution(primary.ActorId, primary.SchoolId,
                    weight, primary.RoleRank, primary.IsKing));
            }
            return result;
        }

        private static bool SameContribution(CourtInfluenceContribution pA, CourtInfluenceContribution pB)
        {
            return pA.ActorId == pB.ActorId && pA.RoleRank == pB.RoleRank &&
                   pA.Weight == pB.Weight && pA.SchoolId == pB.SchoolId && pA.IsKing == pB.IsKing;
        }

        private static CourtDirectionVector WeightedAverage(IEnumerable<CourtInfluenceContribution> pItems)
        {
            float total = 0f, livelihood = 0f, aggression = 0f, peace = 0f;
            foreach (CourtInfluenceContribution item in pItems)
            {
                CourtDirectionVector vector = SchoolVector(item.SchoolId);
                total += item.Weight;
                livelihood += vector.Livelihood * item.Weight;
                aggression += vector.Aggression * item.Weight;
                peace += vector.Peace * item.Weight;
            }
            return total <= 0f ? Neutral() :
                new CourtDirectionVector(livelihood / total, aggression / total, peace / total);
        }

        private static CourtDirectionVector Neutral() => new CourtDirectionVector(0.5f, 0.5f, 0.5f);
        private static float Clamp01(float pValue) => Math.Max(0f, Math.Min(1f, pValue));
        private static float Clamp(float pValue, float pMin, float pMax) => Math.Max(pMin, Math.Min(pMax, pValue));
    }
}
