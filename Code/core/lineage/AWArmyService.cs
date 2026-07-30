using System;
using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

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
        private static readonly Dictionary<long, HashSet<string>> LookupCacheKeysByArmy =
            new Dictionary<long, HashSet<string>>();
        private static readonly Dictionary<string, HashSet<long>> RoleArmyIdsByKingdomRole =
            new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private static readonly Dictionary<long, string> RoleIndexKeyByArmy =
            new Dictionary<long, string>();
        private static readonly HashSet<Army> SpecialArmiesBeingCreated =
            new HashSet<Army>();

        internal static bool IsSpecialArmyCreationInProgress(Army pArmy)
        {
            return pArmy != null && SpecialArmiesBeingCreated.Contains(pArmy);
        }

        public static void ClearRuntimeCaches()
        {
            RoleArmyCache.Clear();
            LookupCacheKeysByArmy.Clear();
            RoleArmyIdsByKingdomRole.Clear();
            RoleIndexKeyByArmy.Clear();
            SpecialArmiesBeingCreated.Clear();
        }

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

        public static void EnsureOrdinaryNativeName(Army pArmy,
            Kingdom pKingdom = null, City pAnchorCity = null)
        {
            if (pArmy?.data == null ||
                !ArmyNativeNameService.IsOrdinaryArmy(pArmy)) return;
            if (pAnchorCity?.data != null &&
                GetAnchorCityId(pArmy) < 0L)
                SetOrdinaryArmyAnchor(pArmy, pAnchorCity);
            if (!ArmyNativeNameService.TryResolve(pArmy, pKingdom,
                    pAnchorCity, out string name) ||
                pArmy.data.name == name) return;
            pArmy.data.custom_name = true;
            try { pArmy.setName(name); }
            catch { }
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
            if (!HistoricalMasterVocationService.CanEnterArmyRole(pCaptain, pRole))
                return null;

            City anchor = pAnchorCity ?? pCaptain.city ?? pKingdom.capital;
            bool detached = pDetached && AWArmyRoleRules.ShouldUseDetachedArmy(pRole);
            Army army = null;
            if (AWArmyRoleRules.MaxArmiesPerKingdom(pRole) == 1)
                TryGetRoleArmy(pKingdom, pRole, out army);
            else
                army = FindArmy(pKingdom, pAnchorCity, pRole);
            if (!CanUseCaptainForArmy(pCaptain, army)) return null;
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

        private static bool CanUseCaptainForArmy(Actor pCaptain,
            Army pRequestedArmy)
        {
            if (pCaptain?.data == null) return false;
            if (IsCivilAuthority(pCaptain)) return false;
            Army currentArmy = pCaptain.army;
            if (currentArmy == null) return true;

            bool currentArmyLive = false;
            bool actorAlive = false;
            bool actorIsCurrentCaptain = false;
            try
            {
                currentArmyLive = currentArmy.data != null &&
                                  currentArmy.isAlive();
                actorAlive = pCaptain.isAlive() && !pCaptain.isRekt();
                actorIsCurrentCaptain = ReferenceEquals(
                    currentArmy.getCaptain(), pCaptain);
            }
            catch { }

            return ArmyCaptainContinuityRules.CanTransferCaptainLease(
                ArmyRtsRuntimeMode.Current,
                AW3MultiplayerReplicaScope.IsReplicaSession ||
                AW3MultiplayerReplicaScope.IsApplying ||
                ArmyCaptainDisposalScope.IsActive(currentArmy),
                currentArmyLive, actorIsCurrentCaptain, actorAlive,
                ReferenceEquals(currentArmy, pRequestedArmy));
        }

        public static Army FindArmy(Kingdom pKingdom, City pAnchorCity, string pRole)
        {
            if (pKingdom?.data == null || !AWArmyRoleRules.IsSpecialRole(pRole)) return null;
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
                RemoveLookupCacheKey(cacheKey, cachedArmyId);
            }

            return TryFindIndexedRoleArmy(pKingdom, cityId, pRole, cacheKey);
        }

        private static Army TryFindIndexedRoleArmy(Kingdom pKingdom, long pCityId,
            string pRole, string pCacheKey)
        {
            string indexKey = BuildRoleIndexKey(pKingdom.id, pRole);
            if (!RoleArmyIdsByKingdomRole.TryGetValue(indexKey, out HashSet<long> armyIds)) return null;
            long staleId = -1L;
            foreach (long armyId in armyIds)
            {
                Army army = GetArmyById(armyId);
                if (!IsValidLookupResult(army, pKingdom, pCityId, pRole))
                {
                    staleId = armyId;
                    continue;
                }
                SetLookupCache(pCacheKey, army.id);
                return army;
            }
            if (staleId >= 0)
            {
                armyIds.Remove(staleId);
                RoleIndexKeyByArmy.Remove(staleId);
                if (armyIds.Count == 0) RoleArmyIdsByKingdomRole.Remove(indexKey);
            }
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
                DetachArmyFromCity(pArmy);
            else if (!AWArmyRoleRules.ShouldUseDetachedArmy(pRole))
                TrySetRuntimeCity(pArmy, pAnchorCity, pKingdom);
            CacheArmy(pArmy, pKingdom, pRole);
            DedupePastCaptains(pArmy);
        }

        public static void AddToArmy(Actor pActor, Army pArmy)
        {
            if (pActor?.data == null || pArmy?.data == null) return;
            if (!RoyalAsylumRules.CanPerformProtectedRole(
                    RoyalAsylumService.IsActive(pActor))) return;
            if (!HistoricalMasterVocationService.CanJoinArmy(pActor, pArmy)) return;
            if (pActor.army == pArmy)
            {
                bool armyListChanged = false;
                try
                {
                    if (!pArmy.units.Contains(pActor))
                    {
                        pArmy.listUnit(pActor);
                        armyListChanged = true;
                    }
                }
                catch { }
                if (TemporaryLevyRules.ShouldNotifyRtsRosterChanged(
                        actorArmyChanged: false, armyListChanged))
                    ArmyRtsControllerService.OnArmyRosterChanged(pArmy);
                EnsureOrdinaryNativeName(pArmy, pActor.kingdom, pActor.city);
                return;
            }
            Army oldArmy = pActor.army;
            if (pActor.hasArmy())
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
                if (ReferenceEquals(pActor.army, oldArmy)) return;
                try { oldArmy?.units?.Remove(pActor); }
                catch { }
            }
            pActor.setArmy(pArmy);
            if (pActor.army != pArmy) return;
            bool currentArmyListChanged = false;
            try
            {
                if (!pArmy.units.Contains(pActor))
                {
                    pArmy.listUnit(pActor);
                    currentArmyListChanged = true;
                }
            }
            catch { }
            if (TemporaryLevyRules.ShouldNotifyRtsRosterChanged(
                    actorArmyChanged: true, currentArmyListChanged))
                ArmyRtsControllerService.OnArmyRosterChanged(pArmy);
            EnsureOrdinaryNativeName(pArmy, pActor.kingdom, pActor.city);
        }

        public static bool TryRemoveEmptyArmy(Army pArmy,
            City pCityHint = null, Kingdom pKingdomHint = null)
        {
            Bench.bench(CityMaintenanceBenchmarkRules.EmptyArmyDetection,
                CityMaintenanceBenchmarkRules.Group);
            bool shouldRemove;
            try
            {
                bool alive = false;
                int listedUnitCount = 0;
                bool hasLinkedLiveUnit = false;
                try { alive = pArmy != null && pArmy.isAlive(); }
                catch { }
                try { listedUnitCount = pArmy?.countUnits() ?? 0; }
                catch { }
                if (listedUnitCount <= 1)
                    hasLinkedLiveUnit = HasLinkedLiveUnit(pArmy);
                shouldRemove = ArmyLifecycleRules.ShouldRemoveEmptyArmy(
                    pArmy?.data != null, alive, listedUnitCount,
                    hasLinkedLiveUnit, IsSpecialArmyCreationInProgress(pArmy));
            }
            finally
            {
                Bench.benchEnd(
                    CityMaintenanceBenchmarkRules.EmptyArmyDetection,
                    CityMaintenanceBenchmarkRules.Group);
            }
            if (!shouldRemove) return false;

            Bench.bench(CityMaintenanceBenchmarkRules.EmptyArmyRemoval,
                CityMaintenanceBenchmarkRules.Group);
            try
            {
                bool nonReplacingShell = IsNonReplacingShell(pArmy);
                return RemoveArmyObject(pArmy, pClearCityReference: true,
                    pCityHint, pKingdomHint,
                    pRequestReplacement: !nonReplacingShell);
            }
            finally
            {
                Bench.benchEnd(CityMaintenanceBenchmarkRules.EmptyArmyRemoval,
                    CityMaintenanceBenchmarkRules.Group);
            }
        }

        public static void RemoveSpecialArmy(Army pArmy)
        {
            if (!IsSpecialArmy(pArmy)) return;
            RemoveArmyObject(pArmy, pClearCityReference: true);
        }

        internal static bool IsNonReplacingShell(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            pArmy.data.get(LineageKeys.AW_ARMY_NON_REPLACING_SHELL,
                out bool marked, false);
            return marked;
        }

        internal static bool RemoveArmyObject(Army pArmy,
            bool pClearCityReference, City pCityHint = null,
            Kingdom pKingdomHint = null,
            bool pRequestReplacement = true)
        {
            if (pArmy == null) return false;
            ArmyManager manager = World.world?.armies;
            if (manager == null) return false;

            bool alive = false;
            try { alive = pArmy.isAlive(); }
            catch { }
            if (!alive)
            {
                try { manager.checkLists(); } catch { }
                return false;
            }

            using (ArmyCaptainDisposalScope.Open(pArmy))
            {
            City city = SafeGetCity(pArmy) ?? pCityHint;
            Kingdom kingdom = SafeGetStoredKingdom(pArmy) ??
                              pKingdomHint ?? SafeGetKingdom(pArmy, city);
            bool wasSpecialArmy = IsSpecialArmy(pArmy);
            long armyId = pArmy.data?.id ?? -1L;
            var units = new List<Actor>();
            try
            {
                foreach (Actor unit in pArmy.getUnits())
                    if (unit?.data != null)
                        units.Add(unit);
            }
            catch { }
            Actor captain = null;
            try { captain = pArmy.getCaptain(); }
            catch { }

            for (int i = 0; i < units.Count; i++)
            {
                Actor unit = units[i];
                if (unit.army != pArmy) continue;
                try { unit.removeFromArmy(); }
                catch
                {
                    try { unit.setArmy(null); }
                    catch { }
                }
            }
            if (captain?.data != null && captain.army == pArmy)
            {
                try { captain.removeFromArmy(); }
                catch
                {
                    try { captain.setArmy(null); }
                    catch { }
                }
            }
            try { pArmy.setCaptain(null); } catch { }
            try { pArmy.units.Clear(); } catch { }

            if (pClearCityReference && city?.data != null)
            {
                try
                {
                    if (city.getArmy() == pArmy) city.setArmy(null);
                }
                catch { }
            }
            try { pArmy.clearCity(); } catch { }

            AWArmyMarchService.ClearArmy(pArmy);
            RemoveArmyFromCache(pArmy);
            WarNoticeService.OnArmyInvalidated(kingdom, armyId);
            KingdomMilitaryReadinessService.MarkCityDirty(city);
            try { manager.removeObject(pArmy); }
            catch (Exception error)
            {
                ModClass.LogWarning("Army cleanup failed: " + error.Message);
                return false;
            }

            bool hasReplacementArmy = false;
            try
            {
                Army replacement = city?.getArmy();
                hasReplacementArmy = replacement?.data != null &&
                                      replacement != pArmy &&
                                      replacement.isAlive() &&
                                      SafeGetKingdom(replacement, city) ==
                                      kingdom;
            }
            catch { }
            if (ArmyLifecycleRules.ShouldRequestOffensiveReinforcement(
                    pRequestReplacement, wasSpecialArmy,
                    MilitaryEmergencyService.HasAny(kingdom),
                    hasReplacementArmy))
                TemporaryLevyService.RequestOffensiveRecovery(kingdom, city);
            return true;
            }
        }

        private static bool HasLinkedLiveUnit(Army pArmy)
        {
            if (pArmy == null) return false;
            try
            {
                foreach (Actor unit in pArmy.getUnits())
                {
                    if (unit?.data == null || unit.army != pArmy ||
                        unit.isRekt() || !unit.isAlive()) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static void SetCaptainIfChanged(Army pArmy, Actor pCaptain)
        {
            if (pArmy?.data == null || pCaptain?.data == null || pCaptain.isRekt()) return;
            if (!IsCaptainLeaseEligible(pArmy, pCaptain,
                    requireMembership: false)) return;
            if (!HistoricalMasterVocationService.CanJoinArmy(pCaptain, pArmy) ||
                !HistoricalMasterVocationService.CanEnter(pCaptain,
                    HistoricalMasterMilitaryContext.ArmyCaptain)) return;
            long currentId = -1L;
            Actor current = null;
            try
            {
                current = pArmy.getCaptain();
                currentId = current?.data?.id ?? -1L;
            }
            catch { }

            bool liveArmy = false;
            bool currentExists = current?.data != null;
            bool currentAlive = false;
            bool currentIsMember = false;
            bool currentAuthority = false;
            try
            {
                liveArmy = pArmy.isAlive();
                currentAlive = currentExists &&
                               current.isAlive() && !current.isRekt();
                currentIsMember = currentExists &&
                                  ReferenceEquals(current.army, pArmy) &&
                                  pArmy.units.Contains(current);
                currentAuthority = IsCivilAuthority(current);
            }
            catch { }
            if (ArmyCaptainContinuityRules.ShouldRejectCaptainMutation(
                    ArmyRtsRuntimeMode.Current,
                    replicaApplying: false,
                    liveArmy,
                    currentExists,
                    currentAlive,
                    currentIsMember,
                    ReferenceEquals(current, pCaptain),
                    currentCaptainIsCivilAuthority: currentAuthority))
                return;

            if (!AWArmyRoleRules.ShouldSetCaptain(currentId, pCaptain.data.id))
            {
                pArmy.data.id_captain = pCaptain.data.id;
                DedupePastCaptains(pArmy);
                return;
            }

            pArmy.setCaptain(pCaptain);
            try
            {
                if (ReferenceEquals(pArmy.getCaptain(), pCaptain))
                    LineageService.OnActorPromoted(pCaptain,
                        NobleTrigger.ArmyCaptain);
            }
            catch { }
            DedupePastCaptains(pArmy);
        }

        internal static bool IsCaptainLeaseEligible(Army pArmy,
            Actor pActor, bool requireMembership)
        {
            try
            {
                if (pArmy?.data == null || pActor?.data == null ||
                    !pActor.isKingdomCiv() || !pActor.isAlive() ||
                    pActor.isRekt() || !pActor.is_profession_warrior ||
                    pActor.isKing() || pActor.isCityLeader() ||
                    !CaptainMatchesArmyKingdom(pArmy, pActor)) return false;
                if (!ArmyCaptainContinuityRules.
                        IsCareerStandingCaptainCandidate(
                            actorAlive: true,
                            currentProfessionIsWarrior: true,
                            pActor.hasArmy(),
                            TemporaryLevyService.IsTemporaryLevy(pActor),
                            WartimeGarrisonService.IsActive(pActor),
                            TemporarySlaveVanguardService.IsMember(pActor),
                            SlaveService.IsSlave(pActor))) return false;
                if (requireMembership &&
                    (pActor.army != pArmy || pArmy?.units == null ||
                     !pArmy.units.Contains(pActor))) return false;
                if (!IsRoleArmy(pArmy, AWArmyRole.RoyalGuard) &&
                    RoyalGuardService.IsRoyalGuard(pActor)) return false;
                return HistoricalMasterVocationService.CanJoinArmy(
                           pActor, pArmy) &&
                       HistoricalMasterVocationService.CanEnter(pActor,
                           HistoricalMasterMilitaryContext.ArmyCaptain);
            }
            catch { return false; }
        }

        internal static Kingdom GetIntendedKingdom(Army pArmy,
            City pAnchorHint = null)
        {
            Kingdom stored = SafeGetStoredKingdom(pArmy);
            if (stored?.data != null && !stored.isRekt()) return stored;
            City anchor = pAnchorHint ?? FindAnchorCity(pArmy);
            if (anchor?.kingdom?.data != null && !anchor.kingdom.isRekt())
                return anchor.kingdom;
            try
            {
                if (pArmy?.data != null && pArmy.data.id_kingdom >= 0)
                {
                    Kingdom saved = World.world?.kingdoms?.get(
                        pArmy.data.id_kingdom);
                    if (saved?.data != null && !saved.isRekt()) return saved;
                }
            }
            catch { }
            try
            {
                Kingdom runtime = pArmy?.getKingdom();
                return runtime?.data != null && !runtime.isRekt()
                    ? runtime
                    : null;
            }
            catch { return null; }
        }

        internal static bool CaptainMatchesArmyKingdom(Army pArmy,
            Actor pCaptain)
        {
            Kingdom intended = GetIntendedKingdom(pArmy);
            return intended?.data != null && pCaptain?.kingdom?.data != null &&
                   ReferenceEquals(intended, pCaptain.kingdom);
        }

        private static bool IsCivilAuthority(Actor pActor)
        {
            try
            {
                return pActor?.data != null &&
                       (pActor.isKing() || pActor.isCityLeader());
            }
            catch { return false; }
        }

        public static void RepairSpecialArmiesAfterLoad()
        {
            if (World.world?.armies == null) return;
            ClearRuntimeCaches();
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

            foreach (Army army in snapshot)
                EnsureOrdinaryNativeName(army);

            ArmyInvalidCleanupQueue.BeginPostLoadSweep(snapshot);
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
            if (pKingdom?.data == null || !AWArmyRoleRules.IsSpecialRole(pRole)) return result;
            string indexKey = BuildRoleIndexKey(pKingdom.id, pRole);
            if (!RoleArmyIdsByKingdomRole.TryGetValue(indexKey, out HashSet<long> armyIds)) return result;

            var stale = new List<long>();
            foreach (long armyId in armyIds)
            {
                Army army = GetArmyById(armyId);
                if (army?.data == null || !army.isAlive() || !IsRoleArmy(army, pRole))
                {
                    stale.Add(armyId);
                    continue;
                }
                Kingdom kingdom = SafeGetKingdom(army, FindAnchorCity(army));
                if (kingdom != pKingdom)
                {
                    stale.Add(armyId);
                    continue;
                }
                result.Add(army);
            }
            foreach (long armyId in stale)
            {
                armyIds.Remove(armyId);
                RoleIndexKeyByArmy.Remove(armyId);
            }
            if (armyIds.Count == 0) RoleArmyIdsByKingdomRole.Remove(indexKey);
            return result;
        }

        public static bool TryGetRoleArmy(Kingdom pKingdom, string pRole, out Army pArmy)
        {
            pArmy = null;
            if (pKingdom?.data == null || !AWArmyRoleRules.IsSpecialRole(pRole)) return false;
            string indexKey = BuildRoleIndexKey(pKingdom.id, pRole);
            if (!RoleArmyIdsByKingdomRole.TryGetValue(indexKey, out HashSet<long> armyIds)) return false;

            long staleId = -1L;
            foreach (long armyId in armyIds)
            {
                Army candidate = GetArmyById(armyId);
                if (candidate?.data == null || !candidate.isAlive() || !IsRoleArmy(candidate, pRole) ||
                    SafeGetKingdom(candidate, FindAnchorCity(candidate)) != pKingdom)
                {
                    staleId = armyId;
                    continue;
                }
                pArmy = candidate;
                return true;
            }

            if (staleId >= 0)
            {
                armyIds.Remove(staleId);
                RoleIndexKeyByArmy.Remove(staleId);
                if (armyIds.Count == 0) RoleArmyIdsByKingdomRole.Remove(indexKey);
            }
            return false;
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

        internal static Army CreateDetachedArmy(Kingdom pKingdom,
            City pAnchorCity, Actor pCaptain)
        {
            return CreateArmy(pKingdom, pAnchorCity, pCaptain,
                pDetached: true);
        }

        private static Army CreateArmy(Kingdom pKingdom, City pCity, Actor pCaptain, bool pDetached)
        {
            if (NewArmyObjectMethod == null || World.world?.armies == null) return null;
            if (pCity?.data == null || pCaptain?.data == null) return null;

            Army army = null;
            bool initialized = false;
            try
            {
                army = NewArmyObjectMethod.Invoke(World.world.armies, null) as Army;
                if (army == null) return null;

                SpecialArmiesBeingCreated.Add(army);
                try { army.createArmy(pCaptain, pCity); }
                finally { SpecialArmiesBeingCreated.Remove(army); }

                initialized = army.data != null;
                if (!initialized) return null;

                if (pDetached)
                    DetachArmyFromCity(army, pCity);
                return army;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Create AW3 special army failed: " + e.Message);
                return null;
            }
            finally
            {
                if (ArmyCreationSafetyRules.ShouldCleanupFailedCreation(
                        army != null, initialized))
                    ArmyInvalidCleanupQueue.RemoveFailedCreation(army,
                        pCaptain, pCity);
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

        private static void DetachArmyFromCity(Army pArmy,
            City pCityHint = null)
        {
            if (pArmy == null) return;
            City city = SafeGetCity(pArmy) ?? pCityHint;
            try
            {
                if (city?.getArmy() == pArmy)
                    city.setArmy(null);
            }
            catch { }
            try
            {
                if (pArmy.hasCity()) pArmy.clearCity();
            }
            catch { }
        }

        private static void SetOrdinaryArmyAnchor(Army pArmy,
            City pAnchorCity)
        {
            if (pArmy?.data == null || pAnchorCity?.data == null) return;
            pArmy.data.set(LineageKeys.AW_ARMY_CITY_ID, pAnchorCity.id);
            WorldTile tile = pAnchorCity.getTile();
            pArmy.data.set(LineageKeys.AW_ARMY_ANCHOR_X, tile?.x ?? -1);
            pArmy.data.set(LineageKeys.AW_ARMY_ANCHOR_Y, tile?.y ?? -1);
        }

        private static void CleanupDuplicateArmies(Kingdom pKingdom, City pAnchorCity, string pRole, Army pKeeper)
        {
            if (pKingdom?.data == null || pKeeper?.data == null) return;

            var duplicates = new List<Army>();
            long anchorId = pAnchorCity?.id ?? -1L;
            foreach (Army army in GetRoleArmies(pKingdom, pRole))
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

            Actor duplicateCaptain = null;
            try { duplicateCaptain = pDuplicate.getCaptain(); }
            catch { }
            using (ArmyCaptainDisposalScope.Open(pDuplicate))
            {
                foreach (Actor unit in units)
                    AddToArmy(unit, pKeeper);
            }
            if (duplicateCaptain?.army == pKeeper)
                SetCaptainIfChanged(pKeeper, duplicateCaptain);
            RemoveArmyObject(pDuplicate, pClearCityReference: true,
                pRequestReplacement: false);
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
            SetLookupCache(key, pArmy.id);
            string roleIndexKey = BuildRoleIndexKey(pKingdom.id, pRole);
            if (RoleIndexKeyByArmy.TryGetValue(pArmy.id, out string previousKey) && previousKey != roleIndexKey &&
                RoleArmyIdsByKingdomRole.TryGetValue(previousKey, out HashSet<long> previousIds))
            {
                previousIds.Remove(pArmy.id);
                if (previousIds.Count == 0) RoleArmyIdsByKingdomRole.Remove(previousKey);
            }
            if (!RoleArmyIdsByKingdomRole.TryGetValue(roleIndexKey, out HashSet<long> ids))
            {
                ids = new HashSet<long>();
                RoleArmyIdsByKingdomRole[roleIndexKey] = ids;
            }
            ids.Add(pArmy.id);
            RoleIndexKeyByArmy[pArmy.id] = roleIndexKey;
        }

        private static void RemoveArmyFromCache(Army pArmy)
        {
            if (pArmy?.data == null) return;
            if (LookupCacheKeysByArmy.TryGetValue(pArmy.id, out HashSet<string> lookupKeys))
            {
                foreach (string key in lookupKeys) RoleArmyCache.Remove(key);
                LookupCacheKeysByArmy.Remove(pArmy.id);
            }
            if (!RoleIndexKeyByArmy.TryGetValue(pArmy.id, out string roleIndexKey)) return;
            RoleIndexKeyByArmy.Remove(pArmy.id);
            if (!RoleArmyIdsByKingdomRole.TryGetValue(roleIndexKey, out HashSet<long> ids)) return;
            ids.Remove(pArmy.id);
            if (ids.Count == 0) RoleArmyIdsByKingdomRole.Remove(roleIndexKey);
        }

        private static void SetLookupCache(string pKey, long pArmyId)
        {
            if (string.IsNullOrEmpty(pKey) || pArmyId < 0) return;
            if (RoleArmyCache.TryGetValue(pKey, out long previousArmyId) && previousArmyId != pArmyId)
                RemoveLookupCacheKey(pKey, previousArmyId);
            RoleArmyCache[pKey] = pArmyId;
            if (!LookupCacheKeysByArmy.TryGetValue(pArmyId, out HashSet<string> keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                LookupCacheKeysByArmy[pArmyId] = keys;
            }
            keys.Add(pKey);
        }

        private static void RemoveLookupCacheKey(string pKey, long pArmyId)
        {
            RoleArmyCache.Remove(pKey);
            if (!LookupCacheKeysByArmy.TryGetValue(pArmyId, out HashSet<string> keys)) return;
            keys.Remove(pKey);
            if (keys.Count == 0) LookupCacheKeysByArmy.Remove(pArmyId);
        }

        private static string BuildRoleIndexKey(long pKingdomId, string pRole)
        {
            return pKingdomId + "|" + (pRole ?? "");
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
            Kingdom intended = GetIntendedKingdom(pArmy, pAnchorCity);
            if (intended?.data != null) return intended;
            try
            {
                Kingdom kingdom = pArmy?.getKingdom();
                if (kingdom?.data != null) return kingdom;
            }
            catch { }
            return pAnchorCity?.kingdom;
        }

        private static Kingdom SafeGetStoredKingdom(Army pArmy)
        {
            try
            {
                Kingdom kingdom = ArmyKingdomField?.GetValue(pArmy) as Kingdom;
                return kingdom?.data != null && !kingdom.isRekt()
                    ? kingdom
                    : null;
            }
            catch { return null; }
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
