using AncientWarfare3.content;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class XiaCultureIntegrationService
    {
        public static bool IsIntegrated(Culture pCulture)
        {
            if (pCulture?.data == null) return false;
            try
            {
                return pCulture.hasTrait(XiaCultureTraits.IntegratedTraitId);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsNativeXiaCulture(Culture pCulture)
        {
            if (pCulture?.data == null) return false;
            return IsNativeXiaSpecies(pCulture.data.creator_species_id) ||
                   IsNativeXiaSpecies(pCulture.data.original_actor_asset);
        }

        public static bool MarkIntegrated(Culture pCulture)
        {
            if (pCulture?.data == null ||
                AssetManager.culture_traits.get(
                    XiaCultureTraits.IntegratedTraitId) == null)
                return false;

            bool changed = !IsIntegrated(pCulture) &&
                           pCulture.addTrait(
                               XiaCultureTraits.IntegratedTraitId);
            if (IsIntegrated(pCulture))
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
