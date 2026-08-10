using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class WartimeGarrisonService
    {
        private sealed class CityPool
        {
            public readonly long CityId;
            public readonly long KingdomId;
            public readonly HashSet<long> ActorIds = new HashSet<long>();
            public readonly long[] MutationBuffer =
                new long[WartimeGarrisonRules.DemobilizationBatchSize];

            public CityPool(long pCityId, long pKingdomId)
            {
                CityId = pCityId;
                KingdomId = pKingdomId;
            }
        }

        private sealed class KingdomPool
        {
            public readonly HashSet<long> ActorIds = new HashSet<long>();
            public readonly long[] MutationBuffer =
                new long[WartimeGarrisonRules.DemobilizationBatchSize];
        }

        private sealed class CityRefreshPlan
        {
            public readonly long KingdomId;
            public readonly List<long> CityIds;
            public int Cursor;

            public CityRefreshPlan(long pKingdomId, List<long> pCityIds)
            {
                KingdomId = pKingdomId;
                CityIds = pCityIds;
            }
        }

        private sealed class ThreatProbeState
        {
            public Actor CachedTarget;
            public double NextSearchAllowed = -1d;
            public long CityId = -1L;
            public int OriginChunkId = -1;
            public int ChunkCursor;
            public int UnitCursor;
        }

        private static readonly Dictionary<long, CityPool> CityPools =
            new Dictionary<long, CityPool>();
        private static readonly Dictionary<long, KingdomPool> KingdomPools =
            new Dictionary<long, KingdomPool>();
        private static readonly Dictionary<long, CityRefreshPlan> RefreshPlans =
            new Dictionary<long, CityRefreshPlan>();
        private static readonly Dictionary<long, HashSet<long>>
            UnderfilledCitiesByKingdom =
                new Dictionary<long, HashSet<long>>();
        private static readonly HashSet<long> ActiveActorIds =
            new HashSet<long>();
        private static readonly Dictionary<long, int> PatrolCursorByActor =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, int> DefenseCursorByActor =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, ThreatProbeState>
            ThreatProbesByActor = new Dictionary<long, ThreatProbeState>();
        private static readonly Dictionary<long, Actor> CityThreatTargets =
            new Dictionary<long, Actor>();
        private static readonly Dictionary<long, double>
            CityThreatSearchNextAllowed = new Dictionary<long, double>();
        private static readonly Dictionary<long, double>
            BoundaryRefreshNextAllowedByCity =
                new Dictionary<long, double>();
        private static readonly HashSet<long> SortieReserveCityIds =
            new HashSet<long>();

        public static bool IsActive(Actor pActor)
        {
            return pActor?.data != null &&
                   ActiveActorIds.Contains(pActor.data.id);
        }

        public static bool HasIndexedDefender(City pCity, Kingdom pKingdom)
        {
            return pCity?.data != null && pKingdom?.data != null &&
                   pCity.kingdom == pKingdom &&
                   OccupiedCitySupplyService.CanProvideToRealm(pCity,
                       pKingdom) &&
                   CityPools.TryGetValue(pCity.id, out CityPool pool) &&
                   pool.KingdomId == pKingdom.id &&
                   pool.ActorIds.Count > 0;
        }

        public static int GetIndexedDefenderCount(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            if (pCity?.data == null || kingdom?.data == null ||
                !OccupiedCitySupplyService.CanProvideToRealm(pCity,
                    kingdom) ||
                !CityPools.TryGetValue(pCity.id, out CityPool pool) ||
                pool.KingdomId != kingdom.id) return 0;
            return pool.ActorIds.Count;
        }

        public static int MinimumDefenseForSortie(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            if (pCity?.data == null || kingdom?.data == null) return 0;
            return pCity == kingdom.capital || HasForeignBorder(pCity, kingdom)
                ? WartimeGarrisonRules.PriorityTarget
                : WartimeGarrisonRules.BaseTarget;
        }

        internal static void RequestSortieReserve(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            if (pCity?.data == null || kingdom?.data == null ||
                !OccupiedCitySupplyService.CanProvideToRealm(pCity,
                    kingdom))
            {
                if (pCity?.data != null)
                    SortieReserveCityIds.Remove(pCity.id);
                return;
            }
            if (!SortieReserveCityIds.Add(pCity.id))
                return;
            ScheduleCity(pCity.id);
        }

        internal static void ClearSortieReserve(City pCity)
        {
            if (pCity != null) SortieReserveCityIds.Remove(pCity.id);
        }

        private static bool HasSortieReserveRequest(long pCityId)
        {
            return SortieReserveCityIds.Contains(pCityId);
        }

        public static IReadOnlyList<Actor> CollectSortieMembers(City pCity,
            int pMaximum)
        {
            Kingdom kingdom = pCity?.kingdom;
            if (pCity?.data == null || kingdom?.data == null ||
                pMaximum <= 0 ||
                !OccupiedCitySupplyService.CanProvideToRealm(pCity,
                    kingdom) ||
                !CityPools.TryGetValue(pCity.id, out CityPool pool) ||
                pool.KingdomId != kingdom.id)
                return Array.Empty<Actor>();
            var ids = new long[pool.ActorIds.Count];
            pool.ActorIds.CopyTo(ids);
            Array.Sort(ids);
            int limit = Math.Min(pMaximum, ids.Length);
            var result = new List<Actor>(limit);
            for (int i = 0; i < ids.Length && result.Count < limit; i++)
            {
                Actor actor = ResolveActor(ids[i]);
                if (actor?.data != null && !actor.isRekt() &&
                    actor.isAlive() && actor.isWarrior() &&
                    actor.city == pCity && IsActive(actor))
                    result.Add(actor);
            }
            return result;
        }

        public static bool ReleaseForSortie(Actor pActor, City pOrigin,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pOrigin?.data == null ||
                pKingdom?.data == null || pActor.city != pOrigin ||
                pActor.kingdom != pKingdom ||
                !OccupiedCitySupplyService.CanProvideToRealm(pOrigin,
                    pKingdom) || !IsActive(pActor))
                return false;
            pActor.data.get(LineageKeys.WARTIME_GARRISON_CITY_ID,
                out long cityId, -1L);
            pActor.data.get(LineageKeys.WARTIME_GARRISON_KINGDOM_ID,
                out long kingdomId, -1L);
            if (cityId != pOrigin.id || kingdomId != pKingdom.id)
                return false;
            RemoveIndexes(pActor.data.id, cityId, kingdomId);
            ClearFields(pActor);
            return true;
        }

        internal static void OnRealmSupplyChanged(City pCity)
        {
            if (pCity?.data == null) return;
            GarrisonSortieService.OnOriginSupplyChanged(pCity);
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null ||
                !OccupiedCitySupplyService.CanProvideToRealm(pCity,
                    kingdom))
                SortieReserveCityIds.Remove(pCity.id);
            ScheduleCity(pCity.id);
        }

        public static void ReturnFromSortie(Actor pActor, City pOrigin,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pOrigin?.data == null ||
                pKingdom?.data == null || pActor.isRekt() ||
                !pActor.isAlive()) return;
            if (pActor.army != null)
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
            }
            if (pActor.city != pOrigin)
            {
                try { pActor.joinCity(pOrigin); }
                catch { }
            }
            if (!HasActiveWar(pKingdom) ||
                !OccupiedCitySupplyService.CanProvideToRealm(pOrigin,
                    pKingdom) || pActor.kingdom != pKingdom ||
                pActor.city != pOrigin)
            {
                ClearFields(pActor);
                TemporaryMilitaryDemobilizationService.RestoreCivilian(
                    pActor);
                return;
            }
            pActor.data.set(LineageKeys.WARTIME_GARRISON, true);
            pActor.data.set(LineageKeys.WARTIME_GARRISON_KINGDOM_ID,
                pKingdom.id);
            pActor.data.set(LineageKeys.WARTIME_GARRISON_CITY_ID,
                pOrigin.id);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            AddIndexes(pActor.data.id, pOrigin.id, pKingdom.id);
            AssignJob(pActor);
        }

        public static bool ShouldBlockArmyAssignment(Actor pActor,
            Army pArmy)
        {
            return pArmy?.data != null &&
                   WartimeGarrisonRules.ShouldBlockOffensiveAssignment(
                       IsActive(pActor));
        }

        public static string GetJob(Actor pActor)
        {
            return IsActive(pActor) ? WartimeGarrisonContent.JobId : "";
        }

        public static void OnWarStarted(War pWar)
        {
            if (!ZhuluWarService.ShouldEnrollInAw3Systems(pWar)) return;
            foreach (Kingdom kingdom in pWar.getAttackers())
                OnKingdomWarStateChanged(kingdom);
            foreach (Kingdom kingdom in pWar.getDefenders())
                OnKingdomWarStateChanged(kingdom);
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            foreach (Kingdom kingdom in pWar.getAttackers())
                OnKingdomWarStateChanged(kingdom);
            foreach (Kingdom kingdom in pWar.getDefenders())
                OnKingdomWarStateChanged(kingdom);
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            GarrisonSortieService.OnKingdomDestroying(pKingdom);
            RefreshPlans.Remove(pKingdom.id);
            UnderfilledCitiesByKingdom.Remove(pKingdom.id);
            ScheduleKingdomDemobilization(pKingdom.id);
        }

        public static void OnKingdomWarStateChanged(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (!HasActiveWar(pKingdom))
            {
                RefreshPlans.Remove(pKingdom.id);
                ScheduleKingdomDemobilization(pKingdom.id);
                return;
            }

            var cityIds = new List<long>(pKingdom.cities.Count);
            for (int i = 0; i < pKingdom.cities.Count; i++)
            {
                City city = pKingdom.cities[i];
                if (city?.data != null && !city.isRekt() &&
                    city.kingdom == pKingdom)
                    cityIds.Add(city.id);
            }
            RefreshPlans[pKingdom.id] =
                new CityRefreshPlan(pKingdom.id, cityIds);
            ScheduleKingdomRefresh(pKingdom.id);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (!HasActiveWar(pKingdom))
            {
                ScheduleKingdomDemobilization(pKingdom.id);
                return;
            }
            if (!UnderfilledCitiesByKingdom.TryGetValue(pKingdom.id,
                    out HashSet<long> cityIds) || cityIds.Count == 0) return;
            foreach (long cityId in cityIds)
            {
                ScheduleCity(cityId);
                break;
            }
        }

        public static void OnCityThreatChanged(City pCity)
        {
            if (pCity?.data == null) return;
            if (HasActiveWar(pCity.kingdom) || CityPools.ContainsKey(pCity.id))
                ScheduleCity(pCity.id);
        }

        public static void OnCityOwnerChanged(City pCity,
            Kingdom pPreviousOwner)
        {
            if (pCity?.data == null) return;
            ClearCityPatrolRuntime(pCity.id);
            if (pPreviousOwner?.data != null)
            {
                RemoveUnderfilled(pPreviousOwner.id, pCity.id);
                ScheduleCity(pCity.id);
            }
            if (pCity.kingdom?.data != null && HasActiveWar(pCity.kingdom))
                ScheduleCity(pCity.id);
        }

        public static void OnCityInvalidated(City pCity)
        {
            if (pCity?.data == null) return;
            CityBoundaryPatrolService.Invalidate(pCity);
            ClearCityPatrolRuntime(pCity.id);
            RemoveUnderfilled(pCity.kingdom?.id ?? -1L, pCity.id);
            ScheduleCity(pCity.id);
        }

        public static void OnActorInvalidated(Actor pActor)
        {
            if (pActor?.data == null ||
                !ActiveActorIds.Contains(pActor.data.id)) return;
            pActor.data.get(LineageKeys.WARTIME_GARRISON_CITY_ID,
                out long cityId, -1L);
            pActor.data.get(LineageKeys.WARTIME_GARRISON_KINGDOM_ID,
                out long kingdomId, -1L);
            RemoveIndexes(pActor.data.id, cityId, kingdomId);
            ClearFields(pActor);
            if (cityId >= 0) ScheduleCity(cityId);
        }

        public static WorldTile GetPatrolTile(Actor pActor)
        {
            if (!ShouldPatrol(pActor)) return null;
            City city = GetGarrisonCity(pActor);
            if (city?.data == null || city.isRekt()) return null;
            TryRefreshBoundaryZones(city);

            long actorId = pActor.data.id;
            if (!PatrolCursorByActor.TryGetValue(actorId, out int cursor))
                cursor = PositiveModulo((int)(actorId % int.MaxValue),
                    Math.Max(1, city.border_zones.Count));
            PatrolCursorByActor[actorId] = cursor == int.MaxValue
                ? 0
                : cursor + 1;
            WorldTile tile = null;
            if (city.border_zones.Count > 0)
            {
                TileZone boundary = CityBoundaryPatrolService.GetBoundaryZone(
                    city, actorId, cursor);
                tile = FindSafePatrolTile(boundary, actorId, cursor,
                    pActor.current_tile);
            }
            if (tile == null)
                tile = FindSafeCityRingTile(city, actorId, cursor,
                    pActor.current_tile);
            if (tile == null) return null;
            if (pActor.current_tile != null &&
                !pActor.current_tile.isSameIsland(tile))
                return FindSafeCityRingTile(city, actorId, cursor,
                    pActor.current_tile);
            return tile;
        }

        public static bool ShouldPatrol(Actor pActor)
        {
            if (!IsActive(pActor)) return false;
            City city = GetGarrisonCity(pActor);
            if (city?.data == null || city.isRekt()) return false;
            bool hasDirectThreat = IsValidThreat(pActor,
                GetDirectThreat(pActor));
            bool hasNearbyThreat = !hasDirectThreat &&
                FindNearbyThreat(pActor, city) != null;
            bool hasAttackTarget;
            try { hasAttackTarget = pActor.has_attack_target; }
            catch { hasAttackTarget = false; }
            return !hasAttackTarget && WartimeGarrisonRules.ShouldPatrol(
                IsCityInDanger(city), IsCityGettingCaptured(city),
                hasDirectThreat, hasNearbyThreat,
                IsCityFrozenControlledByEnemy(city, pActor.kingdom));
        }

        public static Actor FindThreatNearGarrison(Actor pActor)
        {
            if (!IsActive(pActor) || pActor?.current_tile == null)
                return null;
            City city = GetGarrisonCity(pActor);
            if (city?.data == null) return null;

            Actor directThreat = GetDirectThreat(pActor);
            if (IsValidThreat(pActor, directThreat)) return directThreat;
            return FindNearbyThreat(pActor, city);
        }

        public static bool IsValidThreatForGarrison(Actor pActor,
            Actor pTarget)
        {
            return IsValidThreat(pActor, pTarget);
        }

        public static WorldTile GetDefenseTile(Actor pActor)
        {
            if (!IsActive(pActor)) return null;
            City city = GetGarrisonCity(pActor);
            bool gettingCaptured = IsCityGettingCaptured(city);
            bool frozenControlled = IsCityFrozenControlledByEnemy(city,
                pActor?.kingdom);
            if (city?.data == null || (!gettingCaptured &&
                !frozenControlled))
                return null;

            long actorId = pActor.data.id;
            if (!DefenseCursorByActor.TryGetValue(actorId, out int cursor))
                cursor = 0;
            DefenseCursorByActor[actorId] = cursor == int.MaxValue
                ? 0
                : cursor + 1;

            int visit = cursor;
            if (gettingCaptured)
            {
                try
                {
                    foreach (TileZone zone in city.danger_zones)
                    {
                        WorldTile tile = FindSafePatrolTile(zone, actorId,
                            visit++, pActor.current_tile);
                        if (tile != null && (pActor.current_tile == null ||
                            pActor.current_tile.isSameIsland(tile)))
                            return tile;
                    }
                }
                catch { }
            }
            if (frozenControlled)
            {
                WorldTile tile = FindSafeCityZoneTile(city, actorId, visit,
                    pActor.current_tile);
                if (tile != null) return tile;
            }
            return FindSafeCityRingTile(city, actorId, cursor,
                pActor.current_tile);
        }

        private static City GetGarrisonCity(Actor pActor)
        {
            if (pActor?.data == null) return null;
            pActor.data.get(LineageKeys.WARTIME_GARRISON_CITY_ID,
                out long cityId, -1L);
            City city = ResolveCity(cityId);
            return city?.data != null && !city.isRekt()
                ? city
                : pActor.city;
        }

        private static Actor GetDirectThreat(Actor pActor)
        {
            try
            {
                BaseSimObject source = pActor?.attackedBy;
                return source?.isActor() == true ? source.a : null;
            }
            catch { return null; }
        }

        private static bool IsValidThreat(Actor pActor, Actor pTarget)
        {
            try
            {
                return IsActive(pActor) && pActor?.kingdom?.data != null &&
                       pActor.current_tile != null && pTarget?.data != null &&
                       !pTarget.isRekt() && pTarget.isAlive() &&
                       pTarget.isWarrior() && pTarget.kingdom?.data != null &&
                       pActor.kingdom.isEnemy(pTarget.kingdom) &&
                       pTarget.current_tile != null &&
                       pActor.current_tile.isSameIsland(
                           pTarget.current_tile) &&
                       pActor.isTargetOkToAttack(pTarget);
            }
            catch { return false; }
        }

        private static Actor FindNearbyThreat(Actor pActor, City pCity)
        {
            const int threatRadius = 12;
            if (pActor?.data == null || pActor.current_tile == null ||
                pCity?.data == null) return null;

            long actorId = pActor.data.id;
            if (!ThreatProbesByActor.TryGetValue(actorId,
                    out ThreatProbeState state))
            {
                state = new ThreatProbeState();
                ThreatProbesByActor[actorId] = state;
            }

            if (IsNearbyThreat(pActor, state.CachedTarget,
                    threatRadius))
                return state.CachedTarget;
            state.CachedTarget = null;

            MapChunk originChunk = pActor.current_tile.chunk;
            if (originChunk == null)
            {
                ResetThreatProbeCursor(state, pCity.id, null);
                return null;
            }
            if (state.CityId != pCity.id ||
                state.OriginChunkId != originChunk.id)
                ResetThreatProbeCursor(state, pCity.id, originChunk);

            if (CityThreatTargets.TryGetValue(pCity.id,
                    out Actor cityTarget))
            {
                if (IsNearbyThreat(pActor, cityTarget, threatRadius))
                {
                    state.CachedTarget = cityTarget;
                    return cityTarget;
                }
                CityThreatTargets.Remove(pCity.id);
            }

            double now;
            try { now = LineageService.CurTime(); }
            catch { return null; }
            double cityNextAllowed = CityThreatSearchNextAllowed.TryGetValue(
                pCity.id, out double nextAllowed) ? nextAllowed : -1d;
            if (!WartimeGarrisonRules.ShouldRunThreatProbe(now,
                    state.NextSearchAllowed, cityNextAllowed))
                return null;

            Actor result = null;
            int bestDistance = int.MaxValue;
            int inspected = 0;
            int chunkSlots = WartimeGarrisonRules.ThreatProbeChunkSlotCount(
                originChunk.neighbours_all?.Length ?? 0);
            int visitedChunks = 0;
            try
            {
                while (visitedChunks < chunkSlots &&
                       WartimeGarrisonRules.CanInspectThreatCandidate(
                           inspected))
                {
                    int chunkSlot = WartimeGarrisonRules.
                        NormalizeThreatProbeCursor(state.ChunkCursor,
                            chunkSlots);
                    MapChunk chunk = GetThreatProbeChunk(originChunk,
                        chunkSlot, chunkSlots);
                    if (chunk?.objects?.units_all == null ||
                        chunk.objects.units_all.Count == 0)
                    {
                        AdvanceThreatProbeChunk(state, chunkSlots);
                        visitedChunks++;
                        continue;
                    }

                    List<Actor> units = chunk.objects.units_all;
                    int unitCursor = WartimeGarrisonRules.
                        NormalizeThreatProbeCursor(state.UnitCursor,
                            units.Count);
                    int inspectedInChunk = 0;
                    while (unitCursor < units.Count &&
                           WartimeGarrisonRules.CanInspectThreatCandidate(
                               inspected))
                    {
                        Actor candidate = units[unitCursor++];
                        inspected++;
                        inspectedInChunk++;
                        if (!IsNearbyThreat(pActor, candidate,
                                threatRadius))
                            continue;
                        int distance = Toolbox.SquaredDistTile(
                            pActor.current_tile, candidate.current_tile);
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        result = candidate;
                    }

                    if (WartimeGarrisonRules.ShouldAdvanceThreatProbeChunk(
                            state.UnitCursor, inspectedInChunk,
                            units.Count))
                    {
                        AdvanceThreatProbeChunk(state, chunkSlots);
                        visitedChunks++;
                    }
                    else
                    {
                        state.ChunkCursor = chunkSlot;
                        state.UnitCursor = WartimeGarrisonRules.
                            AdvanceThreatProbeUnitCursor(state.UnitCursor,
                                inspectedInChunk, units.Count);
                    }
                }
            }
            catch { }

            CityThreatSearchNextAllowed[pCity.id] = now +
                WartimeGarrisonRules.ThreatProbeCityCooldownSeconds;
            if (result != null)
            {
                state.CachedTarget = result;
                CityThreatTargets[pCity.id] = result;
                return result;
            }

            state.NextSearchAllowed = now +
                WartimeGarrisonRules.ThreatProbeActorCooldownSeconds;
            return null;
        }

        private static MapChunk GetThreatProbeChunk(MapChunk pOrigin,
            int pSlot, int pSlotCount)
        {
            if (pOrigin == null) return null;
            int slot = WartimeGarrisonRules.NormalizeThreatProbeCursor(
                pSlot, pSlotCount);
            if (slot == 0) return pOrigin;
            MapChunk[] neighbours = pOrigin.neighbours_all;
            int neighbourIndex = slot - 1;
            return neighbourIndex >= 0 &&
                   neighbourIndex < (neighbours?.Length ?? 0)
                ? neighbours[neighbourIndex]
                : null;
        }

        private static void AdvanceThreatProbeChunk(ThreatProbeState pState,
            int pChunkSlots)
        {
            if (pState == null) return;
            pState.ChunkCursor = WartimeGarrisonRules.
                NormalizeThreatProbeCursor(pState.ChunkCursor + 1,
                    pChunkSlots);
            pState.UnitCursor = 0;
        }

        private static void ResetThreatProbeCursor(ThreatProbeState pState,
            long pCityId, MapChunk pOriginChunk)
        {
            if (pState == null) return;
            pState.CachedTarget = null;
            pState.NextSearchAllowed = -1d;
            pState.CityId = pCityId;
            pState.OriginChunkId = pOriginChunk?.id ?? -1;
            pState.ChunkCursor = 0;
            pState.UnitCursor = 0;
        }

        private static bool IsNearbyThreat(Actor pActor, Actor pTarget,
            int pRadius)
        {
            if (!IsValidThreat(pActor, pTarget) || pRadius < 0)
                return false;
            try
            {
                int distance = Toolbox.SquaredDistTile(pActor.current_tile,
                    pTarget.current_tile);
                return distance <= pRadius * pRadius;
            }
            catch { return false; }
        }

        private static bool IsCityInDanger(City pCity)
        {
            try { return pCity?.data != null && pCity.isInDanger(); }
            catch { return false; }
        }

        private static bool IsCityGettingCaptured(City pCity)
        {
            try
            {
                return pCity?.data != null && pCity.isGettingCaptured();
            }
            catch { return false; }
        }

        private static bool IsCityFrozenControlledByEnemy(City pCity,
            Kingdom pDefender)
        {
            try
            {
                return pCity?.data != null && pDefender?.data != null &&
                       pCity.kingdom == pDefender &&
                       WarScoreService.IsCityFrozenControlledByEnemySide(
                           pCity, pDefender);
            }
            catch { return false; }
        }

        private static void TryRefreshBoundaryZones(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.border_zones.Count > 0) return;
            double now;
            try { now = LineageService.CurTime(); }
            catch { return; }
            double nextAllowed = BoundaryRefreshNextAllowedByCity.TryGetValue(
                pCity.id, out double next) ? next : -1d;
            if (!WartimeGarrisonRules.ShouldRetryBoundaryRefresh(now,
                    nextAllowed))
                return;

            BoundaryRefreshNextAllowedByCity[pCity.id] = now +
                WartimeGarrisonRules.BoundaryRefreshRetrySeconds;
            try { pCity.recalculateNeighbourZones(); }
            catch { return; }
            if (pCity.border_zones.Count > 0)
            {
                BoundaryRefreshNextAllowedByCity.Remove(pCity.id);
                CityBoundaryPatrolService.Invalidate(pCity);
            }
        }

        private static WorldTile FindSafePatrolTile(TileZone pZone,
            long pActorId, int pVisit, WorldTile pAvoid)
        {
            WorldTile center = pZone?.centerTile;
            WorldTile[] tiles = pZone?.tiles;
            int count = tiles?.Length ?? 0;
            if (count <= 0)
                return IsSafePatrolTile(center) ? center : null;
            int start = WartimeGarrisonRules.PatrolStartIndex(pActorId,
                pVisit, count);
            int checks = Math.Min(16, count);
            WorldTile fallback = null;
            for (int offset = 0; offset < checks; offset++)
            {
                WorldTile tile = tiles[(start + offset) % count];
                if (!IsSafePatrolTile(tile)) continue;
                if (tile != pAvoid) return tile;
                fallback = tile;
            }
            if (IsSafePatrolTile(center) && center != pAvoid) return center;
            return fallback;
        }

        private static WorldTile FindSafeCityZoneTile(City pCity,
            long pActorId, int pVisit, WorldTile pAvoid)
        {
            int count = pCity?.zones?.Count ?? 0;
            if (count <= 0) return null;
            int start = WartimeGarrisonRules.PatrolStartIndex(pActorId,
                pVisit, count);
            for (int offset = 0; offset < count; offset++)
            {
                TileZone zone = pCity.zones[(start + offset) % count];
                WorldTile tile = FindSafePatrolTile(zone, pActorId,
                    pVisit + offset, pAvoid);
                if (tile != null && (pAvoid == null ||
                    pAvoid.isSameIsland(tile)))
                    return tile;
            }
            return null;
        }

        private static WorldTile FindSafeCityRingTile(City pCity,
            long pActorId, int pVisit, WorldTile pAvoid)
        {
            WorldTile center = pCity?.getTile();
            WorldTile[] neighbours = center?.neighboursAll;
            int count = neighbours?.Length ?? 0;
            if (count <= 0) return null;

            int targetIndex = PositiveModulo((int)(pActorId %
                int.MaxValue) + Math.Max(0, pVisit) * 7, 32);
            int candidateIndex = 0;
            WorldTile fallback = null;
            for (int first = 0; first < count; first++)
            {
                WorldTile[] ring = neighbours[first]?.neighboursAll;
                int ringCount = ring?.Length ?? 0;
                for (int second = 0; second < ringCount; second++)
                {
                    WorldTile tile = ring[second];
                    if (tile == center || !IsSafePatrolTile(tile) ||
                        tile == pAvoid || (pAvoid != null &&
                        !pAvoid.isSameIsland(tile))) continue;
                    if (candidateIndex++ >= targetIndex) return tile;
                    fallback = tile;
                }
            }

            int start = WartimeGarrisonRules.PatrolStartIndex(pActorId,
                pVisit, count);
            for (int offset = 0; offset < count; offset++)
            {
                WorldTile tile = neighbours[(start + offset) % count];
                if (!IsSafePatrolTile(tile) || (pAvoid != null &&
                    !pAvoid.isSameIsland(tile))) continue;
                if (tile != pAvoid) return tile;
                fallback = tile;
            }
            return fallback;
        }

        private static bool IsSafePatrolTile(WorldTile pTile)
        {
            TileTypeBase type = pTile?.Type;
            return WartimeGarrisonRules.CanUsePatrolCandidate(
                pTile?.data != null, type?.ground == true,
                type?.liquid == true, type?.ocean == true,
                type?.lava == true, type?.block == true);
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            List<Actor> actors = World.world?.units?.units_only_alive;
            if (actors != null)
            {
                for (int i = 0; i < actors.Count; i++)
                {
                    Actor actor = actors[i];
                    if (!HasPersistedFlag(actor)) continue;
                    actor.data.get(LineageKeys.WARTIME_GARRISON_KINGDOM_ID,
                        out long kingdomId, -1L);
                    actor.data.get(LineageKeys.WARTIME_GARRISON_CITY_ID,
                        out long cityId, -1L);
                    Kingdom kingdom = ResolveKingdom(kingdomId);
                    City city = ResolveCity(cityId);
                    if (kingdom?.data == null || city?.data == null ||
                        actor.isRekt() || !actor.isAlive())
                    {
                        ClearFields(actor);
                        continue;
                    }
                    AddIndexes(actor.data.id, cityId, kingdomId);
                    AssignJob(actor);
                }
            }

            GarrisonSortieService.RebuildRuntime();

            if (World.world?.wars == null) return;
            foreach (War war in World.world.wars)
                if (war?.data != null && !war.hasEnded()) OnWarStarted(war);
            var kingdomIds = new List<long>(KingdomPools.Keys);
            for (int i = 0; i < kingdomIds.Count; i++)
            {
                Kingdom kingdom = ResolveKingdom(kingdomIds[i]);
                if (kingdom?.data == null || !HasActiveWar(kingdom))
                    ScheduleKingdomDemobilization(kingdomIds[i]);
            }
        }

        public static void ClearRuntime()
        {
            GarrisonSortieService.ClearRuntime();
            CityPools.Clear();
            KingdomPools.Clear();
            RefreshPlans.Clear();
            UnderfilledCitiesByKingdom.Clear();
            ActiveActorIds.Clear();
            PatrolCursorByActor.Clear();
            DefenseCursorByActor.Clear();
            ThreatProbesByActor.Clear();
            CityThreatTargets.Clear();
            CityThreatSearchNextAllowed.Clear();
            BoundaryRefreshNextAllowedByCity.Clear();
            SortieReserveCityIds.Clear();
            CityBoundaryPatrolService.ClearRuntime();
        }

        private static void ClearCityPatrolRuntime(long pCityId)
        {
            if (pCityId < 0) return;
            CityThreatTargets.Remove(pCityId);
            CityThreatSearchNextAllowed.Remove(pCityId);
            foreach (ThreatProbeState state in ThreatProbesByActor.Values)
                if (state.CityId == pCityId)
                    ResetThreatProbeCursor(state, -1L, null);
            BoundaryRefreshNextAllowedByCity.Remove(pCityId);
        }

        private static void ScheduleKingdomRefresh(long pKingdomId)
        {
            if (pKingdomId < 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "wartime_garrison_kingdom", pKingdomId),
                DeferredWorkClass.Runtime,
                () => Measure(() => ProcessKingdomRefresh(pKingdomId)));
        }

        private static void ProcessKingdomRefresh(long pKingdomId)
        {
            if (!RefreshPlans.TryGetValue(pKingdomId,
                    out CityRefreshPlan plan)) return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt() ||
                !HasActiveWar(kingdom))
            {
                RefreshPlans.Remove(pKingdomId);
                ScheduleKingdomDemobilization(pKingdomId);
                return;
            }

            int processed = 0;
            while (plan.Cursor < plan.CityIds.Count &&
                   processed < WartimeGarrisonRules.MaxCitiesPerWorkItem)
            {
                ScheduleCity(plan.CityIds[plan.Cursor++]);
                processed++;
            }
            if (plan.Cursor < plan.CityIds.Count)
                ScheduleKingdomRefresh(pKingdomId);
            else
                RefreshPlans.Remove(pKingdomId);
        }

        private static void ScheduleCity(long pCityId)
        {
            if (pCityId < 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "wartime_garrison_city", pCityId),
                DeferredWorkClass.Runtime,
                () => Measure(() => ProcessCity(pCityId)));
        }

        private static void ProcessCity(long pCityId)
        {
            City city = ResolveCity(pCityId);
            if (city?.data == null || city.isRekt() ||
                city.kingdom?.data == null || city.kingdom.isRekt())
            {
                SortieReserveCityIds.Remove(pCityId);
                DemobilizeCityBatch(pCityId, 0, pForce: true);
                return;
            }

            Kingdom kingdom = city.kingdom;
            if (CityPools.TryGetValue(pCityId, out CityPool existing) &&
                existing.KingdomId != kingdom.id)
            {
                DemobilizeCityBatch(pCityId, 0, pForce: true);
                if (CityPools.ContainsKey(pCityId))
                {
                    ScheduleCity(pCityId);
                    return;
                }
            }
            if (!OccupiedCitySupplyService.CanProvideToRealm(city, kingdom))
            {
                SortieReserveCityIds.Remove(pCityId);
                DemobilizeCityBatch(pCityId, 0, pForce: true);
                return;
            }

            bool atWar = HasActiveWar(kingdom);
            if (!atWar) SortieReserveCityIds.Remove(pCityId);
            int minimumDefense = MinimumDefenseForSortie(city);
            int target = WartimeGarrisonRules.TargetSize(atWar,
                city == kingdom.capital, HasForeignBorder(city, kingdom),
                IsUnderAttack(city));
            float garrisonMultiplier = KingdomPolicyEffectService
                .Read(kingdom).GarrisonMultiplier;
            target = WartimeGarrisonRules.ScaleTarget(target,
                garrisonMultiplier);
            if (atWar && HasSortieReserveRequest(pCityId))
                target = Math.Max(target,
                    GarrisonSortieRules.RequiredGarrisonForSortie(minimumDefense));
            int current = CleanAndCount(pCityId, kingdom.id);
            bool completedPass = true;
            if (current < target)
            {
                int need = WartimeGarrisonRules.RecruitmentNeed(current,
                    target);
                completedPass = RecruitFromCity(city, kingdom, need,
                    out int recruited);
                current += recruited;
            }
            bool launched = GarrisonSortieService.TryLaunch(city);
            if (launched)
                current = CleanAndCount(pCityId, kingdom.id);
            if (current > target)
            {
                DemobilizeCityBatch(pCityId, target);
                return;
            }
            if (current >= target)
                RemoveUnderfilled(kingdom.id, pCityId);
            else if (!completedPass || launched)
                ScheduleCity(pCityId);
            else
                MarkUnderfilled(kingdom.id, pCityId);
        }

        private static bool RecruitFromCity(City pCity, Kingdom pKingdom,
            int pNeed, out int pRecruited)
        {
            pRecruited = 0;
            int population;
            try { population = pCity.getPopulationPeople(); }
            catch { return true; }
            int recruitLimit = WartimeRecruitmentPopulationRules.RecruitmentCapacity(
                population, pNeed);
            if (recruitLimit <= 0 || pCity.units.Count == 0) return true;
            pCity.data.get(LineageKeys.WARTIME_GARRISON_SCAN_CURSOR,
                out int cursor, 0);
            if (cursor < 0 || cursor >= pCity.units.Count) cursor = 0;
            int available = pCity.units.Count - cursor;
            int limit = Math.Min(available,
                WartimeGarrisonRules.MaxCandidatesScannedPerWorkItem);
            int scanned = 0;
            for (int i = 0; i < limit; i++)
            {
                Actor actor = pCity.units[cursor + i];
                scanned++;
                if (!CanEnlist(pKingdom, pCity, actor)) continue;
                if (!Enlist(pKingdom, pCity, actor)) continue;
                pRecruited++;
                if (pRecruited >= recruitLimit ||
                    pRecruited >= WartimeGarrisonRules.MaxRecruitsPerWorkItem)
                    break;
            }
            bool complete = cursor + scanned >= pCity.units.Count;
            pCity.data.set(LineageKeys.WARTIME_GARRISON_SCAN_CURSOR,
                complete ? 0 : cursor + scanned);
            return complete;
        }

        private static bool CanEnlist(Kingdom pKingdom, City pCity,
            Actor pActor)
        {
            if (pActor?.data == null) return false;
            bool local = pActor.kingdom == pKingdom && pActor.city == pCity;
            bool civilian = pActor.isProfession(UnitProfession.Unit);
            bool protectedIdentity = IsProtectedIdentity(pKingdom, pActor);
            bool originalEligible;
            using (MilitaryRecruitmentScope.Open(
                       MilitaryRecruitmentKind.WartimeGarrison))
                originalEligible = pCity.checkCanMakeWarrior(pActor);
            return WartimeGarrisonRules.CanEnlist(originalEligible,
                protectedIdentity, local, civilian, pActor.getAge());
        }

        private static bool IsProtectedIdentity(Kingdom pKingdom,
            Actor pActor)
        {
            if (pActor.isRekt() || !pActor.isAlive() || !pActor.isAdult() ||
                pActor.asset?.is_boat == true) return true;
            if (pActor.isKing() || pActor.isCityLeader() ||
                HeirService.IsCurrentHeir(pKingdom, pActor)) return true;
            if (GeneralService.IsActiveGeneralFast(pActor) ||
                RoyalGuardService.IsRoyalGuard(pActor) ||
                RoyalAsylumService.IsActive(pActor) ||
                SlaveService.IsSlave(pActor) ||
                SlaveService.IsRetiredSoldier(pActor) ||
                SyntheticLevyService.IsSynthetic(pActor) ||
                TemporarySlaveVanguardService.IsMember(pActor) ||
                IsActive(pActor)) return true;
            if (pActor.army != null ||
                !HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.OrdinaryWarrior))
                return true;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string office, "");
            return !string.IsNullOrEmpty(office);
        }

        private static bool Enlist(Kingdom pKingdom, City pCity,
            Actor pActor)
        {
            using (MilitaryRecruitmentScope.Open(
                       MilitaryRecruitmentKind.WartimeGarrison))
            {
                if (!pCity.checkCanMakeWarrior(pActor)) return false;
                pCity.makeWarrior(pActor);
            }
            if (!pActor.isWarrior()) return false;
            if (pActor.army != null)
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
            }

            pActor.data.set(LineageKeys.WARTIME_GARRISON, true);
            pActor.data.set(LineageKeys.WARTIME_GARRISON_KINGDOM_ID,
                pKingdom.id);
            pActor.data.set(LineageKeys.WARTIME_GARRISON_CITY_ID, pCity.id);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            AddIndexes(pActor.data.id, pCity.id, pKingdom.id);
            AssignJob(pActor);
            return true;
        }

        private static int CleanAndCount(long pCityId, long pKingdomId)
        {
            if (!CityPools.TryGetValue(pCityId, out CityPool pool) ||
                pool.KingdomId != pKingdomId) return 0;
            int valid = 0;
            int stale = 0;
            foreach (long actorId in pool.ActorIds)
            {
                Actor actor = ResolveActor(actorId);
                if (actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                    actor.isWarrior() && IsActive(actor) &&
                    actor.kingdom?.id == pKingdomId &&
                    actor.city?.id == pCityId)
                {
                    valid++;
                    continue;
                }
                if (stale >= pool.MutationBuffer.Length) break;
                pool.MutationBuffer[stale++] = actorId;
            }

            for (int i = 0; i < stale; i++)
            {
                long actorId = pool.MutationBuffer[i];
                Actor actor = ResolveActor(actorId);
                RemoveIndexes(actorId, pCityId, pKingdomId);
                if (actor?.data != null) ClearFields(actor);
            }
            return valid;
        }

        private static void DemobilizeCityBatch(long pCityId, int pTarget,
            bool pForce = false)
        {
            if (!CityPools.TryGetValue(pCityId, out CityPool pool)) return;
            Kingdom kingdom = ResolveKingdom(pool.KingdomId);
            if (!pForce && pTarget <= 0 &&
                !TemporaryMilitaryServiceRules.ShouldDemobilize(
                    pool.ActorIds.Count > 0,
                    kingdom?.data != null &&
                    MilitaryEmergencyService.HasAny(kingdom))) return;
            int excess = Math.Max(0, pool.ActorIds.Count -
                                      Math.Max(0, pTarget));
            int count = 0;
            foreach (long actorId in pool.ActorIds)
            {
                pool.MutationBuffer[count++] = actorId;
                if (count >= pool.MutationBuffer.Length || count >= excess)
                    break;
            }
            for (int i = 0; i < count; i++)
            {
                long actorId = pool.MutationBuffer[i];
                Actor actor = ResolveActor(actorId);
                RemoveIndexes(actorId, pCityId, pool.KingdomId);
                if (actor?.data != null) DemobilizeActor(actor);
            }
            if (CityPools.TryGetValue(pCityId, out CityPool remaining) &&
                remaining.ActorIds.Count > Math.Max(0, pTarget))
                ScheduleCity(pCityId);
        }

        private static void ScheduleKingdomDemobilization(long pKingdomId)
        {
            if (pKingdomId < 0 ||
                !KingdomPools.TryGetValue(pKingdomId, out KingdomPool pool) ||
                pool.ActorIds.Count == 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "wartime_garrison_demobilize", pKingdomId),
                DeferredWorkClass.Runtime,
                () => Measure(() => DemobilizeKingdomBatch(pKingdomId)));
        }

        private static void DemobilizeKingdomBatch(long pKingdomId)
        {
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (!KingdomPools.TryGetValue(pKingdomId,
                    out KingdomPool pool)) return;
            if (!TemporaryMilitaryServiceRules.ShouldDemobilize(
                    pool.ActorIds.Count > 0,
                    kingdom?.data != null &&
                    MilitaryEmergencyService.HasAny(kingdom))) return;
            int count = 0;
            foreach (long actorId in pool.ActorIds)
            {
                pool.MutationBuffer[count++] = actorId;
                if (count >= pool.MutationBuffer.Length) break;
            }
            for (int i = 0; i < count; i++)
            {
                long actorId = pool.MutationBuffer[i];
                Actor actor = ResolveActor(actorId);
                long cityId = -1L;
                actor?.data?.get(LineageKeys.WARTIME_GARRISON_CITY_ID,
                    out cityId, -1L);
                RemoveIndexes(actorId, cityId, pKingdomId);
                if (actor?.data != null) DemobilizeActor(actor);
            }
            if (KingdomPools.TryGetValue(pKingdomId,
                    out KingdomPool remaining) && remaining.ActorIds.Count > 0)
                ScheduleKingdomDemobilization(pKingdomId);
        }

        private static void DemobilizeActor(Actor pActor)
        {
            ClearFields(pActor);
            TemporaryMilitaryDemobilizationService.RestoreCivilian(pActor);
        }

        private static void AddIndexes(long pActorId, long pCityId,
            long pKingdomId)
        {
            ActiveActorIds.Add(pActorId);
            if (!CityPools.TryGetValue(pCityId, out CityPool cityPool) ||
                cityPool.KingdomId != pKingdomId)
            {
                cityPool = new CityPool(pCityId, pKingdomId);
                CityPools[pCityId] = cityPool;
            }
            cityPool.ActorIds.Add(pActorId);
            if (!KingdomPools.TryGetValue(pKingdomId,
                    out KingdomPool kingdomPool))
            {
                kingdomPool = new KingdomPool();
                KingdomPools[pKingdomId] = kingdomPool;
            }
            kingdomPool.ActorIds.Add(pActorId);
        }

        private static void RemoveIndexes(long pActorId, long pCityId,
            long pKingdomId)
        {
            ActiveActorIds.Remove(pActorId);
            if (CityPools.TryGetValue(pCityId, out CityPool cityPool))
            {
                cityPool.ActorIds.Remove(pActorId);
                if (cityPool.ActorIds.Count == 0) CityPools.Remove(pCityId);
            }
            if (KingdomPools.TryGetValue(pKingdomId,
                    out KingdomPool kingdomPool))
            {
                kingdomPool.ActorIds.Remove(pActorId);
                if (kingdomPool.ActorIds.Count == 0)
                    KingdomPools.Remove(pKingdomId);
            }
        }

        private static void AssignJob(Actor pActor)
        {
            if (pActor?.data == null || pActor.ai == null) return;
            try
            {
                if (pActor.ai.job?.id != WartimeGarrisonContent.JobId)
                    pActor.ai.setJob(WartimeGarrisonContent.JobId);
            }
            catch { }
        }

        private static bool HasForeignBorder(City pCity, Kingdom pOwner)
        {
            if (pCity?.data == null || pOwner?.data == null) return false;
            try
            {
                foreach (Kingdom neighbour in pCity.neighbours_kingdoms)
                    if (neighbour?.data != null && neighbour != pOwner)
                        return true;
            }
            catch { }
            return false;
        }

        private static bool IsUnderAttack(City pCity)
        {
            try
            {
                return pCity?.data != null &&
                       (pCity.isInDanger() || pCity.isGettingCaptured());
            }
            catch { return false; }
        }

        private static bool HasActiveWar(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   MilitaryEmergencyService.TryGetActiveWarId(pKingdom,
                       out _);
        }

        private static void MarkUnderfilled(long pKingdomId, long pCityId)
        {
            if (!UnderfilledCitiesByKingdom.TryGetValue(pKingdomId,
                    out HashSet<long> cityIds))
            {
                cityIds = new HashSet<long>();
                UnderfilledCitiesByKingdom[pKingdomId] = cityIds;
            }
            cityIds.Add(pCityId);
        }

        private static void RemoveUnderfilled(long pKingdomId, long pCityId)
        {
            if (pKingdomId < 0 ||
                !UnderfilledCitiesByKingdom.TryGetValue(pKingdomId,
                    out HashSet<long> cityIds)) return;
            cityIds.Remove(pCityId);
            if (cityIds.Count == 0)
                UnderfilledCitiesByKingdom.Remove(pKingdomId);
        }

        private static void ClearFields(Actor pActor)
        {
            if (pActor?.data == null) return;
            ActiveActorIds.Remove(pActor.data.id);
            PatrolCursorByActor.Remove(pActor.data.id);
            DefenseCursorByActor.Remove(pActor.data.id);
            ThreatProbesByActor.Remove(pActor.data.id);
            pActor.data.set(LineageKeys.WARTIME_GARRISON, false);
            pActor.data.set(LineageKeys.WARTIME_GARRISON_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.WARTIME_GARRISON_CITY_ID, -1L);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
        }

        private static bool HasPersistedFlag(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.WARTIME_GARRISON,
                out bool active, false);
            return active;
        }

        private static void Measure(Action pAction)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try { pAction(); }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.WartimeGarrisonIndex,
                    benchmark);
            }
        }

        private static int PositiveModulo(int pValue, int pModulo)
        {
            if (pModulo <= 0) return 0;
            int value = pValue % pModulo;
            return value < 0 ? value + pModulo : value;
        }

        private static Actor ResolveActor(long pId)
        {
            try { return pId >= 0 ? World.world?.units?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            try { return pId >= 0 ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }
    }
}
