using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelGuiyiService
    {
        private static readonly Dictionary<long, long> ActiveByOccupier =
            new Dictionary<long, long>();
        private static readonly Queue<long> PendingCities =
            new Queue<long>();
        private static readonly HashSet<long> PendingCityIds =
            new HashSet<long>();
        private static readonly Dictionary<long, int> LastEvaluatedYear =
            new Dictionary<long, int>();
        private static bool _activeIndexDirty = true;

        internal static void Schedule(City pCity)
        {
            long cityId = pCity?.data?.id ?? -1L;
            if (cityId <= 0L || !PendingCityIds.Add(cityId)) return;
            PendingCities.Enqueue(cityId);
        }

        internal static void InvalidateActiveIndex()
        {
            _activeIndexDirty = true;
        }

        internal static void ResetRuntime()
        {
            ActiveByOccupier.Clear();
            PendingCities.Clear();
            PendingCityIds.Clear();
            LastEvaluatedYear.Clear();
            _activeIndexDirty = true;
        }

        internal static bool IsGuiyi(Kingdom pKingdom)
        {
            return PeasantRebelBanditStateStore.TryRead(pKingdom,
                       out PeasantRebelBanditStrongholdState state) &&
                   string.Equals(state.RouteSubtype,
                       PeasantRebelGuiyiRules.RouteSubtype,
                       StringComparison.Ordinal);
        }

        internal static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            long kingdomId = pKingdom.getID();
            foreach (long occupierId in ActiveByOccupier.Where(pair =>
                         pair.Key == kingdomId || pair.Value == kingdomId)
                         .Select(pair => pair.Key).ToList())
                ActiveByOccupier.Remove(occupierId);
            LastEvaluatedYear.Remove(kingdomId);
            _activeIndexDirty = true;
        }

        internal static void ProcessAuthorityCycle()
        {
            if (!CanMutate() || World.world?.kingdoms == null ||
                World.world.cities == null) return;
            EnsureActiveIndex();
            for (int i = 0; i < 2 && PendingCities.Count > 0; i++)
            {
                long cityId = PendingCities.Dequeue();
                PendingCityIds.Remove(cityId);
                TryCreateGuiyi(ResolveCity(cityId));
            }
            EvaluateActiveRestorations();
        }

        private static bool TryCreateGuiyi(City pCity)
        {
            Kingdom occupier = pCity?.kingdom;
            if (pCity?.data == null || pCity.isRekt() ||
                occupier?.data == null || occupier.isRekt() ||
                PeasantRebelRouteService.IsBanditOrEntering(occupier))
                return false;
            pCity.data.get(LineageKeys.CITY_ORIGINAL_KINGDOM_ID,
                out long originalKingdomId, -1L);
            bool foreignOccupation = originalKingdomId > 0L &&
                                     originalKingdomId != occupier.getID();
            int loyalty;
            try { loyalty = pCity.getLoyalty(); }
            catch { return false; }
            bool strongholdAvailable = false;
            bool residentAvailable = false;
            try
            {
                strongholdAvailable = pCity.zones.Count >= 4 &&
                    !PeasantRebelBanditStrongholdService.
                        IsStrongholdCity(pCity);
                residentAvailable = pCity.units.Any(actor =>
                    actor?.data != null && !actor.isRekt() &&
                    actor.asset?.is_boat != true);
            }
            catch { }
            var facts = new GuiyiSpawnFacts(
                cityAlive: true,
                cityIntegrated:
                    XiaCultureIntegrationService.IsIntegrated(
                        pCity.culture),
                occupierIntegrated:
                    XiaCultureIntegrationService.IsIntegrated(
                        occupier.culture),
                foreignOccupation: foreignOccupation,
                loyalty: loyalty,
                occupierHasGuiyi:
                    ActiveByOccupier.ContainsKey(occupier.getID()),
                strongholdAvailable: strongholdAvailable,
                residentAvailable: residentAvailable);
            if (!PeasantRebelGuiyiRules.CanSpawn(facts)) return false;

            long originalCityId = pCity.getID();
            if (!PeasantRebelBanditStrongholdService.TryCreateDirect(
                    pCity, out Kingdom guiYi, out City stronghold,
                    out _)) return false;
            if (!PeasantRebelBanditStateStore.TryRead(guiYi,
                    out PeasantRebelBanditStrongholdState state))
                return false;
            state.RouteSubtype = PeasantRebelGuiyiRules.RouteSubtype;
            state.GuiyiOccupierKingdomId = occupier.getID();
            state.GuiyiOriginalKingdomId = originalKingdomId;
            state.GuiyiOriginalCityId = originalCityId;
            state.GuiyiCreatedYear = Date.getCurrentYear();
            state.GuiyiStage = "active";
            if (!PeasantRebelBanditStateStore.Write(guiYi, state))
            {
                PeasantRebelBanditStrongholdService.
                    QueuePopulationFall(stronghold?.getID() ?? -1L);
                return false;
            }
            ActiveByOccupier[occupier.getID()] = guiYi.getID();
            PeasantRebelRouteService.TryApplyRouteName(guiYi,
                (pCity.name ?? "") + AW_L10n.Text(
                    "aw_guiyi_army", " Guiyi Army"));
            RecordEstablished(guiYi, occupier, pCity);
            return true;
        }

        private static void EvaluateActiveRestorations()
        {
            int year = Date.getCurrentYear();
            foreach (KeyValuePair<long, long> pair in
                     ActiveByOccupier.ToList())
            {
                Kingdom guiYi = ResolveKingdom(pair.Value);
                if (guiYi?.data == null || guiYi.isRekt() ||
                    !PeasantRebelBanditStateStore.TryResolveActive(guiYi,
                        out PeasantRebelBanditStrongholdState state) ||
                    !string.Equals(state.RouteSubtype,
                        PeasantRebelGuiyiRules.RouteSubtype,
                        StringComparison.Ordinal))
                {
                    ReleaseActiveSlot(pair.Key);
                    continue;
                }
                if (LastEvaluatedYear.TryGetValue(guiYi.getID(),
                        out int lastYear) && lastYear == year) continue;
                LastEvaluatedYear[guiYi.getID()] = year;
                if (!string.Equals(state.GuiyiStage, "active",
                        StringComparison.Ordinal)) continue;
                Kingdom occupier = ResolveKingdom(
                    state.GuiyiOccupierKingdomId);
                Kingdom original = ResolveKingdom(
                    state.GuiyiOriginalKingdomId);
                GuiyiRestorationObjective objective =
                    PeasantRebelGuiyiRules.ResolveObjective(
                        original?.data != null && !original.isRekt(),
                        state.GuiyiOriginalKingdomId > 0L);
                if (!PeasantRebelGuiyiRules.ShouldBeginRestoration(
                        PeasantRebelRouteService.RealmStrength(guiYi),
                        PeasantRebelRouteService.RealmStrength(occupier),
                        objective)) continue;
                BeginRestoration(guiYi, state, objective);
            }
        }

        private static void BeginRestoration(Kingdom pGuiyi,
            PeasantRebelBanditStrongholdState pState,
            GuiyiRestorationObjective pObjective)
        {
            Actor leader = pGuiyi.king ??
                           PeasantRebelBanditStrongholdService.
                               ResolveStronghold(pGuiyi)?.leader;
            if (leader?.data == null) return;
            pState.GuiyiStage = "restoration_pending";
            if (!PeasantRebelBanditStateStore.Write(pGuiyi, pState)) return;
            long occupierId = pState.GuiyiOccupierKingdomId;
            long originalId = pState.GuiyiOriginalKingdomId;
            long originalCityId = pState.GuiyiOriginalCityId;
            long leaderId = leader.getID();
            PeasantRebelBanditStrongholdService.QueueGuiyiRestorationFall(
                pGuiyi, mother => CompleteRestoration(mother, occupierId,
                    originalId, originalCityId, leaderId, pObjective));
        }

        private static void CompleteRestoration(City pMother,
            long pOccupierId, long pOriginalId, long pOriginalCityId,
            long pLeaderId, GuiyiRestorationObjective pObjective)
        {
            if (pMother?.data == null) return;
            Kingdom occupier = ResolveKingdom(pOccupierId);
            Kingdom original = ResolveKingdom(pOriginalId);
            Actor leader = ResolveActor(pLeaderId);
            bool completed = false;
            if (pObjective ==
                    GuiyiRestorationObjective.ReturnToLivingKingdom &&
                original?.data != null && !original.isRekt())
            {
                try
                {
                    pMother.setKingdom(original);
                    completed = pMother.kingdom == original;
                    if (completed)
                        ReturnClaimedCities(pOriginalId, occupier,
                            original, pMother);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Guiyi city return failed: " +
                                        error.Message);
                }
            }
            else if (pObjective ==
                     GuiyiRestorationObjective.RestoreExtinctKingdom &&
                     leader?.data != null)
            {
                var request = new KingdomRestorationRequest
                {
                    original_kingdom_id = pOriginalId,
                    original_capital_city_id = pOriginalCityId,
                    mode = "guiyi"
                };
                Kingdom restored = KingdomIdentityContinuityService.
                    RestoreFromCity(pMother, leader, request, out string error);
                completed = restored?.data != null;
                if (!completed)
                    ModClass.LogWarning("Guiyi identity restoration failed: " +
                                        error);
                else
                    ReturnClaimedCities(pOriginalId, occupier,
                        restored, pMother);
                original = restored;
            }
            ReleaseActiveSlot(pOccupierId);
            if (completed)
                RecordRestored(original, occupier, pMother, pObjective);
        }

        private static void ReturnClaimedCities(long pOriginalKingdomId,
            Kingdom pOccupier, Kingdom pRestored, City pSeed)
        {
            if (pOriginalKingdomId <= 0L || pOccupier?.data == null ||
                pRestored?.data == null) return;
            List<City> candidates;
            try
            {
                candidates = pOccupier.getCities().Where(city =>
                    city?.data != null && !city.isRekt() && city != pSeed &&
                    city.kingdom == pOccupier).ToList();
            }
            catch { return; }
            foreach (City city in candidates)
            {
                city.data.get(LineageKeys.CITY_ORIGINAL_KINGDOM_ID,
                    out long originalId, -1L);
                if (originalId != pOriginalKingdomId) continue;
                try { city.setKingdom(pRestored); }
                catch (Exception error)
                {
                    ModClass.LogWarning("Guiyi claimed city return failed: " +
                                        error.Message);
                }
            }
        }

        private static void EnsureActiveIndex()
        {
            if (!_activeIndexDirty) return;
            ActiveByOccupier.Clear();
            foreach (Kingdom kingdom in World.world.kingdoms.ToList())
            {
                if (kingdom?.data == null || kingdom.isRekt() ||
                    !PeasantRebelBanditStateStore.TryResolveActive(kingdom,
                        out PeasantRebelBanditStrongholdState state) ||
                    !string.Equals(state.RouteSubtype,
                        PeasantRebelGuiyiRules.RouteSubtype,
                        StringComparison.Ordinal) ||
                    state.GuiyiOccupierKingdomId <= 0L) continue;
                ActiveByOccupier[state.GuiyiOccupierKingdomId] =
                    kingdom.getID();
            }
            _activeIndexDirty = false;
        }

        private static void ReleaseActiveSlot(long pOccupierId)
        {
            if (pOccupierId > 0L) ActiveByOccupier.Remove(pOccupierId);
            _activeIndexDirty = true;
        }

        private static void RecordEstablished(Kingdom pGuiyi,
            Kingdom pOccupier, City pCity)
        {
            HistoryText text = HistoryText.Kingdom(pGuiyi) +
                HistoryLocalizationRules.H("aw_hist_guiyi_established") +
                HistoryText.City(pCity, pOccupier);
            HistoryWriter.RecordKingdom(pGuiyi,
                KingdomEvent.MANDATE_REBELLION, text,
                HistoryTarget.City(pCity));
            HistoryWriter.RecordKingdom(pOccupier,
                KingdomEvent.MANDATE_REBELLION, text,
                HistoryTarget.Kingdom(pGuiyi));
            HistoryWriter.RecordCity(pCity, pOccupier,
                CityEvent.FOREIGN_OCCUPATION, text,
                HistoryTarget.Kingdom(pGuiyi));
        }

        internal static void RecordSuppressed(Kingdom pGuiyi,
            Kingdom pSuppressor, City pStronghold)
        {
            HistoryText text = HistoryText.Kingdom(pGuiyi) +
                HistoryLocalizationRules.H("aw_hist_guiyi_suppressed");
            if (pSuppressor?.data != null)
                HistoryWriter.RecordKingdom(pSuppressor,
                    KingdomEvent.MANDATE_REBELLION, text,
                    HistoryTarget.Kingdom(pGuiyi));
            if (pStronghold?.data != null)
                HistoryWriter.RecordCity(pStronghold, pSuppressor,
                    CityEvent.FOREIGN_OCCUPATION, text,
                    HistoryTarget.Kingdom(pSuppressor));
        }

        private static void RecordRestored(Kingdom pOriginal,
            Kingdom pOccupier, City pCity,
            GuiyiRestorationObjective pObjective)
        {
            HistoryText text = HistoryText.City(pCity, pOriginal) +
                HistoryLocalizationRules.H(pObjective ==
                    GuiyiRestorationObjective.ReturnToLivingKingdom
                    ? "aw_hist_guiyi_returned"
                    : "aw_hist_guiyi_restored");
            if (pOriginal?.data != null)
                HistoryWriter.RecordKingdom(pOriginal,
                    KingdomEvent.MANDATE_REBELLION, text,
                    HistoryTarget.City(pCity));
            if (pOccupier?.data != null)
                HistoryWriter.RecordKingdom(pOccupier,
                    KingdomEvent.MANDATE_REBELLION, text,
                    HistoryTarget.City(pCity));
            HistoryWriter.RecordCity(pCity, pOriginal,
                CityEvent.FOREIGN_OCCUPATION, text,
                HistoryTarget.Kingdom(pOriginal));
        }

        private static City ResolveCity(long pCityId)
        {
            if (pCityId <= 0L) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            if (pKingdomId <= 0L) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static Actor ResolveActor(long pActorId)
        {
            if (pActorId <= 0L) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
