using System.Collections.Generic;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.performance;
using HarmonyLib;
using ai.behaviours;
using life.taxi;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Keeps the AW3 boat index in sync with the native actor transport
    /// lifecycle. Native mode remains untouched so the original scheduler
    /// owns all boat checks when the large scheduler is disabled.
    /// </summary>
    [HarmonyPatch]
    internal static class AW_ActorBoatLifecyclePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehBoatFindRequest),
            nameof(BehBoatFindRequest.execute))]
        private static bool BoatFindRequestPrefix(
            BehBoatFindRequest __instance, Actor pActor,
            ref BehResult __result)
        {
            if (!PathfindingOwnershipService.ShouldIntercept ||
                __instance?.boat == null || pActor?.data == null)
                return true;

            Boat boat = __instance.boat;
            TaxiRequest current = boat.taxi_request;
            if (current != null &&
                AWDockTaxiRouteService.TryGetBinding(current, out _) &&
                !current.isAlreadyUsedByBoat(pActor))
            {
                TaxiManager.cancelRequest(current);
                boat.taxi_request = null;
            }

            if (!AWDockTaxiRouteService.TryGetNewRequestForBoat(
                    pActor, out TaxiRequest request)) return true;
            request.assign(boat);
            boat.taxi_request = request;
            __result = __instance.forceTask(pActor,
                "boat_transport_go_load", pClean: true,
                pForceAction: true);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehBoatTransportFindTilePickUp),
            nameof(BehBoatTransportFindTilePickUp.execute))]
        private static bool BoatPickupPrefix(
            BehBoatTransportFindTilePickUp __instance, Actor pActor,
            ref BehResult __result)
        {
            TaxiRequest request = __instance?.boat?.taxi_request;
            if (!PathfindingOwnershipService.ShouldIntercept ||
                !AWDockTaxiRouteService.TryGetBinding(request,
                    out AWDockTaxiRouteBinding binding)) return true;
            WorldTile pickup = ResolveTile(binding.PickupSeaTileId);
            if (pickup?.data == null ||
                !AWDockTransportService.IsRouteLive(binding.Route))
            {
                TaxiManager.cancelRequest(request);
                __result = BehResult.Stop;
                return false;
            }
            __instance.boat.passengerWaitCounter = 0;
            __instance.boat.pickup_near_dock = false;
            pActor.beh_tile_target = pickup;
            __result = BehResult.Continue;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehBoatTransportFindTileUnload),
            nameof(BehBoatTransportFindTileUnload.execute))]
        private static bool BoatUnloadTargetPrefix(
            BehBoatTransportFindTileUnload __instance, Actor pActor,
            ref BehResult __result)
        {
            TaxiRequest request = __instance?.boat?.taxi_request;
            if (!PathfindingOwnershipService.ShouldIntercept ||
                !AWDockTaxiRouteService.TryGetBinding(request,
                    out AWDockTaxiRouteBinding binding)) return true;
            WorldTile destination = ResolveTile(
                binding.DestinationSeaTileId);
            if (destination?.data == null)
            {
                TaxiManager.cancelRequest(request);
                __result = BehResult.Stop;
                return false;
            }
            pActor.beh_tile_target = destination;
            __result = BehResult.Continue;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehBoatTransportUnloadUnits),
            nameof(BehBoatTransportUnloadUnits.execute))]
        private static bool BoatUnloadUnitsPrefix(
            BehBoatTransportUnloadUnits __instance, Actor pActor,
            ref BehResult __result)
        {
            TaxiRequest request = __instance?.boat?.taxi_request;
            if (!PathfindingOwnershipService.ShouldIntercept ||
                !AWDockTaxiRouteService.TryGetBinding(request,
                    out AWDockTaxiRouteBinding binding)) return true;
            WorldTile landing = ResolveTile(binding.LandingLandTileId);
            if (landing?.data == null)
            {
                TaxiManager.cancelRequest(request);
                __result = BehResult.Stop;
                return false;
            }
            var passengers = new List<Actor>();
            try
            {
                HashSet<Actor> requested = request.getActors();
                if (requested != null) passengers.AddRange(requested);
            }
            catch { }
            try { __instance.boat.unloadPassengers(landing, false); }
            catch { }
            for (int i = 0; i < passengers.Count; i++)
                AWInsideBoatActorIndex.Notify(passengers[i], false);
            TaxiManager.finish(request);
            __instance.boat.taxi_request = null;
            __instance.boat.taxi_target = null;
            __result = BehResult.Stop;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehBoatTransportDoLoading),
            nameof(BehBoatTransportDoLoading.execute))]
        private static void BoatTransportDoLoadingPrefix(
            BehBoatTransportDoLoading __instance, Actor pActor)
        {
            if (!PathfindingOwnershipService.ShouldIntercept ||
                __instance?.boat == null || pActor?.data == null)
                return;
            Boat boat = __instance.boat;
            TaxiRequest request = boat.taxi_request;
            if (request == null) return;
            LoadCommonPassengers(pActor, boat, request);
        }

        private static void LoadCommonPassengers(Actor pBoatActor,
            Boat boat, TaxiRequest request)
        {
            HashSet<Actor> requested;
            try { requested = request.getActors(); }
            catch { requested = null; }
            if (requested == null || requested.Count == 0) return;

            var passengers = new List<Actor>(requested);
            int loaded = 0;
            for (int i = 0; i < passengers.Count; i++)
            {
                Actor passenger = passengers[i];
                bool valid = false;
                try
                {
                    valid = passenger?.data != null &&
                            passenger.isAlive() && !passenger.isRekt() &&
                            !passenger.is_inside_boat;
                }
                catch { }
                if (!valid) continue;
                try
                {
                    passenger.stopMovement();
                }
                catch
                {
                    try { passenger._is_moving = false; }
                    catch { }
                }
                try
                {
                    passenger.clearOldPath();
                    passenger.data.transportID = pBoatActor.data.id;
                    passenger.is_inside_boat = true;
                    passenger.inside_boat = boat;
                    passenger.setCurrentTilePosition(
                        pBoatActor.current_tile);
                    passenger.next_step_position =
                        pBoatActor.current_position;
                    boat.addPassenger(passenger);
                    AWInsideBoatActorIndex.Notify(passenger, true);
                    loaded++;
                }
                catch { }
            }

            if (loaded > 0 &&
                AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
            {
                ModClass.LogInfo(
                    "[AW3 vanilla taxi] phase=boat_side_loaded boat=" +
                    pBoatActor.data.id + " count=" + loaded);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BatchActors), "u1_checkInside")]
        private static bool CheckInsideBatchPrefix(
            BatchActors __instance)
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler)
            {
                return true;
            }

            if (!AWInsideBoatActorIndex.TryGetSnapshot(
                    __instance,
                    out Actor[] actors,
                    out int count))
            {
                // Actors that were already aboard when the scheduler was
                // enabled have not passed through embarkInto yet. Keep the
                // native scan for this unindexed batch so they are not
                // stranded; subsequent lifecycle notifications use the
                // incremental index.
                return true;
            }

            int processed = 0;
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                if (actor == null ||
                    actor.data == null ||
                    !ReferenceEquals(actor.batch, __instance) ||
                    !actor.is_inside_boat)
                {
                    AWInsideBoatActorIndex.Notify(actor, false);
                    continue;
                }

                actor.u1_checkInside(0f);
                processed++;
            }

            AWInsideBoatActorIndex.RecordProcessed(processed);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.u1_checkInside))]
        private static bool CheckInsidePrefix(Actor __instance)
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler)
            {
                return true;
            }

            if (__instance == null ||
                __instance.data == null ||
                !__instance.isInsideSomething())
            {
                return false;
            }

            if (__instance.is_inside_boat)
            {
                Actor boat = __instance.inside_boat?.actor ??
                    World.world?.units?.get(__instance.data.transportID);
                if (boat == null)
                {
                    __instance.is_inside_boat = false;
                    AWInsideBoatActorIndex.Notify(__instance, false);
                    return false;
                }

                __instance.setCurrentTilePosition(boat.current_tile);
                __instance.skipUpdates();
            }

            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.embarkInto))]
        private static void EmbarkIntoPostfix(Actor __instance)
        {
            AWInsideBoatActorIndex.Notify(__instance, true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.exitBoat))]
        private static void ExitBoatPostfix(Actor __instance)
        {
            AWInsideBoatActorIndex.Notify(__instance, false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.clearManagers))]
        private static void ClearManagersPostfix(Actor __instance)
        {
            AWInsideBoatActorIndex.Notify(__instance, false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.Dispose))]
        private static void DisposePrefix(Actor __instance)
        {
            AWInsideBoatActorIndex.Notify(__instance, false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorldPrefix()
        {
            AWInsideBoatActorIndex.Reset();
            AWDockTaxiRouteService.Clear();
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
