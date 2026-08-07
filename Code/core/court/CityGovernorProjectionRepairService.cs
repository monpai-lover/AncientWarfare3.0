using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CityGovernorProjectionRepairService
    {
        public static void OnLeaderAssigned(City pCity, Actor pActor,
            bool pNewAssignment)
        {
            CityGovernorProjectionDecision decision = Classify(
                pCity, pActor, pNewAssignment);
            if (decision == CityGovernorProjectionDecision.ApplyNow)
            {
                Apply(pCity, pActor);
                return;
            }
            if (decision != CityGovernorProjectionDecision.Defer) return;

            long actorId = pActor.data.id;
            long cityId = pCity.data.id;
            Schedule(actorId, cityId, 0);
        }

        private static void Schedule(long pActorId, long pCityId,
            int pAttempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                CityGovernorProjectionTimingRules.CoalescingKey(
                    pActorId, pCityId),
                DeferredWorkClass.Runtime,
                () => Repair(pActorId, pCityId, pAttempt));
        }

        private static void Repair(long pActorId, long pCityId,
            int pAttempt)
        {
            Actor actor = FindActor(pActorId);
            City city = FindCity(pCityId);
            CityGovernorProjectionDecision decision = Classify(
                city, actor, pNewAssignment: true);
            if (decision == CityGovernorProjectionDecision.ApplyNow)
            {
                Apply(city, actor);
                return;
            }
            if (decision != CityGovernorProjectionDecision.Defer) return;

            int nextAttempt = pAttempt + 1;
            if (CityGovernorProjectionTimingRules.ShouldRetry(nextAttempt))
            {
                Schedule(pActorId, pCityId, nextAttempt);
                return;
            }

            ModClass.LogWarning(
                "City governor projection remained unstable: actor=" +
                pActorId + " city=" + pCityId + " actor_kingdom=" +
                (actor?.kingdom?.data?.id ?? -1L) + " city_kingdom=" +
                (city?.kingdom?.data?.id ?? -1L));
        }

        private static CityGovernorProjectionDecision Classify(City pCity,
            Actor pActor, bool pNewAssignment)
        {
            bool actorValid = pActor?.data != null;
            bool cityValid = pCity?.data != null && !pCity.isRekt();
            bool currentLeader = actorValid && cityValid &&
                                 pCity.leader == pActor;
            Kingdom actorKingdom = actorValid ? pActor.kingdom : null;
            Kingdom cityKingdom = cityValid ? pCity.kingdom : null;
            bool actorKingdomValid = actorKingdom?.data != null;
            bool cityKingdomValid = cityKingdom?.data != null;
            bool sameKingdom = actorKingdomValid && cityKingdomValid &&
                               actorKingdom == cityKingdom;
            bool asylum = actorValid && RoyalAsylumService.IsActive(pActor);
            return CityGovernorProjectionTimingRules.Decide(
                pNewAssignment, actorValid, cityValid, currentLeader,
                actorKingdomValid, cityKingdomValid, sameKingdom, asylum);
        }

        private static void Apply(City pCity, Actor pActor)
        {
            if (AlreadyProjected(pCity, pActor)) return;
            string officeId = CourtService.ResolveCityOffice(pCity.kingdom,
                pCity);
            if (string.IsNullOrEmpty(officeId)) return;
            bool formal = CivilServiceQualificationService.
                CanReceiveFormalCivilAppointment(pActor, pCity.kingdom,
                    CourtOfficeLayer.City, officeId);
            bool appointed = formal
                ? CourtService.TryAssignCityGovernor(pActor, pCity.kingdom,
                    pCity)
                : CourtService.TryAssignActingCityGovernor(pActor,
                    pCity.kingdom, pCity);
            if (appointed) return;
            ModClass.LogWarning(
                "City governor career projection failed after stable " +
                "assignment: actor=" + (pActor?.data?.id ?? -1L) +
                " city=" + (pCity?.data?.id ?? -1L));
        }

        private static bool AlreadyProjected(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null) return false;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long kingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_CITY_ID,
                out long cityId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            string expectedOffice = CourtService.ResolveCityOffice(
                pCity.kingdom, pCity);
            return kingdomId == pCity.kingdom?.id && cityId == pCity.data.id &&
                   layer == CourtOfficeLayer.City &&
                   office == expectedOffice;
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
}
