using System.Collections.Generic;

namespace AncientWarfare3.core.presentation
{
    public static class MandateCapitalBuildingTextureRules
    {
        public static bool ShouldUseCapitalTexture(bool pIsXiaBuilding,
            bool pIsMandateKingdom, bool pIsKingdomCapital)
        {
            return pIsXiaBuilding && pIsMandateKingdom && pIsKingdomCapital;
        }

        public static int ResolveVariantIndex(
            IReadOnlyList<int> pAvailableIndices, int pRequestedIndex)
        {
            if (pAvailableIndices == null || pAvailableIndices.Count == 0)
                return -1;
            for (int i = 0; i < pAvailableIndices.Count; i++)
                if (pAvailableIndices[i] == pRequestedIndex)
                    return pRequestedIndex;
            for (int i = 0; i < pAvailableIndices.Count; i++)
                if (pAvailableIndices[i] == 0)
                    return 0;
            return pAvailableIndices[0];
        }
    }
}
