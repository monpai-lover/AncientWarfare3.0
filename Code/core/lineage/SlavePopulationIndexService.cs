using System;

namespace AncientWarfare3.core.lineage
{
    internal static class SlavePopulationIndexService
    {
        public static int Count(City pCity)
        {
            if (pCity?.data == null) return 0;
            pCity.data.get(LineageKeys.SLAVE_POPULATION_COUNT, out int count, 0);
            return Math.Max(0, count);
        }

        public static bool HasAny(City pCity)
        {
            return Count(pCity) > 0;
        }

        public static void Activate(Actor pActor, City pCity)
        {
            if (pActor?.data == null) return;
            long nextCityId = pCity?.data != null ? pCity.id : -1L;
            pActor.data.get(LineageKeys.SLAVE_COUNTED_CITY_ID, out long currentCityId, -1L);
            if (currentCityId == nextCityId) return;
            if (currentCityId >= 0) Adjust(ResolveCity(currentCityId), -1);
            if (nextCityId >= 0) Adjust(pCity, 1);
            pActor.data.set(LineageKeys.SLAVE_COUNTED_CITY_ID, nextCityId);
        }

        public static void Deactivate(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.SLAVE_COUNTED_CITY_ID, out long cityId, -1L);
            if (cityId < 0) return;
            Adjust(ResolveCity(cityId), -1);
            pActor.data.set(LineageKeys.SLAVE_COUNTED_CITY_ID, -1L);
        }

        public static void OnActorCityChanged(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.SLAVE_COUNTED_CITY_ID, out long cityId, -1L);
            bool slave = SlaveService.IsSlave(pActor);
            if (cityId < 0 && !slave) return;
            if (!slave)
            {
                Deactivate(pActor);
                return;
            }
            Activate(pActor, pActor.city);
        }

        private static void Adjust(City pCity, int pDelta)
        {
            if (pCity?.data == null || pDelta == 0) return;
            int current = Count(pCity);
            int next = Math.Max(0, current + pDelta);
            if (next != current) pCity.data.set(LineageKeys.SLAVE_POPULATION_COUNT, next);
        }

        private static City ResolveCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
}
