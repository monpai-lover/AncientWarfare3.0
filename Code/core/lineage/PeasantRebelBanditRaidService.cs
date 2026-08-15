using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditRaidService
    {
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
                        pState.Raid.Stage = BanditRaidStage.Looted;
                        PeasantRebelBanditStateStore.Write(pKingdom, pState);
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
                    if (survivors.Any(actor => IsInside(actor, stronghold)))
                    {
                        BeginCooldown(pKingdom, pState, currentYear);
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
                pCandidates.Add(candidate);
                targets[city.getID()] = city;
            }
            return targets;
        }

        private static List<Actor> SelectParty(Kingdom pKingdom,
            City pStronghold)
        {
            if (pStronghold?.units == null) return new List<Actor>();
            return pStronghold.units.Where(actor => actor?.data != null &&
                    !actor.isRekt() && actor.isAlive() && actor.isWarrior() &&
                    actor.kingdom == pKingdom && !actor.isKing() &&
                    !HeirService.IsCurrentHeir(pKingdom, actor))
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
            pRaid.LastRouteDistance = 0;
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

        private static int SafeFood(City pCity)
        {
            try { return Math.Max(0, pCity?.countFoodTotal() ?? 0); }
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
