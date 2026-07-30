using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal sealed class FeudatoryJingnanState
    {
        public FeudatoryJingnanState(FeudatorySnapshot pSnapshot,
            long pRebelKingdomId, long pWarId, string pPrinceName)
        {
            Snapshot = pSnapshot;
            RebelKingdomId = pRebelKingdomId;
            WarId = pWarId;
            PrinceName = pPrinceName ?? "";
        }

        public FeudatorySnapshot Snapshot { get; }
        public long RebelKingdomId { get; }
        public long WarId { get; }
        public string PrinceName { get; }
    }

    internal static class FeudatoryJingnanService
    {
        public static bool TryActivate(long pFeudatoryId, string pReason,
            int pRisk, out War pWar)
        {
            return TryActivateInternal(pFeudatoryId, pReason, pRisk,
                pMandateCollapse: false, out pWar);
        }

        public static bool TryActivateForMandateCollapse(long pFeudatoryId,
            out War pWar)
        {
            return TryActivateInternal(pFeudatoryId,
                MandateFeudatoryCompletionRules.CollapseReason,
                FeudatoryJingnanRiskRules.MaximumRisk,
                pMandateCollapse: true, out pWar);
        }

        private static bool TryActivateInternal(long pFeudatoryId,
            string pReason, int pRisk, bool pMandateCollapse, out War pWar)
        {
            pWar = null;
            if (!FeudatoryService.TryGet(pFeudatoryId,
                    out FeudatorySnapshot snapshot)) return false;
            Kingdom empire = FindKingdom(snapshot.EmpireKingdomId);
            Actor prince = FindActor(snapshot.PrinceActorId);
            if (!Validate(snapshot, empire, prince, pMandateCollapse,
                    out List<City> cities, out City seat)) return false;
            War existingWar = FindExistingWar(empire);
            bool firstRebelInWar = existingWar?.data == null;
            Kingdom rebel = null;
            bool statusChanged = false;
            try
            {
                FeudatoryService.BeginIntentionalJingnanTransfer();
                rebel = seat.makeOwnKingdom(prince, pRebellion: true,
                    pFellApart: false);
                if (rebel?.data == null) return false;
                for (int i = 0; i < cities.Count; i++)
                {
                    City city = cities[i];
                    if (city == seat) continue;
                    city.joinAnotherKingdom(rebel, pCaptured: false,
                        pRebellion: true);
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory Jingnan split failed: " +
                                    exception.Message);
                Rollback(snapshot, empire, rebel, cities);
                return false;
            }
            finally
            {
                FeudatoryService.EndIntentionalJingnanTransfer();
            }

            if (!AllOwnedBy(cities, rebel))
            {
                Rollback(snapshot, empire, rebel, cities);
                return false;
            }

            FeudatoryGarrisonService.ReassignForJingnan(snapshot, rebel);
            statusChanged = FeudatoryService.TryPersistJingnanStatus(snapshot,
                FeudatoryRules.StatusActive, FeudatoryRules.StatusRebelling,
                pReason ?? "jingnan");
            if (!statusChanged)
            {
                Rollback(snapshot, empire, rebel, cities);
                return false;
            }

            War war = existingWar;
            if (war?.data != null)
            {
                try
                {
                    using (WarParticipantEntrySourceScope.Open(war, rebel,
                               WarParticipantEntrySourceKind.ScriptedJoin,
                               empire))
                        war.joinAttackers(rebel);
                }
                catch { }
                if (!war.isAttacker(rebel)) war = null;
            }
            else
            {
                war = WarDecisionService.TryStartSystemWar(rebel, empire,
                    FeudatoryJingnanRules.WarTypeId, "jingnan");
            }
            if (war?.data == null)
            {
                FeudatoryService.TryPersistJingnanStatus(snapshot,
                    FeudatoryRules.StatusRebelling,
                    FeudatoryRules.StatusActive, "");
                Rollback(snapshot, empire, rebel, cities);
                return false;
            }

            if (!FeudatoryService.TryBindJingnanWar(snapshot, war.data.id,
                    rebel.id))
            {
                try { war.lostWar(rebel); }
                catch { }
                FeudatoryService.TryPersistJingnanStatus(snapshot,
                    FeudatoryRules.StatusRebelling,
                    FeudatoryRules.StatusActive, "");
                Rollback(snapshot, empire, rebel, cities);
                return false;
            }

            WriteOrigin(snapshot, empire, rebel, war, pReason, pRisk,
                pMandateCollapse);
            FeudatoryService.FinalizeJingnanActivation(snapshot);
            ChronicleEvents.OnFeudatoryJingnanStarted(empire, prince, seat,
                pRisk);
            MandatePhaseService.AdjustCatalyst(
                FeudatoryJingnanRules.CatalystDeltaForOutbreak(
                    firstRebelInWar),
                firstRebelInWar ? "jingnan_outbreak" : "jingnan_allied_prince");
            pWar = war;
            return true;
        }

        private static bool Validate(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire, Actor pPrince, bool pMandateCollapse,
            out List<City> pCities,
            out City pSeat)
        {
            pCities = new List<City>(FeudatoryRules.MaximumCities);
            pSeat = null;
            if (pSnapshot == null || pEmpire?.data == null ||
                pEmpire.isRekt() ||
                pPrince?.data == null || pPrince.isRekt() ||
                pPrince.kingdom != pEmpire ||
                AssetManager.war_types_library.get(
                    FeudatoryJingnanRules.WarTypeId) == null)
                return false;
            for (int i = 0; i < pSnapshot.CityIds.Count; i++)
            {
                City city = FindCity(pSnapshot.CityIds[i]);
                if (city?.data == null || city.isRekt() ||
                    city.kingdom != pEmpire) return false;
                pCities.Add(city);
                if (city.id == pSnapshot.SeatCityId) pSeat = city;
            }
            bool citiesValid = pSeat?.data != null && pCities.Count > 0 &&
                               pSeat != pEmpire.capital;
            if (!citiesValid) return false;
            if (!pMandateCollapse)
                return MandateService.IsMandateKingdom(pEmpire);
            return MandateFeudatoryCompletionRules.CanActivateCollapseFeudatory(
                MandatePhaseService.CurrentPhase,
                MandateService.ReadReport().active,
                snapshotActive: true,
                parentAlive: !pEmpire.isRekt(),
                princeValid: pPrince.kingdom == pEmpire,
                citiesValid: true);
        }

        private static void WriteOrigin(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire, Kingdom pRebel, War pWar, string pReason,
            int pRisk, bool pMandateCollapse)
        {
            pRebel.data.set(LineageKeys.JINGNAN_FEUDATORY_ID,
                pSnapshot.FeudatoryId);
            pRebel.data.set(LineageKeys.JINGNAN_PRINCE_ACTOR_ID,
                pSnapshot.PrinceActorId);
            pRebel.data.set(LineageKeys.JINGNAN_EMPIRE_KINGDOM_ID,
                pEmpire.id);
            pRebel.data.set(LineageKeys.JINGNAN_REBEL_KINGDOM_ID,
                pRebel.id);
            pRebel.data.set(LineageKeys.JINGNAN_WAR_ID, pWar.data.id);
            pRebel.data.set(LineageKeys.JINGNAN_RISK,
                Math.Max(0, Math.Min(FeudatoryJingnanRiskRules.MaximumRisk,
                    pRisk)));
            pRebel.data.set(LineageKeys.JINGNAN_REASON,
                pReason ?? "jingnan");
            pWar.data.set(LineageKeys.JINGNAN_EMPIRE_KINGDOM_ID,
                pEmpire.id);
            pWar.data.set(LineageKeys.JINGNAN_WAR_ID, pWar.data.id);
            if (pMandateCollapse)
                pWar.data.set(LineageKeys.JINGNAN_MANDATE_COLLAPSE, true);
            pWar.data.get(LineageKeys.JINGNAN_CAPITAL_CITY_ID,
                out long capitalCityId, -1L);
            if (capitalCityId < 0)
                pWar.data.set(LineageKeys.JINGNAN_CAPITAL_CITY_ID,
                    pEmpire.capital?.id ?? -1L);
        }

        public static void OnCityTransferred(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (pCity?.data == null || pNewKingdom?.data == null ||
                pOldKingdom == pNewKingdom) return;
            pNewKingdom.data.get(LineageKeys.JINGNAN_WAR_ID,
                out long warId, -1L);
            if (warId < 0) return;
            War war;
            try { war = World.world?.wars?.get(warId); }
            catch { war = null; }
            if (war?.data == null) return;
            bool activeWar = !war.hasEnded();
            bool jingnanWar = activeWar &&
                              FeudatoryJingnanRules.IsJingnanWar(
                                  war.getAsset()?.id);
            war.data.get(LineageKeys.JINGNAN_CAPITAL_CITY_ID,
                out long capitalCityId, -1L);
            war.data.get(LineageKeys.JINGNAN_VICTOR_REBEL_ID,
                out long victorId, -1L);
            if (!FeudatoryJingnanRules.ShouldFinalizeCapitalCapture(
                    activeWar, jingnanWar, war.isAttacker(pNewKingdom),
                    capitalCityId >= 0 && pCity.id == capitalCityId,
                    victorId < 0)) return;
            war.data.set(LineageKeys.JINGNAN_VICTOR_REBEL_ID,
                pNewKingdom.id);
        }

        public static void OnCityTransferCompleted(City pCity)
        {
            Kingdom owner = pCity?.kingdom;
            if (pCity?.data == null || owner?.data == null) return;
            owner.data.get(LineageKeys.JINGNAN_WAR_ID,
                out long warId, -1L);
            if (warId < 0) return;
            War war;
            try { war = World.world?.wars?.get(warId); }
            catch { war = null; }
            if (war?.data == null || war.hasEnded() ||
                !FeudatoryJingnanRules.IsJingnanWar(war.getAsset()?.id) ||
                !war.isAttacker(owner)) return;
            war.data.get(LineageKeys.JINGNAN_CAPITAL_CITY_ID,
                out long capitalCityId, -1L);
            war.data.get(LineageKeys.JINGNAN_VICTOR_REBEL_ID,
                out long victorId, -1L);
            if (capitalCityId != pCity.id || victorId != owner.id) return;
            try { World.world?.wars?.endWar(war, WarWinner.Attackers); }
            catch (Exception exception)
            {
                ModClass.LogWarning("Jingnan capital settlement failed: " +
                                    exception.Message);
            }
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null ||
                !FeudatoryJingnanRules.IsJingnanWar(pWar.getAsset()?.id))
                return;
            List<FeudatoryJingnanState> states =
                FeudatoryService.ReadJingnanStates(pWar.data.id);
            if (states.Count == 0) return;
            pWar.data.get(LineageKeys.JINGNAN_EMPIRE_KINGDOM_ID,
                out long empireId, -1L);
            Kingdom empire = FindKingdom(empireId) ?? pWar.getMainDefender();
            if (empire?.data == null) return;
            pWar.data.get(LineageKeys.JINGNAN_CAPITAL_CITY_ID,
                out long originalCapitalId, -1L);
            JingnanWarWinner winner = ToJingnanWinner(pWinner);
            FeudatoryJingnanSettlement settlement =
                FeudatoryJingnanRules.ResolveWarEnd(
                    rebelsAreAttackers:
                        pWar.data.main_defender == empire.id, winner);
            if (settlement ==
                FeudatoryJingnanSettlement.CentralAbolishesFeudatory)
            {
                SettleCentralVictory(states, empire, originalCapitalId);
                MandatePhaseService.AdjustCatalyst(
                    FeudatoryJingnanRules.CatalystDeltaForCentralVictory(),
                    "jingnan_suppressed");
                return;
            }
            if (settlement ==
                FeudatoryJingnanSettlement.PrinceTakesThrone)
            {
                SettlePrinceVictory(pWar, states, empire, originalCapitalId);
                if (FeudatoryJingnanRules.PrinceVictoryEntersRenewal(
                        MandateService.Exists))
                    MandatePhaseService.EnterRenewal("jingnan_accession");
                else
                    MandatePhaseService.ForceChaos(
                        "jingnan_accession_without_mandate");
                return;
            }
            if (settlement ==
                FeudatoryJingnanSettlement.StalemateLeavesClaimants)
                SettleStalemate(states, empire);
        }

        private static void SettleStalemate(
            IReadOnlyList<FeudatoryJingnanState> pStates, Kingdom pEmpire)
        {
            for (int i = 0; i < pStates.Count; i++)
            {
                FeudatoryJingnanState state = pStates[i];
                if (!FeudatoryService.CloseJingnanStalemate(state)) continue;
                Kingdom claimant = FindKingdom(state.RebelKingdomId);
                ChronicleEvents.OnFeudatoryJingnanStalemate(pEmpire,
                    claimant, FindActor(state.Snapshot.PrinceActorId));
            }
            MandatePhaseService.ForceChaos("jingnan_stalemate");
        }

        private static void SettleCentralVictory(
            IReadOnlyList<FeudatoryJingnanState> pStates, Kingdom pEmpire,
            long pOriginalCapitalId)
        {
            for (int i = 0; i < pStates.Count; i++)
            {
                FeudatoryJingnanState state = pStates[i];
                ReturnRebelCities(state, pEmpire);
                if (FeudatoryService.AbolishJingnanFeudatory(state,
                        "jingnan_suppressed", pDemotePrince: true))
                    ChronicleEvents.OnFeudatoryJingnanSuppressed(pEmpire,
                        FindActor(state.Snapshot.PrinceActorId),
                        FindCity(state.Snapshot.SeatCityId));
                RemoveEmptyRebelKingdom(state.RebelKingdomId);
            }
            RestoreRecordedCapital(pEmpire, pOriginalCapitalId);
        }

        private static void SettlePrinceVictory(War pWar,
            IReadOnlyList<FeudatoryJingnanState> pStates, Kingdom pEmpire,
            long pOriginalCapitalId)
        {
            pWar.data.get(LineageKeys.JINGNAN_MANDATE_COLLAPSE,
                out bool mandateCollapseRestoration, false);
            pWar.data.get(LineageKeys.JINGNAN_VICTOR_REBEL_ID,
                out long victorRebelId,
                pWar.getMainAttacker()?.id ?? -1L);
            FeudatoryJingnanState victor = null;
            for (int i = 0; i < pStates.Count; i++)
            {
                ReturnRebelCities(pStates[i], pEmpire);
                if (pStates[i].RebelKingdomId == victorRebelId)
                    victor = pStates[i];
            }
            victor ??= pStates[0];
            RestoreRecordedCapital(pEmpire, pOriginalCapitalId);

            for (int i = 0; i < pStates.Count; i++)
            {
                FeudatoryJingnanState state = pStates[i];
                if (state == victor)
                    FeudatoryService.AbolishJingnanFeudatory(state,
                        "jingnan_victory", pDemotePrince: false);
                else
                    FeudatoryService.RestoreJingnanFeudatory(state);
            }

            Actor victorPrince = FindActor(victor.Snapshot.PrinceActorId);
            if (!PrepareAccession(victorPrince, pEmpire)) return;
            try
            {
                if (pEmpire.king?.data != null && pEmpire.king != victorPrince)
                    pEmpire.kingLeftEvent();
                pEmpire.setKing(victorPrince);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Jingnan accession failed: " +
                                    exception.Message);
                return;
            }
            if (pEmpire.king == victorPrince)
            {
                ChronicleEvents.OnFeudatoryJingnanVictory(pEmpire,
                    victorPrince);
                if (mandateCollapseRestoration)
                {
                    RulerTitleRestorationStateService.
                        MarkDynasticRestorationCompleted(pEmpire,
                            victorPrince);
                    MandateService.TryDeclareMandate(pEmpire,
                        MandateFeudatoryCompletionRules.RestorationReason,
                        MandateFeudatoryCompletionRules.RestorationOrigin,
                        MandateFeudatoryCompletionRules.RestorationClaimant);
                }
            }
            for (int i = 0; i < pStates.Count; i++)
                RemoveEmptyRebelKingdom(pStates[i].RebelKingdomId);
        }

        private static bool PrepareAccession(Actor pPrince, Kingdom pEmpire)
        {
            City capital = pEmpire?.capital;
            if (pPrince?.data == null || pPrince.isRekt() ||
                capital?.data == null) return false;
            CourtService.ClearOfficeForReignTransition(pPrince,
                "jingnan_accession");
            try { if (pPrince.hasArmy()) pPrince.removeFromArmy(); }
            catch { }
            try { pPrince.stopBeingWarrior(); }
            catch { }
            try
            {
                using (FormalAffiliationTransferScope.Open(pPrince.data.id,
                           pEmpire.id, capital.data.id))
                    pPrince.joinCity(capital);
            }
            catch { return false; }
            return pPrince.kingdom == pEmpire && pPrince.city == capital;
        }

        private static void ReturnRebelCities(FeudatoryJingnanState pState,
            Kingdom pEmpire)
        {
            Kingdom rebel = FindKingdom(pState.RebelKingdomId);
            if (rebel?.data == null || pEmpire?.data == null) return;
            var cities = new List<City>();
            try
            {
                foreach (City city in rebel.getCities())
                    if (city?.data != null)
                        cities.Add(city);
            }
            catch { }
            FeudatoryService.BeginIntentionalJingnanTransfer();
            try
            {
                for (int i = 0; i < cities.Count; i++)
                {
                    City city = cities[i];
                    if (city.kingdom != rebel) continue;
                    city.joinAnotherKingdom(pEmpire, pCaptured: false,
                        pRebellion: false);
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Jingnan city return failed: " +
                                    exception.Message);
            }
            finally
            {
                FeudatoryService.EndIntentionalJingnanTransfer();
            }
            FeudatoryGarrisonService.ReassignForJingnan(pState.Snapshot,
                pEmpire);
        }

        private static void RestoreRecordedCapital(Kingdom pEmpire,
            long pCapitalCityId)
        {
            City capital = FindCity(pCapitalCityId);
            if (pEmpire?.data == null || capital?.data == null ||
                capital.kingdom != pEmpire) return;
            if (!FeudatoryJingnanRules.ShouldRestoreRecordedCapital(
                    pEmpire.hasCapital(), recordedCapitalOwnedByEmpire: true) &&
                pEmpire.capital == capital) return;
            try { pEmpire.setCapital(capital); }
            catch (Exception exception)
            {
                ModClass.LogWarning("Jingnan capital restoration failed: " +
                                    exception.Message);
            }
        }

        private static JingnanWarWinner ToJingnanWinner(WarWinner pWinner)
        {
            return pWinner == WarWinner.Attackers
                ? JingnanWarWinner.Attackers
                : pWinner == WarWinner.Defenders
                    ? JingnanWarWinner.Defenders
                    : JingnanWarWinner.None;
        }

        private static void RemoveEmptyRebelKingdom(long pKingdomId)
        {
            Kingdom rebel = FindKingdom(pKingdomId);
            if (rebel?.data == null || SafeCityCount(rebel) > 0) return;
            try { World.world?.kingdoms?.removeObject(rebel); }
            catch { }
        }

        private static War FindExistingWar(Kingdom pEmpire)
        {
            try
            {
                foreach (War war in pEmpire.getWars())
                    if (war?.data != null && !war.hasEnded() &&
                        FeudatoryJingnanRules.IsJingnanWar(
                            war.getAsset()?.id) && war.isDefender(pEmpire))
                        return war;
            }
            catch { }
            return null;
        }

        private static bool AllOwnedBy(IReadOnlyList<City> pCities,
            Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pCities == null) return false;
            for (int i = 0; i < pCities.Count; i++)
                if (pCities[i]?.kingdom != pKingdom) return false;
            return true;
        }

        private static void Rollback(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire, Kingdom pRebel, IReadOnlyList<City> pCities)
        {
            if (pEmpire?.data == null || pCities == null) return;
            FeudatoryService.BeginIntentionalJingnanTransfer();
            try
            {
                for (int i = 0; i < pCities.Count; i++)
                {
                    City city = pCities[i];
                    if (city?.data == null || city.kingdom == pEmpire) continue;
                    city.joinAnotherKingdom(pEmpire, pCaptured: false,
                        pRebellion: false);
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory Jingnan rollback failed: " +
                                    exception.Message);
            }
            finally
            {
                FeudatoryService.EndIntentionalJingnanTransfer();
            }
            FeudatoryGarrisonService.ReassignForJingnan(pSnapshot, pEmpire);
            if (pRebel?.data == null || SafeCityCount(pRebel) > 0) return;
            try { World.world?.kingdoms?.removeObject(pRebel); }
            catch { }
        }

        private static int SafeCityCount(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
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

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
