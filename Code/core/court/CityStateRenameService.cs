using System;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal enum CityStateRenameResult
    {
        Success = 0,
        CityNotFound = 1,
        Unauthorized = 2,
        EmptyCityName = 3,
        EmptyStateName = 4,
        InvalidRegion = 5,
        NoChange = 6,
        CommitFailed = 7
    }

    internal static class CityStateRenameService
    {
        [ThreadStatic]
        private static int _nativeSeatSyncSuppressionDepth;

        internal static event Action<long, long> Changed;

        internal static bool IsNativeSeatSyncSuppressed =>
            _nativeSeatSyncSuppressionDepth > 0;

        internal static CityStateRenameResult TryApply(long pKingdomId,
            long pCityId, string pCityName, string pStateName)
        {
            Kingdom kingdom;
            City city;
            try
            {
                kingdom = World.world?.kingdoms?.get(pKingdomId);
                city = World.world?.cities?.get(pCityId);
            }
            catch
            {
                return CityStateRenameResult.CityNotFound;
            }
            if (kingdom?.data == null || kingdom.isRekt() ||
                city?.data == null || city.isRekt())
                return CityStateRenameResult.CityNotFound;
            if (city.kingdom != kingdom)
                return CityStateRenameResult.Unauthorized;

            string cityName = CityStateRenameRules.Normalize(pCityName);
            string stateName = CityStateRenameRules.Normalize(pStateName);
            if (cityName.Length == 0)
                return CityStateRenameResult.EmptyCityName;

            bool hasRegion = DeJureRegionStore.TryGetForCity(pCityId,
                out DeJureRegion region);
            if (hasRegion && stateName.Length == 0)
                return CityStateRenameResult.EmptyStateName;
            if (!hasRegion && stateName.Length > 0)
                return CityStateRenameResult.InvalidRegion;

            string oldCityName = city.data.name ?? string.Empty;
            string oldStateName = region?.RegionName ?? string.Empty;
            bool cityChanged = !string.Equals(oldCityName, cityName,
                StringComparison.Ordinal);
            bool stateChanged = hasRegion && !string.Equals(oldStateName,
                stateName, StringComparison.Ordinal);
            if (!cityChanged && !stateChanged)
                return CityStateRenameResult.NoChange;

            try
            {
                if (cityChanged)
                {
                    using (SuppressNativeSeatSync())
                        city.setName(cityName, pTrack: true);
                    if (!string.Equals(city.data.name, cityName,
                            StringComparison.Ordinal))
                        return CityStateRenameResult.CommitFailed;
                }

                if (stateChanged && !DeJureRegionStore.TryRenameRegion(
                        region.RegionId, pCityId, stateName, out _))
                {
                    RollBackCityName(city, oldCityName, cityChanged);
                    return CityStateRenameResult.CommitFailed;
                }
            }
            catch (Exception error)
            {
                RollBackCityName(city, oldCityName, cityChanged);
                ModClass.LogWarning("City/state rename failed: " +
                    error.Message);
                return CityStateRenameResult.CommitFailed;
            }

            HierarchicalVassalMapModeService.MarkCityDirty(city);
            if (hasRegion)
                DeJureRegionMaintenanceService.MarkRegionDirty(
                    region.RegionId, DeJureDirtyReason.Name);
            try { Changed?.Invoke(pKingdomId, pCityId); }
            catch { }
            return CityStateRenameResult.Success;
        }

        internal static IDisposable SuppressNativeSeatSync()
        {
            _nativeSeatSyncSuppressionDepth++;
            return new NativeSeatSyncScope();
        }

        private static void RollBackCityName(City pCity, string pOldName,
            bool pChanged)
        {
            if (!pChanged || pCity?.data == null || pCity.isRekt()) return;
            try
            {
                using (SuppressNativeSeatSync())
                    pCity.setName(pOldName ?? string.Empty, pTrack: false);
            }
            catch { }
        }

        private sealed class NativeSeatSyncScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_nativeSeatSyncSuppressionDepth > 0)
                    _nativeSeatSyncSuppressionDepth--;
            }
        }
    }
}
