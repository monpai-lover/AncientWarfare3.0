using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using Newtonsoft.Json;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditTerritoryService
    {
        private const string MISSING_VALUE = "\u0001";

        internal static bool CaptureCurrentCities(Kingdom pKingdom)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying ||
                pKingdom?.data == null || pKingdom.isRekt()) return false;

            var ids = new List<long>();
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    long id = city.getID();
                    if (id > 0 && !ids.Contains(id)) ids.Add(id);
                }
            }
            catch
            {
                return false;
            }

            if (ids.Count == 0) return false;
            ids.Sort();
            pKingdom.data.set(
                LineageKeys.MANDATE_REBEL_BANDIT_ENTRY_CITY_IDS,
                JsonConvert.SerializeObject(ids));
            return true;
        }

        internal static bool EnsureLegacyWhitelist(Kingdom pKingdom)
        {
            if (HasValidWhitelist(pKingdom)) return true;
            return IsWhitelistMissing(pKingdom) &&
                   CaptureCurrentCities(pKingdom);
        }

        internal static bool HasValidWhitelist(Kingdom pKingdom)
        {
            return TryReadIds(pKingdom, out HashSet<long> ids) &&
                   ids.Count > 0;
        }

        internal static bool IsWhitelistMissing(Kingdom pKingdom)
        {
            return ReadRaw(pKingdom) == MISSING_VALUE;
        }

        internal static bool CanAcquire(Kingdom pKingdom, City pCity,
            bool pBandit)
        {
            bool alreadyOwned = pCity?.kingdom == pKingdom;
            long cityId = -1L;
            try { cityId = pCity?.getID() ?? -1L; }
            catch { }
            return PeasantRebelRouteRules.CanAcquireWhitelistedCity(
                pBandit, alreadyOwned, ReadIds(pKingdom).Contains(cityId));
        }

        private static HashSet<long> ReadIds(Kingdom pKingdom)
        {
            return TryReadIds(pKingdom, out HashSet<long> ids)
                ? ids
                : new HashSet<long>();
        }

        private static bool TryReadIds(Kingdom pKingdom,
            out HashSet<long> pIds)
        {
            pIds = new HashSet<long>();
            string raw = ReadRaw(pKingdom);
            if (raw == MISSING_VALUE || string.IsNullOrWhiteSpace(raw))
                return false;
            try
            {
                List<long> values =
                    JsonConvert.DeserializeObject<List<long>>(raw);
                if (values == null || values.Count == 0) return false;
                for (int i = 0; i < values.Count; i++)
                {
                    long id = values[i];
                    if (id <= 0) return false;
                    pIds.Add(id);
                }
                return pIds.Count > 0;
            }
            catch
            {
                pIds.Clear();
                return false;
            }
        }

        private static string ReadRaw(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return MISSING_VALUE;
            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_BANDIT_ENTRY_CITY_IDS,
                out string value, MISSING_VALUE);
            return value ?? "";
        }
    }
}
