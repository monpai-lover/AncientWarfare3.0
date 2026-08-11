using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.naming
{
    internal sealed class ActorManualBranchPlan
    {
        internal ActorManualBranchPlan(IReadOnlyList<long> pActorIds,
            bool pRequiresBranchFork)
        {
            ActorIds = pActorIds;
            RequiresBranchFork = pRequiresBranchFork;
        }

        internal IReadOnlyList<long> ActorIds { get; }
        internal bool RequiresBranchFork { get; }
    }

    internal sealed class ActorManualFamilyWritePlan
    {
        internal ActorManualFamilyWritePlan(string pFamilyName)
        {
            FamilyName = pFamilyName;
            ChineseFamilyName = pFamilyName;
            ClanName = pFamilyName;
            LocalizedFamilyComponent = pFamilyName;
            NameIntegrated = pFamilyName.Length > 0;
        }

        internal string FamilyName { get; }
        internal string ChineseFamilyName { get; }
        internal string ClanName { get; }
        internal string LocalizedFamilyComponent { get; }
        internal bool NameIntegrated { get; }
    }

    internal static class ActorManualRenameRules
    {
        internal static ActorManualNameMode ResolveMode(bool isXia,
            NamingProfileId profile)
        {
            if (isXia || profile == NamingProfileId.Xia)
                return ActorManualNameMode.Xia;
            return profile == NamingProfileId.Monkey ||
                   profile == NamingProfileId.NativeSinitic
                ? ActorManualNameMode.SiniticMerged
                : ActorManualNameMode.NonXia;
        }

        internal static bool UsesFamilyFirst(ActorManualNameMode pMode)
        {
            return pMode != ActorManualNameMode.NonXia;
        }

        internal static bool UsesSingleMergedShi(ActorManualNameMode pMode)
        {
            return pMode == ActorManualNameMode.SiniticMerged;
        }

        internal static string ResolveFamilyIdentity(
            ActorManualNameMode pMode, string clanName, string familyName,
            string chineseFamilyName, string localizedFamilyComponent)
        {
            string clan = Normalize(clanName);
            string family = Normalize(familyName);
            string chineseFamily = Normalize(chineseFamilyName);
            string localizedFamily = Normalize(localizedFamilyComponent);
            if (pMode != ActorManualNameMode.NonXia && clan.Length > 0)
                return clan;
            if (family.Length > 0) return family;
            if (clan.Length > 0) return clan;
            if (chineseFamily.Length > 0) return chineseFamily;
            return localizedFamily;
        }

        internal static ActorManualFamilyWritePlan PlanIntegratedFamilyWrite(
            string pFamilyName)
        {
            return new ActorManualFamilyWritePlan(Normalize(pFamilyName));
        }

        internal static ActorManualBranchPlan PlanBranchChange(long pRootId,
            string pCurrentFamily, string pRequestedFamily,
            IEnumerable<long> pPatrilinealIds)
        {
            string current = Normalize(pCurrentFamily);
            string requested = Normalize(pRequestedFamily);
            var result = new List<long>();
            var seen = new HashSet<long>();
            if (pPatrilinealIds != null)
            {
                foreach (long id in pPatrilinealIds)
                {
                    if (id < 0 || !seen.Add(id)) continue;
                    result.Add(id);
                }
            }
            if (pRootId >= 0 && seen.Add(pRootId))
                result.Insert(0, pRootId);
            return new ActorManualBranchPlan(result,
                !string.Equals(current, requested,
                    StringComparison.Ordinal));
        }

        private static string Normalize(string pValue)
        {
            return string.Join(" ", (pValue ?? string.Empty)
                .Trim()
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
