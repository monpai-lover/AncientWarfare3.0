using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalAffiliationService
    {
        private static readonly Dictionary<long, HistoricalSchoolAffiliationSnapshot> ByActor =
            new Dictionary<long, HistoricalSchoolAffiliationSnapshot>();

        public static void LoadState()
        {
            ByActor.Clear();
            foreach (HistoricalSchoolAffiliationSnapshot state in
                     HistoricalSchoolStore.LoadAffiliations())
                if (state.ActorId >= 0) ByActor[state.ActorId] = state;
        }

        public static void ClearRuntime()
        {
            ByActor.Clear();
        }

        internal static void RegisterDescent(long pActorId, long pHomeKingdomId,
            string pHomeKingdomName, long pHometownCityId, int pYear)
        {
            if (pActorId < 0 || pHomeKingdomId < 0 || pHometownCityId < 0) return;
            ByActor[pActorId] = HistoricalSchoolAffiliationSnapshot.CreateHome(pActorId,
                pHomeKingdomId, pHomeKingdomName, pHometownCityId, pYear);
        }

        internal static bool EnsureMemberAffiliation(Actor pActor, long pCityId)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return false;
            if (ByActor.TryGetValue(pActor.data.id,
                    out HistoricalSchoolAffiliationSnapshot existing))
                return existing.LifecycleState != HistoricalSchoolLifecycleState.Dead;
            City city = FindCity(pCityId) ?? pActor.city;
            Kingdom kingdom = city?.kingdom ?? pActor.kingdom;
            if (city?.data == null || city.isRekt() || kingdom?.data == null || kingdom.isRekt())
                return false;
            int year = Date.getCurrentYear();
            if (!HistoricalSchoolStore.EnsureMemberAffiliation(pActor.data.id, kingdom.id,
                    kingdom.name, city.data.id, year, WorldTime())) return false;
            ByActor[pActor.data.id] = HistoricalSchoolAffiliationSnapshot.CreateHome(
                pActor.data.id, kingdom.id, kingdom.name, city.data.id, year);
            return true;
        }

        internal static void EnsureMembershipAffiliations()
        {
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                foreach (long actorId in SchoolMembershipService.Members(school.Id))
                {
                    Actor actor = FindActor(actorId);
                    if (actor?.data == null ||
                        (!HistoricalSchoolDescentService.IsCanonicalMaster(actor) &&
                         !SchoolLineageService.IsQualifiedTeacher(actor))) continue;
                    EnsureMemberAffiliation(actor, actor.city?.data?.id ?? -1L);
                }
        }

        public static HistoricalSchoolAffiliationSnapshot Get(long pActorId)
        {
            return ByActor.TryGetValue(pActorId, out HistoricalSchoolAffiliationSnapshot state)
                ? state
                : null;
        }

        public static HistoricalSchoolAffiliationSnapshot[] ActiveSnapshots(
            bool pTravelEligibleOnly = false, bool pTravelOnly = false,
            int pTravelBucket = -1)
        {
            IEnumerable<HistoricalSchoolAffiliationSnapshot> query = ByActor.Values
                .Where(p => p.LifecycleState != HistoricalSchoolLifecycleState.Dead);
            if (pTravelEligibleOnly)
                query = query.Where(p => IsTravelEligible(FindActor(p.ActorId)));
            if (pTravelOnly)
                query = query.Where(p => p.LifecycleState == HistoricalSchoolLifecycleState.Travelling ||
                    p.LifecycleState == HistoricalSchoolLifecycleState.Voyage);
            if (pTravelBucket >= 0)
                query = query.Where(p => HistoricalSchoolRules.TravelBucket(p.ActorId) ==
                    pTravelBucket);
            return query.ToArray();
        }

        public static bool IsTravelEligible(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return false;
            return HistoricalSchoolDescentService.IsCanonicalMaster(pActor) ||
                   SchoolLineageService.IsQualifiedTeacher(pActor);
        }

        public static City ResidenceCity(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot state = Get(pActor?.data?.id ?? -1L);
            return state == null ? null : FindCity(state.ResidenceCityId);
        }

        public static Kingdom HomeKingdom(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot state = Get(pActor?.data?.id ?? -1L);
            return state == null ? null : FindKingdom(state.HomeKingdomId);
        }

        public static Kingdom ServiceKingdom(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot state = Get(pActor?.data?.id ?? -1L);
            return state == null ? null : FindKingdom(state.ServiceKingdomId);
        }

        public static bool IsAffiliatedWith(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            HistoricalSchoolAffiliationSnapshot state = Get(pActor.data.id);
            if (state == null) return pActor.kingdom == pKingdom;
            return state.HomeKingdomId == pKingdom.id ||
                   state.ServiceKingdomId == pKingdom.id &&
                   state.LifecycleState == HistoricalSchoolLifecycleState.Serving;
        }

        public static bool IsAvailableForOffice(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot state = Get(pActor?.data?.id ?? -1L);
            return state == null || state.LifecycleState == HistoricalSchoolLifecycleState.AtHome ||
                   state.LifecycleState == HistoricalSchoolLifecycleState.Resident;
        }

        public static bool IsPresentForInfluence(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot state = Get(pActor?.data?.id ?? -1L);
            if (state == null) return true;
            return state.LifecycleState != HistoricalSchoolLifecycleState.ChoosingDestination &&
                   state.LifecycleState != HistoricalSchoolLifecycleState.Travelling &&
                   state.LifecycleState != HistoricalSchoolLifecycleState.Voyage &&
                   state.LifecycleState != HistoricalSchoolLifecycleState.Dead;
        }

        public static bool TryBeginTravel(Actor pActor, long pDestinationCityId, int pYear)
        {
            HistoricalSchoolAffiliationSnapshot current = Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor, current) || current.ServiceKingdomId >= 0) return false;
            HistoricalSchoolAffiliationSnapshot chosen = current.ChooseDestination(
                pDestinationCityId, pYear);
            if (ReferenceEquals(chosen, current) || !Save(chosen)) return false;
            HistoricalSchoolAffiliationSnapshot travelling = chosen.StartTravel();
            if (!ReferenceEquals(travelling, chosen) && Save(travelling)) return true;
            Save(current);
            return false;
        }

        public static bool TryArrive(Actor pActor, long pCityId, int pYear)
        {
            HistoricalSchoolAffiliationSnapshot current = Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor, current)) return false;
            HistoricalSchoolAffiliationSnapshot next = current.Arrive(pCityId, pYear);
            return !ReferenceEquals(next, current) && Save(next);
        }

        internal static bool RollbackArrival(Actor pActor,
            HistoricalSchoolAffiliationSnapshot pPrevious)
        {
            if (pActor?.data == null || pPrevious == null ||
                pPrevious.ActorId != pActor.data.id ||
                (pPrevious.LifecycleState != HistoricalSchoolLifecycleState.Travelling &&
                 pPrevious.LifecycleState != HistoricalSchoolLifecycleState.Voyage))
                return false;
            return Save(pPrevious);
        }

        public static bool TryStartChosenTravel(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot current = Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor, current) ||
                current.LifecycleState != HistoricalSchoolLifecycleState.ChoosingDestination)
                return false;
            HistoricalSchoolAffiliationSnapshot next = current.StartTravel();
            return !ReferenceEquals(next, current) && Save(next);
        }

        public static bool RegisterTransportFailure(Actor pActor, int pYear)
        {
            HistoricalSchoolAffiliationSnapshot current = Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor, current) ||
                current.LifecycleState != HistoricalSchoolLifecycleState.Travelling)
                return false;
            return Save(current.RegisterTransportFailure(pYear));
        }

        public static bool CancelTravel(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot current = Get(pActor?.data?.id ?? -1L);
            if (current == null) return false;
            HistoricalSchoolAffiliationSnapshot next = current.CancelTravel();
            return !ReferenceEquals(next, current) && Save(next);
        }

        public static bool TryBeginVoyage(Actor pActor, int pStartYear, int pArrivalYear)
        {
            HistoricalSchoolAffiliationSnapshot current = Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor, current) || current.ServiceKingdomId >= 0) return false;
            HistoricalSchoolAffiliationSnapshot next = current.BeginVoyage(pStartYear,
                pArrivalYear);
            return !ReferenceEquals(next, current) && Save(next);
        }

        public static bool TryBeginService(Actor pActor, Kingdom pKingdom, int pStartYear,
            int pEndYear)
        {
            HistoricalSchoolAffiliationSnapshot current = Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor, current) || pKingdom?.data == null || pKingdom.isRekt())
                return false;
            HistoricalSchoolAffiliationSnapshot next = current.BeginService(pKingdom.id,
                pStartYear, pEndYear);
            return !ReferenceEquals(next, current) && Save(next);
        }

        public static bool EndService(Actor pActor, int pYear)
        {
            HistoricalSchoolAffiliationSnapshot current = Get(pActor?.data?.id ?? -1L);
            // A dead guest still needs its service row closed.  Death hooks can run
            // before the annual cleanup and IsUsable intentionally rejects dead actors.
            if (pActor?.data == null || current == null ||
                current.LifecycleState == HistoricalSchoolLifecycleState.Dead) return false;
            HistoricalSchoolAffiliationSnapshot next = current.EndService(pYear);
            return !ReferenceEquals(next, current) && Save(next);
        }

        internal static bool EndService(long pActorId, int pYear)
        {
            if (pActorId < 0) return false;
            Actor actor = FindActor(pActorId);
            if (actor?.data != null) return EndService(actor, pYear);
            HistoricalSchoolAffiliationSnapshot current = Get(pActorId);
            if (current == null || current.LifecycleState == HistoricalSchoolLifecycleState.Dead)
                return false;
            HistoricalSchoolAffiliationSnapshot next = current.EndService(pYear);
            return !ReferenceEquals(next, current) && Save(next);
        }

        internal static bool AdoptCommittedDeath(
            HistoricalSchoolAffiliationSnapshot pCommittedState)
        {
            if (pCommittedState == null ||
                pCommittedState.LifecycleState == HistoricalSchoolLifecycleState.Dead)
                return false;
            var dead = new HistoricalSchoolAffiliationSnapshot(pCommittedState.ActorId,
                pCommittedState.HomeKingdomId, pCommittedState.HomeKingdomName,
                pCommittedState.HometownCityId, pCommittedState.ResidenceCityId,
                pCommittedState.PreviousResidenceCityId, -1, -1,
                HistoricalSchoolLifecycleState.Dead, -1, -1,
                pCommittedState.LastTravelYear, -1, -1, -1,
                pCommittedState.TransportFailures);
            ByActor[pCommittedState.ActorId] = dead;
            CitySchoolSnapshotService.MarkDirty(FindCity(pCommittedState.ResidenceCityId));
            return true;
        }

        public static bool CanJoinCity(Actor pActor, City pTarget)
        {
            HistoricalSchoolAffiliationSnapshot state = Get(pActor?.data?.id ?? -1L);
            if (state == null || pTarget == null || pTarget == pActor.city) return true;
            if (!IsTravelEligible(pActor)) return true;
            Kingdom home = FindKingdom(state.HomeKingdomId);
            if (home?.data == null || home.isRekt()) return true;
            return pTarget.kingdom == home;
        }

        public static bool CanJoinKingdom(Actor pActor, Kingdom pTarget)
        {
            HistoricalSchoolAffiliationSnapshot state = Get(pActor?.data?.id ?? -1L);
            if (state == null || pTarget == null || pTarget == pActor.kingdom) return true;
            if (!IsTravelEligible(pActor)) return true;
            Kingdom home = FindKingdom(state.HomeKingdomId);
            if (home?.data == null || home.isRekt()) return true;
            return pTarget == home;
        }

        public static void RepairEnginePointers(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot state = Get(pActor?.data?.id ?? -1L);
            if (state == null) return;
            Kingdom home = FindKingdom(state.HomeKingdomId);
            if (home?.data != null && !home.isRekt()) return;
            City residence = FindCity(state.ResidenceCityId);
            if (residence?.data == null || residence.isRekt() ||
                residence.kingdom?.data == null || residence.kingdom.isRekt()) return;
            if (pActor.kingdom?.data == null || pActor.kingdom.isRekt())
                pActor.setKingdom(residence.kingdom);
            if (pActor.city?.data == null || pActor.city.isRekt() ||
                pActor.city.kingdom?.data == null || pActor.city.kingdom.isRekt())
                pActor.setCity(residence);
        }

        private static bool Save(HistoricalSchoolAffiliationSnapshot pState)
        {
            if (!HistoricalSchoolStore.SaveAffiliation(pState, WorldTime())) return false;
            ByActor[pState.ActorId] = pState;
            CitySchoolSnapshotService.MarkDirty(FindCity(pState.ResidenceCityId));
            return true;
        }

        private static bool IsUsable(Actor pActor, HistoricalSchoolAffiliationSnapshot pState)
        {
            return pActor?.data != null && pActor.isAlive() && !pActor.isRekt() &&
                   pState != null &&
                   pState.LifecycleState != HistoricalSchoolLifecycleState.Dead;
        }

        private static City FindCity(long pId)
        {
            if (pId < 0) return null;
            try { return World.world?.cities?.get(pId); }
            catch { return null; }
        }

        private static Actor FindActor(long pId)
        {
            if (pId < 0) return null;
            try { return World.world?.units?.get(pId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pId)
        {
            if (pId < 0) return null;
            try { return World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }

        private static double WorldTime()
        {
            return World.world?.getCurWorldTime() ?? 0d;
        }
    }
}
