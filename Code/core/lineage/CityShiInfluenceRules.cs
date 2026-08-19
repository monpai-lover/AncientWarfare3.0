using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public enum CityShiRole
    {
        Member = 0,
        Noble = 1,
        Official = 2,
        Heir = 3,
        CityLeader = 4,
        King = 5
    }

    public readonly struct CityShiInfluenceContribution
    {
        public CityShiInfluenceContribution(long pActorId, long pShiId,
            CityShiRole pRole, long pBranchCreated)
        {
            ActorId = pActorId;
            ShiId = pShiId;
            Role = pRole;
            BranchCreated = pBranchCreated;
        }

        public long ActorId { get; }
        public long ShiId { get; }
        public CityShiRole Role { get; }
        public long BranchCreated { get; }
    }

    public sealed class CityShiInfluenceBranch
    {
        public long ShiId { get; internal set; }
        public long BranchCreated { get; internal set; }
        public int Weight { get; internal set; }
        public int HighestMemberWeight { get; internal set; }
        public int LivingMembers { get; internal set; }
        public string DisplayName { get; internal set; } = "";
        public bool IsValid { get; internal set; }
    }

    public sealed class CityShiInfluenceSnapshot
    {
        public CityShiInfluenceSnapshot(int pGeneration,
            IReadOnlyList<CityShiInfluenceBranch> pBranches)
        {
            Generation = pGeneration;
            Branches = pBranches ?? Array.Empty<CityShiInfluenceBranch>();
            TotalWeight = Branches.Sum(pBranch => pBranch.Weight);
            DominantShiId = Branches.Count == 0 ? -1L : Branches[0].ShiId;
        }

        public long CityId { get; internal set; } = -1L;
        public int Generation { get; }
        public IReadOnlyList<CityShiInfluenceBranch> Branches { get; }
        public int TotalWeight { get; }
        public long DominantShiId { get; }

        public CityShiInfluenceBranch FindBranch(long pShiId)
        {
            for (int i = 0; i < Branches.Count; i++)
                if (Branches[i].ShiId == pShiId) return Branches[i];
            return null;
        }

        public int SharePercent(long pShiId)
        {
            return TotalWeight <= 0 ? 0 :
                (int)Math.Round(ShareWeight(pShiId) * 100d / TotalWeight);
        }

        public int SharePerThousand(long pShiId)
        {
            return TotalWeight <= 0 ? 0 :
                (int)Math.Round(ShareWeight(pShiId) * 1000d / TotalWeight);
        }

        private int ShareWeight(long pShiId)
        {
            for (int i = 0; i < Branches.Count; i++)
                if (Branches[i].ShiId == pShiId) return Branches[i].Weight;
            return 0;
        }
    }

    public static class CityShiInfluenceRules
    {
        public static int RoleWeight(CityShiRole pRole)
        {
            return pRole switch
            {
                CityShiRole.King => 10,
                CityShiRole.CityLeader => 8,
                CityShiRole.Heir => 6,
                CityShiRole.Official => 4,
                CityShiRole.Noble => 2,
                _ => 1
            };
        }

        public static CityShiInfluenceSnapshot BuildSnapshot(int pGeneration,
            IEnumerable<CityShiInfluenceContribution> pContributions)
        {
            var actors = new Dictionary<long, CityShiInfluenceContribution>();
            foreach (CityShiInfluenceContribution contribution in
                     pContributions ?? Array.Empty<CityShiInfluenceContribution>())
            {
                if (contribution.ActorId < 0L || contribution.ShiId < 0L) continue;
                if (!actors.TryGetValue(contribution.ActorId,
                        out CityShiInfluenceContribution current) ||
                    IsPreferred(contribution, current))
                    actors[contribution.ActorId] = contribution;
            }

            var branches = new Dictionary<long, CityShiInfluenceBranch>();
            foreach (CityShiInfluenceContribution contribution in actors.Values)
            {
                if (!branches.TryGetValue(contribution.ShiId,
                        out CityShiInfluenceBranch branch))
                {
                    branch = new CityShiInfluenceBranch
                    {
                        ShiId = contribution.ShiId,
                        BranchCreated = contribution.BranchCreated
                    };
                    branches[contribution.ShiId] = branch;
                }

                int weight = RoleWeight(contribution.Role);
                branch.Weight += weight;
                branch.HighestMemberWeight = Math.Max(
                    branch.HighestMemberWeight, weight);
                branch.LivingMembers++;
                branch.BranchCreated = Math.Min(branch.BranchCreated,
                    contribution.BranchCreated);
            }

            CityShiInfluenceBranch[] ordered = branches.Values
                .OrderByDescending(pBranch => pBranch.Weight)
                .ThenByDescending(pBranch => pBranch.HighestMemberWeight)
                .ThenByDescending(pBranch => pBranch.LivingMembers)
                .ThenBy(pBranch => pBranch.BranchCreated)
                .ThenBy(pBranch => pBranch.ShiId)
                .ToArray();
            return new CityShiInfluenceSnapshot(pGeneration, ordered);
        }

        private static bool IsPreferred(CityShiInfluenceContribution pCandidate,
            CityShiInfluenceContribution pCurrent)
        {
            int candidateWeight = RoleWeight(pCandidate.Role);
            int currentWeight = RoleWeight(pCurrent.Role);
            return candidateWeight > currentWeight ||
                   candidateWeight == currentWeight &&
                   pCandidate.ShiId < pCurrent.ShiId;
        }
    }
}
