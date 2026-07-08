using System;

namespace AncientWarfare3.core.lineage
{
    public static class PathfindingSafetyRules
    {
        public static bool ShouldConvertGlobalPathExceptionToNotFound(
            Exception pException,
            bool pHasStartTile,
            bool pHasTargetTile)
        {
            if (pException == null) return false;
            if (!pHasStartTile || !pHasTargetTile) return false;
            return pException is NullReferenceException;
        }
    }
}
