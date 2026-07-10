using System;
using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class AWArmyService
    {
        private static readonly MethodInfo NewArmyObjectMethod = ResolveNewArmyObjectMethod();
        private static readonly FieldInfo ArmyCityField = typeof(Army).GetField("_city",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ArmyKingdomField = typeof(Army).GetField("_kingdom",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<string, long> RoleArmyCache = new Dictionary<string, long>();
        private static bool _creatingSpecialArmy;

        public static bool IsCreatingSpecialArmy => _creatingSpecialArmy;

        public static string GetRole(Army pArmy)
        {
            if (pArmy?.data == null) return "";
            pArmy.data.get(LineageKeys.AW_ARMY_ROLE, out string role, "");
            return role ?? "";
        }

        public static bool IsRoleArmy(Army pArmy, string pRole)
        {
            return pArmy?.data != null && GetRole(pArmy) == pRole;
        }

        public static bool IsSpecialArmy(Army pArmy)
        {
            return AWArmyRoleRules.IsSpecialRole(GetRole(pArmy));
        }

        public static long GetAnchorCityId(Army pArmy)
        {
            if (pArmy?.data == null) return -1L;
            pArmy.data.get(LineageKeys.AW_ARMY_CITY_ID, out long cityId, -1L);
            if (cityId >= 0) return cityId;
            City city = SafeGetCity(pArmy);
            return city?.id ?? -1L;
        }

        public static City FindAnchorCity(Army pArmy)
        {
            long cityId = GetAnchorCityId(pArmy);
            if (cityId >= 0 && World.world?.cities != null)
            {
                try
                {
                    City city = World.world.cities.get(cityId);
                    if (city?.data != null && !city.isRekt()) return city;
                }
                catch { }
            }

            return SafeGetCity(pArmy);
        }

        public static Army EnsureArmy(Kingdom pKingdom, City pAnchorCity, Actor pCaptain, string pRole,
            string pName, bool pDetached)
        {
            if (pKingdom?.data == null || pCaptain?.data == null || !AWArmyRoleRules.IsSpecialRole(pRole))
                return null;

            City anchor = pAnchorCity ?? pCaptain.city ?? pKingdom.capital;
            bool detached = pDetached && AWArmyRoleRules.ShouldUseDetachedArmy(pRole);
            Army army = FindArmy(pKingdom, pAnchorCity, pRole);
            bool created = false;
            if (army == null)
            {
                army = CreateArmy(pKingdom, anchor, pCaptain, detached);
                created = army != null;
            }
            if (army == null) return null;

            MarkArmy(army, pKingdom, anchor, pRole, pName);
            if (!pCaptain.isRekt())
            {
                AddToArmy(pCaptain, army);
                SetCaptainIfChanged(army, pCaptain);
            }
            if (SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
                    pCreated: created, pReanchored: false, pPostLoadRepair: false))
                CleanupDuplicateArmies(pKingdom, anchor, pRole, army);
            return army;
        }

        public static Army FindArmy(Kingdom pKingdom, City pAnchorCity, string pRole)
        {
            if (pKingdom?.data == null || World.world?.armies == null) return null;
            long cityId = pAnchorCity?.id ?? -1L;
            string cacheKey = SpecialArmyLookupCacheRules.BuildKey(pKingdom.id, pRole, cityId);
            if (RoleArmyCache.TryGetValue(cacheKey, out long cachedArmyId))
            {
                Army cachedArmy = GetArmyById(cachedArmyId);
                if (IsValidLookupResult(cachedArmy, pKingdom, cityId, pRole))
                {
                    Bench.bench(CityMaintenanceBenchmarkRules.SpecialArmyCacheHit,
                        CityMaintenanceBenchmarkRules.Group);
                    Bench.benchEnd(CityMaintenanceBenchmarkRules.SpecialArmyCacheHit,
                        CityMaintenanceBenchmarkRules.Group);
                    return cachedArmy;
                }
                Bench.bench(CityMaintenanceBenchmarkRules.SpecialArmyCacheMiss,
                    CityMaintenanceBenchmarkRules.Group);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.SpecialArmyCacheMiss,
                    CityMaintenanceBenchmarkRules.Group);
                RoleArmyCache.Remove(cacheKey);
            }

            Bench.bench(CityMaintenanceBenchmarkRules.SpecialArmyGlobalScan,
                CityMaintenanceBenchmarkRules.Group);
            foreach (Army army in World.world.armies)
            {
                if (!IsValidLookupResult(army, pKingdom, cityId, pRole)) continue;
                RoleArmyCache[cacheKey] = army.id;
                Bench.benchEnd(CityMaintenanceBenchmarkRules.SpecialArmyGlobalScan,
                    CityMaintenanceBenchmarkRules.Group);
                return army;
            }
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SpecialArmyGlobalScan,
                CityMaintenanceBenchmarkRules.Group);
            return null;
        }

        public static void MarkArmy(Army pArmy, Kingdom pKingdom, City pAnchorCity, string pRole, string pName)
        {
            if (pArmy?.data == null || pKingdom?.data == null || !AWArmyRoleRules.IsSpecialRole(pRole)) return;
            pArmy.data.set(LineageKeys.AW_ARMY_ROLE, pRole);
            pArmy.data.set(LineageKeys.AW_ARMY_CITY_ID, pAnchorCity?.id ?? -1L);
            WorldTile tile = pAnchorCity?.getTile();
            pArmy.data.set(LineageKeys.AW_ARMY_ANCHOR_X, tile?.x ?? -1);
            pArmy.data.set(LineageKeys.AW_ARMY_ANCHOR_Y, tile?.y ?? -1);
            pArmy.data.custom_name = true;
            if (!string.IsNullOrEmpty(pName) && pArmy.data.name != pName)
                pArmy.setName(pName);
            TrySetRuntimeKingdom(pArmy, pKingdom);
            if (AWArmyRoleRules.ShouldUseDetachedArmy(pRole) && pArmy.hasCity())
                pArmy.clearCity();
            else if (!AWArmyRoleRules.ShouldUseDetachedArmy(pRole))
                TrySetRuntimeCity(pArmy, pAnchorCity, pKingdom);
            CacheArmy(pArmy, pKingdom, pRole);
            DedupePastCaptains(pArmy);
        }

        public static void AddToArmy(Actor pActor, Army pArmy)
        {
            if (pActor?.data == null || pArmy?.data == null) return;
            if (pActor.army == pArmy)
            {
                try
                {
                    if (!pArmy.units.Contains(pActor))
                        pArmy.listUnit(pActor);
                }
                catch { }
                return;
            }
            Army oldArmy = pActor.army;
            if (pActor.hasArmy())
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
                try { oldArmy?.units?.Remove(pActor); }
                catch { }
            }
            pActor.setArmy(pArmy);
            try
            {
                if (!pArmy.units.Contains(pActor))
                    pArmy.listUnit(pActor);
            }
            catch { }
        }

        public static void TryRemoveEmptyArmy(Army pArmy)
        {
            if (!IsSpecialArmy(pArmy)) return;
            if (pArmy.countUnits() > 0 || pArmy.hasCaptain()) return;
            RemoveArmyFromCache(pArmy);
            try { World.world?.armies?.removeObject(pArmy); }
            catch { }
        }

        public static void SetCaptainIfChanged(Army pArmy, Actor pCaptain)
        {
            if (pArmy?.data == null || pCaptain?.data == null || pCaptain.isRekt()) return;
            long currentId = -1L;
            try { currentId = pArmy.getCaptain()?.data?.id ?? -1L; }
            catch { }

            if (!AWArmyRoleRules.ShouldSetCaptain(currentId, pCaptain.data.id))
            {
                pArmy.data.id_captain = pCaptain.data.id;
                DedupePastCaptains(pArmy);
                return;
            }

            pArmy.setCaptain(pCaptain);
            DedupePastCaptains(pArmy);
        }

        public static void RepairSpecialArmiesAfterLoad()
        {
            if (World.world?.armies == null) return;
            RoleArmyCache.Clear();
            var snapshot = new List<Army>();
            foreach (Army army in World.world.armies)
                snapshot.Add(army);

            foreach (Army army in snapshot)
            {
                if (!IsSpecialArmy(army)) continue;
                string role = GetRole(army);
                City anchor = FindAnchorCity(army);
                Kingdom kingdom = SafeGetKingdom(army, anchor);
                if (anchor?.data != null && !AWArmyRoleRules.ShouldUseDetachedArmy(role))
                    TrySetRuntimeCity(army, anchor, kingdom);
                else if (AWArmyRoleRules.ShouldUseDetachedArmy(role))
                {
                    try
                    {
                        if (army.hasCity()) army.clearCity();
                    }
                    catch { }
                }
                CacheArmy(army, kingdom, role);
                DedupePastCaptains(army);
            }

            var repairedKeys = new HashSet<string>();
            foreach (Army army in snapshot)
            {
                if (!IsSpecialArmy(army)) continue;
                string role = GetRole(army);
                City anchor = FindAnchorCity(army);
                Kingdom kingdom = SafeGetKingdom(army, anchor);
                if (kingdom?.data == null) continue;
                string key = SpecialArmyLookupCacheRules.BuildKey(
                    kingdom.id, role, anchor?.id ?? -1L);
                if (!repairedKeys.Add(key)) continue;
                if (SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
                        pCreated: false, pReanchored: false, pPostLoadRepair: true))
                    CleanupDuplicateArmies(kingdom, anchor, role, army);
                CacheArmy(army, kingdom, role);
            }
        }

        public static void ReanchorArmy(Army pArmy, Kingdom pKingdom, City pAnchorCity, string pRole, string pName)
        {
            MarkArmy(pArmy, pKingdom, pAnchorCity, pRole, pName);
            if (SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
                    pCreated: false, pReanchored: true, pPostLoadRepair: false))
                CleanupDuplicateArmies(pKingdom, pAnchorCity, pRole, pArmy);
        }

        public static List<Army> GetRoleArmies(Kingdom pKingdom, string pRole)
        {
            var result = new List<Army>();
            if (pKingdom?.data == null || World.world?.armies == null) return result;

            foreach (Army army in World.world.armies)
            {
                if (army?.data == null || !army.isAlive()) continue;
                if (!IsRoleArmy(army, pRole)) continue;
                Kingdom kingdom = SafeGetKingdom(army, FindAnchorCity(army));
                if (kingdom != pKingdom) continue;
                result.Add(army);
            }
            return result;
        }

        public static void DedupePastCaptains(Army pArmy)
        {
            if (pArmy?.data?.past_captains == null || pArmy.data.past_captains.Count < 2) return;
            List<LeaderEntry> list = pArmy.data.past_captains;
            for (int i = list.Count - 1; i > 0; i--)
            {
                LeaderEntry current = list[i];
                LeaderEntry previous = list[i - 1];
                if (current == null || previous == null) continue;
                if (current.id != previous.id) continue;
                if (previous.timestamp_end < previous.timestamp_ago)
                    previous.timestamp_end = current.timestamp_end;
                list.RemoveAt(i);
            }
        }

        private static Army CreateArmy(Kingdom pKingdom, City pCity, Actor pCaptain, bool pDetached)
        {
            if (NewArmyObjectMethod == null || World.world?.armies == null) return null;
            if (pCity?.data == null || pCaptain?.data == null) return null;

            try
            {
                var army = NewArmyObjectMethod.Invoke(World.world.armies, null) as Army;
                if (army == null) return null;

                _creatingSpecialArmy = true;
                try { army.createArmy(pCaptain, pCity); }
                finally { _creatingSpecialArmy = false; }

                if (pDetached)
                    army.clearCity();
                return army;
            }
            catch (Exception e)
            {
                _creatingSpecialArmy = false;
                ModClass.LogWarning("Create AW3 special army failed: " + e.Message);
                return null;
            }
        }

        private static MethodInfo ResolveNewArmyObjectMethod()
        {
            Type type = typeof(ArmyManager);
            while (type != null)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (method.Name != "newObject") continue;
                    if (method.GetParameters().Length == 0) return method;
                }
                type = type.BaseType;
            }
            return null;
        }

        private static void CleanupDuplicateArmies(Kingdom pKingdom, City pAnchorCity, string pRole, Army pKeeper)
        {
            if (World.world?.armies == null || pKingdom?.data == null || pKeeper?.data == null) return;

            var duplicates = new List<Army>();
            long anchorId = pAnchorCity?.id ?? -1L;
            foreach (Army army in World.world.armies)
            {
                if (army == pKeeper || army?.data == null || !army.isAlive()) continue;
                if (!IsRoleArmy(army, pRole)) continue;
                Kingdom kingdom = SafeGetKingdom(army, FindAnchorCity(army));
                if (kingdom != pKingdom) continue;
                if (AWArmyRoleRules.ShouldCleanupDuplicateArmy(pRole, anchorId, GetAnchorCityId(army)))
                    duplicates.Add(army);
            }

            foreach (Army duplicate in duplicates)
                MergeDuplicateIntoKeeper(duplicate, pKeeper);
        }

        private static void MergeDuplicateIntoKeeper(Army pDuplicate, Army pKeeper)
        {
            if (pDuplicate?.data == null || pKeeper?.data == null) return;

            var units = new List<Actor>();
            try
            {
                foreach (Actor unit in pDuplicate.getUnits())
                    if (unit?.data != null && !unit.isRekt())
                        units.Add(unit);
            }
            catch { }

            foreach (Actor unit in units)
                AddToArmy(unit, pKeeper);

            try { pDuplicate.setCaptain(null); }
            catch { }
            try
            {
                pDuplicate.data.removeString(LineageKeys.AW_ARMY_ROLE);
                pDuplicate.data.removeLong(LineageKeys.AW_ARMY_CITY_ID);
            }
            catch
            {
                pDuplicate.data.set(LineageKeys.AW_ARMY_ROLE, "");
                pDuplicate.data.set(LineageKeys.AW_ARMY_CITY_ID, -1L);
            }

            RemoveArmyFromCache(pDuplicate);
            try { World.world?.armies?.removeObject(pDuplicate); }
            catch { }
        }

        private static Army GetArmyById(long pArmyId)
        {
            if (pArmyId < 0 || World.world?.armies == null) return null;
            try { return World.world.armies.get(pArmyId); }
            catch { return null; }
        }

        private static bool IsValidLookupResult(Army pArmy, Kingdom pKingdom, long pRequestedCityId, string pRole)
        {
            if (pArmy?.data == null || !pArmy.isAlive()) return false;
            if (!IsRoleArmy(pArmy, pRole)) return false;
            City anchor = FindAnchorCity(pArmy);
            Kingdom kingdom = SafeGetKingdom(pArmy, anchor);
            if (!SpecialArmyLookupCacheRules.ShouldUseCachedArmy(
                    pCachedArmyId: pArmy.id,
                    pCachedArmyAlive: true,
                    pRoleMatches: true,
                    pKingdomMatches: kingdom == pKingdom,
                    pAnchorMatches: AWArmyRoleRules.ShouldMatchArmyAnchor(
                        pRole, pRequestedCityId, GetAnchorCityId(pArmy))))
                return false;
            return true;
        }

        private static void CacheArmy(Army pArmy, Kingdom pKingdom, string pRole)
        {
            if (pArmy?.data == null || pKingdom?.data == null || !AWArmyRoleRules.IsSpecialRole(pRole)) return;
            string key = SpecialArmyLookupCacheRules.BuildKey(pKingdom.id, pRole, GetAnchorCityId(pArmy));
            RoleArmyCache[key] = pArmy.id;
        }

        private static void RemoveArmyFromCache(Army pArmy)
        {
            if (pArmy?.data == null || RoleArmyCache.Count == 0) return;
            var staleKeys = new List<string>();
            foreach (KeyValuePair<string, long> entry in RoleArmyCache)
                if (entry.Value == pArmy.id)
                    staleKeys.Add(entry.Key);
            foreach (string key in staleKeys)
                RoleArmyCache.Remove(key);
        }

        private static City SafeGetCity(Army pArmy)
        {
            try
            {
                City city = pArmy?.getCity();
                return city?.data != null && !city.isRekt() ? city : null;
            }
            catch { return null; }
        }

        private static Kingdom SafeGetKingdom(Army pArmy, City pAnchorCity)
        {
            try
            {
                Kingdom kingdom = pArmy?.getKingdom();
                if (kingdom?.data != null) return kingdom;
            }
            catch { }
            return pAnchorCity?.kingdom;
        }

        private static void TrySetRuntimeCity(Army pArmy, City pCity, Kingdom pKingdom)
        {
            if (pArmy?.data == null || pCity?.data == null) return;
            try { ArmyCityField?.SetValue(pArmy, pCity); }
            catch { }
            TrySetRuntimeKingdom(pArmy, pKingdom ?? pCity.kingdom);
            pArmy.data.id_city = -1L;
            if ((pKingdom ?? pCity.kingdom)?.data != null)
                pArmy.data.id_kingdom = (pKingdom ?? pCity.kingdom).id;
        }

        private static void TrySetRuntimeKingdom(Army pArmy, Kingdom pKingdom)
        {
            if (pArmy?.data == null || pKingdom?.data == null) return;
            try { ArmyKingdomField?.SetValue(pArmy, pKingdom); }
            catch { }
            pArmy.data.id_kingdom = pKingdom.id;
        }
    }
}
