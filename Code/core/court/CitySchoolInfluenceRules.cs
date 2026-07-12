using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public static class CitySchoolRole
    {
        public const string King = "king";
        public const string Heir = "heir";
        public const string Leader = "leader";
        public const string CentralOfficer = "central_officer";
        public const string General = "general";
        public const string LocalOfficer = "local_officer";
    }

    public sealed class CitySchoolInfluenceContribution
    {
        public long ActorId { get; }
        public string SchoolId { get; }
        public string Role { get; }
        public float BaseWeight { get; }
        public float Ability { get; }
        public int RoleRank { get; }
        public string ActorName { get; }
        public float Score { get; internal set; }

        public CitySchoolInfluenceContribution(long actorId, string schoolId, string role,
            float baseWeight, float ability, int roleRank, string actorName = "")
        {
            ActorId = actorId;
            SchoolId = schoolId ?? "";
            Role = role ?? "";
            BaseWeight = baseWeight;
            Ability = ability;
            RoleRank = roleRank;
            ActorName = actorName ?? "";
        }
    }

    public sealed class CitySchoolSnapshot
    {
        public long CityId { get; internal set; } = -1L;
        public long KingdomId { get; internal set; } = -1L;
        public int Generation { get; internal set; }
        public string DominantSchool { get; internal set; } = CourtSchoolId.None;
        public float TotalScore { get; internal set; }
        public IReadOnlyDictionary<string, float> Scores { get; internal set; } =
            new Dictionary<string, float>();
        public IReadOnlyList<CitySchoolInfluenceContribution> Contributors { get; internal set; } =
            Array.Empty<CitySchoolInfluenceContribution>();

        public float Share(string pSchoolId)
        {
            if (TotalScore <= 0f || string.IsNullOrEmpty(pSchoolId) ||
                !Scores.TryGetValue(pSchoolId, out float score)) return 0f;
            return score / TotalScore;
        }
    }

    public static class CitySchoolInfluenceRules
    {
        public static float RoleBaseWeight(string pRole)
        {
            switch (pRole ?? "")
            {
                case CitySchoolRole.King: return 8f;
                case CitySchoolRole.Heir: return 5f;
                case CitySchoolRole.Leader: return 5f;
                case CitySchoolRole.CentralOfficer: return 4f;
                case CitySchoolRole.General: return 3f;
                case CitySchoolRole.LocalOfficer: return 2f;
                default: return 0f;
            }
        }

        public static float ApplyAbilityModifier(float pBaseWeight, float pAbility)
        {
            float normalized = Math.Max(0f, Math.Min(100f, pAbility)) / 100f;
            return Math.Max(0f, pBaseWeight) * (0.8f + normalized * 0.4f);
        }

        public static CitySchoolSnapshot BuildSnapshot(int pGeneration,
            IEnumerable<CitySchoolInfluenceContribution> pContributions)
        {
            List<CitySchoolInfluenceContribution> unique = (pContributions ??
                    Array.Empty<CitySchoolInfluenceContribution>())
                .Where(IsValid)
                .GroupBy(p => p.ActorId)
                .Select(p => p.OrderByDescending(RoleWeight)
                    .ThenBy(v => v.RoleRank)
                    .ThenBy(v => RegistryOrder(v.SchoolId))
                    .First())
                .ToList();

            foreach (CitySchoolInfluenceContribution contribution in unique)
                contribution.Score = ApplyAbilityModifier(RoleWeight(contribution), contribution.Ability);

            Dictionary<string, float> scores = unique
                .GroupBy(p => p.SchoolId)
                .ToDictionary(p => p.Key, p => p.Sum(v => v.Score), StringComparer.Ordinal);
            float total = scores.Values.Sum();
            string dominant = scores.Count == 0
                ? CourtSchoolId.None
                : scores.Keys
                    .OrderByDescending(p => scores[p])
                    .ThenByDescending(p => unique.Where(v => v.SchoolId == p).Max(v => v.Score))
                    .ThenBy(p => unique.Where(v => v.SchoolId == p).Min(v => v.RoleRank))
                    .ThenBy(RegistryOrder)
                    .First();

            return new CitySchoolSnapshot
            {
                Generation = pGeneration,
                DominantSchool = dominant,
                TotalScore = total,
                Scores = scores,
                Contributors = unique.OrderByDescending(p => p.Score)
                    .ThenBy(p => p.RoleRank)
                    .ThenBy(p => p.ActorId)
                    .ToArray()
            };
        }

        private static bool IsValid(CitySchoolInfluenceContribution pContribution)
        {
            return pContribution != null && pContribution.ActorId >= 0 &&
                   CourtSchoolRegistry.Find(pContribution.SchoolId) != null &&
                   RoleWeight(pContribution) > 0f;
        }

        private static float RoleWeight(CitySchoolInfluenceContribution pContribution)
        {
            if (pContribution == null) return 0f;
            return pContribution.BaseWeight > 0f
                ? pContribution.BaseWeight
                : RoleBaseWeight(pContribution.Role);
        }

        private static int RegistryOrder(string pSchoolId)
        {
            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
                if (CourtSchoolRegistry.All[i].Id == pSchoolId) return i;
            return int.MaxValue;
        }
    }
}
