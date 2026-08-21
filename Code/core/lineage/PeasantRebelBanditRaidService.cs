using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditRaidService
    {
        private static readonly ResType[] FoodResourceTypes =
        {
            ResType.Food,
            ResType.Ingredient_Food
        };

        private static int _authorityCursor;

        internal static void ResetRuntime()
        {
            _authorityCursor = 0;
        }

        internal static void ScheduleYear(Kingdom pKingdom)
        {
            if (!CanMutate() || pKingdom?.data == null ||
                !PeasantRebelBanditStateStore.TryResolveActive(pKingdom,
                    out PeasantRebelBanditStrongholdState state)) return;

            int currentYear = Date.getCurrentYear();
            if (PruneSuppressionRights(state, currentYear) &&
                !PeasantRebelBanditStateStore.Write(pKingdom, state))
                return;
            if (state.Raid.Stage == BanditRaidStage.Cooldown)
            {
                if (!PeasantRebelBanditRaidRules.CooldownExpired(
                        currentYear, state.Raid.CooldownUntilYear)) return;
                ClearMission(state.Raid, BanditRaidStage.None);
                if (!PeasantRebelBanditStateStore.Write(pKingdom, state))
                    return;
            }
            if (state.Raid.Stage != BanditRaidStage.None) return;

            City stronghold = ResolveCity(state.StrongholdCityId);
            if (stronghold?.kingdom != pKingdom) return;
            int strongholdFood = SafeFood(stronghold);
            int strongholdPopulation = SafePopulation(stronghold);
            if (!PeasantRebelBanditRaidRules.NeedsRaid(strongholdFood,
                    strongholdPopulation)) return;

            Dictionary<long, City> targets = BuildTargets(pKingdom,
                stronghold, strongholdFood, strongholdPopulation,
                out List<BanditRaidCandidate> candidates);
            BanditRaidCandidate selected =
                PeasantRebelBanditRaidRules.RankTargets(candidates).
                    FirstOrDefault();
            if (selected == null || !targets.TryGetValue(selected.CityId,
                    out City target)) return;

            List<Actor> party = SelectParty(pKingdom, stronghold);
            int partySize = PeasantRebelBanditRaidRules.PartySize(
                party.Count);
            if (partySize <= 0) return;
            party = party.Take(partySize).ToList();
            WorldTile destination = target.getTile();
            if (destination == null) return;

            state.Raid.Stage = BanditRaidStage.Outbound;
            state.Raid.MemberActorIds = party.Select(actor => actor.getID()).
                ToList();
            state.Raid.LeaderActorId = party[0].getID();
            state.Raid.TargetCityId = target.getID();
            state.Raid.TargetX = destination.x;
            state.Raid.TargetY = destination.y;
            state.Raid.CarriedFood = 0;
            state.Raid.CarriedFoodByResourceId.Clear();
            state.Raid.CarriedFoodByActorId.Clear();
            state.Raid.LastRouteDistance = selected.RouteDistance;
            if (!PeasantRebelBanditStateStore.Write(pKingdom, state))
                return;
            MoveParty(party, destination);
        }

        internal static void ProcessAuthorityCycle()
        {
            if (!CanMutate() || World.world?.kingdoms == null) return;
            List<Kingdom> kingdoms = World.world.kingdoms.ToList();
            if (kingdoms.Count == 0)
            {
                _authorityCursor = 0;
                return;
            }
            if (_authorityCursor >= kingdoms.Count) _authorityCursor = 0;
            int inspected = 0;
            while (inspected < kingdoms.Count)
            {
                Kingdom kingdom = kingdoms[_authorityCursor];
                _authorityCursor = (_authorityCursor + 1) % kingdoms.Count;
                inspected++;
                if (kingdom?.data == null || kingdom.isRekt() ||
                    !PeasantRebelBanditStateStore.TryResolveActive(kingdom,
                        out PeasantRebelBanditStrongholdState state) ||
                    state.Raid.Stage == BanditRaidStage.None) continue;
                ProcessMission(kingdom, state);
                return;
            }
        }

        private static void ProcessMission(Kingdom pKingdom,
            PeasantRebelBanditStrongholdState pState)
        {
            int currentYear = Date.getCurrentYear();
            if (pState.Raid.Stage == BanditRaidStage.Cooldown)
            {
                if (!PeasantRebelBanditRaidRules.CooldownExpired(
                        currentYear, pState.Raid.CooldownUntilYear)) return;
                ClearMission(pState.Raid, BanditRaidStage.None);
                PeasantRebelBanditStateStore.Write(pKingdom, pState);
                return;
            }

            City stronghold = ResolveCity(pState.StrongholdCityId);
            List<Actor> survivors = ResolveSurvivors(pKingdom, pState.Raid);
            if (survivors.Count == 0)
            {
                pState.Raid.CarriedFood = 0;
                pState.Raid.CarriedFoodByResourceId.Clear();
                pState.Raid.CarriedFoodByActorId.Clear();
                BeginCooldown(pKingdom, pState, currentYear);
                return;
            }

            switch (pState.Raid.Stage)
            {
                case BanditRaidStage.Outbound:
                {
                    City target = ResolveCity(pState.Raid.TargetCityId);
                    if (!IsValidTarget(pKingdom, stronghold, target))
                    {
                        BeginReturn(pKingdom, pState, stronghold, survivors);
                        return;
                    }
                    if (survivors.Any(actor => IsInside(actor, target)))
                    {
                        if (!TryLoot(pKingdom, pState, stronghold, target))
                            BeginReturn(pKingdom, pState, stronghold,
                                survivors);
                        return;
                    }
                    WorldTile targetTile = ResolveDestination(pState.Raid,
                        target);
                    if (targetTile != null) MoveParty(survivors, targetTile);
                    else BeginReturn(pKingdom, pState, stronghold, survivors);
                    return;
                }
                case BanditRaidStage.Looted:
                    BeginReturn(pKingdom, pState, stronghold, survivors);
                    return;
                case BanditRaidStage.Returning:
                    if (stronghold?.data == null || stronghold.isRekt())
                    {
                        BeginCooldown(pKingdom, pState, currentYear);
                        return;
                    }
                    if (PruneLostCargoAudit(pKingdom, pState.Raid) &&
                        !PeasantRebelBanditStateStore.Write(pKingdom,
                            pState)) return;
                    if (pState.Raid.CarriedFoodByActorId.Count == 0)
                    {
                        BeginCooldown(pKingdom, pState, currentYear);
                        return;
                    }
                    if (survivors.Any(actor => IsInside(actor, stronghold)))
                    {
                        DeliverFoodAndBeginCooldown(pKingdom, pState,
                            stronghold, currentYear);
                        return;
                    }
                    MoveParty(survivors, stronghold.getTile());
                    return;
                default:
                    BeginCooldown(pKingdom, pState, currentYear);
                    return;
            }
        }

        private static Dictionary<long, City> BuildTargets(Kingdom pBandit,
            City pStronghold, int pStrongholdFood,
            int pStrongholdPopulation,
            out List<BanditRaidCandidate> pCandidates)
        {
            var targets = new Dictionary<long, City>();
            pCandidates = new List<BanditRaidCandidate>();
            if (World.world?.cities == null) return targets;
            WorldTile origin = pStronghold.getTile();
            foreach (City city in World.world.cities.ToList())
            {
                if (city?.data == null || city.isRekt() ||
                    city == pStronghold || city.kingdom?.data == null ||
                    city.kingdom == pBandit) continue;
                bool islandBandit = PeasantRebelBanditStateStore.TryRead(
                    pBandit, out PeasantRebelBanditStrongholdState banditState) &&
                    banditState.StrongholdKind == BanditStrongholdKind.Island;
                bool coastal = !islandBandit || IsCoastalCity(city);
                bool reachable = false;
                try { reachable = city.reachableFrom(pStronghold); }
                catch { }
                bool allied = IsAllied(pBandit, city.kingdom);
                bool stronghold = IsActiveStronghold(city);
                int stealable = PeasantRebelBanditRaidRules.StealableFood(
                    pStrongholdFood, pStrongholdPopulation, SafeFood(city),
                    SafePopulation(city));
                int distance = TileDistance(origin, city.getTile());
                var candidate = new BanditRaidCandidate(city.getID(),
                    distance, stealable, reachable, allied, stronghold);
                if (!PeasantRebelBanditIslandRules.IsEligiblePiracyTarget(
                        coastal, reachable, allied, stronghold, stealable))
                    continue;
                pCandidates.Add(candidate);
                targets[city.getID()] = city;
            }
            return targets;
        }

        private static bool IsCoastalCity(City pCity)
        {
            if (pCity?.data == null) return false;
            try
            {
                foreach (TileZone zone in pCity.zones)
                foreach (WorldTile tile in zone?.tiles ??
                         new List<WorldTile>())
                foreach (WorldTile neighbour in tile?.neighboursAll ??
                         Array.Empty<WorldTile>())
                    if (neighbour?.data != null && !neighbour.Type.ground)
                        return true;
            }
            catch { }
            return false;
        }

        private static List<Actor> SelectParty(Kingdom pKingdom,
            City pStronghold)
        {
            if (pStronghold?.units == null) return new List<Actor>();
            return pStronghold.units.Where(actor => actor?.data != null &&
                    !actor.isRekt() && actor.kingdom == pKingdom &&
                    PeasantRebelBanditRaidRules.CanJoinRaid(
                        actor.isAlive(), actor.isWarrior(), actor.isKing(),
                        HeirService.IsCurrentHeir(pKingdom, actor),
                        actor.isCarryingResources()))
                .OrderByDescending(actor => GeneralService.IsGeneral(actor))
                .ThenBy(actor => actor.getID()).ToList();
        }

        private static void BeginReturn(Kingdom pKingdom,
            PeasantRebelBanditStrongholdState pState, City pStronghold,
            List<Actor> pSurvivors)
        {
            if (pStronghold?.data == null || pStronghold.isRekt())
            {
                BeginCooldown(pKingdom, pState, Date.getCurrentYear());
                return;
            }
            WorldTile destination = pStronghold.getTile();
            if (destination == null)
            {
                BeginCooldown(pKingdom, pState, Date.getCurrentYear());
                return;
            }
            pState.Raid.Stage = BanditRaidStage.Returning;
            pState.Raid.TargetX = destination.x;
            pState.Raid.TargetY = destination.y;
            if (!PeasantRebelBanditStateStore.Write(pKingdom, pState))
                return;
            MoveParty(pSurvivors, destination);
        }

        private static void BeginCooldown(Kingdom pKingdom,
            PeasantRebelBanditStrongholdState pState, int pCurrentYear)
        {
            ClearMission(pState.Raid, BanditRaidStage.Cooldown);
            pState.Raid.CooldownUntilYear = pCurrentYear + 1;
            PeasantRebelBanditStateStore.Write(pKingdom, pState);
        }

        private static void ClearMission(BanditRaidMissionState pRaid,
            BanditRaidStage pStage)
        {
            pRaid.Stage = pStage;
            pRaid.MemberActorIds.Clear();
            pRaid.LeaderActorId = -1L;
            pRaid.TargetCityId = -1L;
            pRaid.TargetX = 0;
            pRaid.TargetY = 0;
            pRaid.CarriedFood = 0;
            pRaid.CarriedFoodByResourceId.Clear();
            pRaid.CarriedFoodByActorId.Clear();
            pRaid.LastRouteDistance = 0;
        }

        private static bool TryLoot(Kingdom pBandit,
            PeasantRebelBanditStrongholdState pState, City pStronghold,
            City pTarget)
        {
            int requested = PeasantRebelBanditRaidRules.StealableFood(
                SafeFood(pStronghold), SafePopulation(pStronghold),
                SafeFood(pTarget), SafePopulation(pTarget));
            if (requested <= 0) return false;

            Dictionary<string, int> plan = BuildFoodCargo(pTarget,
                requested);
            if (plan.Count == 0) return false;
            List<Actor> carriers = ResolveSurvivors(pBandit, pState.Raid)
                .Where(actor => IsInside(actor, pTarget) &&
                                !actor.isCarryingResources())
                .OrderBy(actor => actor.getID()).ToList();
            if (carriers.Count == 0) return false;
            var observed = plan.Keys.ToDictionary(id => id,
                id => SafeResourceAmount(pTarget, id));
            var removed = new Dictionary<string, int>();
            var manifest = new Dictionary<long,
                Dictionary<string, int>>();
            int totalRemoved = 0;
            bool durable = false;
            long victimId = pTarget.kingdom.getID();
            bool hadExpiry = pState.SuppressionExpiryByKingdomId.
                TryGetValue(victimId, out int previousExpiry);
            try
            {
                foreach (KeyValuePair<string, int> item in plan)
                {
                    int before = SafeResourceAmount(pTarget, item.Key);
                    pTarget.takeResource(item.Key, item.Value);
                    int actual = Math.Max(0, before -
                        SafeResourceAmount(pTarget, item.Key));
                    if (actual <= 0) continue;
                    removed[item.Key] = actual;
                    totalRemoved += actual;
                }
                if (totalRemoved <= 0) return false;

                var carriersById = carriers.ToDictionary(
                    actor => actor.getID());
                foreach (KeyValuePair<string, int> item in removed)
                {
                    IReadOnlyDictionary<long, int> shares =
                        PeasantRebelBanditRaidRules.DistributeCargo(
                            carriersById.Keys, item.Value);
                    foreach (KeyValuePair<long, int> share in shares)
                    {
                        Actor carrier = carriersById[share.Key];
                        carrier.addToInventory(item.Key, share.Value);
                        if (!manifest.TryGetValue(share.Key,
                                out Dictionary<string, int> cargo))
                        {
                            cargo = new Dictionary<string, int>(
                                StringComparer.Ordinal);
                            manifest[share.Key] = cargo;
                        }
                        cargo[item.Key] = share.Value;
                    }
                }
                if (manifest.Count == 0)
                    throw new InvalidOperationException(
                        "native cargo distribution produced no carriers");

                pState.Raid.CarriedFoodByResourceId = removed;
                pState.Raid.CarriedFoodByActorId = manifest;
                pState.Raid.CarriedFood = totalRemoved;
                int expiry = PeasantRebelBanditRaidRules.
                    SuppressionExpiryYear(Date.getCurrentYear());
                pState.SuppressionExpiryByKingdomId[victimId] =
                    Math.Max(previousExpiry, expiry);
                pState.Raid.Stage = BanditRaidStage.Looted;
                durable = PeasantRebelBanditStateStore.Write(pBandit,
                    pState);
                if (durable) return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Bandit raid loot failed: " +
                                    error.Message);
            }

            if (!durable)
            {
                RemoveAssignedCargo(manifest, carriers);
                RestoreObservedInventory(pTarget, observed);
            }
            pState.Raid.Stage = BanditRaidStage.Outbound;
            pState.Raid.CarriedFood = 0;
            pState.Raid.CarriedFoodByResourceId.Clear();
            pState.Raid.CarriedFoodByActorId.Clear();
            if (hadExpiry)
                pState.SuppressionExpiryByKingdomId[victimId] =
                    previousExpiry;
            else pState.SuppressionExpiryByKingdomId.Remove(victimId);
            return false;
        }

        private static Dictionary<string, int> BuildFoodCargo(City pTarget,
            int pRequested)
        {
            var cargo = new Dictionary<string, int>(
                StringComparer.Ordinal);
            int remaining = Math.Max(0, pRequested);
            if (pTarget?.data == null || remaining == 0) return cargo;
            using (ListPool<CityStorageSlot> slots =
                   pTarget.getTotalResourceSlots(FoodResourceTypes))
            {
                foreach (CityStorageSlot slot in slots)
                {
                    ResourceAsset resource = slot?.asset;
                    if (remaining <= 0) break;
                    if (resource == null || !resource.food ||
                        slot.amount <= 0) continue;
                    int amount = Math.Min(remaining, slot.amount);
                    cargo[resource.id] = amount;
                    remaining -= amount;
                }
            }
            return cargo;
        }

        private static void DeliverFoodAndBeginCooldown(Kingdom pBandit,
            PeasantRebelBanditStrongholdState pState, City pStronghold,
            int pCurrentYear)
        {
            List<long> resolved = new List<long>();
            foreach (long actorId in pState.Raid.CarriedFoodByActorId.Keys.
                         OrderBy(id => id).ToList())
            {
                Actor carrier = ResolveActor(actorId);
                if (carrier?.data == null || carrier.isRekt() ||
                    !carrier.isAlive() || carrier.kingdom != pBandit)
                {
                    resolved.Add(actorId);
                    continue;
                }
                if (!IsInside(carrier, pStronghold)) continue;
                carrier.giveInventoryResourcesToCity();
                resolved.Add(actorId);
            }
            foreach (long actorId in resolved)
                pState.Raid.CarriedFoodByActorId.Remove(actorId);
            if (pState.Raid.CarriedFoodByActorId.Count > 0)
            {
                PeasantRebelBanditStateStore.Write(pBandit, pState);
                return;
            }
            BeginCooldown(pBandit, pState, pCurrentYear);
        }

        private static void RemoveAssignedCargo(
            Dictionary<long, Dictionary<string, int>> pManifest,
            IEnumerable<Actor> pCarriers)
        {
            if (pManifest == null || pCarriers == null) return;
            Dictionary<long, Actor> carriers = pCarriers
                .Where(actor => actor?.data != null)
                .GroupBy(actor => actor.getID())
                .ToDictionary(group => group.Key, group => group.First());
            foreach (KeyValuePair<long, Dictionary<string, int>> actorCargo
                     in pManifest)
            {
                if (!carriers.TryGetValue(actorCargo.Key,
                        out Actor carrier)) continue;
                foreach (KeyValuePair<string, int> item in actorCargo.Value)
                    carrier.takeFromInventory(item.Key,
                        Math.Max(0, item.Value));
            }
        }

        private static bool PruneLostCargoAudit(Kingdom pBandit,
            BanditRaidMissionState pRaid)
        {
            if (pRaid?.CarriedFoodByActorId == null) return false;
            List<long> lost = pRaid.CarriedFoodByActorId.Keys.Where(id =>
            {
                Actor actor = ResolveActor(id);
                return actor?.data == null || actor.isRekt() ||
                       !actor.isAlive() || actor.kingdom != pBandit;
            }).ToList();
            foreach (long actorId in lost)
                pRaid.CarriedFoodByActorId.Remove(actorId);
            return lost.Count > 0;
        }

        private static void RestoreObservedInventory(City pCity,
            Dictionary<string, int> pObserved)
        {
            if (pCity?.data == null || pObserved == null) return;
            foreach (KeyValuePair<string, int> item in pObserved)
            {
                int current = SafeResourceAmount(pCity, item.Key);
                if (current < item.Value)
                    pCity.addResourcesToRandomStockpile(item.Key,
                        item.Value - current);
                else if (current > item.Value)
                    pCity.takeResource(item.Key, current - item.Value);
            }
        }

        private static bool PruneSuppressionRights(
            PeasantRebelBanditStrongholdState pState, int pCurrentYear)
        {
            if (pState?.SuppressionExpiryByKingdomId == null) return false;
            List<long> expired = pState.SuppressionExpiryByKingdomId.
                Where(item => item.Value <= pCurrentYear).
                Select(item => item.Key).ToList();
            foreach (long kingdomId in expired)
                pState.SuppressionExpiryByKingdomId.Remove(kingdomId);
            return expired.Count > 0;
        }

        private static List<Actor> ResolveSurvivors(Kingdom pKingdom,
            BanditRaidMissionState pRaid)
        {
            var survivors = new List<Actor>();
            if (World.world?.units == null ||
                pRaid?.MemberActorIds == null) return survivors;
            var seen = new HashSet<long>();
            foreach (long actorId in pRaid.MemberActorIds)
            {
                if (actorId <= 0 || !seen.Add(actorId)) continue;
                Actor actor = null;
                try { actor = World.world.units.get(actorId); }
                catch { }
                if (actor?.data == null || actor.isRekt() ||
                    !actor.isAlive() || actor.kingdom != pKingdom) continue;
                survivors.Add(actor);
            }
            return survivors;
        }

        private static WorldTile ResolveDestination(
            BanditRaidMissionState pRaid, City pFallback)
        {
            try
            {
                WorldTile tile = World.world?.GetTile(pRaid.TargetX,
                    pRaid.TargetY);
                return tile ?? pFallback?.getTile();
            }
            catch { return pFallback?.getTile(); }
        }

        private static void MoveParty(IEnumerable<Actor> pParty,
            WorldTile pDestination)
        {
            if (pDestination == null) return;
            foreach (Actor actor in pParty)
            {
                if (actor?.data == null || actor.isRekt()) continue;
                try
                {
                    actor.goTo(pDestination, pLimitPathfindingRegions: 6);
                }
                catch { }
            }
        }

        private static bool IsValidTarget(Kingdom pBandit,
            City pStronghold, City pTarget)
        {
            if (pTarget?.data == null || pTarget.isRekt() ||
                pTarget.kingdom?.data == null ||
                pTarget.kingdom == pBandit || IsAllied(pBandit,
                    pTarget.kingdom) || IsActiveStronghold(pTarget))
                return false;
            try { return pTarget.reachableFrom(pStronghold); }
            catch { return false; }
        }

        private static bool IsAllied(Kingdom pKingdom, Kingdom pOther)
        {
            try
            {
                Alliance alliance = pKingdom.getAlliance();
                return alliance != null && alliance.hasKingdom(pOther);
            }
            catch { return false; }
        }

        private static bool IsActiveStronghold(City pCity)
        {
            return pCity?.kingdom?.data != null &&
                   PeasantRebelBanditStateStore.TryResolveActive(
                       pCity.kingdom,
                       out PeasantRebelBanditStrongholdState state) &&
                   state.StrongholdCityId == pCity.getID();
        }

        private static bool IsInside(Actor pActor, City pCity)
        {
            try { return pActor?.current_tile?.zone?.city == pCity; }
            catch { return false; }
        }

        private static City ResolveCity(long pCityId)
        {
            if (pCityId <= 0 || World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(pCityId);
                return city?.data != null && !city.isRekt() ? city : null;
            }
            catch { return null; }
        }

        private static Actor ResolveActor(long pActorId)
        {
            if (pActorId <= 0 || World.world?.units == null) return null;
            try
            {
                Actor actor = World.world.units.get(pActorId);
                return actor?.data != null ? actor : null;
            }
            catch { return null; }
        }

        private static int SafeFood(City pCity)
        {
            try { return Math.Max(0, pCity?.countFoodTotal() ?? 0); }
            catch { return 0; }
        }

        private static int SafeResourceAmount(City pCity,
            string pResourceId)
        {
            if (pCity?.data == null ||
                string.IsNullOrWhiteSpace(pResourceId)) return 0;
            try
            {
                return Math.Max(0,
                    pCity.getResourcesAmount(pResourceId));
            }
            catch { return 0; }
        }

        private static int SafePopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static int TileDistance(WorldTile pLeft, WorldTile pRight)
        {
            if (pLeft == null || pRight == null) return int.MaxValue;
            return Math.Abs(pLeft.x - pRight.x) +
                   Math.Abs(pLeft.y - pRight.y);
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
