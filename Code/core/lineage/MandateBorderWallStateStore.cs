using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateBorderWallStateStore
    {
        internal static MandateBorderWallState Read(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return new MandateBorderWallState();
            try
            {
                pKingdom.data.get(LineageKeys.MANDATE_BORDER_WALL_STATE,
                    out string json, "");
                if (string.IsNullOrWhiteSpace(json))
                    return new MandateBorderWallState();
                MandateBorderWallState state =
                    JsonConvert.DeserializeObject<MandateBorderWallState>(
                        json);
                if (state == null || state.SchemaVersion <= 0 ||
                    state.SchemaVersion >
                    MandateBorderWallState.CurrentSchemaVersion)
                    return new MandateBorderWallState();
                Normalize(state);
                return state;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate border wall state read failed: " +
                                    e.Message);
                return new MandateBorderWallState();
            }
        }

        internal static bool Write(Kingdom pKingdom,
            MandateBorderWallState pState)
        {
            if (pKingdom?.data == null || pState == null) return false;
            try
            {
                pState.SchemaVersion =
                    MandateBorderWallState.CurrentSchemaVersion;
                Normalize(pState);
                pKingdom.data.set(LineageKeys.MANDATE_BORDER_WALL_STATE,
                    JsonConvert.SerializeObject(pState));
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Mandate border wall state write failed: " + e.Message);
                return false;
            }
        }

        private static void Normalize(MandateBorderWallState pState)
        {
            pState.Cities ??=
                new Dictionary<long, MandateBorderCityWallManifest>();
            foreach (long id in new List<long>(pState.Cities.Keys))
            {
                MandateBorderCityWallManifest manifest = pState.Cities[id];
                if (manifest == null)
                {
                    pState.Cities.Remove(id);
                    continue;
                }
                manifest.CityId = id;
                manifest.WallTypeId ??= "";
                if (manifest.BuiltYear == int.MinValue)
                    manifest.BuiltYear = Date.getCurrentYear() -
                        MandateBorderWallRefreshRules.WallLifespanYears;
                manifest.Points ??= new List<MandateBorderWallPointState>();
                foreach (MandateBorderWallPointState point in manifest.Points)
                    if (point != null) point.OriginalTopTypeId ??= "";
            }
        }
    }
}
