namespace AncientWarfare3.core.court
{
    internal static class CityGovernorPlacementService
    {
        public static void OnCommittedAssignment(City pCity, Actor pActor)
        {
            if (!TryGetPlacementTile(pCity, pActor, out WorldTile cityTile))
                return;
            try
            {
                pActor.stopMovement();
                pActor.setCurrentTilePosition(cityTile);
                pActor.next_step_position = cityTile.posV3;
                pActor.dirty_current_tile = true;
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning(
                    "City governor placement failed: actor=" +
                    (pActor?.data?.id ?? -1L) + " city=" +
                    (pCity?.data?.id ?? -1L) + " error=" + error.Message);
            }
        }

        private static bool TryGetPlacementTile(City pCity, Actor pActor,
            out WorldTile pCityTile)
        {
            pCityTile = null;
            bool actorValid = pActor?.data != null && pActor.city == pCity;
            bool cityValid = pCity?.data != null && !pCity.isRekt();
            bool currentLeader = actorValid && cityValid &&
                                 pCity.leader == pActor;
            bool isInDestinationZone = actorValid && cityValid &&
                                       pActor.current_zone?.city == pCity;
            if (!CityGovernorPlacementRules.ShouldPlace(
                    newAssignment: true, actorValid, cityValid,
                    currentLeader, isInDestinationZone)) return false;

            pCityTile = pCity.getTile();
            return pCityTile != null;
        }
    }
}
