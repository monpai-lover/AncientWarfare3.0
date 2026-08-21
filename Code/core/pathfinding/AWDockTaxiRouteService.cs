using System;
using System.Collections.Generic;
using life.taxi;

namespace AncientWarfare3.core.pathfinding
{
    internal readonly struct AWDockTaxiRouteBinding
    {
        internal AWDockTaxiRouteBinding(AWDockRouteCandidate pRoute,
            int pTargetTileId, Kingdom pKingdom)
        {
            Route = pRoute;
            TargetTileId = pTargetTileId;
            Kingdom = pKingdom;
        }

        internal AWDockRouteCandidate Route { get; }
        internal int TargetTileId { get; }
        internal Kingdom Kingdom { get; }
        internal int EntryLandTileId => Route.Entry.LandTileId;
        internal int PickupSeaTileId => Route.Entry.OceanTileId;
        internal int DestinationSeaTileId => Route.Exit.OceanTileId;
        internal int LandingLandTileId => Route.Exit.LandTileId;
    }

    internal static class AWDockTaxiRouteService
    {
        private static readonly Dictionary<TaxiRequest,
            AWDockTaxiRouteBinding> Bindings =
            new Dictionary<TaxiRequest, AWDockTaxiRouteBinding>();

        internal static bool TryCreateOrJoinRequest(Actor pActor,
            AWPathStep pStep, out TaxiRequest pRequest)
        {
            pRequest = null;
            if (pActor?.data == null || pActor.kingdom?.data == null ||
                pActor.is_inside_boat ||
                !TryBuildRoute(pStep, out AWDockRouteCandidate route) ||
                !AWDockTransportService.TryResolveRouteTiles(route,
                    out WorldTile entryLand, out _, out _,
                    out WorldTile landingLand) ||
                entryLand?.data == null || landingLand?.data == null)
                return false;

            Kingdom kingdom = pActor.kingdom;
            foreach (KeyValuePair<TaxiRequest,
                AWDockTaxiRouteBinding> pair in Bindings)
            {
                TaxiRequest existing = pair.Key;
                AWDockTaxiRouteBinding binding = pair.Value;
                if (!IsPending(existing) ||
                    binding.Kingdom != kingdom ||
                    binding.TargetTileId != pStep.TileId ||
                    !SameRoute(binding.Route, route)) continue;
                existing.addActor(pActor);
                pRequest = existing;
                return true;
            }

            var created = new TaxiRequest(pActor, kingdom, entryLand,
                landingLand);
            TaxiManager.list.Add(created);
            Bindings[created] = new AWDockTaxiRouteBinding(route,
                pStep.TileId, kingdom);
            pRequest = created;
            return true;
        }

        internal static bool TryCreateOrJoinRequest(Actor pActor,
            WorldTile pTarget, out TaxiRequest pRequest)
        {
            pRequest = null;
            return TryCreateOrJoinRequest(pActor, pActor?.current_tile,
                pTarget, out pRequest);
        }

        internal static bool TryCreateOrJoinRequest(Actor pActor,
            WorldTile pEntryTile, WorldTile pTarget,
            out TaxiRequest pRequest)
        {
            pRequest = null;
            if (pActor?.data == null || pEntryTile?.data == null ||
                pTarget?.data == null || pActor.is_inside_boat)
                return false;
            if (!AWDockTransportService.TryResolveRoute(pEntryTile,
                    pTarget, out AWDockRouteCandidate route)) return false;
            var step = new AWPathStep(pTarget.data.tile_id,
                AWMovementMethod.Transport,
                new AWTraversalEstimate(route.EstimatedRouteTiles, 0f, 0f,
                    0f, AWHazardFlags.Ocean), -1L,
                AWPathTileFlags.None, route.Entry.Id, route.Exit.Id,
                route.Entry.LandTileId, route.Entry.OceanTileId,
                route.Exit.OceanTileId, route.Exit.LandTileId);
            return TryCreateOrJoinRequest(pActor, step, out pRequest);
        }

        internal static bool TryGetBinding(TaxiRequest pRequest,
            out AWDockTaxiRouteBinding pBinding)
        {
            if (pRequest != null && Bindings.TryGetValue(pRequest,
                    out pBinding)) return true;
            pBinding = default;
            return false;
        }

        internal static bool HasLiveAssignedBoat(TaxiRequest pRequest)
        {
            try
            {
                Boat boat = pRequest?.getBoat();
                Actor actor = boat?.actor;
                return actor?.data != null && actor.isAlive() &&
                       !actor.isRekt();
            }
            catch { return false; }
        }

