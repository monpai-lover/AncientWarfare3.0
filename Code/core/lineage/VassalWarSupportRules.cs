namespace AncientWarfare3.core.lineage
{
    public static class VassalWarSupportRules
    {
        public static bool ShouldPullIntoSuzerainWar(bool pSuzerainInWar,
            bool pVassalAlreadyHelping,
            bool pVassalAlreadyInWar,
            bool pVassalOpposesSuzerain,
            bool independenceSuspended = false)
        {
            if (independenceSuspended) return false;
            if (!pSuzerainInWar) return false;
            if (pVassalAlreadyHelping) return false;
            if (pVassalOpposesSuzerain) return false;
            if (pVassalAlreadyInWar) return false;
            return true;
        }

        public static bool ShouldLeaveForIndependence(bool isIndependenceWar,
            bool rebelInWar, bool suzerainInWar, bool sameSide)
        {
            return !isIndependenceWar && rebelInWar && suzerainInWar && sameSide;
        }

        public static bool HasActiveIndependenceSuspension(bool markerMatches,
            bool warActive, bool rebelOpposesSuzerain)
        {
            return markerMatches && warActive && rebelOpposesSuzerain;
        }
    }
}
