namespace AncientWarfare3.core.policy
{
    // Compatibility facade for existing MapBox/minimap patches. Rendering is
    // owned by HierarchicalVassalBoundaryMeshLayer; this type intentionally
    // contains no legacy per-edge implementation.
    internal static class HierarchicalVassalMapModeBoundaryLayer
    {
        internal static void ProcessFrame()
        {
            HierarchicalVassalBoundaryMeshLayer.ProcessFrame();
        }

        internal static void Reset()
        {
            HierarchicalVassalBoundaryMeshLayer.Reset();
        }

        internal static void SetMinimapHidden(bool pHidden)
        {
            HierarchicalVassalBoundaryMeshLayer.SetMinimapHidden(pHidden);
        }
    }
}
