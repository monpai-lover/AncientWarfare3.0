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
        private const int GateTowerInwardSearchSteps = 6;

        [ThreadStatic]
        private static int _directBanditKingInstallationDepth;

        internal static bool IsInstallingDirectBanditKing =>
            _directBanditKingInstallationDepth > 0;

        private sealed class DirectBanditKingInstallationScope : IDisposable
        {
            private bool _disposed;

            internal DirectBanditKingInstallationScope()
            {
                _directBanditKingInstallationDepth++;
            }

            public void Dispose()
            {
                if (_disposed) return;
                if (_directBanditKingInstallationDepth > 0)
                    _directBanditKingInstallationDepth--;
                _disposed = true;
            }
        }

        private static IDisposable EnterDirectBanditKingInstallationScope()
        {
            return new DirectBanditKingInstallationScope();
        }

        private static readonly CultiwayWallPoint[]
            GateTowerInwardDirections =
            {
                new CultiwayWallPoint(0, -1),
                new CultiwayWallPoint(-1, 0),
                new CultiwayWallPoint(0, 1),
                new CultiwayWallPoint(1, 0)
            };

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
            internal List<Building> Towers = new List<Building>();
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
            if (motherZones.Count < 4)
            {
                pFailureKey = "aw_bandit_stronghold_split_failed";
                LogPlanFailure("fewer_than_four_mother_zones",
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
                    RankFourZoneCandidates(facts, ZoneKey(centerZone));
            if (candidates.Count == 0)
            {
                pFailureKey = "aw_bandit_stronghold_split_failed";
                LogPlanFailure("no_complete_four_zone_candidate",
                    motherZones.Count, candidates.Count, pFailureKey);
                return false;
            }

            List<TileZone> interior = null;
            BanditZoneWallPlan zoneWallPlan = null;
            BuildingAsset towerAsset = ResolveTowerAsset(pRuler);
            List<WorldTile> towerTiles = null;
            List<TileZone> wallFallbackInterior = null;
            BanditZoneWallPlan wallFallbackPlan = null;
            foreach (IReadOnlyList<string> candidateKeys in candidates)
            {
                if (candidateKeys.Count != 4) continue;
                List<TileZone> candidate = motherZones.Where(zone =>
                    candidateKeys.Contains(ZoneKey(zone))).ToList();
                if (candidate.Count != 4) continue;
                if (!PeasantRebelBanditZoneWallService.TryPlan(
                        pMother, candidate, strongholdCenter,
                        out BanditZoneWallPlan candidateWall) ||
                    candidateWall.WallPoints.Count == 0) continue;
                if (wallFallbackInterior == null)
                {
                    wallFallbackInterior = candidate;
                    wallFallbackPlan = candidateWall;
                }
                List<WorldTile> candidateTowerTiles =
                    FindGateTowerTiles(candidateWall, strongholdCenter,
                        towerAsset, candidate);
                if (!PeasantRebelBanditStrongholdRules.
                        CanUseWallCandidate(true,
                            candidateTowerTiles?.Count ?? 0)) continue;
                if (candidateTowerTiles?.Count != 4) continue;
                interior = candidate;
                zoneWallPlan = candidateWall;
                towerTiles = candidateTowerTiles;
                break;
            }
            if (interior == null && wallFallbackInterior != null)
            {
                interior = wallFallbackInterior;
                zoneWallPlan = wallFallbackPlan;
                towerTiles = new List<WorldTile>();
            }
            if (interior == null || zoneWallPlan == null)
            {
                pFailureKey = "aw_bandit_stronghold_wall_failed";
                LogPlanFailure("no_wallable_four_zone_candidate",
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
            Actor reserve = exterior.Count == 0
                ? null
                : FindMotherReserve(pMother, pRuler, exteriorSet,
                    false) ?? FindMotherReserve(pMother, pRuler,
                    new HashSet<TileZone>(interior), true);
            if (exterior.Count > 0 && reserve == null)
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
            if (exterior.Count > 0 && !hasExteriorCore && (coreTile == null ||
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
                GateCenters = zoneWallPlan.GateCenters.ToList(),
                TowerTiles = towerTiles,
                TowerAsset = towerAsset,
                FixedZoneKeys = interior.Select(ZoneKey)
                    .OrderBy(key => key, StringComparer.Ordinal).ToList(),
                ReserveMotherActor = reserve,
                MotherCoreTile = coreTile,
                RequiresMotherCore = exterior.Count > 0 && !hasExteriorCore
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
            return TryCreatePlanned(plan, out pStronghold,
                out pFailureKey);
        }

        private static bool TryCreatePlanned(
            PeasantRebelBanditStrongholdPlan pPlan,
            out City pStronghold, out string pFailureKey)
        {
            pStronghold = null;
            pFailureKey = "aw_bandit_stronghold_invalid_city";
            if (pPlan?.Context?.Mother?.data == null ||
                pPlan.Context.Bandit?.data == null ||
                pPlan.Context.Origin?.data == null ||
                pPlan.Context.Ruler?.data == null ||
                pPlan.CenterZone == null ||
                pPlan.InteriorZones?.Count != 4 ||
                pPlan.WallPoints == null || pPlan.WallPoints.Count == 0 ||
                pPlan.TowerTiles == null) return false;
            PeasantRebelBanditStrongholdPlan plan = pPlan;
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
                PlaceTowers(transaction, stronghold);
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
                if (!RecordEstablishment(stronghold,
                        plan.Context.Bandit))
                    ModClass.LogWarning(
                        "Stronghold establishment chronicle failed; " +
                        "world state was retained");
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
            return TryCreateDirect(pMother, out pBandit, out pStronghold,
                out pFailureKey, out _, pAllowClaimRedirect: true);
        }

        internal static bool TryCreateDirect(City pMother,
            out Kingdom pBandit, out City pStronghold,
            out string pFailureKey, out bool restorationRedirected,
            bool pAllowClaimRedirect = true)
        {
            pBandit = null;
            pStronghold = null;
            restorationRedirected = false;
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
            if (pAllowClaimRedirect)
            {
                RestorationRebellionStartOutcome redirect =
                    RestorationRebellionRedirectService
                        .TryRedirectBanditFounder(
                            ruler, pMother, out Kingdom restored,
                            out string redirectError);
                if (RestorationRebellionRedirectRules
                    .ShouldSuppressVanilla(redirect))
                {
                    restorationRedirected = true;
                    pBandit = restored;
                    pStronghold = restored?.capital ?? pMother;
                    pFailureKey = redirect ==
                        RestorationRebellionStartOutcome.Started
                        ? ""
                        : (string.IsNullOrEmpty(redirectError)
                            ? "restoration_initialization_pending"
                            : redirectError);
                    return true;
                }
            }
            if (!TryPlan(pMother, origin, origin, ruler,
                    out PeasantRebelBanditStrongholdPlan plan,
                    out pFailureKey))
                return false;

            bool rulerWasMotherCityLeader = pMother.leader == ruler;
            if (rulerWasMotherCityLeader)
                pMother.removeLeader();

            try
            {
                Kingdom bandit = World.world.kingdoms.makeNewCivKingdom(
                    ruler);
                pBandit = bandit;
                if (!EnsureDirectBanditKing(bandit, ruler,
                        "after_make_new_kingdom"))
                    throw new InvalidOperationException(
                        "native kingdom creation did not install bandit king");
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

                plan.Context.Bandit = bandit;
                plan.Context.RemoveBanditOnFailure = true;
                plan.Context.FinalizeGovernment = stronghold =>
                    PeasantRebelRouteService.FinalizeDirectBanditGovernment(
                        bandit, stronghold);
                plan.Context.RollbackGovernment = () => { };
                if (!TryCreatePlanned(plan, out pStronghold,
                        out pFailureKey))
                {
                    if (bandit?.data != null && !bandit.isRekt())
                    {
                        ruler.joinCity(pMother);
                        if (rulerWasMotherCityLeader &&
                            pMother.leader != ruler)
                            pMother.setLeader(ruler, pNew: true);
                        PrepareBanditKingdomRemoval(
                            bandit, origin, pMother, null, ruler);
                        RemoveBanditKingdomAndDrain(bandit);
                    }
                    pBandit = null;
                    return false;
                }
                if (!EnsureDirectBanditKing(bandit, ruler,
                        "after_stronghold_creation"))
                    throw new InvalidOperationException(
                        "bandit king was lost during stronghold creation");
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Direct bandit creation failed: " +
                                    e.Message);
                if (pBandit?.data != null && !pBandit.isRekt())
                {
                    ruler.joinCity(pMother);
                    if (rulerWasMotherCityLeader &&
                        pMother.leader != ruler)
                        pMother.setLeader(ruler, pNew: true);
                    PrepareBanditKingdomRemoval(
                        pBandit, origin, pMother, null, ruler);
                    RemoveBanditKingdomAndDrain(pBandit);
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
                           state) && IsTrackedStrongholdCity(state,
                               pCity.getID());
        }

        internal static bool HasActiveStronghold(Kingdom pKingdom)
        {
            return PeasantRebelBanditStateStore.TryResolveActive(pKingdom,
                out _);
        }

        internal static bool IsStrongholdKingdom(Kingdom pKingdom)
        {
            return HasActiveStronghold(pKingdom);
        }

        internal static bool IsStrongholdCity(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null)
                return false;
            return PeasantRebelBanditStateStore.TryResolveActive(
                       pCity.kingdom,
                       out PeasantRebelBanditStrongholdState state) &&
                   IsTrackedStrongholdCity(state, pCity.getID());
        }

        private static bool IsTrackedStrongholdCity(
            PeasantRebelBanditStrongholdState pState, long pCityId)
        {
            return pState != null && pCityId > 0 &&
                (pState.StrongholdCityId == pCityId ||
                 pState.InheritedStrongholdCityIds.Contains(pCityId));
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
            Kingdom origin = pMother?.kingdom;
            Actor ordinary = pMother?.units?.Where(actor =>
                    IsOrdinaryResident(actor, origin))
                .OrderBy(actor => actor.getID()).FirstOrDefault();
            if (PeasantRebelBanditStrongholdRules.ShouldPreferOrdinaryRuler(
                    ordinary != null))
                return ordinary;

            Actor cityLeader = pMother?.leader;
            bool alive = false;
            bool adult = false;
            try
            {
                alive = cityLeader != null && cityLeader.isAlive() &&
                    !cityLeader.isRekt();
                adult = alive && cityLeader.isAdult();
            }
            catch { }
            return PeasantRebelBanditStrongholdRules.CanUseCityLeaderAsRuler(
                    alive, adult, cityLeader?.city == pMother)
                ? cityLeader
                : null;
        }

        private static bool EnsureDirectBanditKing(Kingdom pBandit,
            Actor pRuler, string pPhase)
        {
            if (pBandit?.data == null || pRuler?.data == null ||
                pBandit.isRekt() || pRuler.isRekt()) return false;
            if (pBandit.king == pRuler) return true;
            try
            {
                using (EnterDirectBanditKingInstallationScope())
                    pBandit.setKing(pRuler);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit king installation failed at " +
                                    pPhase + ": " + e.Message);
                return false;
            }
            bool installed = pBandit.king == pRuler;
            if (!installed)
                ModClass.LogWarning("Bandit king installation was rejected at " +
                                    pPhase + "; kingdom=" +
                                    pBandit.getID() + "; ruler=" +
                                    pRuler.getID());
            return installed;
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
            state.Raid.CarriedFoodByActorId.Clear();
            return PeasantRebelBanditStateStore.Write(pKingdom, state);
        }

        internal static bool DestroyForOrdinaryGovernment(Kingdom pKingdom)
        {
            if (!CanMutate() || pKingdom?.data == null || pKingdom.isRekt())
                return false;
            if (!PeasantRebelBanditStateStore.TryRead(pKingdom,
                    out PeasantRebelBanditStrongholdState state)) return true;
            City stronghold = ResolveCity(state.StrongholdCityId);
            if (stronghold?.data == null || stronghold.kingdom != pKingdom)
            {
                PeasantRebelBanditStateStore.Clear(pKingdom);
                return true;
            }
            City mother = ResolveCity(state.MotherCityId);
            if (mother?.data == null || mother.isRekt() ||
                mother == stronghold)
            {
                mother = pKingdom.getCities().FirstOrDefault(city =>
                    city?.data != null && !city.isRekt() &&
                    city != stronghold);
                if (mother?.data == null) return false;
                state.MotherCityId = mother.getID();
            }
            foreach (long inheritedCityId in state.InheritedStrongholdCityIds
                         .ToList())
            {
                if (inheritedCityId <= 0 ||
                    inheritedCityId == state.StrongholdCityId) continue;
                City inherited = ResolveCity(inheritedCityId);
                if (inherited?.data == null || inherited.kingdom != pKingdom)
                    continue;
                if (!DestroyInheritedStronghold(inherited, mother))
                    return false;
            }
            if (!CompleteFall(pKingdom, stronghold, state,
                    pSuppressor: null, pRecordSuppressionChronicle: false))
                return false;
            PeasantRebelBanditStateStore.Clear(pKingdom);
            return true;
        }

        internal static bool TryCompleteLeadershipCollapse(Kingdom pBandit,
            Kingdom pSuppressor)
        {
            if (!CanMutate() || pBandit?.data == null || pBandit.isRekt() ||
                !PeasantRebelBanditStateStore.TryRead(pBandit,
                    out PeasantRebelBanditStrongholdState state)) return false;
            City stronghold = ResolveCity(state.StrongholdCityId);
            if (stronghold?.data == null || stronghold.kingdom != pBandit)
                return false;
            if (state.Phase == BanditStrongholdPhase.Completed)
                return true;
            if (state.Phase != BanditStrongholdPhase.Active &&
                state.Phase != BanditStrongholdPhase.Falling)
                return false;
            return CompleteFall(pBandit, stronghold, state, pSuppressor,
                pRecordSuppressionChronicle: true);
        }

        internal static void QueueGuiyiRestorationFall(Kingdom pBandit,
            Action<City> pOnCompleted)
        {
            if (pBandit?.data == null) return;
            long banditId = pBandit.getID();
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "guiyi_restoration_fall:" + banditId,
                DeferredWorkClass.CriticalRuntime,
                () =>
                {
                    Kingdom bandit = ResolveKingdom(banditId);
                    if (bandit?.data == null ||
                        !PeasantRebelBanditStateStore.TryRead(bandit,
                            out PeasantRebelBanditStrongholdState state))
                        return;
                    City mother = ResolveCity(state.MotherCityId);
                    if (mother?.data == null ||
                        !DestroyForOrdinaryGovernment(bandit)) return;
                    pOnCompleted?.Invoke(mother);
                });
        }

        private static bool DestroyInheritedStronghold(City pStronghold,
            City pMother)
        {
            if (pStronghold?.data == null || pMother?.data == null ||
                pStronghold == pMother) return false;
            try
            {
                WorldTile motherTile = pMother.getTile();
                foreach (Actor actor in pStronghold.units.ToList())
                {
                    if (actor?.data == null || actor.isRekt()) continue;
                    actor.joinCity(pMother);
                    if (motherTile != null) actor.spawnOn(motherTile);
                }
                List<TileZone> zones = pStronghold.zones.ToList();
                foreach (TileZone zone in zones)
                    pMother.addZone(zone);
                if (pStronghold.buildings != null &&
                    World.world?.buildings != null)
                    foreach (Building building in pStronghold.buildings
                                 .ToList())
                    {
                        if (building?.data == null) continue;
                        World.world.buildings.removeObject(building);
                    }
                foreach (TileZone zone in zones)
                    if (zone?.tiles != null)
                        foreach (WorldTile tile in zone.tiles)
                            if (tile?.top_type == TopTileLibrary.wall_wild)
                                tile.setTopTileType(null);
                pMother.recalculateNeighbourZones();
                pStronghold.recalculateNeighbourZones();
                if (!pStronghold.isRekt())
                    BanditStrongholdCityDisposalService.Schedule(
                        pStronghold.getID(),
                        pStronghold.kingdom?.getID() ?? -1L);
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Inherited bandit stronghold destruction failed: " +
                    e.Message);
                return false;
            }
        }

        internal static bool CanAcquireCity(Kingdom pKingdom, City pCity)
        {
            if (!PeasantRebelBanditStateStore.TryResolveActive(pKingdom,
                    out PeasantRebelBanditStrongholdState state)) return true;
            if (pCity?.data == null) return false;
            if (pCity.kingdom == pKingdom) return true;
            return state.Pressure >=
                       PeasantRebelBanditPressureRules.MaximumPressure &&
                   state.PressureTargetCityId == pCity.getID();
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
                !IsTrackedStrongholdCity(state, pCity.getID()) ||
                state.Phase != BanditStrongholdPhase.Active &&
                state.Phase != BanditStrongholdPhase.Falling &&
                state.Phase != BanditStrongholdPhase.Completed)
                return true;
            bool isPrimaryStronghold = state.StrongholdCityId ==
                pCity.getID();
            if (PeasantRebelBanditAnnexationRules.CanAnnex(
                    PeasantRebelBanditStateStore.TryResolveActive(
                        pOccupier, out _),
                    state.Phase == BanditStrongholdPhase.Active &&
                        (isPrimaryStronghold || state.InheritedStrongholdCityIds
                            .Contains(pCity.getID())), true,
                    pOccupier != bandit))
            {
                pHandled = CompleteBanditAnnexation(pCity, bandit,
                    pOccupier, state);
                return !pHandled;
            }
            if (!isPrimaryStronghold) return true;
            pHandled = true;
            if (!CanMutate()) return false;
            QueueFall(pCity.getID(), pOccupier?.getID() ?? -1L);
            return false;
        }

        private static bool CompleteBanditAnnexation(City pStronghold,
            Kingdom pDefender, Kingdom pAttacker,
            PeasantRebelBanditStrongholdState pState)
        {
            if (!CanMutate() || pStronghold?.data == null ||
                pDefender?.data == null || pAttacker?.data == null) return false;
            try
            {
                pStronghold.joinAnotherKingdom(pAttacker,
                    pCaptured: false, pRebellion: false);
                if (pStronghold.kingdom != pAttacker) return false;
                if (PeasantRebelBanditStateStore.TryRead(pAttacker,
                        out PeasantRebelBanditStrongholdState attackerState))
                {
                    if (!attackerState.InheritedStrongholdCityIds.Contains(
                            pStronghold.getID()))
                        attackerState.InheritedStrongholdCityIds.Add(
                            pStronghold.getID());
                    PeasantRebelBanditStateStore.Write(pAttacker,
                        attackerState);
                }
                if (pState.StrongholdCityId == pStronghold.getID())
                {
                    pState.Phase = BanditStrongholdPhase.Completed;
                    pState.SuppressorKingdomId = pAttacker.getID();
                    PeasantRebelBanditStateStore.Clear(pDefender);
                }
                else
                {
                    pState.InheritedStrongholdCityIds.Remove(
                        pStronghold.getID());
                    PeasantRebelBanditStateStore.Write(pDefender, pState);
                }
                HistoryWriter.TryRecordCity(pStronghold, pAttacker,
                    CityEvent.BANDIT_STRONGHOLD_ESTABLISHED,
                    HistoryText.City(pStronghold, pAttacker) +
                    HistoryLocalizationRules.H(
                        "aw_hist_bandit_stronghold_annexed"),
                    HistoryTarget.Kingdom(pAttacker),
                    "bandit-stronghold-annexed:" + pStronghold.getID());
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit stronghold annexation failed: " +
                                    e.Message);
                return false;
            }
        }

        internal static bool IsHostileKingdom(Kingdom pBandit,
            Kingdom pOther)
        {
            if (pBandit?.data == null || pOther?.data == null ||
                pBandit == pOther || pBandit.isRekt() || pOther.isRekt())
                return false;
            try
            {
                foreach (War war in pBandit.getWars())
                    if (war?.data != null && !war.hasEnded() &&
                        war.hasKingdom(pOther)) return true;
            }
            catch { }
            return false;
        }

        internal static void OnBanditResidentDied(long pStrongholdCityId,
            long pHostileKillerKingdomId)
        {
            if (!CanMutate() || pStrongholdCityId <= 0) return;
            City stronghold = ResolveCity(pStrongholdCityId);
            Kingdom bandit = stronghold?.kingdom;
            if (stronghold?.data == null || bandit?.data == null ||
                !PeasantRebelBanditStateStore.TryRead(bandit,
                    out PeasantRebelBanditStrongholdState state) ||
                state.Phase != BanditStrongholdPhase.Active ||
                state.StrongholdCityId != pStrongholdCityId) return;

            if (pHostileKillerKingdomId > 0 &&
                state.LastHostileKillerKingdomId !=
                pHostileKillerKingdomId)
            {
                state.LastHostileKillerKingdomId =
                    pHostileKillerKingdomId;
                if (!PeasantRebelBanditStateStore.Write(bandit, state))
                    return;
            }

            PeasantRebelBanditStrongholdPopulationService.
                EnqueueStronghold(pStrongholdCityId);
        }

        internal static void QueuePopulationFall(long pStrongholdCityId)
        {
            QueueFall(pStrongholdCityId, -1L);
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
                    {
                        int population;
                        try
                        {
                            population = stronghold.getPopulationPeople();
                        }
                        catch { continue; }
                        BanditStrongholdFallAction action =
                            PeasantRebelBanditStrongholdRules.
                                ResolveFallAction(population,
                                    state.LastHostileKillerKingdomId,
                                    captureFinished: false);
                        if (action == BanditStrongholdFallAction.QueueFall)
                            QueueFall(stronghold.getID(), -1L);
                        continue;
                    }
                    if (stronghold?.data == null)
                    {
                        state.Phase = BanditStrongholdPhase.Completed;
                        state.Raid.Stage = BanditRaidStage.None;
                        state.Raid.MemberActorIds.Clear();
                        state.Raid.CarriedFood = 0;
                        state.Raid.CarriedFoodByResourceId.Clear();
                        state.Raid.CarriedFoodByActorId.Clear();
                        PeasantRebelBanditStateStore.Write(kingdom, state);
                    }
                    continue;
                }
                if ((state.Phase == BanditStrongholdPhase.Falling ||
                     state.Phase == BanditStrongholdPhase.Completed) &&
                    stronghold?.data != null)
                    QueueFall(stronghold.getID(),
                        state.SuppressorKingdomId);
            }
            PeasantRebelBanditIslandMigrationService.RestoreRuntime();
        }

        private static void QueueFall(long pStrongholdCityId,
            long pSuppressorKingdomId)
        {
            if (pStrongholdCityId <= 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "bandit_stronghold_fall:" + pStrongholdCityId,
                DeferredWorkClass.CriticalRuntime,
                () =>
                {
                    if (!CanMutate()) return;
                    City stronghold = ResolveCity(pStrongholdCityId);
                    Kingdom bandit = stronghold?.kingdom;
                    if (stronghold?.data == null || bandit?.data == null ||
                        !PeasantRebelBanditStateStore.TryRead(bandit,
                            out PeasantRebelBanditStrongholdState state) ||
                        state.StrongholdCityId != pStrongholdCityId ||
                        state.Phase != BanditStrongholdPhase.Active &&
                        state.Phase != BanditStrongholdPhase.Falling &&
                        state.Phase != BanditStrongholdPhase.Completed)
                        return;
                    Kingdom suppressor = ResolveKingdom(
                        pSuppressorKingdomId);
                    CompleteFall(bandit, stronghold, state, suppressor);
                });
        }

        private static bool CompleteFall(Kingdom pBandit,
            City pStronghold, PeasantRebelBanditStrongholdState pState,
            Kingdom pSuppressor = null,
            bool pRecordSuppressionChronicle = true)
        {
            City mother = ResolveCity(pState.MotherCityId);
            if (pBandit?.data == null || pStronghold?.data == null ||
                mother?.data == null || mother.isRekt()) return false;
            Kingdom motherKingdom = mother.kingdom;
            if (motherKingdom?.data == null || motherKingdom.asset == null ||
                motherKingdom.isRekt() || motherKingdom == pBandit)
                return false;
            try
            {
                if (pSuppressor?.data != null &&
                    pSuppressor != pBandit && !pSuppressor.isRekt())
                    pState.SuppressorKingdomId = pSuppressor.getID();
                if (pState.SuppressorKingdomId <= 0)
                {
                    Kingdom origin = ResolveKingdom(pState.OriginKingdomId);
                    pState.SuppressorKingdomId =
                        PeasantRebelBanditStrongholdRules.
                            ResolveSuppressorKingdomId(
                                pState.LastHostileKillerKingdomId,
                                pState.OriginKingdomId,
                                IsHostileKingdom(pBandit, origin));
                }
                if (pState.Phase == BanditStrongholdPhase.Active)
                {
                    pState.Phase = BanditStrongholdPhase.Falling;
                    if (!PeasantRebelBanditStateStore.Write(pBandit,
                            pState)) return false;
                }

                Kingdom suppressor = ResolveKingdom(
                    pState.SuppressorKingdomId);
                if (pRecordSuppressionChronicle &&
                    !RecordSuppressionChronicles(pBandit, pStronghold,
                        suppressor)) return false;
                if (pRecordSuppressionChronicle && string.Equals(
                        pState.RouteSubtype,
                        PeasantRebelGuiyiRules.RouteSubtype,
                        StringComparison.Ordinal))
                    PeasantRebelGuiyiService.RecordSuppressed(pBandit,
                        suppressor, pStronghold);

                WorldTile motherTile = mother.getTile();
                foreach (Actor actor in pStronghold.units.ToList())
                {
                    if (actor?.data == null || actor.isRekt()) continue;
                    bool wasBanditSurvivor =
                        PeasantRebelBanditStrongholdRules.
                            ShouldTransferFallenSurvivor(
                                actor.isAlive(), actor.kingdom == pBandit,
                                true,
                                motherKingdom == pBandit);
                    using (FormalAffiliationTransferScope.Open(
                               actor.data.id, motherKingdom.id,
                               mother.data.id))
                    {
                        actor.joinCity(mother);
                        if (wasBanditSurvivor &&
                            actor.kingdom != motherKingdom)
                            actor.joinKingdom(motherKingdom);
                    }
                    if (motherTile != null) actor.spawnOn(motherTile);
                }
                foreach (TileZone zone in pStronghold.zones.ToList())
                    mother.addZone(zone);
                mother.recalculateNeighbourZones();
                pStronghold.recalculateNeighbourZones();
                if (!RemoveStrongholdTowers(pState)) return false;
                if (!RestoreWalls(pState)) return false;

                pState.Phase = BanditStrongholdPhase.Completed;
                pState.Raid.Stage = BanditRaidStage.None;
                pState.Raid.MemberActorIds.Clear();
                pState.Raid.CarriedFood = 0;
                pState.Raid.CarriedFoodByResourceId.Clear();
                pState.Raid.CarriedFoodByActorId.Clear();
                if (!PeasantRebelBanditStateStore.Write(pBandit, pState))
                    return false;
                if (!pStronghold.isRekt())
                    BanditStrongholdCityDisposalService.Schedule(
                        pStronghold.getID(),
                        pStronghold.kingdom?.getID() ?? -1L);
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit stronghold fall failed: " +
                                    e.Message);
                return false;
            }
        }

        internal static void QueueOrphanCleanup(Kingdom pBandit)
        {
            if (!CanMutate() || pBandit?.data == null ||
                !PeasantRebelBanditStateStore.TryRead(pBandit,
                    out PeasantRebelBanditStrongholdState state)) return;
            int liveCities = PeasantRebelRouteService.SafeCityCount(pBandit);
            if (!PeasantRebelBanditPressureRules.ShouldQueueOrphanCleanup(
                    liveCities)) return;
            long banditId = pBandit.getID();
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "bandit_stronghold_orphan_cleanup:" + banditId,
                DeferredWorkClass.CriticalRuntime,
                () =>
                {
                    RemoveStrongholdTowers(state);
                    RestoreWalls(state);
                    Kingdom resolved = ResolveKingdom(banditId);
                    if (resolved?.data != null &&
                        PeasantRebelRouteService.SafeCityCount(resolved) == 0)
                        PeasantRebelBanditStateStore.Clear(resolved);
                    PeasantRebelBanditPressureService.
                        InvalidateTargetIndex();
                });
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

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            if (pKingdomId <= 0 || World.world?.kingdoms == null)
                return null;
            try
            {
                Kingdom kingdom = World.world.kingdoms.get(pKingdomId);
                return kingdom?.data != null && !kingdom.isRekt()
                    ? kingdom
                    : null;
            }
            catch { return null; }
        }

        private static bool RecordEstablishment(City pStronghold,
            Kingdom pBandit)
        {
            if (pStronghold?.data == null || pBandit?.data == null)
                return false;
            return HistoryWriter.TryRecordCity(pStronghold, pBandit,
                CityEvent.BANDIT_STRONGHOLD_ESTABLISHED,
                HistoryLocalizationRules.H(
                    "aw_hist_bandit_stronghold_established_prefix") +
                HistoryText.City(pStronghold, pBandit),
                HistoryTarget.City(pStronghold),
                "bandit-stronghold-established:" + pStronghold.getID());
        }

        private static bool RecordSuppressionChronicles(Kingdom pBandit,
            City pStronghold, Kingdom pSuppressor)
        {
            if (pBandit?.data == null || pStronghold?.data == null)
                return false;
            long cityId = pStronghold.getID();
            long banditId = pBandit.getID();
            bool hasSuppressor = pSuppressor?.data != null &&
                                 pSuppressor != pBandit &&
                                 !pSuppressor.isRekt();
            HistoryText cityFall = HistoryText.City(pStronghold, pBandit);
            if (hasSuppressor)
                cityFall += HistoryLocalizationRules.H(
                                "aw_hist_bandit_stronghold_suppressed_by") +
                            HistoryText.Kingdom(pSuppressor) +
                            HistoryLocalizationRules.H(
                                "aw_hist_bandit_stronghold_suppressed_suffix");
            else
                cityFall += HistoryLocalizationRules.H(
                    "aw_hist_bandit_stronghold_empty_suffix");

            if (!HistoryWriter.TryRecordCity(pStronghold, pBandit,
                    CityEvent.BANDIT_STRONGHOLD_SUPPRESSED, cityFall,
                    hasSuppressor
                        ? HistoryTarget.Kingdom(pSuppressor)
                        : HistoryTarget.City(pStronghold),
                    "bandit-suppressed-city:" + cityId)) return false;

            HistoryText realmFall = HistoryText.Kingdom(pBandit);
            if (hasSuppressor)
                realmFall += HistoryLocalizationRules.H(
                                 "aw_hist_bandit_suppressed_mid") +
                             HistoryText.Kingdom(pSuppressor) +
                             HistoryLocalizationRules.H(
                                 "aw_hist_bandit_suppressed_suffix");
            else
                realmFall += HistoryLocalizationRules.H(
                    "aw_hist_bandit_stronghold_empty_suffix");
            if (!HistoryWriter.TryRecordKingdom(pBandit,
                    KingdomEvent.BANDIT_SUPPRESSED, realmFall,
                    hasSuppressor
                        ? HistoryTarget.Kingdom(pSuppressor)
                        : HistoryTarget.City(pStronghold),
                    "bandit-suppressed-kingdom:" + banditId + ":" +
                    cityId)) return false;

            if (!hasSuppressor) return true;
            return HistoryWriter.TryRecordKingdom(pSuppressor,
                KingdomEvent.BANDIT_SUPPRESSION_VICTORY,
                HistoryLocalizationRules.H(
                    "aw_hist_bandit_suppression_victory_prefix") +
                HistoryText.City(pStronghold, pBandit) +
                HistoryLocalizationRules.H(
                    "aw_hist_bandit_suppression_victory_suffix"),
                HistoryTarget.Kingdom(pBandit),
                "bandit-suppression-victory:" + pSuppressor.getID() +
                ":" + cityId);
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
                if (actor == pPlan.Context.Ruler)
                {
                    actor.joinCity(pStronghold);
                    continue;
                }
                if (interior.Contains(actor.current_tile?.zone) &&
                    IsOrdinaryResident(actor, pPlan.Context.Origin))
                    actor.joinCity(pStronghold);
            }
            pPlan.Context.Ruler.joinCity(pStronghold);
            pPlan.Context.Ruler.spawnOn(pStronghold.getTile());
            if (pPlan.ReserveMotherActor != null &&
                pPlan.MotherCoreTile != null && interior.Contains(
                    pPlan.ReserveMotherActor.current_tile?.zone))
                pPlan.ReserveMotherActor.spawnOn(pPlan.MotherCoreTile);
        }

        private static bool IsOrdinaryResident(Actor pActor,
            Kingdom pOrigin)
        {
            if (pActor?.data == null || pActor.isRekt()) return false;
            bool adult;
            bool king;
            bool cityLeader;
            try
            {
                adult = pActor.isAdult();
                king = pActor.isKing();
                cityLeader = pActor.isCityLeader();
            }
            catch { return false; }
            return PeasantRebelBanditStrongholdRules.
                CanRelocateOrdinaryResident(adult,
                    pActor.profession_asset?.is_civilian == true, king,
                    cityLeader,
                    HeirService.IsCurrentHeir(pOrigin, pActor));
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
            plan.ReserveMotherActor?.joinCity(plan.Context.Mother);
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

        private static BuildingAsset ResolveTowerAsset(Actor pRuler)
        {
            try
            {
                return pRuler?.asset?.architecture_asset?
                    .getBuilding("order_watch_tower");
            }
            catch { return null; }
        }

        private static List<WorldTile> FindGateTowerTiles(
            BanditZoneWallPlan pWallPlan, WorldTile pCenter,
            BuildingAsset pAsset, IReadOnlyCollection<TileZone> pZones)
        {
            if (pWallPlan?.GateCenters == null ||
                pWallPlan.GateCenters.Count != 4 || pCenter == null ||
                pAsset?.fundament == null || pZones == null ||
                pZones.Count != 4)
            {
                LogGateTowerFailure("invalid_input", pAsset, pCenter,
                    pZones?.Count ?? 0, null, null);
                return null;
            }
            var zones = new HashSet<TileZone>(pZones);
            var territoryPoints = new HashSet<CultiwayWallPoint>();
            foreach (TileZone zone in zones)
            {
                if (zone?.tiles == null) continue;
                foreach (WorldTile tile in zone.tiles)
                    if (tile != null)
                        territoryPoints.Add(new CultiwayWallPoint(
                            tile.x, tile.y));
            }
            var wallPoints = new HashSet<CultiwayWallPoint>(
                pWallPlan.WallPoints);
            var reservedFootprint = new HashSet<WorldTile>();
            var result = new List<WorldTile>(4);
            for (int gateIndex = 0;
                 gateIndex < pWallPlan.GateCenters.Count;
                 gateIndex++)
            {
                CultiwayWallPoint gate = pWallPlan.GateCenters[gateIndex];
                WorldTile selected = null;
                var rejected = new List<string>();
                foreach (CultiwayWallPoint point in
                         PeasantRebelBanditZoneWallRules.
                             RankInwardTowerCandidates(gate,
                                 GateTowerInwardDirections[gateIndex],
                                 GateTowerInwardSearchSteps))
                {
                    WorldTile tile = World.world.GetTile(point.X, point.Y);
                    string reason = null;
                    bool verticalGate = gateIndex == 0 || gateIndex == 2;
                    if (!IsTowerFootprintInside(tile, pAsset, zones,
                            territoryPoints, verticalGate,
                            out List<WorldTile> footprint))
                        reason = "footprint";
                    else if (reservedFootprint.Overlaps(footprint))
                        reason = "reserved";
                    else if (footprint.Any(footprintTile =>
                                 wallPoints.Contains(new CultiwayWallPoint(
                                     footprintTile.x, footprintTile.y))))
                        reason = "wall";
                    else if (!CanPlaceGateTower(tile, pAsset))
                        reason = "native";
                    if (reason != null)
                    {
                        rejected.Add(point.X + ":" + point.Y + "=" +
                                     reason);
                        continue;
                    }
                    selected = tile;
                    reservedFootprint.UnionWith(footprint);
                    break;
                }
                if (selected == null)
                {
                    LogGateTowerFailure("gate_rejected", pAsset, pCenter,
                        pZones.Count, gate, rejected);
                    return null;
                }
                result.Add(selected);
            }
            return result.Distinct().Count() == 4 ? result : null;
        }

        private static void LogGateTowerFailure(string pStage,
            BuildingAsset pAsset, WorldTile pCenter, int pZoneCount,
            CultiwayWallPoint? pGate,
            IReadOnlyCollection<string> pRejected)
        {
            BuildingFundament fundament = pAsset?.fundament;
            string footprint = fundament == null
                ? "null"
                : fundament.left + "/" + fundament.right + "/" +
                  fundament.top + "/" + fundament.bottom;
            ModClass.LogWarning("Bandit gate tower preflight failed: stage=" +
                pStage + ", asset=" + (pAsset?.id ?? "null") +
                ", footprint=" + footprint + ", center=" +
                (pCenter == null ? "null" : pCenter.x + ":" + pCenter.y) +
                ", zones=" + pZoneCount + ", gate=" +
                (!pGate.HasValue ? "null" :
                    pGate.Value.X + ":" + pGate.Value.Y) +
                ", rejected=[" + string.Join(",", pRejected ??
                    Array.Empty<string>()) + "]");
        }

        private static bool IsTowerFootprintInside(WorldTile pTile,
            BuildingAsset pAsset, HashSet<TileZone> pZones,
            HashSet<CultiwayWallPoint> pTerritoryPoints,
            bool pAllowOneTileBoundaryStraddle,
            out List<WorldTile> pFootprint)
        {
            pFootprint = null;
            BuildingFundament fundament = pAsset?.fundament;
            if (pTile == null || fundament == null || pZones == null ||
                pZones.Count == 0) return false;
            var footprint = new List<WorldTile>(
                fundament.width * fundament.height);
            int originX = pTile.x - fundament.left;
            int originY = pTile.y - fundament.bottom;
            for (int x = 0; x < fundament.width; x++)
            for (int y = 0; y < fundament.height; y++)
            {
                WorldTile tile = World.world.GetTile(
                    originX + x, originY + y);
                if (tile == null) return false;
                footprint.Add(tile);
            }
            if (pAllowOneTileBoundaryStraddle)
            {
                if (!PeasantRebelBanditZoneWallRules.
                        IsOneTileBoundaryStraddleAllowed(
                            footprint.Select(tile =>
                                new CultiwayWallPoint(tile.x, tile.y)),
                            pTerritoryPoints)) return false;
            }
            else if (footprint.Any(tile => !pZones.Contains(tile.zone)))
                return false;
            pFootprint = footprint;
            return true;
        }

        private static bool CanPlaceGateTower(WorldTile pTile,
            BuildingAsset pAsset)
        {
            return pTile != null && pAsset != null &&
                   World.world?.buildings != null &&
                   World.world.buildings.canBuildFrom(pTile, pAsset,
                       null, BuildPlacingType.Load);
        }

        private static void PlaceTowers(Transaction pTransaction,
            City pStronghold)
        {
            PeasantRebelBanditStrongholdPlan plan = pTransaction.Plan;
            if (pStronghold?.data == null || plan == null)
                throw new InvalidOperationException(
                    "stronghold tower context is invalid");
            if (plan.TowerAsset == null || plan.TowerTiles == null ||
                plan.TowerTiles.Count == 0) return;
            foreach (WorldTile tile in plan.TowerTiles)
            {
                Building building = World.world.buildings.addBuilding(
                    plan.TowerAsset, tile, pCheckForBuild: true,
                    pSfx: false, pType: BuildPlacingType.Load);
                if (building?.data == null)
                {
                    ModClass.LogWarning(
                        "Bandit gate tower creation skipped at " +
                        (tile == null ? "null" : tile.x + ":" + tile.y));
                    continue;
                }
                building.setKingdom(plan.Context.Bandit);
                pTransaction.Towers.Add(building);
            }
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
                PressureTargetCityId = plan.Context.Mother.getID(),
                Pressure = 0,
                LastPressureYear = Date.getCurrentYear(),
                FixedZoneKeys = new List<string>(plan.FixedZoneKeys),
                WallPoints = pTransaction.WallTiles.Select(snapshot =>
                    new BanditStrongholdPoint
                    {
                        X = snapshot.Tile.x,
                        Y = snapshot.Tile.y,
                        OriginalTopTypeId = snapshot.TopType?.id ?? ""
                    }).ToList(),
                Towers = pTransaction.Towers.Select(building =>
                    new BanditStrongholdTower
                    {
                        TowerBuildingId = building.getID(),
                        X = building.current_tile.x,
                        Y = building.current_tile.y,
                        AssetId = building.asset?.id ?? ""
                    }).ToList()
            };
        }

        private static bool RemoveStrongholdTowers(
            PeasantRebelBanditStrongholdState pState)
        {
            if (pState?.Towers == null || World.world?.buildings == null)
                return true;
            foreach (BanditStrongholdTower towerState in pState.Towers)
            {
                if (towerState == null || towerState.TowerBuildingId <= 0)
                    continue;
                Building building = null;
                try
                {
                    building = World.world.buildings.get(
                        towerState.TowerBuildingId);
                }
                catch { }
                if (building?.data == null || building.isRekt() ||
                    building.isRemoved() || building.isOnRemove() ||
                    building.current_tile?.zone == null) continue;
                try
                {
                    building.removeBuildingFinal();
                }
                catch (Exception e)
                {
                    ModClass.LogWarning(
                        "Bandit stronghold tower removal failed: " +
                        e.Message);
                    return false;
                }
            }
            return true;
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
                foreach (Building tower in pTransaction.Towers)
                    if (tower?.data != null)
                        World.world.buildings.removeObject(tower);
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
                    BanditStrongholdCityDisposalService.Schedule(
                        pTransaction.Stronghold.getID(),
                        pTransaction.Stronghold.kingdom?.getID() ?? -1L);
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
                        pTransaction.Actors, plan.Context.Ruler);
                    RemoveBanditKingdomAndDrain(plan.Context.Bandit);
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
            IReadOnlyCollection<ActorSnapshot> pSnapshots,
            Actor pPrimaryActor)
        {
            if (pBandit == null) return;
            var candidates = new HashSet<Actor>();
            var snapshotCities = new Dictionary<Actor, City>();
            if (pPrimaryActor != null) candidates.Add(pPrimaryActor);
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
                ActorKingdomSafetyService.DetachForTransfer(actor);
            }
        }

        private static void RemoveBanditKingdomAndDrain(Kingdom pBandit)
        {
            if (pBandit?.data == null) return;
            World.world.kingdoms.removeObject(pBandit);

            // The native manager only disposes removed kingdoms at the next
            // maintenance boundary. Drain actor repairs now, while the
            // actor still has a valid asset/tile and before map layers draw.
            ActorKingdomSafetyService.DrainPendingRepairs(4096);
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
