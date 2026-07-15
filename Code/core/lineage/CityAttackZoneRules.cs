namespace AncientWarfare3.core.lineage
{
    public static class CityAttackZoneRules
    {
        public static bool ShouldRepairTargetZone(bool hasTargetCity, bool targetHasZones,
            bool hasCurrentZone, bool currentZoneBelongsToTarget)
        {
            return hasTargetCity && targetHasZones &&
                   (!hasCurrentZone || !currentZoneBelongsToTarget);
        }
    }
}
