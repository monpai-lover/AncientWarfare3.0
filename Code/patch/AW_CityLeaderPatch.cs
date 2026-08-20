using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;
using ai;
using ai.behaviours;
using System;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityLeaderPatch
    {
        internal static int FillVacanciesAfterCivilServiceExam(
            Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
            int attempts = 0;
            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || city.isRekt()) continue;
                bool shouldAttempt = CivilServiceExamRules.
                    ShouldAttemptCityVacancyFill(city.hasLeader(),
                        city.isGettingCaptured(), city.kingdom == pKingdom,
                        CivilServiceExamRules.CityVacancyFillBudget -
                        attempts);
                if (!shouldAttempt) continue;
                attempts++;
                CheckFindLeader_Prefix(city);
                if (attempts >= CivilServiceExamRules.CityVacancyFillBudget)
                    break;
            }
            return attempts;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityBehCheckLeader), "checkFindLeader")]
        public static bool CheckFindLeader_Prefix(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return false;
            if (pCity.hasLeader())
            {
                if (IsLiveCityLeader(pCity, pCity.leader) ||
                    pCity.isGettingCaptured()) return false;
                // Native City can retain a dead/disposed leader pointer for a
                // tick. Clear it before selecting a replacement.
                try { pCity.removeLeader(); }
                catch { return false; }
            }
            if (pCity.isGettingCaptured()) return false;

            Kingdom kingdom = pCity.kingdom;
            long heirId = GetHeirId(kingdom);
            bool hasNineRankSystem = CourtService.HasNineRankSystem(kingdom);
            bool civilServiceCareer = CivilServiceExamRules.
                ShouldUseCivilServiceGovernorPipeline(hasNineRankSystem);
            bool circulating = CivilServiceExamRules.
                ShouldUseIntercityGovernorCirculation(hasNineRankSystem,
                    CountLiveCities(kingdom));

            Actor actor = TryGetRealmLeader(pCity, kingdom, heirId,
                circulating, pAllowVacancyPromotion: false);
            bool vacancyPromotion = false;
            bool acting = false;
            if (actor == null && civilServiceCareer)
            {
                actor = TryGetRealmLeader(pCity, kingdom, heirId,
                    circulating, pAllowVacancyPromotion: true);
                vacancyPromotion = actor != null;
            }
            if (actor == null && civilServiceCareer)
            {
                actor = TryGetActingLocalLeader(pCity, kingdom, heirId);
                acting = actor != null;
            }
            if (actor != null &&
                !ActiveMilitaryLifecycleService.
                    TryPrepareCivilAppointment(actor))
            {
                CityLeaderCandidateRetryService.RecordFailure(pCity, actor);
                actor = null;
            }
            if (actor != null)
            {
                City previousCity = actor.city;
                if (circulating)
                    OfficialCareerStateService.FreezeNativeCityFast(actor);
                using (GovernorRotationRuntimeScope.Enter())
                {
                    actor.joinCity(pCity);
                    pCity.setLeader(actor, pNew: true);
                    bool appointed = acting
                        ? CourtService.TryAssignActingCityGovernor(actor, kingdom,
                            pCity)
                        : CourtService.TryAssignCityGovernor(actor, kingdom,
                            pCity, vacancyPromotion);
                    if (appointed)
                    {
                        CityLeaderCandidateRetryService.Clear(pCity, actor);
                        CityGovernorPlacementService.OnCommittedAssignment(
                            pCity, actor);
                        CityBureauAnnualWorkService.RequestImmediateReconcile(
                            kingdom, pCity.data.id);
                    }
                    else
                    {
                        CityLeaderCandidateRetryService.RecordFailure(pCity,
                            actor);
                        if (pCity.leader == actor)
                            pCity.removeLeader();
                        if (previousCity != null && actor.city != previousCity)
                            actor.joinCity(previousCity);
                    }
                }
                return false;
            }

            if (civilServiceCareer) return false;

            int bestScore = 0;
            foreach (Actor unit in pCity.getUnits())
            {
                if (!IsDirectLeaderCandidate(unit, heirId, kingdom, pCity) ||
                    CityLeaderCandidateRetryService.IsSuppressed(pCity, unit))
                    continue;

                int dice = 1;
                if (unit.isFavorite()) dice += 2;
                int score = ActorTool.attributeDice(unit, dice);
                if (actor == null || score > bestScore)
                {
                    actor = unit;
                    bestScore = score;
                }
            }

            if (actor != null &&
                ActiveMilitaryLifecycleService.
                    TryPrepareCivilAppointment(actor))
            {
                pCity.setLeader(actor, pNew: true);
                CityLeaderCandidateRetryService.Clear(pCity, actor);
                CityBureauAnnualWorkService.RequestImmediateReconcile(
                    kingdom, pCity.data.id);
            }
            return false;
        }

        private static Actor TryGetRealmLeader(City pCity, Kingdom pKingdom,
            long pHeirId, bool pCirculating,
            bool pAllowVacancyPromotion)
        {
            if (pCity == null || pKingdom?.data == null) return null;
            string cityOffice = CourtService.ResolveCityOffice(pKingdom,
                pCity);
            if (string.IsNullOrEmpty(cityOffice)) return null;

            using ListPool<Actor> candidates = new ListPool<Actor>();
            foreach (City city in pKingdom.getCities())
            {
                if (!IsValidSourceCity(city, pKingdom)) continue;
                foreach (Actor unit in city.getUnits())
                {
                    if (!IsRealmLeaderCandidate(unit, pHeirId, pKingdom,
                            city) ||
                        CityLeaderCandidateRetryService.IsSuppressed(pCity,
                            unit)) continue;
                    if (!HistoricalSchoolEducationService.CanAppoint(unit,
                            pKingdom, CourtOfficeLayer.City,
                            cityOffice)) continue;
                    if (!CivilServiceQualificationService.
                            CanReceiveFormalCivilAppointment(unit, pKingdom,
                                CourtOfficeLayer.City,
                                cityOffice, pAllowVacancyPromotion)) continue;
                    if (pCirculating && !CanServeTarget(unit, pCity)) continue;
                    if (CityLeaderCandidateScoringRules.CanEnterUnifiedPool(
                            eligible: true, hasClan: unit.hasClan()))
                        candidates.Add(unit);
                }
            }
            return PickLeader(candidates, pCity);
        }

        private static Actor TryGetActingLocalLeader(City pCity,
            Kingdom pKingdom, long pHeirId)
        {
            if (pCity?.data == null || pKingdom?.data == null) return null;
            string cityOffice = CourtService.ResolveCityOffice(pKingdom,
                pCity);
            if (string.IsNullOrEmpty(cityOffice)) return null;
            using ListPool<Actor> candidates = new ListPool<Actor>();
            foreach (Actor unit in pCity.getUnits())
            {
                if (!IsDirectLeaderCandidate(unit, pHeirId, pKingdom,
                        pCity) ||
                    CityLeaderCandidateRetryService.IsSuppressed(pCity, unit))
                    continue;
                if (!HistoricalSchoolEducationService.CanAppoint(unit,
                        pKingdom, CourtOfficeLayer.City,
                        cityOffice)) continue;
                candidates.Add(unit);
            }
            return PickLeader(candidates, pCity);
        }

        private static Actor PickLeader(ListPool<Actor> pCandidates, City pCity)
        {
            if (pCandidates == null || pCandidates.Count == 0) return null;
            var clanCounts = new System.Collections.Generic.Dictionary<long, int>();
            try
            {
                foreach (City city in pCity?.kingdom?.getCities() ??
                         System.Array.Empty<City>())
                {
                    Actor leader = city?.leader;
                    long clanId = leader?.clan?.data?.id ?? -1L;
                    if (clanId >= 0L)
                        clanCounts[clanId] = clanCounts.TryGetValue(clanId,
                            out int count) ? count + 1 : 1;
                }
            }
            catch { }
            Actor best = null;
            int bestScore = int.MinValue;
            for (int candidateIndex = 0; candidateIndex < pCandidates.Count;
                 candidateIndex++)
            {
                Actor actor = pCandidates[candidateIndex];
                if (actor?.data == null) continue;
                int ability = 0;
                try { ability = (int)Math.Max(Math.Max(actor.stats?["intelligence"] ?? 0f, actor.stats?["stewardship"] ?? 0f), Math.Max(actor.stats?["warfare"] ?? 0f, actor.stats?["diplomacy"] ?? 0f)); }
                catch { }
                actor.data.get(LineageKeys.OFFICER_MERIT, out float merit, 0f);
                long clanId = actor.clan?.data?.id ?? -1L;
                clanCounts.TryGetValue(clanId, out int concentration);
                int score = CityLeaderCandidateScoringRules.Score(ability, (int)Math.Max(0f, merit), concentration, actor.city == pCity, clanId >= 0L, false);
                if (best == null || score > bestScore || score == bestScore && actor.data.id < best.data.id)
                { best = actor; bestScore = score; }
            }
            return best;
        }

        private static bool IsRealmLeaderCandidate(Actor pUnit, long pHeirId,
            Kingdom pKingdom, City pSourceCity)
        {
            if (!IsLiveCandidate(pUnit, pKingdom, pSourceCity)) return false;
            if (ActiveMilitaryLifecycleService.
                    HasActiveMilitaryIdentity(pUnit)) return false;
            if (!RoyalAsylumRules.CanPerformProtectedRole(
                    RoyalAsylumService.IsActive(pUnit))) return false;
            if (pUnit.data.id == pHeirId) return false;
            if (!pUnit.isSexMale()) return false;
            pUnit.data.get(LineageKeys.COURT_OFFICE_ID,
                out string currentOffice, "");
            if (!string.IsNullOrEmpty(currentOffice)) return false;
            return pUnit.isUnitFitToRule();
        }

        private static bool CanServeTarget(Actor pActor, City pTarget)
        {
            if (pActor?.data == null || pTarget?.data == null) return false;
            pActor.data.get(LineageKeys.OFFICER_NATIVE_CITY_ID,
                out long nativeCityId, pActor.city?.data?.id ?? -1L);
            long currentCityId = pActor.city?.data?.id ?? pActor.data.cityID;
            if (currentCityId < 0) return false;
            return OfficialCirculationRules.CanServeCity(nativeCityId,
                currentCityId, pTarget.data.id);
        }

        private static int CountLiveCities(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }

        private static bool IsDirectLeaderCandidate(Actor pUnit, long pHeirId,
            Kingdom pKingdom, City pSourceCity)
        {
            if (!IsLiveCandidate(pUnit, pKingdom, pSourceCity)) return false;
            if (ActiveMilitaryLifecycleService.
                    HasActiveMilitaryIdentity(pUnit)) return false;
            if (!RoyalAsylumRules.CanPerformProtectedRole(
                    RoyalAsylumService.IsActive(pUnit))) return false;
            if (pUnit.data.id == pHeirId) return false;
            if (!pUnit.isSexMale()) return false;
            if (pUnit.isKing() || pUnit.isCityLeader()) return false;
            pUnit.data.get(LineageKeys.COURT_OFFICE_ID,
                out string currentOffice, "");
            return CivilServiceExamRules.CanEnterActingGovernorCandidatePool(
                pUnit.is_profession_citizen,
                !string.IsNullOrEmpty(currentOffice));
        }

        private static bool IsLiveCandidate(Actor pUnit, Kingdom pKingdom,
            City pSourceCity)
        {
            bool hasData = pUnit?.data != null;
            bool alive = false;
            bool rekt = true;
            bool isKing = false;
            bool isCityLeader = false;
            bool isLeaderProfession = false;
            try
            {
                if (hasData)
                {
                    alive = pUnit.isAlive();
                    rekt = pUnit.isRekt();
                    isKing = pUnit.isKing();
                    isCityLeader = pUnit.isCityLeader();
                    isLeaderProfession = pUnit.isProfession(
                        UnitProfession.Leader);
                }
            }
            catch { }
            bool sourceValid = pSourceCity?.data != null &&
                               !pSourceCity.isRekt();
            bool sourceKingdomMatches = sourceValid &&
                                        pSourceCity.kingdom == pKingdom;
            bool actorKingdomMatches = hasData && pUnit.kingdom == pKingdom;
            bool actorCityMatches = hasData && pUnit.city == pSourceCity;
            if (!CityLeaderCandidateRules.CanUseCandidate(hasData, alive,
                    rekt, actorKingdomMatches, actorCityMatches, sourceValid,
                    sourceKingdomMatches, isKing, isCityLeader,
                    isLeaderProfession)) return false;
            try
            {
                Actor registered = World.world?.units?.get(pUnit.data.id);
                return ReferenceEquals(registered, pUnit);
            }
            catch { return false; }
        }

        private static bool IsValidSourceCity(City pCity,
            Kingdom pKingdom)
        {
            return pCity?.data != null && !pCity.isRekt() &&
                   pCity.kingdom == pKingdom;
        }

        private static bool IsLiveCityLeader(City pCity, Actor pLeader)
        {
            return pLeader?.data != null && !pLeader.isRekt() &&
                   pLeader.isAlive() && pLeader.city == pCity &&
                   pLeader.kingdom == pCity.kingdom && pLeader.isCityLeader();
        }

        private static long GetHeirId(Kingdom pKingdom)
        {
            Actor heir = HeirService.PeekRegisteredHeir(pKingdom);
            return heir?.data?.id ?? -1L;
        }
    }
}
