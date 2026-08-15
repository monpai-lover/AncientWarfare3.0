namespace AncientWarfare3.core.policy
{
    internal static class CivicLeaderLandClaimService
    {
        private static bool _claimLandPipelineReady;

        internal static void SetClaimLandPipelineReady(bool pReady)
        {
            _claimLandPipelineReady = pReady;
        }

        internal static bool CanUseExternalSelector(Actor pActor)
        {
            return XiaExpansionDecisionRules.ShouldUseExternalClaimSelector(
                _claimLandPipelineReady, IsCivicLeader(pActor));
        }

        internal static bool IsCivicLeader(Actor pActor)
        {
            City city = pActor?.city;
            if (pActor?.data == null || city?.data == null) return false;
            bool isKing = false;
            try { isKing = pActor.isKing(); }
            catch { }
            return XiaExpansionDecisionRules.IsCivicLeader(
                isKing, city.leader == pActor);
        }

        internal static bool TrySetExternalTarget(Actor pActor)
        {
            City city = pActor?.city;
            if (!IsCivicLeader(pActor) || city?.data == null)
                return false;
            if (TrySetCitySeedTarget(pActor, city)) return true;
            if (city.border_zones == null) return false;
            foreach (TileZone border in city.border_zones)
            {
                TileZone[] neighbours = border?.neighbours_all;
                if (neighbours == null) continue;
                for (int i = 0; i < neighbours.Length; i++)
                {
                    TileZone candidate = neighbours[i];
                    if (!IsValidExternalZone(pActor, candidate)) continue;
                    pActor.beh_tile_target = candidate.centerTile;
                    return true;
                }
            }
            return false;
        }

        private static bool TrySetCitySeedTarget(Actor pActor, City pCity)
        {
            bool hasNoZones;
            try { hasNoZones = pCity.countZones() == 0; }
            catch { return false; }
            if (!hasNoZones) return false;

            TileZone seedZone = pCity.getTile()?.zone;
            if (!IsValidExternalZone(pActor, seedZone)) return false;
            pActor.beh_tile_target = seedZone.centerTile;
            return true;
        }

        internal static bool IsValidArrival(Actor pActor)
        {
            TileZone currentZone = pActor?.current_tile?.zone;
            TileZone selectedZone = pActor?.beh_tile_target?.zone;
            bool zoneStillValid = IsValidExternalZone(pActor, currentZone);
            return XiaExpansionDecisionRules.CanBeginExternalClaimAnimation(
                ReferenceEquals(currentZone, selectedZone), zoneStillValid);
        }

        internal static bool IsValidExternalZone(Actor pActor,
            TileZone pZone)
        {
            City city = pActor?.city;
            WorldTile cityTile = city?.getTile();
            bool exists = pZone != null;
            bool hasCenter = pZone?.centerTile != null;
            bool hasCity = exists && pZone.hasCity();
            bool touchesOwnCity = TouchesCityBoundary(city, pZone);
            bool isCitySeedZone = false;
            try
            {
                isCitySeedZone = city?.countZones() == 0 &&
                    ReferenceEquals(pZone, cityTile?.zone);
            }
            catch { }
            bool sameIsland = hasCenter && cityTile != null &&
                              pZone.centerTile.isSameIsland(cityTile);
            bool nativeAllowed = exists && cityTile != null &&
                                 city.isZoneToClaimStillGood(
                                     pActor, pZone, cityTile);
            return XiaExpansionDecisionRules.IsExternalClaimZoneValid(
                exists, hasCenter, hasCity, touchesOwnCity, sameIsland,
                nativeAllowed, isCitySeedZone);
        }

        private static bool TouchesCityBoundary(City pCity,
            TileZone pZone)
        {
            if (pCity?.data == null || pZone?.neighbours_all == null)
                return false;
            TileZone[] neighbours = pZone.neighbours_all;
            for (int i = 0; i < neighbours.Length; i++)
                if (neighbours[i]?.city == pCity) return true;
            return false;
        }
    }
}
