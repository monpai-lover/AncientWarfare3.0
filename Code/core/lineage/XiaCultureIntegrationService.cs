using AncientWarfare3.content;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class XiaCultureIntegrationService
    {
        public static bool IsIntegrated(Culture pCulture)
        {
            return HasTrait(pCulture, XiaCultureTraits.IntegratedTraitId);
        }

        public static bool IsFullyIntegrated(Culture pCulture)
        {
            return HasTrait(pCulture,
                XiaCultureTraits.FullyIntegratedTraitId);
        }

        public static bool IsNativeXiaCulture(Culture pCulture)
        {
            if (pCulture?.data == null) return false;
            return IsNativeXiaSpecies(pCulture.data.creator_species_id) ||
                   IsNativeXiaSpecies(pCulture.data.original_actor_asset);
        }

        public static bool MarkIntegrated(Culture pCulture)
        {
            bool changed = MarkTrait(pCulture,
                XiaCultureTraits.IntegratedTraitId);
            if (IsIntegrated(pCulture) || IsFullyIntegrated(pCulture))
                IntegratedCultureNamingMigrationService.Request(pCulture);
            return changed;
        }

        public static bool MarkFullyIntegrated(Culture pCulture)
        {
            bool integratedChanged = MarkTrait(pCulture,
                XiaCultureTraits.IntegratedTraitId);
            bool fullChanged = MarkTrait(pCulture,
                XiaCultureTraits.FullyIntegratedTraitId);
            if (IsIntegrated(pCulture) || IsFullyIntegrated(pCulture))
                IntegratedCultureNamingMigrationService.Request(pCulture);
            return integratedChanged || fullChanged;
        }

        public static bool InheritFullyIntegrated(Culture pChild,
            Culture pParent)
        {
            return pChild?.data != null && pParent?.data != null &&
                   !object.ReferenceEquals(pChild, pParent) &&
                   IsFullyIntegrated(pParent) &&
                   MarkFullyIntegrated(pChild);
        }

        private static bool HasTrait(Culture pCulture, string pTraitId)
        {
            if (pCulture?.data == null) return false;
            try
            {
                return pCulture.hasTrait(pTraitId);
            }
            catch
            {
                return false;
            }
        }

        private static bool MarkTrait(Culture pCulture, string pTraitId)
        {
            if (pCulture?.data == null ||
                AssetManager.culture_traits.get(pTraitId) == null)
                return false;

            bool changed = !HasTrait(pCulture, pTraitId) &&
                           pCulture.addTrait(pTraitId);
            if (HasTrait(pCulture, pTraitId))
                pCulture.data.saved_traits = pCulture.getTraitsAsStrings();
            return changed;
        }

        private static bool IsNativeXiaSpecies(string pSpeciesId)
        {
            return pSpeciesId == LineageService.XIA_ASSET_ID ||
                   CivMonkeyPolicyRules.IsNativeXiaCultureSpecies(
                       pSpeciesId);
        }
    }
}
