using System.Collections.Generic;

namespace AncientWarfare3.core.performance;

internal static class AWPresentationVisibility
{
    internal static ulong GetSignature(bool renderGameplay)
    {
        return BuildSignature(renderGameplay);
    }

    private static ulong BuildSignature(bool renderGameplay)
    {
        unchecked
        {
            const ulong offset = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            ulong hash = (offset ^ (renderGameplay ? 1UL : 0UL)) * prime;
            List<TileZone> zones =
                World.world?.zone_camera?.getVisibleZones();
            if (zones == null)
            {
                return hash;
            }

            hash = (hash ^ (ulong)zones.Count) * prime;
            if (zones.Count == 0) return hash;

            // ZoneCamera owns this ordered rectangular list. Its count and
            // endpoints change whenever the native visible range changes.
            hash = (hash ^ (uint)(zones[0]?.id ?? -1)) * prime;
            hash = (hash ^ (uint)(zones[zones.Count - 1]?.id ?? -1)) * prime;

            return hash;
        }
    }
}
