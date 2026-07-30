using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateDeclineRebellionService
    {
        public static void OnMandateYear(Kingdom pKingdom,
            int pMandateValue, int pAuthority, int pCatalystScore)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.cities == null) return;
            int year = Date.getCurrentYear();
            List<long> active = ReconcileActiveRoster(pKingdom);
            pKingdom.data.get(LineageKeys.MANDATE_DECLINE_REBELLION_LAST_YEAR,
                out int lastYear, -1);
            var facts = new MandateDeclineRebellionFacts(
                MandatePhaseService.CurrentPhase, year, lastYear,
                pKingdom.id, pMandateValue, pAuthority, pCatalystScore,
                pKingdom.cities.Count, active.Count);
            if (!MandateDeclineRebellionRules.ShouldAttempt(facts)) return;

            City city = PickCandidateCity(pKingdom);
            if (city?.data == null) return;
            Kingdom rebel = CreateLocalRebellion(pKingdom, city);
            if (rebel?.data == null) return;

            active.Add(rebel.id);
            pKingdom.data.set(LineageKeys.MANDATE_DECLINE_REBELLION_ROSTER,
                MandateDeclineRebellionRules.EncodeRoster(active));
            pKingdom.data.set(
                LineageKeys.MANDATE_DECLINE_REBELLION_LAST_YEAR, year);
            MandatePhaseService.AdjustCatalyst(
                MandateDeclineRebellionRules.SuccessCatalystPressure,
                "decline_local_rebellion");
        }

        private static List<long> ReconcileActiveRoster(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.MANDATE_DECLINE_REBELLION_ROSTER,
                out string raw, "");
            var active = new List<long>(
                MandateDeclineRebellionRules.MaximumActiveRebellions);
            foreach (long id in MandateDeclineRebellionRules.DecodeRoster(raw))
            {
                Kingdom rebel = FindKingdom(id);
                if (IsActiveLocalRebellion(rebel, pKingdom) &&
                    AreAtWar(rebel, pKingdom))
                {
                    active.Add(id);
                    continue;
                }
                if (rebel?.data != null)
                    rebel.data.set(LineageKeys.MANDATE_DECLINE_REBEL, false);
            }
            pKingdom.data.set(LineageKeys.MANDATE_DECLINE_REBELLION_ROSTER,
                MandateDeclineRebellionRules.EncodeRoster(active));
            return active;
        }

        private static City PickCandidateCity(Kingdom pKingdom)
        {
            int count = pKingdom.cities?.Count ?? 0;
            if (count == 0) return null;
            pKingdom.data.get(LineageKeys.MANDATE_DECLINE_CITY_CURSOR,
                out int cursor, 0);
            cursor = Math.Max(0, cursor) % count;
            int inspected = Math.Min(
                MandateDeclineRebellionRules.CityScanBudget, count);
            City best = null;
            float bestScore = float.MinValue;
            for (int offset = 0; offset < inspected; offset++)
            {
                City city = pKingdom.cities[(cursor + offset) % count];
                Actor founder = city?.leader;
                var facts = new MandateDeclineCityFacts(
                    alive: city?.data != null && !city.isRekt(),
                    ownedByMandate: city?.kingdom == pKingdom,
                    capital: city == pKingdom.capital ||
                             city?.isCapitalCity() == true,
                    capitalRing: IsCapitalRing(city, pKingdom.capital),
                    feudatory: city?.data != null &&
                                FeudatoryService.TryGetByCity(city.id, out _),
                    population: SafePopulation(city),
                    founderEligible: CanLeadRebellion(founder, pKingdom));
                if (!MandateDeclineRebellionRules.IsEligibleCity(facts))
                    continue;
                float score = CityPressure(city);
                if (score <= bestScore) continue;
                best = city;
                bestScore = score;
            }
            pKingdom.data.set(LineageKeys.MANDATE_DECLINE_CITY_CURSOR,
                MandateDeclineRebellionRules.NextCityCursor(cursor,
                    inspected, count));
            return best;
        }

        private static Kingdom CreateLocalRebellion(Kingdom pKingdom,
            City pCity)
        {
            Actor pFounder = pCity == null ? null : pCity.leader;
            if (!CanLeadRebellion(pFounder, pKingdom)) return null;
            Kingdom rebel;
            try
            {
                rebel = pCity.makeOwnKingdom(pFounder, pRebellion: true,
                    pFellApart: false);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Decline rebellion split failed: " +
                                    exception.Message);
                return null;
            }
            if (rebel?.data == null) return null;
            rebel.data.set(LineageKeys.MANDATE_DECLINE_REBEL, true);
            rebel.data.set(LineageKeys.MANDATE_DECLINE_REBEL_ORIGIN_ID,
                pKingdom.id);
            if (VassalService.IsVassalKingdom(rebel))
                VassalService.EndVassal(rebel,
                    "decline_local_rebellion");

            War war = null;
            try
            {
                war = WarDecisionService.TryStartSystemWar(rebel, pKingdom,
                    GeneralRebellionService.WAR_GENERAL_REBELLION,
                    "mandate_decline_rebellion");
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Decline rebellion war failed: " +
                                    exception.Message);
            }
            if (!IsValidRebellionWar(war, rebel, pKingdom))
            {
                RollbackFailedRebellion(pKingdom, rebel, pCity);
                return null;
            }

            HistoryText text = HistoryText.Kingdom(rebel) +
                               HistoryLocalizationRules.H(
                                   "aw_hist_decline_rebel_rose_at") +
                               HistoryText.City(pCity, rebel) +
                               HistoryLocalizationRules.H(
                                   "aw_hist_decline_rebel_against") +
                               HistoryText.Kingdom(pKingdom);
            HistoryWriter.RecordKingdom(pKingdom,
                "mandate_decline_rebellion", text,
                HistoryTarget.Kingdom(rebel));
            HistoryWriter.RecordKingdom(rebel,
                "mandate_decline_rebellion", text,
                HistoryTarget.Kingdom(pKingdom));
            HistoryWriter.RecordCity(pCity, rebel,
                "mandate_decline_rebellion", text,
                HistoryTarget.Actor(pFounder));
            HistoryWriter.RecordPerson(pFounder.data.id, rebel,
                pFounder.getName(), "mandate_decline_rebellion", text,
                ChronicleCategory.WAR, HistoryTarget.Kingdom(pKingdom));
            return rebel;
        }

        private static bool IsValidRebellionWar(War pWar, Kingdom pRebel,
            Kingdom pOrigin)
        {
            if (pWar?.data == null || pRebel?.data == null ||
                pOrigin?.data == null) return false;
            try
            {
                return !pWar.hasEnded() && pWar.isAttacker(pRebel) &&
                       pWar.isDefender(pOrigin);
            }
            catch
            {
                return false;
            }
        }

        private static void RollbackFailedRebellion(Kingdom pOrigin,
            Kingdom pRebel, City pCity)
        {
            if (pRebel?.data != null)
            {
                pRebel.data.set(LineageKeys.MANDATE_DECLINE_REBEL, false);
                pRebel.data.set(LineageKeys.MANDATE_DECLINE_REBEL_ORIGIN_ID,
                    -1L);
            }
            if (pOrigin?.data == null || pCity?.data == null ||
                pCity.kingdom != pRebel) return;
            try
            {
                pCity.joinAnotherKingdom(pOrigin, pCaptured: false,
                    pRebellion: false);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Decline rebellion rollback failed: " +
                                    exception.Message);
                return;
            }
            if (pRebel?.data == null || SafeCityCount(pRebel) > 0) return;
            try { World.world?.kingdoms?.removeObject(pRebel); }
            catch { }
        }

        private static bool CanLeadRebellion(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || !pActor.isAlive() ||
                pActor.isRekt() || !pActor.isAdult() ||
                pActor.kingdom != pKingdom || pActor.isKing() ||
                pActor.asset?.is_boat == true ||
                SlaveService.IsSlave(pActor) ||
                RoyalGuardService.IsRoyalGuard(pActor) ||
                RoyalAsylumService.IsActive(pActor) ||
                pActor.hasTrait("figure") || pActor.hasTrait("first"))
                return false;
            Actor heir = HeirService.PeekRegisteredHeir(pKingdom);
            return heir?.data == null || heir.data.id != pActor.data.id;
        }

        private static bool IsCapitalRing(City pCity, City pCapital)
        {
            if (pCity?.data == null || pCapital?.data == null) return false;
            if (pCity == pCapital) return true;
            try
            {
                return pCity.neighbours_cities_kingdom.Contains(pCapital) ||
                       pCapital.neighbours_cities_kingdom.Contains(pCity);
            }
            catch
            {
                return false;
            }
        }

        private static float CityPressure(City pCity)
        {
            float score = ForeignOccupationService.GetResentment(pCity) +
                          SafePopulation(pCity) * 0.1f;
            try { if (!pCity.hasAnyFood()) score += 15f; }
            catch { }
            return score;
        }

        private static bool IsActiveLocalRebellion(Kingdom pRebel,
            Kingdom pOrigin)
        {
            if (pRebel?.data == null || pRebel.isRekt() ||
                pOrigin?.data == null) return false;
            pRebel.data.get(LineageKeys.MANDATE_DECLINE_REBEL,
                out bool localRebel, false);
            pRebel.data.get(LineageKeys.MANDATE_DECLINE_REBEL_ORIGIN_ID,
                out long originId, -1L);
            return localRebel && originId == pOrigin.id;
        }

        private static bool AreAtWar(Kingdom pLeft, Kingdom pRight)
        {
            try { return pLeft?.isEnemy(pRight) == true; }
            catch { return false; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0 || World.world?.kingdoms == null) return null;
            try { return World.world.kingdoms.get(pKingdomId); }
            catch { return null; }
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int SafeCityCount(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }
    }
}
