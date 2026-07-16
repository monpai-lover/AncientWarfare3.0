namespace AncientWarfare3.core.policy
{
    public static class SchoolNameplateRenderRules
    {
        public static bool CanRender(string pDominantSchool, float pTotalScore,
            bool pDefinitionExists)
        {
            return pDefinitionExists && pTotalScore > 0f &&
                   !string.IsNullOrEmpty(pDominantSchool);
        }
    }
}
