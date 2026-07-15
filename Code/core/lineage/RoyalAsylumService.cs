namespace AncientWarfare3.core.lineage
{
    internal static class RoyalAsylumService
    {
        public static bool IsActive(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_ACTIVE, out bool active, false);
            return active;
        }

        public static bool TryGetRoamTile(Actor pActor, out WorldTile pTile)
        {
            pTile = null;
            if (!IsActive(pActor) || pActor.isRekt() || !pActor.isAlive()) return false;
            City hostCity = ResolveHostCity(pActor);
            if (hostCity?.data == null || hostCity.isRekt()) return false;
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_HOST_KINGDOM_ID,
                out long hostKingdomId, -1L);
            if (hostCity.kingdom?.data == null || hostCity.kingdom.id != hostKingdomId)
                return false;
            return RoyalAsylumVenueService.TryPick(hostCity, pActor.data.id,
                Date.getCurrentYear(), out pTile);
        }

        public static City ResolveHostCity(Actor pActor)
        {
            if (pActor?.data == null) return null;
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_HOST_CITY_ID,
                out long hostCityId, -1L);
            if (hostCityId < 0) return null;
            try { return World.world?.cities?.get(hostCityId); }
            catch { return null; }
        }
    }
}