        internal static bool TryGetNewRequestForBoat(Actor pBoatActor,
            out TaxiRequest pRequest)
        {
            pRequest = null;
            if (pBoatActor?.data == null || pBoatActor.kingdom?.data == null)
                return false;
            if (!AWDockTransportService.TryGetWaterComponent(
                    pBoatActor.current_tile, out int component)) return false;

            int bestCount = -1;
            for (int i = 0; i < TaxiManager.list.Count; i++)
            {
                TaxiRequest request = TaxiManager.list[i];
                if (!TryGetBinding(request,
                        out AWDockTaxiRouteBinding binding) ||
                    binding.Kingdom != pBoatActor.kingdom) continue;
                try
                {
                    if (request.isState(TaxiRequestState.Assigned) &&
                        request.isAssignedToBoat(pBoatActor))
                    {
                        pRequest = request;
                        return true;
                    }
                    if (!IsPending(request) ||
                        binding.Route.Entry.WaterComponent != component ||
                        !AWDockTransportService.IsRouteLive(binding.Route))
                        continue;
                    int count = request.countActors();
                    if (count > bestCount)
                    {
                        bestCount = count;
                        pRequest = request;
                    }
                }
                catch { }
            }
            return pRequest != null;
        }

        internal static void Remove(TaxiRequest pRequest)
        {
            if (pRequest != null) Bindings.Remove(pRequest);
        }

        internal static void Clear()
        {
            Bindings.Clear();
        }

        private static bool TryBuildRoute(AWPathStep pStep,
            out AWDockRouteCandidate pRoute)
        {
            pRoute = default;
            bool dock = pStep.EntryPortalId > 0L &&
                        pStep.ExitPortalId > 0L;
            bool shore = pStep.EntryPortalId == 0L &&
                         pStep.ExitPortalId == 0L;
            if ((!dock && !shore) || pStep.EntryLandTileId < 0 ||
                pStep.PickupSeaTileId < 0 ||
                pStep.DestinationSeaTileId < 0 ||
                pStep.LandingLandTileId < 0 ||
                !AWDockTransportService.TryGetWaterComponent(
                    ResolveTile(pStep.PickupSeaTileId),
                    out int component) ||
                !AWDockTransportService.TryGetWaterComponent(
                    ResolveTile(pStep.DestinationSeaTileId),
                    out int destination) || component != destination)
                return false;
            var entry = new AWDockEndpoint(pStep.EntryPortalId,
                pStep.EntryLandTileId, pStep.PickupSeaTileId, component);
            var exit = new AWDockEndpoint(pStep.ExitPortalId,
                pStep.LandingLandTileId, pStep.DestinationSeaTileId,
                destination);
            pRoute = new AWDockRouteCandidate(dock
                    ? AWTransportRouteSource.DockPortal
                    : AWTransportRouteSource.ShoreFallback,
                entry, exit, 0f);
            if (!pRoute.IsValid) return false;
            if (dock && (!AWDockTransportService.IsEndpointLive(entry.Id) ||
                         !AWDockTransportService.IsEndpointLive(exit.Id)))
                return false;
            return true;
        }

        private static bool IsPending(TaxiRequest pRequest)
        {
            try
            {
                return pRequest != null &&
                       pRequest.isState(TaxiRequestState.Pending) &&
                       pRequest.countActors() > 0;
            }
            catch { return false; }
        }

        private static bool SameRoute(AWDockRouteCandidate pLeft,
            AWDockRouteCandidate pRight)
        {
            return pLeft.Source == pRight.Source &&
                   pLeft.Entry.Id == pRight.Entry.Id &&
                   pLeft.Exit.Id == pRight.Exit.Id &&
                   pLeft.Entry.LandTileId == pRight.Entry.LandTileId &&
                   pLeft.Entry.OceanTileId == pRight.Entry.OceanTileId &&
                   pLeft.Exit.LandTileId == pRight.Exit.LandTileId &&
                   pLeft.Exit.OceanTileId == pRight.Exit.OceanTileId &&
                   pLeft.Entry.WaterComponent == pRight.Entry.WaterComponent;
        }

        private static WorldTile ResolveTile(int pTileId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            return tiles != null && pTileId >= 0 && pTileId < tiles.Length
                ? tiles[pTileId]
                : null;
        }
    }
}
