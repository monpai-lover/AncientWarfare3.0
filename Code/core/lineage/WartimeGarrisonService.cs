using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    // The custom wartime garrison layer has been retired. Vanilla army
    // recruitment, defense, jobs, and lifecycle now own these decisions.
    // This facade remains so old save/patch call sites fail closed without
    // adding another scan or deferred-work queue.
    internal static class WartimeGarrisonService
    {
        internal static bool Disabled { get { return true; } }

        public static bool IsActive(Actor pActor) { return false; }
        public static bool HasIndexedDefender(City pCity, Kingdom pKingdom)
        { return false; }
        public static int GetIndexedDefenderCount(City pCity) { return 0; }
        public static int MinimumDefenseForSortie(City pCity) { return 0; }
        internal static void RequestSortieReserve(City pCity) { }
        internal static void ClearSortieReserve(City pCity) { }
        public static IReadOnlyList<Actor> CollectSortieMembers(City pCity,
            int pMaximum) { return Array.Empty<Actor>(); }
        public static bool ReleaseForSortie(Actor pActor, City pOrigin,
            Kingdom pKingdom) { return false; }
        internal static void OnRealmSupplyChanged(City pCity) { }
        public static void ReturnFromSortie(Actor pActor, City pOrigin,
            Kingdom pKingdom) { }
        public static bool ShouldBlockArmyAssignment(Actor pActor,
            Army pArmy) { return false; }
        public static string GetJob(Actor pActor)
        {
            // Old saves can contain this flag. Clear it at the actor's next
            // vanilla job selection instead of scanning all loaded actors.
            if (pActor?.data != null)
            {
                pActor.data.get(LineageKeys.WARTIME_GARRISON,
                    out bool active, false);
                if (active)
                {
                    pActor.data.set(LineageKeys.WARTIME_GARRISON, false);
                    pActor.data.set(
                        LineageKeys.WARTIME_GARRISON_KINGDOM_ID, -1L);
                    pActor.data.set(
                        LineageKeys.WARTIME_GARRISON_CITY_ID, -1L);
                    pActor.data.set(
                        LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
                }
            }
            return string.Empty;
        }
        public static void OnWarStarted(War pWar) { }
        public static void OnWarEnded(War pWar) { }
        public static void OnKingdomDestroying(Kingdom pKingdom) { }
        public static void OnKingdomWarStateChanged(Kingdom pKingdom) { }
        public static void OnKingdomYear(Kingdom pKingdom) { }
        public static void OnCityThreatChanged(City pCity) { }
        public static void OnCityOwnerChanged(City pCity,
            Kingdom pPreviousOwner) { }
        public static void OnCityInvalidated(City pCity) { }

        // Clear stale flags only when vanilla already tells us that this
        // actor is being invalidated. No world or city scan is performed.
        public static void OnActorInvalidated(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.WARTIME_GARRISON,
                out bool active, false);
            if (!active) return;
            pActor.data.set(LineageKeys.WARTIME_GARRISON, false);
            pActor.data.set(LineageKeys.WARTIME_GARRISON_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.WARTIME_GARRISON_CITY_ID, -1L);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
        }

        public static WorldTile GetPatrolTile(Actor pActor) { return null; }
        public static bool ShouldPatrol(Actor pActor) { return false; }
        public static Actor FindThreatNearGarrison(Actor pActor)
        { return null; }
        public static bool IsValidThreatForGarrison(Actor pActor,
            Actor pTarget) { return false; }
        public static WorldTile GetDefenseTile(Actor pActor) { return null; }
        public static void RebuildRuntime() { }
        public static void ClearRuntime() { }
    }
}
