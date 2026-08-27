namespace AncientWarfare3.core.performance
{
    internal static class NameplateRefreshRules
    {
        internal static bool ShouldRefresh(bool ready, double now,
            double nextAllowedAt, ulong previousSignature,
            ulong currentSignature)
        {
            // Camera zoom/pan changes the visible-range signature every
            // frame. The candidate list is deliberately throttled; map-mode
            // changes clear the ready flag and still refresh immediately.
            if (!ready) return true;
            return now >= nextAllowedAt;
        }
    }
}
