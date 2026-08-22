using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditPressureService
    {
        private static readonly HashSet<long> PressureTargetCityIds =
            new HashSet<long>();
        private static int _targetIndexYear = int.MinValue;

        internal static void InvalidateTargetIndex()
        {
            _targetIndexYear = int.MinValue;
            PressureTargetCityIds.Clear();
        }

        internal static bool IsPressureTarget(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return false;
            RefreshTargetIndex();
            return PressureTargetCityIds.Contains(pCity.getID());
        }

        internal static void OnKingdomYear(Kingdom pBandit)
        {
            if (!CanMutate() || pBandit?.data == null ||
                pBandit.isRekt() ||
                !PeasantRebelBanditStateStore.TryResolveActive(pBandit,
                    out PeasantRebelBanditStrongholdState state)) return;

            int currentYear = Date.getCurrentYear();
            Kingdom origin = ResolveKingdom(state.OriginKingdomId);
            if (!IsOriginViable(origin))
            {
                QueuePressureResolution(pBandit.getID());
                return;
            }

            City target = ResolveCity(state.PressureTargetCityId);
            if (!IsValidTarget(target, origin))
            {
                target = ResolveInitialOrAdjacentTarget(pBandit, origin,
                    state);
                state.PressureTargetCityId = target?.getID() ?? -1L;
                state.Pressure = 0;
                state.LastPressureYear = currentYear;
                PeasantRebelBanditStateStore.Write(pBandit, state);
                return;
            }

            if (state.LastPressureYear == int.MinValue)
            {
                state.Pressure = 0;
                state.LastPressureYear = currentYear;
                PeasantRebelBanditStateStore.Write(pBandit, state);
                return;
            }

            int next = PeasantRebelBanditPressureRules.AdvancePressure(
                state.Pressure, state.LastPressureYear, currentYear);
            if (next != state.Pressure ||
                state.LastPressureYear != currentYear)
            {
                state.Pressure = next;
                state.LastPressureYear = currentYear;
                if (!PeasantRebelBanditStateStore.Write(pBandit, state))
                    return;
            }
            City stronghold = ResolveCity(state.StrongholdCityId);
            bool famine = false;
            try { famine = stronghold != null && !stronghold.hasAnyFood(); }
            catch { }
            bool highCorruption = stronghold != null &&
                CorruptionService.ReadCity(stronghold).Score >=
                CorruptionRules.HighThreshold;
            PeasantRebelBanditStrongholdService.TryExpandActiveStronghold(
                pBandit, famine, highCorruption);
            if (state.Pressure >=
                PeasantRebelBanditPressureRules.MaximumPressure)
                QueuePressureResolution(pBandit.getID());
        }

        private static void QueuePressureResolution(long pBanditId)
        {
            if (pBanditId <= 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "bandit_pressure_annex:" + pBanditId,
                DeferredWorkClass.CriticalRuntime,
                () => ResolvePressure(pBanditId));
        }

        private static void ResolvePressure(long pBanditId)
        {
            if (!CanMutate()) return;
            Kingdom bandit = ResolveKingdom(pBanditId);
            if (bandit?.data == null ||
                !PeasantRebelBanditStateStore.TryResolveActive(bandit,
                    out PeasantRebelBanditStrongholdState state)) return;

            Kingdom origin = ResolveKingdom(state.OriginKingdomId);
            if (!IsOriginViable(origin))
            {
                ConvertToRevolution(bandit, origin);
                return;
            }

            City target = ResolveCity(state.PressureTargetCityId);
            if (state.Pressure <
                    PeasantRebelBanditPressureRules.MaximumPressure ||
                !IsValidTarget(target, origin))
            {
                ResetInvalidTarget(bandit, origin, state);
                return;
            }

            target.joinAnotherKingdom(bandit, pCaptured: false,
                pRebellion: true);
            if (target.kingdom != bandit) return;

            RecordAnnexation(target, bandit, origin);
            bool originViable = IsOriginViable(origin);
            int banditStrength =
                PeasantRebelRouteService.RealmStrength(bandit);
            int originStrength = originViable
                ? PeasantRebelRouteService.RealmStrength(origin)
                : 0;
            if (PeasantRebelBanditPressureRules.ShouldStartRevolution(
                    banditStrength, originStrength, originViable))
            {
                ConvertToRevolution(bandit, origin);
                return;
            }

            City next = SelectAdjacentTarget(bandit, origin);
            state.PressureTargetCityId = next?.getID() ?? -1L;
            state.Pressure = 0;
            state.LastPressureYear = Date.getCurrentYear();
            PeasantRebelBanditStateStore.Write(bandit, state);
        }

        private static void ResetInvalidTarget(Kingdom pBandit,
            Kingdom pOrigin, PeasantRebelBanditStrongholdState pState)
        {
            City next = SelectAdjacentTarget(pBandit, pOrigin);
            pState.PressureTargetCityId = next?.getID() ?? -1L;
            pState.Pressure = 0;
            pState.LastPressureYear = Date.getCurrentYear();
            PeasantRebelBanditStateStore.Write(pBandit, pState);
        }

        private static void ConvertToRevolution(Kingdom pBandit,
            Kingdom pOrigin)
        {
            if (!PeasantRebelRouteService.ConvertBanditToFounding(
                    pBandit, pOrigin)) return;
            HistoryWriter.RecordKingdom(pBandit,
                KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(pBandit) +
                HistoryLocalizationRules.H(
                    "aw_hist_bandit_revolution_started"),
                pOrigin?.data != null
                    ? HistoryTarget.Kingdom(pOrigin)
                    : HistoryTarget.Kingdom(pBandit));
        }

        private static void RecordAnnexation(City pCity, Kingdom pBandit,
            Kingdom pOrigin)
        {
            HistoryText text = HistoryText.City(pCity, pBandit) +
                HistoryLocalizationRules.H(
                    "aw_hist_bandit_pressure_annexed");
            HistoryWriter.RecordCity(pCity, pBandit,
                CityEvent.CITY_TRANSFER, text,
                HistoryTarget.Kingdom(pBandit));
            HistoryWriter.RecordKingdom(pBandit,
                KingdomEvent.MANDATE_REBELLION, text,
                HistoryTarget.City(pCity));
            if (pOrigin?.data != null && !pOrigin.isRekt())
                HistoryWriter.RecordKingdom(pOrigin,
                    KingdomEvent.MANDATE_REBELLION, text,
                    HistoryTarget.City(pCity));
        }

        private static City ResolveInitialOrAdjacentTarget(Kingdom pBandit,
            Kingdom pOrigin, PeasantRebelBanditStrongholdState pState)
        {
            City mother = ResolveCity(pState.MotherCityId);
            return IsValidTarget(mother, pOrigin)
                ? mother
                : SelectAdjacentTarget(pBandit, pOrigin);
        }

        private static City SelectAdjacentTarget(Kingdom pBandit,
            Kingdom pOrigin)
        {
            if (pBandit?.data == null || pOrigin?.data == null) return null;
            var byId = new Dictionary<long, City>();
            try
            {
                foreach (City owned in pBandit.getCities())
                {
                    if (owned?.data == null || owned.isRekt() ||
                        owned.neighbours_cities == null) continue;
                    foreach (City adjacent in owned.neighbours_cities)
                    {
                        if (!IsValidTarget(adjacent, pOrigin)) continue;
                        byId[adjacent.getID()] = adjacent;
                    }
                }
            }
            catch { return null; }

            var candidates = new List<BanditPressureTargetCandidate>(
                byId.Count);
            foreach (KeyValuePair<long, City> pair in byId)
                candidates.Add(new BanditPressureTargetCandidate(pair.Key,
                    SafeLoyalty(pair.Value), adjacent: true,
                    ownedByOrigin: true, live: true));
            return ResolveCity(PeasantRebelBanditPressureRules.
                SelectTargetCityId(candidates));
        }

        private static void RefreshTargetIndex()
        {
            int year = Date.getCurrentYear();
            if (_targetIndexYear == year) return;
            PressureTargetCityIds.Clear();
            if (World.world?.kingdoms != null)
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (!PeasantRebelBanditStateStore.TryResolveActive(
                            kingdom, out PeasantRebelBanditStrongholdState
                                state) || state.PressureTargetCityId <= 0)
                        continue;
                    PressureTargetCityIds.Add(state.PressureTargetCityId);
                }
            }
            _targetIndexYear = year;
        }

        private static bool IsValidTarget(City pCity, Kingdom pOrigin)
        {
            return pCity?.data != null && !pCity.isRekt() &&
                   pOrigin?.data != null && pCity.kingdom == pOrigin;
        }

        private static bool IsOriginViable(Kingdom pOrigin)
        {
            return pOrigin?.data != null && !pOrigin.isRekt() &&
                   pOrigin.isCiv() &&
                   PeasantRebelRouteService.SafeCityCount(pOrigin) > 0;
        }

        private static int SafeLoyalty(City pCity)
        {
            try { return pCity?.getLoyalty() ?? int.MaxValue; }
            catch { return int.MaxValue; }
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

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
