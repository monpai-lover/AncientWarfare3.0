namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapActivationRules
    {
        internal static bool ShouldOwnRenderer(bool coordinatorActive,
            bool cachedAssetMatches)
        {
            return coordinatorActive || cachedAssetMatches;
        }
    }
}
