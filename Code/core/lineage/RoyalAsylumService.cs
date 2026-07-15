using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal static class RoyalAsylumService
    {
        private const int MaxRosterSize = 64;
        private static readonly Dictionary<long, long> HomeKingdomByActorId =
            new Dictionary<long, long>();

        public static bool IsActive(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_ACTIVE, out bool active, false);
            return active;
        }

        public static void ClearRuntime()
        {
            HomeKingdomByActorId.Clear();
            RoyalAsylumVenueService.Clear();
        }

        public static void LoadRuntimeState()
        {
            ClearRuntime();
            KingdomManager kingdoms = World.world?.kingdoms;
            if (kingdoms == null) return;
            foreach (Kingdom home in kingdoms)
            {
                if (home?.data == null) continue;
                List<long> valid = new List<long>();
                foreach (long actorId in ReadRoster(home))
                {
                    Actor actor = ResolveActor(actorId);
                    if (!IsActiveForHome(actor, home)) continue;
                    HomeKingdomByActorId[actorId] = home.id;
                    valid.Add(actorId);
                    EnsurePresentation(actor);
                }
                WriteRoster(home, valid);
            }
        }

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null) return;
            var participantIds = new HashSet<long>();
            try
            {
                foreach (Kingdom attacker in pWar.getAttackers())
                    if (attacker?.data != null) participantIds.Add(attacker.id);
                foreach (Kingdom defender in pWar.getDefenders())
                {
                    if (defender?.data == null) continue;
                    participantIds.Add(defender.id);
                    OnKingdomYear(defender);
                }
            }
            catch { return; }
            RelocateFromNewWarParticipants(participantIds);
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            try
            {
                foreach (Kingdom defender in pWar.getDefenders())
                    if (defender?.data != null) OnKingdomYear(defender);
            }
            catch { }
        }

        public static void OnKingdomYear(Kingdom pHome)
        {
            if (pHome?.data == null || pHome.isNeutral() || pHome.wild) return;
            bool homeAlive = IsLivingKingdom(pHome);
            bool hasDefensiveWar = homeAlive && HasActiveDefensiveWar(pHome);
            bool monarchy = homeAlive && !RepublicGovernmentService.IsRepublic(pHome) &&
                            IsLivingActor(pHome.king);
            Actor heir = monarchy ? HeirService.PeekRegisteredHeir(pHome) : null;
            List<Actor> protectedFamily = monarchy
                ? CollectProtectedFamily(pHome, pHome.king, heir)
                : new List<Actor>();
            var protectedIds = new HashSet<long>();
            for (int i = 0; i < protectedFamily.Count; i++)
                protectedIds.Add(protectedFamily[i].data.id);

            List<long> retained = new List<long>();
            foreach (long actorId in ReadRoster(pHome))
            {
                Actor actor = ResolveActor(actorId);
                if (!IsActiveForHome(actor, pHome))
                {
                    HomeKingdomByActorId.Remove(actorId);
                    continue;
                }
                if (RoyalAsylumRules.ShouldReturn(homeAlive, hasDefensiveWar) ||
                    !protectedIds.Contains(actorId))
                {
                    if (!TryReturn(actor, pHome)) retained.Add(actorId);
                    continue;
                }
                if (HostNeedsRelocation(actor))
                {
                    if (TryRelocateOrReturn(actor, pHome)) retained.Add(actorId);
                    continue;
                }
                EnsurePresentation(actor);
                retained.Add(actorId);
            }
            WriteRoster(pHome, retained);

            if (!homeAlive || !monarchy || !hasDefensiveWar) return;
            for (int i = 0; i < protectedFamily.Count; i++)
            {
                Actor actor = protectedFamily[i];
                if (IsActive(actor)) continue;
                TryEvacuate(actor, pHome);
            }
        }

        public static void NaturalizeBeforeExtinction(Kingdom pHome)
        {
            if (pHome?.data == null) return;
            List<long> retained = new List<long>();
            foreach (long actorId in ReadRoster(pHome))
            {
                Actor actor = ResolveActor(actorId);
                if (!IsActiveForHome(actor, pHome))
                {
                    HomeKingdomByActorId.Remove(actorId);
                    continue;
                }

                City hostCity = ResolveHostCity(actor);
                Kingdom host = hostCity?.kingdom;
                bool validRecordedHost = IsLivingKingdom(host) &&
                                         IsLivingCity(hostCity) &&
                                         hostCity.kingdom == host && host != pHome;
                if (!validRecordedHost)
                {
                    WorldTile origin = actor.current_tile ?? HomeOriginTile(pHome);
                    if (!TrySelectHost(pHome, origin, actorId, pExcludeHostId: -1L,
                            out host, out hostCity, out _))
                    {
                        CloseBeforeNomadFallback(actor, pHome);
                        continue;
                    }
                }

                if (!RoyalAsylumRules.ShouldNaturalize(
                        homeRealmAlive: false,
                        hostCityValid: IsLivingCity(hostCity) && hostCity.kingdom == host))
                {
                    CloseBeforeNomadFallback(actor, pHome);
                    continue;
                }
                actor.data.get(LineageKeys.ROYAL_ASYLUM_HOME_KINGDOM_NAME,
                    out string homeName, pHome.name ?? "");
                try
                {
                    actor.cancelAllBeh();
                    actor.joinCity(hostCity);
                }
                catch
                {
                    CloseBeforeNomadFallback(actor, pHome);
                    continue;
                }
                if (actor.city != hostCity || actor.kingdom != host)
                {
                    CloseBeforeNomadFallback(actor, pHome);
                    continue;
                }
                RoyalAsylumHistoryService.RecordNaturalized(actor, homeName, host, hostCity);
                ClearActorState(actor, pHome);
                pHome.units.Remove(actor);
            }
            WriteRoster(pHome, retained);
        }

        private static void CloseBeforeNomadFallback(Actor pActor, Kingdom pHome)
        {
            if (pActor?.data == null) return;
            ClearActorState(pActor, pHome);
            if (pActor.kingdom != pHome)
                pHome.units.Remove(pActor);
        }

        public static bool TryGetRoamTile(Actor pActor, out WorldTile pTile)
        {
            pTile = null;
            if (!IsActive(pActor) || !IsLivingActor(pActor)) return false;
            City hostCity = ResolveHostCity(pActor);
            if (!IsLivingCity(hostCity)) return false;
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_HOST_KINGDOM_ID,
                out long hostKingdomId, -1L);
            if (hostCity.kingdom?.data == null || hostCity.kingdom.id != hostKingdomId)
                return false;
            return RoyalAsylumVenueService.TryPick(hostCity, pActor.data.id,
                Date.getCurrentYear(), out pTile);
        }

        public static City ResolveHostCity(Actor pActor)
        {
            if (pActor?.data == null) return null;
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_HOST_CITY_ID,
                out long hostCityId, -1L);
            return ResolveCity(hostCityId);
        }

        public static bool RecallForSuccession(Actor pActor, Kingdom pHome)
        {
            if (!IsActive(pActor)) return true;
            if (pActor?.data == null || pHome?.data == null) return false;
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_HOME_KINGDOM_ID,
                out long homeKingdomId, -1L);
            if (homeKingdomId != pHome.id || pActor.kingdom != pHome) return false;
            return TryReturn(pActor, pHome);
        }

        private static List<Actor> CollectProtectedFamily(Kingdom pHome, Actor pKing,
            Actor pHeir)
        {
            var result = new List<Actor>();
            var seen = new HashSet<long>();
            AddChildren(result, seen, pHome, pKing, pKing, pHeir,
                pKingsChildren: true);
            AddChildren(result, seen, pHome, pHeir, pKing, pHeir,
                pKingsChildren: false);
            return result;
        }

        private static void AddChildren(List<Actor> pResult, HashSet<long> pSeen,
            Kingdom pHome, Actor pParent, Actor pKing, Actor pHeir,
            bool pKingsChildren)
        {
            if (pParent?.data == null) return;
            try
            {
                foreach (Actor child in pParent.getChildren(pOnlyCurrentFamily: false))
                {
                    if (child?.data == null || !pSeen.Add(child.data.id)) continue;
                    bool actorIsKing = child == pKing || SafeIsKing(child);
                    bool actorIsForeignKing = actorIsKing && child.kingdom != pHome;
                    if (!RoyalAsylumRules.IsProtectedFamilyCandidate(
                            homeAlive: IsLivingKingdom(pHome),
                            monarchy: !RepublicGovernmentService.IsRepublic(pHome),
                            actorAlive: IsLivingActor(child),
                            actorBelongsToHome: child.kingdom == pHome,
                            actorIsSlave: SlaveService.IsSlave(child),
                            actorIsForeignKing: actorIsForeignKing,
                            actorIsKing: actorIsKing,
                            actorIsCurrentHeir: child == pHeir,
                            isKingsDirectChild: pKingsChildren,
                            isHeirsDirectChild: !pKingsChildren)) continue;
                    pResult.Add(child);
                }
            }
            catch { }
        }

        private static bool TryEvacuate(Actor pActor, Kingdom pHome)
        {
            if (pActor?.data == null || pHome?.data == null || IsActive(pActor) ||
                pActor.kingdom != pHome) return false;
            List<long> roster = ReadRoster(pHome);
            if (!roster.Contains(pActor.data.id) && roster.Count >= MaxRosterSize)
                return false;
            WorldTile origin = pActor.current_tile ?? HomeOriginTile(pHome);
            if (!TrySelectHost(pHome, origin, pActor.data.id, pExcludeHostId: -1L,
                    out Kingdom host, out City hostCity, out WorldTile hostTile))
                return false;
            if (!RoyalAsylumRules.ShouldEvacuate(
                    homeAlive: IsLivingKingdom(pHome),
                    monarchy: !RepublicGovernmentService.IsRepublic(pHome),
                    hasDefensiveWar: HasActiveDefensiveWar(pHome),
                    hostAvailable: host?.data != null && hostCity?.data != null))
                return false;

            City formerCity = pActor.city;
            PrepareForEvacuation(pActor);
            pActor.setCity(null);
            if (pActor.kingdom != pHome)
            {
                if (IsLivingCity(formerCity) && formerCity.kingdom == pHome)
                    pActor.joinCity(formerCity);
                return false;
            }
            try { pActor.spawnOn(hostTile); }
            catch
            {
                if (IsLivingCity(formerCity) && formerCity.kingdom == pHome)
                    pActor.joinCity(formerCity);
                return false;
            }

            int year = Date.getCurrentYear();
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_ACTIVE, true);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOME_KINGDOM_ID, pHome.id);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOME_KINGDOM_NAME, pHome.name ?? "");
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_FORMER_CITY_ID,
                formerCity?.data?.id ?? -1L);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOST_KINGDOM_ID, host.id);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOST_CITY_ID, hostCity.data.id);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_START_YEAR, year);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_LAST_RELOCATION_YEAR, year);
            HomeKingdomByActorId[pActor.data.id] = pHome.id;
            AddToRoster(pHome, pActor.data.id);
            EnsurePresentation(pActor);
            RoyalAsylumHistoryService.RecordStarted(pActor, pHome, hostCity);
            return true;
        }

        private static void PrepareForEvacuation(Actor pActor)
        {
            if (pActor?.data == null) return;
            City city = pActor.city;
            if (city?.leader == pActor) city.removeLeader();
            CourtService.ClearOfficeForReignTransition(pActor, "royal_asylum");
            GeneralService.SuspendForAsylum(pActor);
            if (RoyalGuardService.IsRoyalGuard(pActor))
                RoyalGuardService.DismissGuard(pActor, "royal_asylum");
            try { if (pActor.hasArmy()) pActor.removeFromArmy(); } catch { }
            try { if (pActor.isWarrior()) pActor.stopBeingWarrior(); } catch { }
            pActor.cancelAllBeh();
        }

        private static bool TryRelocateOrReturn(Actor pActor, Kingdom pHome)
        {
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_HOST_KINGDOM_ID,
                out long oldHostId, -1L);
            WorldTile origin = pActor.current_tile ?? HomeOriginTile(pHome);
            if (TrySelectHost(pHome, origin, pActor.data.id, oldHostId,
                    out Kingdom host, out City hostCity, out WorldTile hostTile))
            {
                try { pActor.spawnOn(hostTile); }
                catch { return TryReturn(pActor, pHome); }
                pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOST_KINGDOM_ID, host.id);
                pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOST_CITY_ID, hostCity.data.id);
                pActor.data.set(LineageKeys.ROYAL_ASYLUM_LAST_RELOCATION_YEAR,
                    Date.getCurrentYear());
                EnsurePresentation(pActor);
                RoyalAsylumHistoryService.RecordRelocated(pActor, pHome, hostCity);
                return true;
            }
            if (TryReturn(pActor, pHome)) return false;
            EnsurePresentation(pActor);
            return true;
        }

        private static bool TryReturn(Actor pActor, Kingdom pHome)
        {
            City destination = FindReturnCity(pActor, pHome);
            if (!IsLivingCity(destination) || destination.kingdom != pHome) return false;
            try
            {
                pActor.cancelAllBeh();
                pActor.joinCity(destination);
            }
            catch { return false; }
            if (pActor.city != destination || pActor.kingdom != pHome) return false;
            RoyalAsylumHistoryService.RecordReturned(pActor, pHome, destination);
            ClearActorState(pActor, pHome);
            return true;
        }

        private static bool HostNeedsRelocation(Actor pActor)
        {
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_HOST_KINGDOM_ID,
                out long hostKingdomId, -1L);
            Kingdom host = ResolveKingdom(hostKingdomId);
            City city = ResolveHostCity(pActor);
            bool hostAlive = IsLivingKingdom(host);
            bool cityAlive = IsLivingCity(city);
            bool cityOwned = cityAlive && city.kingdom == host;
            bool hostAtWar = hostAlive && HasAnyWar(host);
            return RoyalAsylumRules.ShouldRelocate(IsActive(pActor), hostAlive,
                cityAlive, cityOwned, hostAtWar);
        }

        private static bool TrySelectHost(Kingdom pHome, WorldTile pOrigin,
            long pActorId, long pExcludeHostId, out Kingdom pHost,
            out City pHostCity, out WorldTile pHostTile)
        {
            pHost = null;
            pHostCity = null;
            pHostTile = null;
            KingdomManager kingdoms = World.world?.kingdoms;
            if (kingdoms == null) return false;
            RoyalAsylumHostRank bestRank = default;
            bool found = false;
            foreach (Kingdom candidate in kingdoms)
            {
                if (candidate?.data == null || candidate.id == pExcludeHostId) continue;
                City city = StableHostCity(candidate);
                bool enemy;
                try { enemy = candidate != pHome && candidate.isEnemy(pHome); }
                catch { enemy = true; }
                if (!RoyalAsylumRules.IsHostEligible(
                        hostAlive: IsLivingKingdom(candidate),
                        hostCivilization: candidate.isCiv(),
                        hostIsForeign: candidate != pHome,
                        hostIsNeutral: candidate.isNeutral(),
                        hostIsWild: candidate.wild,
                        hostHasLivingCity: IsLivingCity(city),
                        hostAtWar: HasAnyWar(candidate),
                        hostIsEnemy: enemy)) continue;
                if (!RoyalAsylumVenueService.TryPick(city, pActorId,
                        Date.getCurrentYear(), out WorldTile tile)) continue;
                RoyalAsylumHostRank rank = new RoyalAsylumHostRank(
                    SameIsland(pOrigin, city.getTile()),
                    DistanceSquared(pOrigin, city.getTile()), candidate.id, city.data.id);
                if (found && rank.CompareTo(bestRank) >= 0) continue;
                found = true;
                bestRank = rank;
                pHost = candidate;
                pHostCity = city;
                pHostTile = tile;
            }
            return found;
        }

        private static City StableHostCity(Kingdom pKingdom)
        {
            if (IsLivingCity(pKingdom?.capital) && pKingdom.capital.kingdom == pKingdom)
                return pKingdom.capital;
            City best = null;
            try
            {
                foreach (City city in pKingdom?.getCities() ?? new List<City>())
                {
                    if (!IsLivingCity(city) || city.kingdom != pKingdom) continue;
                    if (best == null || city.data.id < best.data.id) best = city;
                }
            }
            catch { }
            return best;
        }

        private static City FindReturnCity(Actor pActor, Kingdom pHome)
        {
            if (pActor?.data == null || pHome?.data == null) return null;
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_FORMER_CITY_ID,
                out long formerCityId, -1L);
            City former = ResolveCity(formerCityId);
            if (IsLivingCity(former) && former.kingdom == pHome) return former;
            if (IsLivingCity(pHome.capital) && pHome.capital.kingdom == pHome)
                return pHome.capital;
            City best = null;
            long bestDistance = long.MaxValue;
            WorldTile origin = pActor.current_tile;
            try
            {
                foreach (City city in pHome.getCities())
                {
                    if (!IsLivingCity(city) || city.kingdom != pHome) continue;
                    long distance = DistanceSquared(origin, city.getTile());
                    if (best != null && (distance > bestDistance ||
                        distance == bestDistance && city.data.id >= best.data.id)) continue;
                    best = city;
                    bestDistance = distance;
                }
            }
            catch { }
            return best;
        }

        private static void RelocateFromNewWarParticipants(HashSet<long> pParticipantIds)
        {
            if (pParticipantIds == null || pParticipantIds.Count == 0 ||
                HomeKingdomByActorId.Count == 0) return;
            var actorIds = new List<long>(HomeKingdomByActorId.Keys);
            for (int i = 0; i < actorIds.Count; i++)
            {
                Actor actor = ResolveActor(actorIds[i]);
                if (!IsActive(actor)) continue;
                actor.data.get(LineageKeys.ROYAL_ASYLUM_HOST_KINGDOM_ID,
                    out long hostId, -1L);
                if (!pParticipantIds.Contains(hostId)) continue;
                Kingdom home = ResolveKingdom(HomeKingdomByActorId[actorIds[i]]);
                if (home?.data != null) TryRelocateOrReturn(actor, home);
            }
        }

        private static bool HasActiveDefensiveWar(Kingdom pHome)
        {
            WarManager wars = World.world?.wars;
            if (wars == null || pHome?.data == null) return false;
            try
            {
                foreach (War war in wars.getWars(pHome))
                    if (war?.data != null && !war.hasEnded() && war.isDefender(pHome))
                        return true;
            }
            catch { }
            return false;
        }

        private static bool HasAnyWar(Kingdom pKingdom)
        {
            try { return pKingdom?.data != null && World.world?.wars?.hasWars(pKingdom) == true; }
            catch { return true; }
        }

        private static void EnsurePresentation(Actor pActor)
        {
            if (!IsActive(pActor) || pActor.ai == null) return;
            try { pActor.addStatusEffect(RoyalAsylumContent.StatusId, 1000000f, pColorEffect: false); }
            catch { }
            try { pActor.ai.setJob(RoyalAsylumContent.ActorJobId); }
            catch { }
        }

        private static void ClearActorState(Actor pActor, Kingdom pHome)
        {
            if (pActor?.data == null) return;
            long actorId = pActor.data.id;
            pActor.finishStatusEffect(RoyalAsylumContent.StatusId);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_ACTIVE, false);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOME_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOME_KINGDOM_NAME, "");
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_FORMER_CITY_ID, -1L);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOST_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_HOST_CITY_ID, -1L);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_START_YEAR, -1);
            pActor.data.set(LineageKeys.ROYAL_ASYLUM_LAST_RELOCATION_YEAR, -1);
            HomeKingdomByActorId.Remove(actorId);
            RemoveFromRoster(pHome, actorId);
        }

        private static bool IsActiveForHome(Actor pActor, Kingdom pHome)
        {
            if (!IsActive(pActor) || !IsLivingActor(pActor) || pHome?.data == null)
                return false;
            pActor.data.get(LineageKeys.ROYAL_ASYLUM_HOME_KINGDOM_ID,
                out long homeId, -1L);
            return homeId == pHome.id && pActor.kingdom == pHome;
        }

        private static List<long> ReadRoster(Kingdom pHome)
        {
            var result = new List<long>(MaxRosterSize);
            if (pHome?.data == null) return result;
            pHome.data.get(LineageKeys.ROYAL_ASYLUM_ROSTER_IDS, out string raw, "");
            if (string.IsNullOrWhiteSpace(raw)) return result;
            var seen = new HashSet<long>();
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length && result.Count < MaxRosterSize; i++)
                if (long.TryParse(parts[i], out long actorId) && actorId >= 0 && seen.Add(actorId))
                    result.Add(actorId);
            return result;
        }

        private static void AddToRoster(Kingdom pHome, long pActorId)
        {
            List<long> ids = ReadRoster(pHome);
            if (!ids.Contains(pActorId) && ids.Count < MaxRosterSize) ids.Add(pActorId);
            WriteRoster(pHome, ids);
        }

        private static void RemoveFromRoster(Kingdom pHome, long pActorId)
        {
            List<long> ids = ReadRoster(pHome);
            if (ids.Remove(pActorId)) WriteRoster(pHome, ids);
        }

        private static void WriteRoster(Kingdom pHome, List<long> pActorIds)
        {
            if (pHome?.data == null) return;
            var unique = new HashSet<long>();
            var normalized = new List<long>(MaxRosterSize);
            if (pActorIds != null)
                for (int i = 0; i < pActorIds.Count && normalized.Count < MaxRosterSize; i++)
                    if (pActorIds[i] >= 0 && unique.Add(pActorIds[i]))
                        normalized.Add(pActorIds[i]);
            normalized.Sort();
            pHome.data.set(LineageKeys.ROYAL_ASYLUM_ROSTER_IDS,
                string.Join(",", normalized));
        }

        private static Actor ResolveActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static City ResolveCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static bool IsLivingKingdom(Kingdom pKingdom)
        {
            try { return pKingdom?.data != null && !pKingdom.isRekt() && pKingdom.isAlive(); }
            catch { return false; }
        }

        private static bool IsLivingCity(City pCity)
        {
            try { return pCity?.data != null && !pCity.isRekt(); }
            catch { return false; }
        }

        private static bool IsLivingActor(Actor pActor)
        {
            try { return pActor?.data != null && !pActor.isRekt() && pActor.isAlive(); }
            catch { return false; }
        }

        private static bool SafeIsKing(Actor pActor)
        {
            try { return pActor?.data != null && pActor.isKing(); }
            catch { return false; }
        }

        private static WorldTile HomeOriginTile(Kingdom pHome)
        {
            City city = StableHostCity(pHome);
            return city?.getTile();
        }

        private static bool SameIsland(WorldTile pFirst, WorldTile pSecond)
        {
            try
            {
                return pFirst?.region?.island != null && pSecond?.region?.island != null &&
                       ReferenceEquals(pFirst.region.island, pSecond.region.island);
            }
            catch { return false; }
        }

        private static long DistanceSquared(WorldTile pFirst, WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return long.MaxValue;
            long dx = pFirst.x - pSecond.x;
            long dy = pFirst.y - pSecond.y;
            return dx * dx + dy * dy;
        }
    }
}
