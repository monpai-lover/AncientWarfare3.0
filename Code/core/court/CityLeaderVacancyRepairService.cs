using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CityLeaderVacancyRepairService
    {
        private const int MaximumAttempts = 2;

        internal static void Request(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom?.data == null) return;
            long kingdomId = pCity.kingdom.id;
            long cityId = pCity.data.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "city-leader-vacancy:" + kingdomId + ":" + cityId,
                DeferredWorkClass.CriticalRuntime,
                () => Repair(kingdomId, cityId, 0));
        }

        private static void Repair(long pKingdomId, long pCityId,
            int pAttempt)
        {
            Kingdom kingdom = FindKingdom(pKingdomId);
            City city = FindCity(pCityId);
            if (kingdom?.data == null || city?.data == null ||
                kingdom.isRekt() || city.isRekt() || city.kingdom != kingdom ||
                city.hasLeader() || city.isGettingCaptured()) return;

            // The Harmony prefix now delegates to the same local appointment
            // reconciler used by the court window.  There is deliberately no
            // second local-only candidate selector here.
            AncientWarfare3.patch.AW_CityLeaderPatch.
                CheckFindLeader_Prefix(city);
            if (city.hasLeader()) return;
            if (pAttempt + 1 < MaximumAttempts)
                DeferredRuntimeWorkService.EnqueueCoalesced(
                    "city-leader-vacancy:" + pKingdomId + ":" + pCityId,
                    DeferredWorkClass.CriticalRuntime,
                    () => Repair(pKingdomId, pCityId, pAttempt + 1));
        }

        private static Kingdom FindKingdom(long pId)
        {
            try { return World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }

        private static City FindCity(long pId)
        {
            try { return World.world?.cities?.get(pId); }
            catch { return null; }
        }
    }
}
