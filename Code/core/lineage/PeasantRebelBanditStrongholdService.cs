using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditStrongholdService
    {
        private sealed class ActorSnapshot
        {
            internal Actor Actor;
            internal City City;
            internal WorldTile Tile;
        }

        private sealed class TileSnapshot
        {
            internal WorldTile Tile;
            internal TopTileType TopType;
        }

        private sealed class Transaction
        {
            internal PeasantRebelBanditStrongholdPlan Plan;
            internal City Stronghold;
            internal Building BuiltMotherCore;
            internal Kingdom MotherOriginalKingdom;
            internal List<City> ReturnedCities = new List<City>();
            internal List<ActorSnapshot> Actors = new List<ActorSnapshot>();
            internal List<TileSnapshot> WallTiles = new List<TileSnapshot>();
            internal PeasantRebelBanditStrongholdState PreviousState;
            internal bool HadPreviousState;
            internal bool GovernmentFinalized;
        }

        internal static bool TryPlan(City pMother, Kingdom pBandit,
            Kingdom pOrigin, Actor pRuler,
            out PeasantRebelBanditStrongholdPlan pPlan,
            out string pFailureKey)
        {
            pPlan = null;
            pFailureKey = "aw_bandit_stronghold_invalid_city";
            if (!CanMutate() || pMother?.data == null || pMother.isRekt() ||
                pBandit?.data == null || pBandit.isRekt() ||
                pOrigin?.data == null || pOrigin.isRekt() ||
                pRuler?.data == null || pRuler.isRekt() ||
                World.world?.cities == null ||
                TopTileLibrary.wall_wild == null) return false;
            if (pMother.kingdom != pBandit && pMother.kingdom != pOrigin)
                return false;
            if (HasStronghold(pBandit) || IsStronghold(pMother) ||
                HasChildStronghold(pMother))
            {
                pFailureKey = "aw_bandit_stronghold_already_exists";
                return false;
            }
            List<TileZone> motherZones = pMother.zones
                .Where(zone => zone != null && zone.city == pMother)
                .Distinct().ToList();
            if (motherZones.Count < 10)
            {
                pFailureKey = "aw_bandit_stronghold_split_failed";
                LogPlanFailure("fewer_than_ten_mother_zones",
                    motherZones.Count, 0, pFailureKey);
                return false;
            }
            var motherSet = new HashSet<TileZone>(motherZones);
            var facts = new List<BanditZoneFact>(motherZones.Count);
            foreach (TileZone zone in motherZones)
            {
                IEnumerable<string> neighbours = (zone.neighbours ??
                        Array.Empty<TileZone>())
                    .Where(motherSet.Contains).Select(ZoneKey);
                facts.Add(BanditZoneFact.At(ZoneKey(zone), zone.x, zone.y,
                    neighbours));
            }
            WorldTile cityTile = pMother.getTile();
            WorldTile strongholdCenter =
                pMother.getBuildingOfType("type_hall")?.current_tile ??
                pMother.getBuildingOfType("type_bonfire")?.current_tile ??
                cityTile;
            TileZone centerZone = strongholdCenter?.zone;
            if (centerZone == null || !motherSet.Contains(centerZone))
            {
                pFailureKey = "aw_bandit_stronghold_split_failed";
                return false;
            }

            IReadOnlyList<IReadOnlyList<string>> candidates =
                PeasantRebelBanditStrongholdRules.
                    RankNineZoneCandidates(facts, ZoneKey(centerZone));
            if (candidates.Count == 0)
            {
                pFailureKey = "aw_bandit_stronghold_split_failed";
                LogPlanFailure("no_complete_nine_zone_candidate",
                    motherZones.Count, candidates.Count, pFailureKey);
                return false;
            }

            List<TileZone> interior = null;
            BanditZoneWallPlan zoneWallPlan = null;
            foreach (IReadOnlyList<string> candidateKeys in candidates)
            {
                if (candidateKeys.Count != 9) continue;
                List<TileZone> candidate = motherZones.Where(zone =>
                    candidateKeys.Contains(ZoneKey(zone))).ToList();
                if (candidate.Count != 9) continue;
                if (!PeasantRebelBanditZoneWallService.TryPlan(
                        pMother, candidate, strongholdCenter,
                        out BanditZoneWallPlan candidateWall) ||
                    candidateWall.WallPoints.Count == 0) continue;
                interior = candidate;
                zoneWallPlan = candidateWall;
                break;
            }
            if (interior == null || zoneWallPlan == null)
            {
                pFailureKey = "aw_bandit_stronghold_wall_failed";
                LogPlanFailure("no_wallable_nine_zone_candidate",
                    motherZones.Count, candidates.Count, pFailureKey);
                return false;
            }
            var interiorSet = new HashSet<TileZone>(interior);
            List<TileZone> exterior = motherZones.Where(zone =>
                !interiorSet.Contains(zone)).ToList();
            if (!PeasantRebelBanditStrongholdRules.IsViableSplit(
                    interior.Count, exterior.Count))
            {
                pFailureKey = "aw_bandit_stronghold_split_failed";
                LogPlanFailure("invalid_exact_four_zone_split",
                    motherZones.Count, candidates.Count, pFailureKey);
                return false;
            }

            var exteriorSet = new HashSet<TileZone>(exterior);
            Actor reserve = FindMotherReserve(pMother, pRuler, exteriorSet,
                false) ?? FindMotherReserve(pMother, pRuler,
                    new HashSet<TileZone>(interior), true);
            if (reserve == null)
            {
                pFailureKey = "aw_bandit_stronghold_population_failed";
                return false;
            }

            bool hasExteriorCore = pMother.buildings.Any(building =>
                IsCivicCore(building) &&
                exteriorSet.Contains(building.current_tile?.zone));
            WorldTile coreTile = exterior
                .OrderBy(zone => DistanceSquared(zone.centerTile, cityTile))
                .Select(zone => zone.centerTile)
                .FirstOrDefault(tile => tile != null);
            if (!hasExteriorCore && (coreTile == null ||
                AssetManager.buildings.get("bonfire") == null))
            {
                pFailureKey = "aw_bandit_stronghold_core_failed";
                return false;
            }

            pPlan = new PeasantRebelBanditStrongholdPlan
            {
                Context = new PeasantRebelBanditCreationContext
                {
                    Mother = pMother,
                    Bandit = pBandit,
                    Origin = pOrigin,
                    Ruler = pRuler
                },
                CenterZone = centerZone,
                InteriorZones = interior,
                ExteriorZones = exterior,
                WallPoints = zoneWallPlan.WallPoints.ToList(),
                FixedZoneKeys = interior.Select(ZoneKey)
                    .OrderBy(key => key, StringComparer.Ordinal).ToList(),
                ReserveMotherActor = reserve,
                MotherCoreTile = coreTile,
                RequiresMotherCore = !hasExteriorCore
            };
            return true;
        }

        internal static bool TryCreate(
            PeasantRebelBanditCreationContext pContext,
            out City pStronghold, out string pFailureKey)
        {
            pStronghold = null;
            pFailureKey = "aw_bandit_stronghold_invalid_city";
            if (pContext == null || !TryPlan(pContext.Mother,
                    pContext.Bandit, pContext.Origin, pContext.Ruler,
                    out PeasantRebelBanditStrongholdPlan plan,
                    out pFailureKey)) return false;
            plan.Context.RemoveBanditOnFailure =
                pContext.RemoveBanditOnFailure;
            plan.Context.FinalizeGovernment =
                pContext.FinalizeGovernment;
            plan.Context.RollbackGovernment =
                pContext.RollbackGovernment;
            var transaction = new Transaction { Plan = plan };
            try
            {
                CaptureSnapshots(transaction);
                var creating = BuildState(transaction,
                    BanditStrongholdPhase.Creating, -1L);
                if (!PeasantRebelBanditStateStore.Write(
                        plan.Context.Bandit, creating))
                    throw new InvalidOperationException(
                        "cannot persist creating phase");

                City stronghold = World.world.cities.newCity(
                    plan.Context.Bandit, plan.CenterZone,
                    plan.Context.Ruler);
                transaction.Stronghold = stronghold;
                if (stronghold?.data == null)
                    throw new InvalidOperationException(
                        "native city creation returned null");
                RemoveUnplannedNewZones(stronghold, plan);
                stronghold.setUnitMetas(plan.Context.Ruler);
                stronghold.newCityEvent(plan.Context.Ruler);
                stronghold.setName(
                    PeasantRebelBanditStrongholdRules.
                        ComposeStrongholdName(ReadOutlawRoot(
                            plan.Context.Bandit)));
                for (int i = 0; i < plan.InteriorZones.Count; i++)
                    stronghold.addZone(plan.InteriorZones[i]);

                MoveResidents(plan, stronghold);
                ReturnOrdinaryCities(transaction);
                EnsureMotherCore(transaction);
                PlaceWalls(transaction);
                stronghold.recalculateNeighbourZones();
                plan.Context.Mother.recalculateNeighbourZones();
                plan.Context.Bandit.setCityMetas(stronghold);
                if (plan.Context.FinalizeGovernment != null)
                {
                    if (!plan.Context.FinalizeGovernment(stronghold))
                        throw new InvalidOperationException(
                            "bandit government finalization failed");
                    transaction.GovernmentFinalized = true;
                }

                PeasantRebelBanditStrongholdState active = BuildState(
                    transaction,
                    BanditStrongholdPhase.Active, stronghold.getID());
                if (!PeasantRebelBanditStateStore.Write(
                        plan.Context.Bandit, active))
                    throw new InvalidOperationException(
                        "cannot persist active phase");
                WorldLog.logNewCity(stronghold);
                pStronghold = stronghold;
                pFailureKey = "";
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit stronghold creation failed: " +
                                    e.Message);
                Rollback(transaction);
                pFailureKey = "aw_bandit_stronghold_transaction_failed";
                return false;
            }
        }

        internal static bool TryCreateDirect(City pMother,
            out Kingdom pBandit, out City pStronghold,
            out string pFailureKey)
        {
            pBandit = null;
            pStronghold = null;
            pFailureKey = "aw_bandit_stronghold_invalid_city";
            if (!CanMutate() || pMother?.data == null ||
                pMother.isRekt() || pMother.kingdom?.data == null ||
                pMother.kingdom.isRekt() ||
                !pMother.kingdom.isCiv() || pMother.kingdom.isNeutral() ||
                MandateRebelService.IsRebelKingdom(pMother.kingdom) ||
                IsStronghold(pMother) || HasChildStronghold(pMother))
                return false;
            Kingdom origin = pMother.kingdom;
            Actor ruler = SelectDirectRuler(pMother);
            if (ruler?.data == null)
            {
                pFailureKey = "aw_bandit_stronghold_population_failed";
                return false;
            }

            try
            {
                Kingdom bandit = World.world.kingdoms.makeNewCivKingdom(
                    ruler);
                pBandit = bandit;
                bandit.copyMetasFromOtherKingdom(origin);
                MandateRebelService.MarkRebelKingdom(bandit, ruler,
                    origin);
                int year = Date.getCurrentYear();
                if (!PeasantRebelOutlawNameService.EnsureRoot(bandit,
                        ruler, year, out _))
                    throw new InvalidOperationException(
                        "cannot assign bandit name root");
                bandit.data.set(
                    LineageKeys.MANDATE_REBEL_FOUNDING_CITY_ID,
                    pMother.getID());
                bandit.data.set(
                    LineageKeys.MANDATE_REBEL_ROUTE_CREATED_YEAR, year);
                bandit.data.set(LineageKeys.MANDATE_REBEL_ROUTE_LAST_YEAR,
                    int.MinValue);
                bandit.data.set(
                    LineageKeys.MANDATE_REBEL_ORIGIN_CITY_COUNT,
                    PeasantRebelRouteService.SafeCityCount(origin));
                bandit.data.set(
                    LineageKeys.MANDATE_REBEL_ORIGIN_STRENGTH,
                    PeasantRebelRouteService.RealmStrength(origin));
                bandit.data.set(
                    LineageKeys.MANDATE_REBEL_ORIGIN_CAPITAL_ID,
                    origin.capital?.getID() ?? -1L);
                bandit.data.set(
                    LineageKeys.MANDATE_REBEL_ORIGIN_RULER_ID,
                    origin.king?.getID() ?? -1L);

                var context = new PeasantRebelBanditCreationContext
                {
                    Bandit = bandit,
                    Origin = origin,
                    Mother = pMother,
                    Ruler = ruler,
                    RemoveBanditOnFailure = true,
                    FinalizeGovernment = stronghold =>
                        PeasantRebelRouteService.
                            FinalizeDirectBanditGovernment(
                                bandit, stronghold),
                    RollbackGovernment = () => { }
                };
                if (!TryCreate(context, out pStronghold,
                        out pFailureKey))
                {
                    if (bandit?.data != null && !bandit.isRekt())
                    {
                        ruler.joinCity(pMother);
                        PrepareBanditKingdomRemoval(
                            bandit, origin, pMother, null);
                        World.world.kingdoms.removeObject(bandit);
                    }
                    pBandit = null;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Direct bandit creation failed: " +
                                    e.Message);
                if (pBandit?.data != null && !pBandit.isRekt())
                {
                    ruler.joinCity(pMother);
                    PrepareBanditKingdomRemoval(
                        pBandit, origin, pMother, null);
                    World.world.kingdoms.removeObject(pBandit);
                }
                pBandit = null;
                pFailureKey = "aw_bandit_stronghold_transaction_failed";
                return false;
            }
        }

        internal static bool IsStronghold(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null)
                return false;
            return PeasantRebelBanditStateStore.TryResolveActive(
                       pCity.kingdom, out PeasantRebelBanditStrongholdState
                           state) && state.StrongholdCityId == pCity.getID();
        }

        internal static bool HasActiveStronghold(Kingdom pKingdom)
        {
            return PeasantRebelBanditStateStore.TryResolveActive(pKingdom,
                out _);
        }

        internal static string ComposeCeremonialTitle(Kingdom pKingdom,
            bool pHeir)
        {
            if (pKingdom?.data == null) return "";
            string root = ReadOutlawRoot(pKingdom);
            string role = AW_L10n.Text(
                pHeir ? "aw_bandit_heir_title" :
                    "aw_bandit_ruler_title",
                pHeir ? "\u5c11\u5f53\u5bb6" : "\u5927\u5f53\u5bb6");
            return PeasantRebelBanditStrongholdRules.
                ComposeCeremonialTitle(root, role);
        }

        private static string ReadOutlawRoot(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string root, pKingdom.name ?? "");
            return PeasantRebelOutlawNameRules.NormalizeRoot(root);
        }

        private static Actor SelectDirectRuler(City pMother)
        {
            Actor leader = pMother?.leader;
            if (leader?.data != null && !leader.isRekt() &&
                !leader.isBaby()) return leader;
            return pMother?.units?.Where(actor => actor?.data != null &&
                    !actor.isRekt() && !actor.isBaby())
                .OrderBy(actor => actor.getID()).FirstOrDefault();
        }

        internal static City ResolveStronghold(Kingdom pKingdom)
        {
            if (!PeasantRebelBanditStateStore.TryResolveActive(pKingdom,
                    out PeasantRebelBanditStrongholdState state) ||
                World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(state.StrongholdCityId);
                return city?.data != null && !city.isRekt() &&
                       city.kingdom == pKingdom
                    ? city
                    : null;
            }
            catch { return null; }
        }

        internal static bool ReleaseToFounding(Kingdom pKingdom)
        {
            if (!CanMutate() ||
                !PeasantRebelBanditStateStore.TryResolveActive(pKingdom,
                    out PeasantRebelBanditStrongholdState state))
                return false;
            state.Phase = BanditStrongholdPhase.Released;
            state.Raid.Stage = BanditRaidStage.None;
            state.Raid.MemberActorIds.Clear();
            state.Raid.CarriedFood = 0;
            state.Raid.CarriedFoodByResourceId.Clear();
            return PeasantRebelBanditStateStore.Write(pKingdom, state);
        }

        internal static bool CanAcquireCity(Kingdom pKingdom, City pCity)
        {
            if (!PeasantRebelBanditStateStore.TryResolveActive(pKingdom,
                    out PeasantRebelBanditStrongholdState state)) return true;
            return pCity?.data != null &&
                   pCity.getID() == state.StrongholdCityId &&
                   pCity.kingdom == pKingdom;
        }

        internal static bool CanAcquireZone(City pCity, TileZone pZone)
        {
            if (pCity?.data == null || pZone == null ||
                pCity.kingdom?.data == null) return true;
            if (!PeasantRebelBanditStateStore.TryResolveActive(
                    pCity.kingdom,
                    out PeasantRebelBanditStrongholdState state) ||
                state.StrongholdCityId != pCity.getID()) return true;
            if (pZone.city == pCity) return true;
            return PeasantRebelBanditStrongholdRules.CanAcquireZone(true,
                ZoneKey(pZone), new HashSet<string>(state.FixedZoneKeys,
                    StringComparer.Ordinal));
        }

        internal static bool TryHandleCapture(City pCity,
            Kingdom pOccupier, out bool pHandled)
        {
            pHandled = false;
            if (pCity?.data == null || pCity.kingdom?.data == null)
                return true;
            Kingdom bandit = pCity.kingdom;
            if (!PeasantRebelBanditStateStore.TryRead(bandit,
                    out PeasantRebelBanditStrongholdState state) ||
                state.StrongholdCityId != pCity.getID() ||
                state.Phase != BanditStrongholdPhase.Active &&
                state.Phase != BanditStrongholdPhase.Falling &&
                state.Phase != BanditStrongholdPhase.Completed)
                return true;
            pHandled = true;
            if (!CanMutate()) return false;
            return CompleteFall(bandit, pCity, state);
        }

        internal static void RestoreRuntime()
        {
            if (!CanMutate() || World.world?.kingdoms == null ||
                World.world.cities == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms.ToList())
            {
                if (kingdom?.data == null || kingdom.isRekt() ||
                    !PeasantRebelBanditStateStore.TryRead(kingdom,
                        out PeasantRebelBanditStrongholdState state))
                    continue;
                City stronghold = ResolveCity(state.StrongholdCityId);
                City mother = ResolveCity(state.MotherCityId);
                if (state.Phase == BanditStrongholdPhase.Active)
                {
                    if (stronghold?.data != null && mother?.data != null)
                        continue;
                    if (stronghold?.data == null)
                    {
                        state.Phase = BanditStrongholdPhase.Completed;
                        state.Raid.Stage = BanditRaidStage.None;
                        state.Raid.MemberActorIds.Clear();
                        state.Raid.CarriedFood = 0;
                        state.Raid.CarriedFoodByResourceId.Clear();
                        PeasantRebelBanditStateStore.Write(kingdom, state);
                    }
                    continue;
                }
                if ((state.Phase == BanditStrongholdPhase.Falling ||
                     state.Phase == BanditStrongholdPhase.Completed) &&
                    stronghold?.data != null)
                    CompleteFall(kingdom, stronghold, state);
            }
        }

        private static bool CompleteFall(Kingdom pBandit,
            City pStronghold, PeasantRebelBanditStrongholdState pState)
        {
            City mother = ResolveCity(pState.MotherCityId);
            if (pBandit?.data == null || pStronghold?.data == null ||
                mother?.data == null || mother.isRekt()) return false;
            try
            {
                if (pState.Phase == BanditStrongholdPhase.Active)
                {
                    pState.Phase = BanditStrongholdPhase.Falling;
                    if (!PeasantRebelBanditStateStore.Write(pBandit,
                            pState)) return false;
                }

                WorldTile motherTile = mother.getTile();
                foreach (Actor actor in pStronghold.units.ToList())
                {
                    if (actor?.data == null || actor.isRekt()) continue;
                    actor.joinCity(mother);
                    if (motherTile != null) actor.spawnOn(motherTile);
                }
                foreach (TileZone zone in pStronghold.zones.ToList())
                    mother.addZone(zone);
                mother.recalculateNeighbourZones();
                pStronghold.recalculateNeighbourZones();
                if (!RestoreWalls(pState)) return false;

                pState.Phase = BanditStrongholdPhase.Completed;
                pState.Raid.Stage = BanditRaidStage.None;
                pState.Raid.MemberActorIds.Clear();
                pState.Raid.CarriedFood = 0;
                pState.Raid.CarriedFoodByResourceId.Clear();
                if (!PeasantRebelBanditStateStore.Write(pBandit, pState))
                    return false;
                if (!pStronghold.isRekt())
                    World.world.cities.removeObject(pStronghold);
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit stronghold fall failed: " +
                                    e.Message);
                return false;
            }
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

        internal static bool HasChildStronghold(City pMother)
        {
            if (pMother?.data == null || World.world?.kingdoms == null)
                return false;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (PeasantRebelBanditStateStore.TryResolveActive(kingdom,
                        out PeasantRebelBanditStrongholdState state) &&
                    state.MotherCityId == pMother.getID()) return true;
            }
            return false;
        }

        private static bool HasStronghold(Kingdom pKingdom)
        {
            return PeasantRebelBanditStateStore.TryResolveActive(pKingdom,
                out _);
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }

        private static Actor FindMotherReserve(City pMother, Actor pRuler,
            HashSet<TileZone> pZones, bool pWillRelocate)
        {
            if (pMother?.units == null) return null;
            return pMother.units.Where(actor => actor?.data != null &&
                    !actor.isRekt() && !actor.isBaby() && actor != pRuler &&
                    (pWillRelocate ||
                     pZones.Contains(actor.current_tile?.zone)))
                .OrderBy(actor => actor.getID()).FirstOrDefault();
        }

        private static bool IsCivicCore(Building pBuilding)
        {
            string type = pBuilding?.asset?.type;
            return type == "type_hall" || type == "type_bonfire";
        }

        private static string ZoneKey(TileZone pZone)
        {
            WorldTile center = pZone?.centerTile;
            return center == null ? "" : center.x + ":" + center.y;
        }

        private static void LogPlanFailure(string pStage,
            int pMotherZoneCount, int pCandidateCount, string pFailureKey)
        {
            ModClass.LogWarning("Bandit stronghold plan failed: stage=" +
                pStage + ", mother_zones=" + pMotherZoneCount +
                ", candidates=" + pCandidateCount + ", failure=" +
                pFailureKey);
        }

        private static int DistanceSquared(WorldTile pLeft,
            WorldTile pRight)
        {
            if (pLeft == null || pRight == null) return int.MaxValue;
            int dx = pLeft.x - pRight.x;
            int dy = pLeft.y - pRight.y;
            return dx * dx + dy * dy;
        }

        private static void CaptureSnapshots(Transaction pTransaction)
        {
            PeasantRebelBanditStrongholdPlan plan = pTransaction.Plan;
            pTransaction.MotherOriginalKingdom = plan.Context.Mother.kingdom;
            pTransaction.HadPreviousState =
                PeasantRebelBanditStateStore.TryRead(plan.Context.Bandit,
                    out pTransaction.PreviousState);
            var seen = new HashSet<long>();
            foreach (Actor actor in plan.Context.Mother.units.ToList())
                AddActorSnapshot(pTransaction, actor, seen);
            AddActorSnapshot(pTransaction, plan.Context.Ruler, seen);
            foreach (CultiwayWallPoint point in plan.WallPoints)
            {
                WorldTile tile = World.world.GetTile(point.X, point.Y);
                if (tile == null) continue;
                pTransaction.WallTiles.Add(new TileSnapshot
                    { Tile = tile, TopType = tile.top_type });
            }
        }

        private static void AddActorSnapshot(Transaction pTransaction,
            Actor pActor, HashSet<long> pSeen)
        {
            if (pActor?.data == null || !pSeen.Add(pActor.getID())) return;
            pTransaction.Actors.Add(new ActorSnapshot
                { Actor = pActor, City = pActor.city, Tile = pActor.current_tile });
        }

        private static void RemoveUnplannedNewZones(City pStronghold,
            PeasantRebelBanditStrongholdPlan pPlan)
        {
            var selected = new HashSet<TileZone>(pPlan.InteriorZones);
            foreach (TileZone zone in pStronghold.zones.ToList())
                if (!selected.Contains(zone)) pStronghold.removeZone(zone);
        }

        private static void MoveResidents(
            PeasantRebelBanditStrongholdPlan pPlan, City pStronghold)
        {
            var interior = new HashSet<TileZone>(pPlan.InteriorZones);
            foreach (Actor actor in pPlan.Context.Mother.units.ToList())
            {
                if (actor?.data == null || actor.isRekt() ||
                    actor == pPlan.ReserveMotherActor) continue;
                if (actor == pPlan.Context.Ruler ||
                    interior.Contains(actor.current_tile?.zone))
                    actor.joinCity(pStronghold);
            }
            pPlan.Context.Ruler.joinCity(pStronghold);
            pPlan.Context.Ruler.spawnOn(pStronghold.getTile());
            if (interior.Contains(
                    pPlan.ReserveMotherActor.current_tile?.zone))
                pPlan.ReserveMotherActor.spawnOn(pPlan.MotherCoreTile);
        }

        private static void ReturnOrdinaryCities(Transaction pTransaction)
        {
            PeasantRebelBanditStrongholdPlan plan = pTransaction.Plan;
            foreach (City city in plan.Context.Bandit.getCities().ToList())
            {
                if (city?.data == null || city.isRekt() ||
                    city == pTransaction.Stronghold) continue;
                pTransaction.ReturnedCities.Add(city);
                city.joinAnotherKingdom(plan.Context.Origin,
                    pCaptured: false, pRebellion: false);
            }
            if (plan.Context.Mother.kingdom != plan.Context.Origin)
            {
                if (!pTransaction.ReturnedCities.Contains(
                        plan.Context.Mother))
                    pTransaction.ReturnedCities.Add(plan.Context.Mother);
                plan.Context.Mother.joinAnotherKingdom(plan.Context.Origin,
                    pCaptured: false, pRebellion: false);
            }
            plan.ReserveMotherActor.joinCity(plan.Context.Mother);
        }

        private static void EnsureMotherCore(Transaction pTransaction)
        {
            PeasantRebelBanditStrongholdPlan plan = pTransaction.Plan;
            if (!plan.RequiresMotherCore) return;
            Building core = World.world.buildings.addBuilding("bonfire",
                plan.MotherCoreTile, pCheckForBuild: false,
                pSfx: false, pType: BuildPlacingType.New);
            if (core == null)
                throw new InvalidOperationException(
                    "native mother civic core creation failed");
            pTransaction.BuiltMotherCore = core;
        }

        private static void PlaceWalls(Transaction pTransaction)
        {
            foreach (TileSnapshot snapshot in pTransaction.WallTiles)
                snapshot.Tile.setTopTileType(TopTileLibrary.wall_wild);
        }

        private static PeasantRebelBanditStrongholdState BuildState(
            Transaction pTransaction,
            BanditStrongholdPhase pPhase, long pStrongholdCityId)
        {
            PeasantRebelBanditStrongholdPlan plan = pTransaction.Plan;
            return new PeasantRebelBanditStrongholdState
            {
                Phase = pPhase,
                StrongholdCityId = pStrongholdCityId,
                MotherCityId = plan.Context.Mother.getID(),
                OriginKingdomId = plan.Context.Origin.getID(),
                FixedZoneKeys = new List<string>(plan.FixedZoneKeys),
                WallPoints = pTransaction.WallTiles.Select(snapshot =>
                    new BanditStrongholdPoint
                    {
                        X = snapshot.Tile.x,
                        Y = snapshot.Tile.y,
                        OriginalTopTypeId = snapshot.TopType?.id ?? ""
                    }).ToList()
            };
        }

        private static bool RestoreWalls(
            PeasantRebelBanditStrongholdState pState)
        {
            if (pState?.WallPoints == null) return true;
            int failed = 0;
            foreach (BanditStrongholdPoint point in pState.WallPoints)
            {
                if (point == null) continue;
                WorldTile tile = World.world?.GetTile(point.X, point.Y);
                if (tile == null ||
                    !PeasantRebelBanditStrongholdRules.ShouldRestoreWall(
                        tile.top_type?.id)) continue;
                try
                {
                    TopTileType originalTopType =
                        string.IsNullOrWhiteSpace(point.OriginalTopTypeId)
                            ? null
                            : AssetManager.top_tiles.get(
                                point.OriginalTopTypeId);
                    tile.setTopTileType(originalTopType);
                }
                catch
                {
                    failed++;
                }
            }
            if (failed <= 0) return true;
            ModClass.LogWarning("Bandit stronghold wall restore failed: " +
                                failed + " tile(s) remain pending.");
            return false;
        }

        private static void Rollback(Transaction pTransaction)
        {
            if (pTransaction?.Plan == null) return;
            PeasantRebelBanditStrongholdPlan plan = pTransaction.Plan;
            try
            {
                foreach (TileSnapshot snapshot in pTransaction.WallTiles)
                    snapshot.Tile?.setTopTileType(snapshot.TopType);
                if (pTransaction.BuiltMotherCore?.data != null)
                    World.world.buildings.removeObject(
                        pTransaction.BuiltMotherCore);
                foreach (TileZone zone in plan.InteriorZones)
                    if (zone != null) plan.Context.Mother.addZone(zone);
                if (pTransaction.Stronghold?.data != null &&
                    !pTransaction.Stronghold.isRekt())
                {
                    foreach (TileZone zone in
                             pTransaction.Stronghold.zones.ToList())
                        pTransaction.Stronghold.removeZone(zone);
                    World.world.cities.removeObject(
                        pTransaction.Stronghold);
                }
                foreach (City city in pTransaction.ReturnedCities)
                    if (city?.data != null && !city.isRekt() &&
                        city.kingdom != plan.Context.Bandit)
                        city.joinAnotherKingdom(plan.Context.Bandit,
                            pCaptured: false, pRebellion: false);
                foreach (ActorSnapshot snapshot in pTransaction.Actors)
                {
                    if (snapshot.Actor?.data == null ||
                        snapshot.Actor.isRekt()) continue;
                    snapshot.Actor.joinCity(snapshot.City);
                    if (snapshot.Tile != null)
                        snapshot.Actor.spawnOn(snapshot.Tile);
                }
                if (pTransaction.HadPreviousState)
                    PeasantRebelBanditStateStore.Write(
                        plan.Context.Bandit, pTransaction.PreviousState);
                else PeasantRebelBanditStateStore.Clear(
                    plan.Context.Bandit);
                if (pTransaction.GovernmentFinalized)
                    plan.Context.RollbackGovernment?.Invoke();
                if (plan.Context.RemoveBanditOnFailure &&
                    plan.Context.Bandit?.data != null &&
                    !plan.Context.Bandit.isRekt())
                {
                    PrepareBanditKingdomRemoval(plan.Context.Bandit,
                        plan.Context.Origin, plan.Context.Mother,
                        pTransaction.Actors);
                    World.world.kingdoms.removeObject(plan.Context.Bandit);
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit stronghold rollback failed: " +
                                    e.Message);
            }
        }

        private static void PrepareBanditKingdomRemoval(Kingdom pBandit,
            Kingdom pOrigin, City pFallbackCity,
            IReadOnlyCollection<ActorSnapshot> pSnapshots)
        {
            if (pBandit == null) return;
            var candidates = new HashSet<Actor>();
            var snapshotCities = new Dictionary<Actor, City>();
            if (pSnapshots != null)
            {
                foreach (ActorSnapshot snapshot in pSnapshots)
                {
                    if (snapshot?.Actor == null) continue;
                    candidates.Add(snapshot.Actor);
                    snapshotCities[snapshot.Actor] = snapshot.City;
                }
            }
            if (pBandit.units != null)
                foreach (Actor actor in pBandit.units.ToList())
                    if (actor != null) candidates.Add(actor);

            foreach (Actor actor in candidates)
            {
                if (actor?.data == null || actor.kingdom != pBandit)
                    continue;
                if (actor.asset != null)
                {
                    snapshotCities.TryGetValue(actor, out City snapshotCity);
                    City targetCity = IsValidRemovalCity(
                        snapshotCity, pBandit)
                        ? snapshotCity
                        : IsValidRemovalCity(pFallbackCity, pBandit)
                            ? pFallbackCity
                            : null;
                    TryRestoreRemovalActor(actor, pBandit, targetCity,
                        pOrigin);
                }
                if (actor.kingdom != pBandit) continue;
                actor.kingdom = null;
                ActorKingdomSafetyService.QueueRepair(actor);
            }
        }

        private static bool IsValidRemovalCity(City pCity,
            Kingdom pBandit)
        {
            return pCity?.data != null && !pCity.isRekt() &&
                   pCity.kingdom != pBandit &&
                   pCity.kingdom?.data != null &&
                   pCity.kingdom.asset != null &&
                   !pCity.kingdom.isRekt();
        }

        private static void TryRestoreRemovalActor(Actor pActor,
            Kingdom pBandit, City pTargetCity, Kingdom pOrigin)
        {
            try
            {
                if (pTargetCity != null)
                    pActor.joinCity(pTargetCity);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Bandit actor city rollback failed: " + e.Message);
            }
            if (pActor.kingdom != pBandit) return;

            Kingdom target = pTargetCity?.kingdom;
            if (target?.data == null || target.asset == null ||
                target.isRekt())
                target = pOrigin?.data != null &&
                         pOrigin.asset != null && !pOrigin.isRekt()
                    ? pOrigin
                    : null;
            if (target == null || target == pBandit) return;
            try
            {
                using (FormalAffiliationTransferScope.Open(
                           pActor.data.id, target.id,
                           pTargetCity?.data?.id ?? -1L))
                {
                    pActor.joinKingdom(target);
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Bandit actor kingdom rollback failed: " + e.Message);
            }
        }
    }
}
