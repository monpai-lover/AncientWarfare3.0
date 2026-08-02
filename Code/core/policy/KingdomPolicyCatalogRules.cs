using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomPolicyCatalogRules
    {
        public static bool BelongsTo(KingdomPolicyDef pDefinition,
            KingdomPolicyProfileId pProfile)
        {
            if (pDefinition == null ||
                !KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                    pProfile)) return false;
            KingdomPolicyProfileId[] memberships =
                pDefinition.ProfileIds ?? Array.Empty<KingdomPolicyProfileId>();
            return memberships.Contains(pProfile) ||
                   memberships.Contains(KingdomPolicyProfileId.Common);
        }

        public static void Validate(
            IEnumerable<KingdomPolicyDef> pDefinitions)
        {
            KingdomPolicyDef[] definitions =
                (pDefinitions ?? Enumerable.Empty<KingdomPolicyDef>())
                .ToArray();
            var byId = new Dictionary<string, KingdomPolicyDef>(
                StringComparer.Ordinal);
            foreach (KingdomPolicyDef definition in definitions)
            {
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.Id))
                    throw new InvalidOperationException(
                        "Policy catalog contains an empty definition id.");
                if (byId.ContainsKey(definition.Id))
                    throw new InvalidOperationException(
                        "Duplicate policy definition id: " + definition.Id);
                byId[definition.Id] = definition;
                ValidateMembership(definition);
            }

            foreach (KingdomPolicyDef definition in definitions)
            {
                ValidateRequirements(definition, byId,
                    KingdomPolicyProfileId.Xia);
                ValidateRequirements(definition, byId,
                    KingdomPolicyProfileId.WesternGeneral);
            }
        }

        private static void ValidateMembership(KingdomPolicyDef pDefinition)
        {
            KingdomPolicyProfileId[] memberships =
                pDefinition.ProfileIds ?? Array.Empty<KingdomPolicyProfileId>();
            if (memberships.Length == 0 ||
                memberships.Any(pProfile =>
                    pProfile == KingdomPolicyProfileId.None) ||
                memberships.Distinct().Count() != memberships.Length)
            {
                throw new InvalidOperationException(
                    "Invalid policy profile membership: " + pDefinition.Id);
            }
        }

        private static void ValidateRequirements(KingdomPolicyDef pDefinition,
            IReadOnlyDictionary<string, KingdomPolicyDef> pById,
            KingdomPolicyProfileId pProfile)
        {
            if (!BelongsTo(pDefinition, pProfile)) return;
            ValidateRequirementSet(pDefinition, pDefinition.RequiredPolicies,
                pById, pProfile);
            ValidateRequirementSet(pDefinition, pDefinition.RequiredTechs,
                pById, pProfile);
        }

        private static void ValidateRequirementSet(KingdomPolicyDef pOwner,
            IEnumerable<string> pRequirementIds,
            IReadOnlyDictionary<string, KingdomPolicyDef> pById,
            KingdomPolicyProfileId pProfile)
        {
            foreach (string requirementId in pRequirementIds ??
                     Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(requirementId) ||
                    !pById.TryGetValue(requirementId,
                        out KingdomPolicyDef requirement))
                {
                    throw new InvalidOperationException(
                        "Missing policy requirement '" + requirementId +
                        "' for " + pOwner.Id);
                }
                if (!BelongsTo(requirement, pProfile))
                    throw new InvalidOperationException(
                        "Cross-profile requirement '" + requirementId +
                        "' for " + pOwner.Id);
            }
        }
    }
}
