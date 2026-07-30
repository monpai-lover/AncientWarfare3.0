namespace AncientWarfare3.core.lineage
{
    public static class EmptyCitySurvivalRules
    {
        public static bool ShouldSuppressNaturalBorderShrink(
            bool cityValid, bool cityRekt, bool ownerValid,
            int zoneCount, bool hasLivingResidents, bool hasRazeIntent)
        {
            return cityValid && !cityRekt && ownerValid && zoneCount > 0 &&
                   !hasLivingResidents && !hasRazeIntent;
        }

        public static bool ShouldSuppressAutomaticAbandonedZoneCleanup(
            bool cityValid, bool cityRekt, int zoneCount)
        {
            return cityValid && !cityRekt && zoneCount > 0;
        }

        public static bool ShouldRecordXenophobicRazeIntent(
            bool gettingCaptured, bool capturerValid,
            bool capturerKingXenophobic, bool differentSpecies,
            bool defenderStillInside)
        {
            return gettingCaptured && capturerValid &&
                   capturerKingXenophobic && differentSpecies &&
                   !defenderStillInside;
        }

        public static bool ShouldClearRazeIntent(
            bool residentJoined, bool ownerChanged,
            bool newOwnerNeutral, bool fromLoad)
        {
            if (fromLoad) return false;
            return residentJoined || ownerChanged && !newOwnerNeutral;
        }

        public static bool ShouldKeepFormalOwner(bool frozenOccupation)
        {
            return frozenOccupation;
        }

    }
}
