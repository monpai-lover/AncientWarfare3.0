namespace AncientWarfare3.core.performance;

/// <summary>
/// Keeps camera-only visibility changes from rebuilding every presentation
/// buffer while the native camera is still moving. Simulation snapshots and
/// render-mode changes always bypass the debounce.
/// </summary>
internal static class PresentationRefreshRules
{
    internal const double CameraRefreshIntervalSeconds = 0.08d;

    internal static bool ShouldRebuild(
        bool hasPrepared,
        bool snapshotChanged,
        bool renderModeChanged,
        bool visibilityChanged,
        double now,
        double nextCameraRefreshAt)
    {
        if (!hasPrepared || snapshotChanged || renderModeChanged)
            return true;
        if (!visibilityChanged)
            return false;
        return now >= nextCameraRefreshAt;
    }

    internal static double ScheduleNextCameraRefresh(double now)
    {
        return now + CameraRefreshIntervalSeconds;
    }

    internal static bool ShouldReadVisibleZones(
        bool hasCachedZones,
        double now,
        double nextReadAt)
    {
        return !hasCachedZones || now >= nextReadAt;
    }

}
