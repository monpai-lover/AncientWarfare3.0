using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;

namespace AncientWarfare3.core.policy
{
    internal static class WesternPolicyDefinitionRules
    {
        public static bool CanAppeaseForeignCity(bool controlledByKingdom,
            long rulerCultureId, long cityCultureId)
        {
            return controlledByKingdom && rulerCultureId >= 0 &&
                   cityCultureId >= 0 && rulerCultureId != cityCultureId;
        }

        public static void ValidateAcyclic(
            IEnumerable<KingdomPolicyDef> pDefinitions)
        {
            KingdomPolicyDef[] definitions =
                (pDefinitions ?? Enumerable.Empty<KingdomPolicyDef>())
                .ToArray();
            var byId = definitions.ToDictionary(definition => definition.Id,
                StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (KingdomPolicyDef definition in definitions)
                Visit(definition.Id, byId, visiting, visited);
        }

        private static void Visit(string pId,
            IReadOnlyDictionary<string, KingdomPolicyDef> pById,
            ISet<string> pVisiting, ISet<string> pVisited)
        {
            if (pVisited.Contains(pId)) return;
            if (!pVisiting.Add(pId))
                throw new InvalidOperationException(
                    "Cyclic Western policy prerequisite: " + pId);
            if (pById.TryGetValue(pId, out KingdomPolicyDef definition))
            {
                foreach (string requiredId in
                         (definition.RequiredTechs ?? Array.Empty<string>())
                         .Concat(definition.RequiredPolicies ??
                                 Array.Empty<string>()))
                {
                    if (pById.ContainsKey(requiredId))
                        Visit(requiredId, pById, pVisiting, pVisited);
                }
            }
            pVisiting.Remove(pId);
            pVisited.Add(pId);
        }
    }
}
