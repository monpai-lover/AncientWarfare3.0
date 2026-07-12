using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public readonly struct CourtDirectionVector
    {
        public readonly float Livelihood;
        public readonly float War;
        public readonly float Aggression;
        public readonly float Peace;
        public readonly float Order;
        public readonly float Commerce;
        public readonly float Technology;

        public CourtDirectionVector(float livelihood, float war, float aggression, float peace,
            float order, float commerce, float technology)
        {
            Livelihood = livelihood;
            War = war;
            Aggression = aggression;
            Peace = peace;
            Order = order;
            Commerce = commerce;
            Technology = technology;
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
        public float War;
        public float Aggression;
        public float Peace;
        public float Order;
        public float Commerce;
        public float Technology;
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
            result.War = Clamp01(royal.War * KingWeight + court.War * CourtWeight);
            result.Aggression = Clamp01(royal.Aggression * KingWeight + court.Aggression * CourtWeight);
            result.Peace = Clamp01(royal.Peace * KingWeight + court.Peace * CourtWeight);
            result.Order = Clamp01(royal.Order * KingWeight + court.Order * CourtWeight);
            result.Commerce = Clamp01(royal.Commerce * KingWeight + court.Commerce * CourtWeight);
            result.Technology = Clamp01(royal.Technology * KingWeight + court.Technology * CourtWeight);
            return result;
        }

        public static CourtDirectionVector SchoolVector(string pSchoolId)
        {
            CourtSchoolDefinition definition = CourtSchoolRegistry.Find(pSchoolId);
            if (definition == null) return Neutral();
            CourtSchoolDirection value = definition.Direction;
            return new CourtDirectionVector(value.Livelihood, value.War, value.Aggression,
                value.Peace, value.Order, value.Commerce, value.Technology);
        }

        public static CourtDirectionSnapshot Smooth(CourtDirectionSnapshot pPrevious,
            CourtDirectionSnapshot pTarget, float alpha, float deadband)
        {
            if (pTarget == null) return pPrevious ?? new CourtDirectionSnapshot();
            if (pPrevious == null) return pTarget;
            float amount = Clamp01(alpha);
            float band = Math.Max(0f, deadband);
            return new CourtDirectionSnapshot
            {
                Livelihood = SmoothValue(pPrevious.Livelihood, pTarget.Livelihood, amount, band),
                War = SmoothValue(pPrevious.War, pTarget.War, amount, band),
                Aggression = SmoothValue(pPrevious.Aggression, pTarget.Aggression, amount, band),
                Peace = SmoothValue(pPrevious.Peace, pTarget.Peace, amount, band),
                Order = SmoothValue(pPrevious.Order, pTarget.Order, amount, band),
                Commerce = SmoothValue(pPrevious.Commerce, pTarget.Commerce, amount, band),
                Technology = SmoothValue(pPrevious.Technology, pTarget.Technology, amount, band),
                KingShare = pTarget.KingShare,
                CountedActorIds = new List<long>(pTarget.CountedActorIds)
            };
        }

        public static float OffensiveWarMultiplier(float aggression, float peace, float livelihood,
            bool protectedWar)
        {
            return OffensiveWarMultiplier(aggression, peace, livelihood, 0.5f, protectedWar);
        }

        public static float OffensiveWarMultiplier(float aggression, float peace, float livelihood,
            float war, bool protectedWar)
        {
            if (protectedWar) return 1f;
            return Clamp(1f + (aggression - 0.5f) * 0.45f - (peace - 0.5f) * 0.35f -
                         (livelihood - 0.5f) * 0.15f + (war - 0.5f) * 0.25f, 0.5f, 1.5f);
        }

        public static float VoluntaryDiplomacyMultiplier(float peace)
        {
            return Clamp(1f + (peace - 0.5f) * 0.7f, 0.5f, 1.5f);
        }

        public static float ForcedVassalMultiplier(float aggression)
        {
            return ForcedVassalMultiplier(aggression, 0.5f);
        }

        public static float ForcedVassalMultiplier(float aggression, float order)
        {
            return Clamp(1f + (aggression - 0.5f) * 0.6f + (order - 0.5f) * 0.25f,
                0.5f, 1.5f);
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
                    .OrderByDescending(p => p.Weight)
                    .ThenBy(p => p.RoleRank)
                    .First();
                result.Add(new CourtInfluenceContribution(primary.ActorId, primary.SchoolId,
                    primary.Weight, primary.RoleRank, primary.IsKing));
            }
            return result;
        }

        private static CourtDirectionVector WeightedAverage(IEnumerable<CourtInfluenceContribution> pItems)
        {
            float total = 0f, livelihood = 0f, war = 0f, aggression = 0f, peace = 0f,
                order = 0f, commerce = 0f, technology = 0f;
            foreach (CourtInfluenceContribution item in pItems)
            {
                CourtDirectionVector vector = SchoolVector(item.SchoolId);
                total += item.Weight;
                livelihood += vector.Livelihood * item.Weight;
                war += vector.War * item.Weight;
                aggression += vector.Aggression * item.Weight;
                peace += vector.Peace * item.Weight;
                order += vector.Order * item.Weight;
                commerce += vector.Commerce * item.Weight;
                technology += vector.Technology * item.Weight;
            }
            return total <= 0f ? Neutral() :
                new CourtDirectionVector(livelihood / total, war / total, aggression / total,
                    peace / total, order / total, commerce / total, technology / total);
        }

        private static float SmoothValue(float pPrevious, float pTarget, float pAlpha, float pDeadband)
        {
            float previous = Clamp01(pPrevious);
            float target = Clamp01(pTarget);
            if (Math.Abs(target - previous) <= pDeadband) return previous;
            return Clamp01(previous + (target - previous) * pAlpha);
        }

        private static CourtDirectionVector Neutral() =>
            new CourtDirectionVector(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
        private static float Clamp01(float pValue) => Math.Max(0f, Math.Min(1f, pValue));
        private static float Clamp(float pValue, float pMin, float pMax) => Math.Max(pMin, Math.Min(pMax, pValue));
    }
}
