using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.pathfinding;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateIslandExileService
    {
        private sealed class Candidate
        {
            internal TileIsland Island;
            internal WorldTile Landing;
            internal City OccupyingCity;
            internal AWDockRouteCandidate Route;
            internal int Area;
            internal int RouteCost;
        }

        internal static void OnKingdomYear(Kingdom pMandate)
        {
            if (!CanMutate() || pMandate?.data == null ||
                !MandateService.IsMandateKingdom(pMandate) ||
                MandateIslandExileRules.IsActive(ReadStage(pMandate))) return;
            List<City> cities = LiveCities(pMandate);
            City origin = cities.Count == 1 ? cities[0] : null;
            if (!MandateIslandExileRules.CanStart(true, origin != null,
                    HasPort(origin), false)) return;
            if (!TrySelect(origin, out Candidate candidate)) return;
            List<Actor> members = CollectResidents(origin);
            Actor leader = ResolveLeader(pMandate, origin, members);
            if (leader?.data == null || members.Count == 0) return;
            if (!IslandEscapeService.TryBegin(new IslandEscapeGroupSpec
            {
                GroupKey = "mandate:island:" + pMandate.id,
                OriginCity = origin,
                EntryTile = origin.getTile(),
                LandingTile = candidate.Landing,
                Members = members,
                Leader = leader,
                OnStageChanged = next => PersistStage(pMandate, next.Stage),
                OnFounded = (next, survivors) => Finish(pMandate, origin,
                    candidate, survivors),
                OnFailed = (next, reason) => Fail(pMandate, reason)
            }, out IslandEscapeGroupState group)) return;

            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STATE,
                (int)MandateIslandExileStage.Evaluating);
            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_ORIGIN_CITY_ID,
                origin.id);
            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_TARGET_ISLAND_ID,
                candidate.Island?.id ?? -1L);
            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_TARGET_TILE_ID,
                candidate.Landing.data.tile_id);
            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_TARGET_KINGDOM_ID,
                candidate.OccupyingCity?.kingdom?.id ?? -1L);
            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STARTED_YEAR,
                Date.getCurrentYear());
            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_MEMBER_IDS,
                string.Join(",", group.MemberActorIds));
            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_LEADER_ID,
                group.LeaderActorId);
            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_WAR_ID, -1L);
            HistoryWriter.RecordKingdom(pMandate, "mandate_island_exile_started",
                HistoryText.Kingdom(pMandate) + " begins an island refuge journey.",
                HistoryTarget.Kingdom(pMandate));
        }

        internal static void RestoreRuntime()
        {
            if (!CanMutate() || World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms.ToList())
            {
                if (kingdom?.data == null || kingdom.isRekt() ||
                    !MandateService.IsMandateKingdom(kingdom) ||
                    !MandateIslandExileRules.IsActive(ReadStage(kingdom)) ||
                    ReadStage(kingdom) == MandateIslandExileStage.WarPending) continue;
                City origin = ResolveCity(ReadLong(kingdom,
                    LineageKeys.MANDATE_ISLAND_EXILE_ORIGIN_CITY_ID));
                WorldTile landing = ResolveTile(ReadInt(kingdom,
                    LineageKeys.MANDATE_ISLAND_EXILE_TARGET_TILE_ID));
                List<Actor> members = ResolveMembers(ReadIds(kingdom));
                Actor leader = ResolveActor(ReadLong(kingdom,
                    LineageKeys.MANDATE_ISLAND_EXILE_LEADER_ID));
                if (origin?.data == null || landing?.data == null ||
                    members.Count == 0 || leader?.data == null)
                {
                    Fail(kingdom, "restore_state_invalid");
                    continue;
                }
                IslandEscapeService.TryBegin(new IslandEscapeGroupSpec
                {
                    GroupKey = "mandate:island:" + kingdom.id,
                    OriginCity = origin,
                    EntryTile = origin.getTile(),
                    LandingTile = landing,
                    Members = members,
                    Leader = leader,
                    OnStageChanged = next => PersistStage(kingdom, next.Stage),
                    OnFounded = (next, survivors) => Finish(kingdom, origin,
                        FindCandidate(landing), survivors),
                    OnFailed = (next, reason) => Fail(kingdom, reason)
                }, out _);
            }
        }

        internal static void ClearRuntime() { IslandEscapeService.Clear(); }

        internal static bool IsActive(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                MandateIslandExileRules.IsActive(ReadStage(pKingdom));
        }

        internal static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (!CanMutate() || pWar?.data == null) return;
            Kingdom attacker = pWar.getMainAttacker();
            if (attacker?.data == null || ReadStage(attacker) !=
                MandateIslandExileStage.WarPending) return;
            attacker.data.get(LineageKeys.MANDATE_ISLAND_EXILE_WAR_ID,
                out long warId, -1L);
            if (warId != pWar.data.id) return;
            MandateIslandExileStage next = pWinner == WarWinner.Attackers
                ? MandateIslandExileStage.Completed
                : MandateIslandExileStage.Failed;
            attacker.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STATE,
                (int)next);
            HistoryWriter.RecordKingdom(attacker,
                pWinner == WarWinner.Attackers
                    ? "mandate_island_exile_war_won"
                    : "mandate_island_exile_war_lost",
                HistoryText.Kingdom(attacker) +
                (pWinner == WarWinner.Attackers
                    ? " secured its island refuge through war."
                    : " lost the island refuge war."),
                HistoryTarget.Kingdom(attacker));
        }

        internal static void OnCityTransferred(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (!CanMutate() || pCity?.data == null ||
                pOldKingdom?.data == null || pNewKingdom?.data == null ||
                pOldKingdom == pNewKingdom ||
                !MandateService.IsMandateKingdom(pOldKingdom)) return;
            MandateIslandExileStage stage = ReadStage(pOldKingdom);
            if (stage != MandateIslandExileStage.Completed &&
                stage != MandateIslandExileStage.WarPending) return;
            int remaining;
            try { remaining = pOldKingdom.countCities(); }
            catch { remaining = 1; }
            if (remaining > 0 || pNewKingdom.isRekt() ||
                !pNewKingdom.isCiv() || pNewKingdom.isNeutral() ||
                !pNewKingdom.hasKing()) return;
            MandateService.ClearMandate("island_exile_handoff");
            if (!MandateService.TryDeclareMandate(pNewKingdom,
                    "island_exile_handoff"))
            {
                ModClass.LogError("Mandate island exile handoff failed: " +
                    pNewKingdom.id);
                return;
            }
            pOldKingdom.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STATE,
                (int)MandateIslandExileStage.Completed);
            HistoryWriter.RecordKingdom(pNewKingdom,
                "mandate_island_exile_handoff",
                HistoryText.Kingdom(pNewKingdom) +
                " received the mainland mandate after the refuge migration.",
                HistoryTarget.Kingdom(pNewKingdom));
        }

        private static void Finish(Kingdom pMandate, City pOrigin,
            Candidate pCandidate, IReadOnlyList<Actor> pSurvivors)
        {
            try
            {
                if (pCandidate?.OccupyingCity?.data != null)
                {
                    Kingdom target = pCandidate.OccupyingCity.kingdom;
                    if (target?.data == null || target == pMandate)
                    {
                        Fail(pMandate, "target_kingdom_invalid");
                        return;
                    }
                    string failure;
                    War war = WarDecisionService.TryStartNotifiedWarWithResult(
                        pMandate, target, WarDecisionService.WAR_NORMAL,
                        "mandate_island_exile", pNoCb: true,
                        pSystemWar: false, out failure);
                    if (war?.data == null)
                    {
                        Fail(pMandate, "war_start_failed:" + failure);
                        return;
                    }
                    pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STATE,
                        (int)MandateIslandExileStage.WarPending);
                    pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_WAR_ID,
                        war.data.id);
                    HistoryWriter.RecordKingdom(pMandate,
                        "mandate_island_exile_war",
                        HistoryText.Kingdom(pMandate) +
                        " landed on an occupied island and declared war.",
                        HistoryTarget.Kingdom(target));
                    return;
                }

                TileZone zone = pCandidate?.Landing?.zone;
                Actor leader = ResolveActor(ReadLong(pMandate,
                    LineageKeys.MANDATE_ISLAND_EXILE_LEADER_ID)) ??
                    pSurvivors?.FirstOrDefault();
                if (zone == null || leader?.data == null)
                {
                    Fail(pMandate, "founding_tile_invalid");
                    return;
                }
                City islandCity = World.world.cities.newCity(pMandate, zone,
                    leader);
                if (islandCity?.data == null)
                {
                    Fail(pMandate, "city_creation_failed");
                    return;
                }
                foreach (Actor actor in pSurvivors ?? Array.Empty<Actor>())
                {
                    if (actor?.data == null || actor.isRekt() ||
                        !actor.isAlive()) continue;
                    actor.joinCity(islandCity);
                    actor.spawnOn(islandCity.getTile());
                }
                pMandate.setCityMetas(islandCity);
                pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STATE,
                    (int)MandateIslandExileStage.Completed);
                HistoryWriter.RecordKingdom(pMandate,
                    "mandate_island_exile_founded",
                    HistoryText.Kingdom(pMandate) +
                    " founded an island refuge city.",
                    HistoryTarget.City(islandCity));
            }
            catch (Exception error)
            {
                ModClass.LogError("Mandate island exile founding failed: " +
                    error.Message);
                Fail(pMandate, "founding_exception");
            }
        }

        private static void Fail(Kingdom pMandate, string pReason)
        {
            if (pMandate?.data == null) return;
            pMandate.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STATE,
                (int)MandateIslandExileStage.Failed);
            ModClass.LogError("Mandate island exile failed: " + pReason);
        }

        private static void PersistStage(Kingdom pKingdom,
            IslandEscapeStage pStage)
        {
            if (pKingdom?.data == null) return;
            MandateIslandExileStage next = pStage switch
            {
                IslandEscapeStage.Evaluating => MandateIslandExileStage.Evaluating,
                IslandEscapeStage.Gathering => MandateIslandExileStage.Boarding,
                IslandEscapeStage.Boarding => MandateIslandExileStage.Boarding,
                IslandEscapeStage.Voyaging => MandateIslandExileStage.Voyaging,
                IslandEscapeStage.Landing => MandateIslandExileStage.Landing,
                IslandEscapeStage.Founding => MandateIslandExileStage.Founding,
                IslandEscapeStage.Completed => MandateIslandExileStage.Completed,
                IslandEscapeStage.Failed => MandateIslandExileStage.Failed,
                _ => MandateIslandExileStage.None
            };
            pKingdom.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STATE,
                (int)next);
        }

        private static bool TrySelect(City pOrigin, out Candidate pSelected)
        {
            pSelected = null;
            if (pOrigin?.getTile()?.data == null ||
                World.world?.islands_calculator?.islands == null) return false;
            var candidates = new List<Candidate>();
            foreach (TileIsland island in World.world.islands_calculator.islands)
            {
                if (island == null || island.type != TileLayerType.Ground ||
                    island == pOrigin.getTile().region?.island) continue;
                City occupying = FindIslandCity(island);
                int area = 0;
                foreach (MapRegion region in island.regions.getSimpleList())
                    foreach (WorldTile tile in region.tiles)
                        if (IsBuildable(tile)) area++;
                if (area == 0) continue;
                foreach (MapRegion region in island.regions.getSimpleList())
                {
                    foreach (WorldTile tile in region.tiles)
                    {
                        if (!IsBuildable(tile) || !HasOceanNeighbour(tile)) continue;
                        if (!AWDockTransportService.TryResolveRoute(
                                pOrigin.getTile(), tile,
                                out AWDockRouteCandidate route)) continue;
                        candidates.Add(new Candidate
                        {
                            Island = island,
                            Landing = tile,
                            OccupyingCity = occupying,
                            Route = route,
                            Area = area,
                            RouteCost = (int)Math.Min(int.MaxValue,
                                Math.Max(0f, route.EstimatedRouteTiles))
                        });
                    }
                }
            }
            pSelected = candidates.OrderBy(c => c.OccupyingCity == null ? 0 : 1)
                .ThenBy(c => c.RouteCost).ThenByDescending(c => c.Area)
                .ThenBy(c => c.Landing.data.tile_id).FirstOrDefault();
            return pSelected != null;
        }

        private static Candidate FindCandidate(WorldTile pLanding)
        {
            if (pLanding?.region?.island == null) return null;
            return new Candidate { Island = pLanding.region.island,
                Landing = pLanding,
                OccupyingCity = FindIslandCity(pLanding.region.island) };
        }

        private static bool HasPort(City pCity)
        {
            try
            {
                return pCity?.buildings?.Any(building =>
                    building?.component_docks != null &&
                    !building.isUnderConstruction() && building.isUsable()) == true;
            }
            catch { return false; }
        }

        private static bool IsBuildable(WorldTile pTile)
        {
            return pTile?.data != null && pTile.Type?.ground == true &&
                !pTile.hasBuilding() && pTile.zone != null;
        }

        private static bool HasOceanNeighbour(WorldTile pTile)
        {
            return pTile.neighboursAll?.Any(n => n?.data != null &&
                n.Type?.ocean == true) == true;
        }

        private static City FindIslandCity(TileIsland pIsland)
        {
            foreach (MapRegion region in pIsland.regions.getSimpleList())
                foreach (WorldTile tile in region.tiles)
                    if (tile?.zone?.city?.data != null &&
                        !tile.zone.city.isRekt()) return tile.zone.city;
            return null;
        }

        private static List<City> LiveCities(Kingdom pKingdom)
        {
            try { return pKingdom.getCities().Where(c => c?.data != null &&
                !c.isRekt()).ToList(); }
            catch { return new List<City>(); }
        }

        private static List<Actor> CollectResidents(City pCity)
        {
            try { return pCity.units.Where(a => a?.data != null &&
                a.isAlive() && !a.isRekt() && !a.is_inside_boat)
                .GroupBy(a => a.data.id).Select(g => g.First()).ToList(); }
            catch { return new List<Actor>(); }
        }

        private static Actor ResolveLeader(Kingdom pKingdom, City pCity,
            List<Actor> pMembers)
        {
            if (pKingdom?.king?.data != null && pMembers.Contains(pKingdom.king))
                return pKingdom.king;
            if (pCity?.leader?.data != null && pMembers.Contains(pCity.leader))
                return pCity.leader;
            return pMembers.FirstOrDefault();
        }

        private static MandateIslandExileStage ReadStage(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.MANDATE_ISLAND_EXILE_STATE,
                out int value, 0);
            return (MandateIslandExileStage)value;
        }

        private static long ReadLong(Kingdom pKingdom, string pKey)
        {
            pKingdom.data.get(pKey, out long value, -1L); return value;
        }

        private static int ReadInt(Kingdom pKingdom, string pKey)
        {
            pKingdom.data.get(pKey, out int value, -1); return value;
        }

        private static IEnumerable<long> ReadIds(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.MANDATE_ISLAND_EXILE_MEMBER_IDS,
                out string value, "");
            return (value ?? "").Split(new[] { ',' },
                StringSplitOptions.RemoveEmptyEntries).Select(id =>
                long.TryParse(id, out long parsed) ? parsed : -1L)
                .Where(id => id > 0);
        }

        private static List<Actor> ResolveMembers(IEnumerable<long> pIds)
        {
            return (pIds ?? Enumerable.Empty<long>()).Select(ResolveActor)
                .Where(a => a?.data != null && a.isAlive() && !a.isRekt())
                .ToList();
        }

        private static Actor ResolveActor(long pId)
        {
            try { return World.world?.units?.get(pId); } catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return World.world?.cities?.get(pId); } catch { return null; }
        }

        private static WorldTile ResolveTile(int pId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            return tiles != null && pId >= 0 && pId < tiles.Length ? tiles[pId] : null;
        }

        private static bool CanMutate()
        {
            return !AW3MultiplayerReplicaScope.IsReplicaSession &&
                !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
