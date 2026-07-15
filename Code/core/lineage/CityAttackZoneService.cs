namespace AncientWarfare3.core.lineage
{
    internal static class CityAttackZoneService
    {
        public static void RepairAfterTargetSelection(City pSourceCity)
        {
            if (pSourceCity?.data == null) return;
            City targetCity = pSourceCity.target_attack_city;
            TileZone currentZone = pSourceCity.target_attack_zone;
            bool targetHasZones = false;
            try { targetHasZones = targetCity?.data != null && targetCity.hasZones(); }
            catch { }

            if (!CityAttackZoneRules.ShouldRepairTargetZone(
                    targetCity?.data != null,
                    targetHasZones,
                    currentZone != null,
                    currentZone?.city == targetCity))
                return;

            try { pSourceCity.target_attack_zone = targetCity.zones.GetRandom(); }
            catch { }
        }
    }
}
