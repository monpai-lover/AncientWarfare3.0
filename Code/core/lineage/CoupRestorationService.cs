using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal sealed class CoupRestorationSeed
    {
        public long OriginalKingdomId;
        public long OriginalCapitalCityId;
        public long OldRulerActorId;
        public long AlternateClaimantActorId;
        public long UsurperActorId;
        public long SupporterActorId;
        public long SeatCityId;
        public int SupportScore;
        public List<CoupRestorationSupportCandidate> Coalition = new();
    }

    internal static class CoupRestorationService
    {
        public static CoupRestorationSeed Prepare(Kingdom pKingdom,
            Actor pUsurper)
        {
            Actor oldRuler = pKingdom?.king;
            if (pKingdom?.data == null || pUsurper?.data == null ||
                oldRuler?.data == null || pUsurper == oldRuler) return null;
            int cityCount = CountCities(pKingdom);
            if (!CoupRestorationRules.CanPrepare(
                    monarchy: !RepublicGovernmentService.IsRepublic(pKingdom),
                    realmAtWar: IsAtWar(pKingdom),
                    realmCityCount: cityCount,
                    oldRulerAlive: IsLive(oldRuler))) return null;

            Actor alternate = HeirService.FindHeirReadOnly(pKingdom);
            if (!IsLive(alternate) || alternate == oldRuler ||
                alternate == pUsurper)
                alternate = null;
            List<CoupRestorationSupportCandidate> coalition =
                SelectSupporters(pKingdom, oldRuler, pUsurper);
            if (coalition.Count == 0)
                return null;
            CoupRestorationSupportCandidate primary = coalition[0];

            return new CoupRestorationSeed
            {
                OriginalKingdomId = pKingdom.id,
                OriginalCapitalCityId = pKingdom.capital?.id ?? -1L,
                OldRulerActorId = oldRuler.data.id,
                AlternateClaimantActorId = alternate?.data?.id ?? -1L,
                UsurperActorId = pUsurper.data.id,
                SupporterActorId = primary.ActorId,
                SeatCityId = primary.CityId,
                SupportScore = primary.SupportScore,
                Coalition = coalition
            };
        }

        public static bool TryStart(CoupRestorationSeed pSeed,
            Kingdom pKingdom, Actor pUsurper)
        {
            if (pSeed == null || pKingdom?.data == null ||
                pUsurper?.data == null || pKingdom.king != pUsurper ||
                pSeed.OriginalKingdomId != pKingdom.id || IsAtWar(pKingdom))
                return false;
            Actor oldRuler = FindActor(pSeed.OldRulerActorId);
            Actor supporter = FindActor(pSeed.SupporterActorId);
            City seat = FindCity(pSeed.SeatCityId);
            if (!IsLive(oldRuler) || !IsLive(supporter) ||
                !ValidSeat(seat, pKingdom)) return false;

            Kingdom rebel = null;
            try
            {
                rebel = seat.makeOwnKingdom(oldRuler, pRebellion: true,
                    pFellApart: false);
                if (rebel?.data == null)
                    return false;
                if (seat.kingdom != rebel || rebel.king != oldRuler)
                {
                    RollbackStart(rebel, pKingdom, oldRuler);
                    return false;
                }
                if (rebel.capital != seat) rebel.setCapital(seat);
                if (rebel.capital != seat)
                {
                    RollbackStart(rebel, pKingdom, oldRuler);
                    return false;
                }
                if (!AccessionIdentityService.Prepare(rebel, oldRuler) ||
                    !AccessionIdentityService.Commit(rebel, oldRuler))
                {
                    RollbackStart(rebel, pKingdom, oldRuler);
                    return false;
                }
                CompleteLoyalistAccession(rebel, oldRuler);
                if (!JoinCoalitionCities(pSeed, pKingdom, rebel))
                {
                    RollbackStart(rebel, pKingdom, oldRuler);
                    return false;
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Coup loyalist split failed: " +
                                    exception.Message);
                RollbackStart(rebel, pKingdom, oldRuler);
                return false;
            }

            War war = null;
            try
            {
                war = WarDecisionService.TryStartInternalSystemWar(rebel,
                    pKingdom,
                    CoupRestorationRules.WarTypeId,
                    "ministerial_coup_loyalist_restoration");
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Coup loyalist war failed: " +
                                    exception.Message);
            }
            if (war?.data == null)
            {
                RollbackStart(rebel, pKingdom, oldRuler);
                return false;
            }

            BindState(pSeed, pKingdom, rebel, war);
            FinalizeCoalitionMembers(pSeed, rebel);
            RecordStarted(pKingdom, rebel, oldRuler, pSeed);
            return true;
        }

        private static void CompleteLoyalistAccession(Kingdom pKingdom,
            Actor pRuler)
        {
            AccessionIdentityService.EnsureRoyalClanAfterNativeAccession(
                pKingdom, pRuler);
            FormerHeirService.ClearSnapshot(pRuler);
            FormerKingService.ClearSnapshot(pRuler);
            RepublicGovernmentService.MarkMonarchyEstablished(pKingdom);
            HeirService.ClearHeir(pKingdom);
            HeirService.RefreshHeir(pKingdom);
            CourtDirectionService.MarkDirty(pKingdom);
        }

        public static void OnCityTransferred(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (pCity?.data == null || pNewKingdom?.data == null ||
                pOldKingdom == pNewKingdom) return;
            pNewKingdom.data.get(LineageKeys.COUP_RESTORATION_WAR_ID,
                out long warId, -1L);
            if (warId < 0) return;
            War war = FindWar(warId);
            if (war?.data == null || war.hasEnded() ||
                !IsWarType(war) || !war.isAttacker(pNewKingdom)) return;
            war.data.get(LineageKeys.COUP_RESTORATION_CAPITAL_CITY_ID,
                out long capitalCityId, -1L);
            war.data.get(LineageKeys.COUP_RESTORATION_VICTOR_REBEL_ID,
                out long victorId, -1L);
            if (!CoupRestorationRules.ShouldFinalizeCapitalCapture(
                    activeWar: true, correctWarType: true,
                    newOwnerIsLoyalist: true,
                    capturedOriginalCapital: pCity.id == capitalCityId,
                    winnerUnset: victorId < 0)) return;
            war.data.set(LineageKeys.COUP_RESTORATION_VICTOR_REBEL_ID,
                pNewKingdom.id);
        }

        public static void OnCityTransferCompleted(City pCity)
        {
            Kingdom owner = pCity?.kingdom;
            if (pCity?.data == null || owner?.data == null) return;
            owner.data.get(LineageKeys.COUP_RESTORATION_WAR_ID,
                out long warId, -1L);
            War war = FindWar(warId);
            if (war?.data == null || war.hasEnded() || !IsWarType(war) ||
                !war.isAttacker(owner)) return;
            war.data.get(LineageKeys.COUP_RESTORATION_CAPITAL_CITY_ID,
                out long capitalCityId, -1L);
            war.data.get(LineageKeys.COUP_RESTORATION_VICTOR_REBEL_ID,
                out long victorId, -1L);
            if (capitalCityId != pCity.id || victorId != owner.id) return;
            try { World.world?.wars?.endWar(war, WarWinner.Attackers); }
            catch (Exception exception)
            {
                ModClass.LogWarning("Coup restoration settlement failed: " +
                                    exception.Message);
            }
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null || !IsWarType(pWar)) return;
            pWar.data.get(LineageKeys.COUP_RESTORATION_ORIGINAL_KINGDOM_ID,
                out long originalId, -1L);
            pWar.data.get(LineageKeys.COUP_RESTORATION_REBEL_KINGDOM_ID,
                out long rebelId, -1L);
            pWar.data.get(LineageKeys.COUP_RESTORATION_OLD_RULER_ID,
                out long oldRulerId, -1L);
            pWar.data.get(LineageKeys.COUP_RESTORATION_ALTERNATE_CLAIMANT_ID,
                out long alternateId, -1L);
            pWar.data.get(LineageKeys.COUP_RESTORATION_SUPPORTER_ID,
                out long supporterId, -1L);
            pWar.data.get(LineageKeys.COUP_RESTORATION_SUPPORTER_IDS,
                out string supporterIdsRaw, "");
            List<long> supporterIds =
                CoupRestorationRules.DecodeCoalitionIds(supporterIdsRaw);
            if (supporterIds.Count == 0 && supporterId >= 0)
                supporterIds.Add(supporterId);
            pWar.data.get(LineageKeys.COUP_RESTORATION_CAPITAL_CITY_ID,
                out long capitalId, -1L);

            Kingdom original = FindKingdom(originalId) ??
                               pWar.getMainDefender();
            Kingdom rebel = FindKingdom(rebelId) ?? pWar.getMainAttacker();
            CoupRestorationWarWinner winner = pWinner == WarWinner.Attackers
                ? CoupRestorationWarWinner.Loyalists
                : pWinner == WarWinner.Defenders
                    ? CoupRestorationWarWinner.Usurper
                    : CoupRestorationWarWinner.None;
            CoupRestorationSettlement settlement =
                CoupRestorationRules.ResolveSettlement(winner);
            if (settlement == CoupRestorationSettlement.LeaveRivalClaim)
            {
                ClearState(original, rebel);
                RecordStalemate(original, rebel, FindActor(oldRulerId));
                return;
            }

            Actor claimant = ResolveClaimant(rebel, oldRulerId, alternateId);
            DemoteRebelKing(rebel);
            ReturnRebelCities(rebel, original);
            RestoreCapital(original, capitalId);

            if (settlement == CoupRestorationSettlement.RestoreOldDynasty &&
                IsLive(claimant) && InstallClaimant(original, claimant))
                RecordRestored(original, claimant, supporterIds);
            else
                RecordSuppressed(original, claimant, supporterIds);

            ClearState(original, rebel);
            RemoveIfEmpty(rebel);
        }

        private static List<CoupRestorationSupportCandidate> SelectSupporters(
            Kingdom pKingdom, Actor pOldRuler, Actor pUsurper)
        {
            var candidates = new List<Actor>();
            var seen = new HashSet<long>();
            var generalLoyalty = new Dictionary<long, int>();
            try
            {
                List<GeneralReadModelEntry> generals =
                    GeneralService.GetActiveGeneralsForReadModel(pKingdom,
                        pAllowUnitFallback: false,
                        pLimit: CoupRestorationRules.MaximumOfficerCandidates);
                for (int i = 0; i < generals.Count && i <
                                    CoupRestorationRules.MaximumOfficerCandidates;
                     i++)
                {
                    Actor generalActor = generals[i].Actor;
                    if (generalActor?.data != null)
                        generalLoyalty[generalActor.data.id] =
                            generals[i].Loyalty;
                    AddCandidate(generalActor, candidates, seen);
                }
            }
            catch { }
            try
            {
                int cityCandidates = 0;
                foreach (City city in pKingdom.getCities())
                {
                    if (cityCandidates >=
                        CoupRestorationRules.MaximumOfficerCandidates) break;
                    if (AddCandidate(city?.leader, candidates, seen))
                        cityCandidates++;
                }
            }
            catch { }
            try
            {
                List<CourtOfficerView> officers = CourtService.GetActiveOfficers(
                    pKingdom, CoupRestorationRules.MaximumOfficerCandidates);
                for (int i = 0; i < officers.Count; i++)
                    AddCandidate(FindActor(officers[i].actor_id), candidates,
                        seen);
            }
            catch { }

            ReadIdentity(pOldRuler, out long oldLineage, out long oldShi);
            ReadIdentity(pUsurper, out long usurperLineage,
                out long usurperShi);
            var eligible = new List<CoupRestorationSupportCandidate>(
                candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                Actor candidate = candidates[i];
                if (!IsLive(candidate) || candidate == pOldRuler ||
                    candidate == pUsurper || candidate.kingdom != pKingdom)
                    continue;
                City seat = ResolveSeat(candidate);
                if (!ValidSeat(seat, pKingdom)) continue;
                ReadIdentity(candidate, out long lineage, out long shi);
                bool hasCachedGeneral = generalLoyalty.TryGetValue(
                    candidate.data.id, out int cachedGeneralLoyalty);
                bool general = hasCachedGeneral ||
                               GeneralService.IsActiveGeneralFast(candidate);
                bool governor = seat.leader == candidate;
                int institutionalLoyalty = general
                    ? (hasCachedGeneral
                        ? cachedGeneralLoyalty
                        : 50)
                    : SafeCityLoyalty(seat);
                int score = CoupRestorationRules.SupportScore(
                    sameOldLineage: oldLineage >= 0 && lineage == oldLineage,
                    sameOldShi: oldShi >= 0 && shi == oldShi,
                    sameUsurperLineage: usurperLineage >= 0 &&
                                         lineage == usurperLineage,
                    sameUsurperShi: usurperShi >= 0 && shi == usurperShi,
                    ambitious: candidate.hasTrait("ambitious"),
                    content: candidate.hasTrait("content"),
                    general: general, governor: governor,
                    institutionalLoyalty: institutionalLoyalty,
                    traitLoyalty: SafeStat(candidate, "loyalty_traits"));
                eligible.Add(new CoupRestorationSupportCandidate(
                    candidate.data.id, seat.id, score,
                    SafePopulation(seat)));
            }
            return CoupRestorationRules.SelectCoalition(eligible);
        }

        private static bool JoinCoalitionCities(CoupRestorationSeed pSeed,
            Kingdom pOriginal, Kingdom pRebel)
        {
            if (pSeed?.Coalition == null || pSeed.Coalition.Count == 0 ||
                pOriginal?.data == null || pRebel?.data == null) return false;
            for (int i = 1; i < pSeed.Coalition.Count && i <
                                CoupRestorationRules.MaximumCoalitionCities;
                 i++)
            {
                CoupRestorationSupportCandidate member = pSeed.Coalition[i];
                City city = FindCity(member.CityId);
                if (!ValidSeat(city, pOriginal)) return false;
                try
                {
                    city.joinAnotherKingdom(pRebel, pCaptured: false,
                        pRebellion: true);
                }
                catch (Exception exception)
                {
                    ModClass.LogWarning("Coup coalition city split failed: " +
                                        exception.Message);
                    return false;
                }
                if (city.kingdom != pRebel) return false;
            }
            for (int i = 0; i < pSeed.Coalition.Count; i++)
            {
                Actor supporter = FindActor(pSeed.Coalition[i].ActorId);
                if (!IsLive(supporter) || supporter.kingdom != pRebel ||
                    supporter.city?.id != pSeed.Coalition[i].CityId)
                    return false;
            }
            return true;
        }

        private static void FinalizeCoalitionMembers(
            CoupRestorationSeed pSeed, Kingdom pRebel)
        {
            if (pSeed?.Coalition == null || pRebel?.data == null) return;
            for (int i = 0; i < pSeed.Coalition.Count; i++)
            {
                Actor supporter = FindActor(pSeed.Coalition[i].ActorId);
                if (!IsLive(supporter) || supporter.kingdom != pRebel)
                    continue;
                if (GeneralService.IsGeneral(supporter))
                {
                    supporter.data.set("aw_general_rebelled_once", true);
                    GeneralService.MarkRebelled(supporter);
                }
                CourtService.ClearOfficeForReignTransition(supporter,
                    "coup_loyalist_rebellion");
            }
        }

        private static void BindState(CoupRestorationSeed pSeed,
            Kingdom pOriginal, Kingdom pRebel, War pWar)
        {
            pWar.data.set(LineageKeys.COUP_RESTORATION_ORIGINAL_KINGDOM_ID,
                pOriginal.id);
            pWar.data.set(LineageKeys.COUP_RESTORATION_REBEL_KINGDOM_ID,
                pRebel.id);
            pWar.data.set(LineageKeys.COUP_RESTORATION_OLD_RULER_ID,
                pSeed.OldRulerActorId);
            pWar.data.set(LineageKeys.COUP_RESTORATION_ALTERNATE_CLAIMANT_ID,
                pSeed.AlternateClaimantActorId);
            pWar.data.set(LineageKeys.COUP_RESTORATION_SUPPORTER_ID,
                pSeed.SupporterActorId);
            var supporterIds = new List<long>(pSeed.Coalition.Count);
            var seatCityIds = new List<long>(pSeed.Coalition.Count);
            for (int i = 0; i < pSeed.Coalition.Count; i++)
            {
                supporterIds.Add(pSeed.Coalition[i].ActorId);
                seatCityIds.Add(pSeed.Coalition[i].CityId);
            }
            pWar.data.set(LineageKeys.COUP_RESTORATION_SUPPORTER_IDS,
                CoupRestorationRules.EncodeCoalitionIds(supporterIds));
            pWar.data.set(LineageKeys.COUP_RESTORATION_SEAT_CITY_IDS,
                CoupRestorationRules.EncodeCoalitionIds(seatCityIds));
            pWar.data.set(LineageKeys.COUP_RESTORATION_CAPITAL_CITY_ID,
                pSeed.OriginalCapitalCityId);
            pWar.data.set(LineageKeys.COUP_RESTORATION_VICTOR_REBEL_ID, -1L);
            pOriginal.data.set(LineageKeys.COUP_RESTORATION_WAR_ID,
                pWar.data.id);
            pRebel.data.set(LineageKeys.COUP_RESTORATION_WAR_ID,
                pWar.data.id);
        }

        private static bool InstallClaimant(Kingdom pOriginal,
            Actor pClaimant)
        {
            if (pOriginal?.data == null || pOriginal.isRekt() ||
                !IsLive(pClaimant) || pOriginal.capital?.data == null)
                return false;
            try
            {
                if (pOriginal.king?.data != null &&
                    pOriginal.king != pClaimant)
                    pOriginal.kingLeftEvent();
                if (!AccessionIdentityService.Prepare(pOriginal, pClaimant))
                    return false;
                pOriginal.setKing(pClaimant);
                return pOriginal.king == pClaimant;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Coup restoration accession failed: " +
                                    exception.Message);
                return false;
            }
        }

        private static void DemoteRebelKing(Kingdom pRebel)
        {
            try
            {
                if (pRebel?.king?.data != null) pRebel.kingLeftEvent();
            }
            catch { }
        }

        private static void ReturnRebelCities(Kingdom pRebel,
            Kingdom pOriginal)
        {
            if (pRebel?.data == null || pOriginal?.data == null) return;
            var cities = new List<City>();
            try
            {
                foreach (City city in pRebel.getCities())
                    if (city?.data != null) cities.Add(city);
            }
            catch { }
            FeudatoryService.BeginIntentionalJingnanTransfer();
            try
            {
                for (int i = 0; i < cities.Count; i++)
                    if (cities[i].kingdom == pRebel)
                        cities[i].joinAnotherKingdom(pOriginal,
                            pCaptured: false, pRebellion: false);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Coup restoration city return failed: " +
                                    exception.Message);
            }
            finally
            {
                FeudatoryService.EndIntentionalJingnanTransfer();
            }
        }

        private static void RollbackStart(Kingdom pRebel,
            Kingdom pOriginal, Actor pOldRuler)
        {
            if (pRebel?.data == null || pOriginal?.data == null) return;
            DemoteRebelKing(pRebel);
            ReturnRebelCities(pRebel, pOriginal);
            try
            {
                City capital = pOriginal.capital;
                if (IsLive(pOldRuler) && capital?.data != null)
                    using (FormalAffiliationTransferScope.Open(
                               pOldRuler.data.id, pOriginal.id,
                               capital.data.id))
                        pOldRuler.joinCity(capital);
            }
            catch { }
            RemoveIfEmpty(pRebel);
        }

        private static void RestoreCapital(Kingdom pKingdom,
            long pCapitalCityId)
        {
            City capital = FindCity(pCapitalCityId);
            if (pKingdom?.data == null || capital?.data == null ||
                capital.kingdom != pKingdom) return;
            try { pKingdom.setCapital(capital); }
            catch { }
        }

        private static void ClearState(Kingdom pOriginal, Kingdom pRebel)
        {
            if (pOriginal?.data != null)
                pOriginal.data.set(LineageKeys.COUP_RESTORATION_WAR_ID, -1L);
            if (pRebel?.data != null)
                pRebel.data.set(LineageKeys.COUP_RESTORATION_WAR_ID, -1L);
        }

        private static Actor ResolveClaimant(Kingdom pLoyalistKingdom,
            long pOldRulerId, long pAlternateId)
        {
            Actor oldRuler = FindActor(pOldRulerId);
            Actor loyalistKing = pLoyalistKingdom?.king;
            Actor alternate = FindActor(pAlternateId);
            Actor dynasticFallback = FindDynasticFallback(
                pLoyalistKingdom, oldRuler, alternate);
            bool oldRulerEligible = IsRecordedClaimant(oldRuler,
                pLoyalistKingdom);
            bool loyalistKingEligible = IsRestorationClaimant(loyalistKing,
                pLoyalistKingdom, pAllowCurrentKing: true);
            bool alternateEligible = IsRecordedClaimant(alternate,
                pLoyalistKingdom);
            bool fallbackEligible = IsRestorationClaimant(dynasticFallback,
                pLoyalistKingdom, pAllowCurrentKing: false);
            return CoupRestorationRules.SelectClaimantSource(
                oldRulerEligible, loyalistKingEligible, alternateEligible,
                fallbackEligible) switch
            {
                CoupRestorationClaimantSource.OldRuler => oldRuler,
                CoupRestorationClaimantSource.LoyalistKing => loyalistKing,
                CoupRestorationClaimantSource.RecordedHeir => alternate,
                CoupRestorationClaimantSource.DynasticFallback =>
                    dynasticFallback,
                _ => null
            };
        }

        private static Actor FindDynasticFallback(Kingdom pLoyalistKingdom,
            Actor pOldRuler, Actor pRecordedHeir)
        {
            if (pLoyalistKingdom?.data == null) return null;
            Actor preview = HeirService.FindHeirReadOnly(pLoyalistKingdom);
            if (IsRestorationClaimant(preview, pLoyalistKingdom,
                    pAllowCurrentKing: false))
                return preview;
            Actor reference = pOldRuler?.data != null
                ? pOldRuler
                : pRecordedHeir;
            try
            {
                InheritanceCandidateSelection selection =
                    InheritanceCandidateService.SelectCandidate(
                        pLoyalistKingdom,
                        InheritanceLaw.Primogeniture, reference);
                return selection?.Actor;
            }
            catch { return null; }
        }

        private static bool IsRestorationClaimant(Actor pActor,
            Kingdom pLoyalistKingdom, bool pAllowCurrentKing)
        {
            if (!IsLive(pActor) || pLoyalistKingdom?.data == null)
                return false;
            try
            {
                if (pActor.isKing() &&
                    (!pAllowCurrentKing || pActor != pLoyalistKingdom.king))
                    return false;
            }
            catch { return false; }

            pLoyalistKingdom.data.get(
                LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long legitimateLineage, -1L);
            pLoyalistKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                out long legitimateShi, -1L);
            pActor.data.get(LineageKeys.LINEAGE_ID, out long actorLineage,
                -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long actorShi, -1L);
            if (legitimateLineage >= 0)
                return actorLineage == legitimateLineage;
            return legitimateShi >= 0 && actorShi == legitimateShi;
        }

        private static bool IsRecordedClaimant(Actor pActor,
            Kingdom pLoyalistKingdom)
        {
            if (!IsLive(pActor)) return false;
            try
            {
                return !pActor.isKing() ||
                       pActor == pLoyalistKingdom?.king;
            }
            catch { return false; }
        }

        private static City ResolveSeat(Actor pActor)
        {
            City residence = pActor?.city;
            if (residence?.data == null) return null;
            City seat = FiefService.GetFiefCity(pActor);
            if (seat?.data != null && seat == residence) return seat;
            try
            {
                if (pActor.isCityLeader()) return residence;
            }
            catch { }
            return null;
        }

        private static bool ValidSeat(City pCity, Kingdom pKingdom)
        {
            return CoupRestorationRules.CanUseSeat(
                cityAlive: pCity?.data != null && !pCity.isRekt(),
                ownedByRealm: pCity?.kingdom == pKingdom,
                isCapital: pCity != null && pCity == pKingdom?.capital,
                population: SafePopulation(pCity));
        }

        private static bool AddCandidate(Actor pActor,
            ICollection<Actor> pCandidates, ISet<long> pSeen)
        {
            if (pActor?.data == null || !pSeen.Add(pActor.data.id)) return false;
            pCandidates.Add(pActor);
            return true;
        }

        private static void ReadIdentity(Actor pActor, out long pLineage,
            out long pShi)
        {
            pLineage = -1L;
            pShi = -1L;
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.LINEAGE_ID, out pLineage, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out pShi, -1L);
        }

        private static void RemoveIfEmpty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            try
            {
                if (pKingdom.countCities() == 0)
                    World.world?.kingdoms?.removeObject(pKingdom);
            }
            catch { }
        }

        private static bool IsWarType(War pWar)
        {
            try
            {
                return pWar?.getAsset()?.id ==
                       CoupRestorationRules.WarTypeId;
            }
            catch { return false; }
        }

        private static bool IsAtWar(Kingdom pKingdom)
        {
            try
            {
                foreach (War war in pKingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) return true;
            }
            catch { }
            return false;
        }

        private static bool IsLive(Actor pActor)
        {
            try
            {
                return pActor?.data != null && !pActor.isRekt() &&
                       pActor.isAlive();
            }
            catch { return false; }
        }

        private static int CountCities(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int SafeCityLoyalty(City pCity)
        {
            try { return Math.Max(0, Math.Min(100, pCity.getLoyalty())); }
            catch { return 50; }
        }

        private static int SafeStat(Actor pActor, string pStat)
        {
            try
            {
                return (int)Math.Round(pActor?.stats?[pStat] ?? 0f,
                    MidpointRounding.AwayFromZero);
            }
            catch { return 0; }
        }

        private static Actor FindActor(long pActorId)
        {
            try { return pActorId >= 0 ? World.world?.units?.get(pActorId) : null; }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return pCityId >= 0 ? World.world?.cities?.get(pCityId) : null; }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try
            {
                return pKingdomId >= 0
                    ? World.world?.kingdoms?.get(pKingdomId)
                    : null;
            }
            catch { return null; }
        }

        private static War FindWar(long pWarId)
        {
            try { return pWarId >= 0 ? World.world?.wars?.get(pWarId) : null; }
            catch { return null; }
        }

        private static HistoryText H(string pKey)
        {
            return HistoryLocalizationRules.H(pKey);
        }

        private static void RecordStarted(Kingdom pOriginal,
            Kingdom pRebel, Actor pClaimant, CoupRestorationSeed pSeed)
        {
            if (pSeed?.Coalition == null || pSeed.Coalition.Count == 0)
                return;
            CoupRestorationSupportCandidate primary = pSeed.Coalition[0];
            Actor primarySupporter = FindActor(primary.ActorId);
            City primarySeat = FindCity(primary.CityId);
            HistoryText text = BuildStartedText(primarySupporter, pClaimant,
                primarySeat, pRebel, primary.SupportScore);
            HistoryWriter.RecordKingdom(pOriginal,
                "coup_loyalist_rebellion_started", text,
                HistoryTarget.Kingdom(pRebel));
            HistoryWriter.RecordKingdom(pRebel,
                "coup_loyalist_rebellion_started", text,
                HistoryTarget.Kingdom(pOriginal));
            for (int i = 0; i < pSeed.Coalition.Count; i++)
            {
                CoupRestorationSupportCandidate member = pSeed.Coalition[i];
                Actor supporter = FindActor(member.ActorId);
                City seat = FindCity(member.CityId);
                if (supporter?.data == null || seat?.data == null) continue;
                HistoryText memberText = BuildStartedText(supporter,
                    pClaimant, seat, pRebel, member.SupportScore);
                HistoryWriter.RecordPerson(supporter.data.id, pRebel,
                    supporter.getName(), "coup_loyalist_rebellion_started",
                    memberText, ChronicleCategory.WAR,
                    HistoryTarget.Actor(pClaimant));
            }
        }

        private static void RecordRestored(Kingdom pOriginal,
            Actor pClaimant, IReadOnlyList<long> pSupporterIds)
        {
            HistoryText text = HistoryText.Actor(pClaimant) +
                               H("aw_hist_coup_loyalist_restored") +
                               HistoryText.Kingdom(pOriginal) +
                               H("aw_hist_coup_loyalist_restored_suffix");
            HistoryWriter.RecordKingdom(pOriginal,
                "coup_loyalist_restoration_victory", text,
                HistoryTarget.Actor(pClaimant));
            for (int i = 0; i < (pSupporterIds?.Count ?? 0); i++)
            {
                Actor supporter = FindActor(pSupporterIds[i]);
                if (supporter?.data == null) continue;
                HistoryWriter.RecordPerson(supporter.data.id, pOriginal,
                    supporter.getName(),
                    "coup_loyalist_restoration_victory", text,
                    ChronicleCategory.WAR,
                    HistoryTarget.Actor(pClaimant));
            }
        }

        private static void RecordSuppressed(Kingdom pOriginal,
            Actor pClaimant, IReadOnlyList<long> pSupporterIds)
        {
            if (pOriginal?.data == null) return;
            Actor primary = pSupporterIds != null && pSupporterIds.Count > 0
                ? FindActor(pSupporterIds[0])
                : null;
            HistoryText text = HistoryText.Actor(primary) +
                               H("aw_hist_coup_loyalist_suppressed") +
                               HistoryText.Actor(pClaimant,
                                    pClaimant?.getName() ?? "");
            HistoryWriter.RecordKingdom(pOriginal,
                "coup_loyalist_rebellion_suppressed", text,
                primary?.data != null
                    ? HistoryTarget.Actor(primary)
                    : HistoryTarget.Kingdom(pOriginal));
            for (int i = 0; i < (pSupporterIds?.Count ?? 0); i++)
            {
                Actor supporter = FindActor(pSupporterIds[i]);
                if (supporter?.data == null) continue;
                HistoryText memberText = HistoryText.Actor(supporter) +
                                         H("aw_hist_coup_loyalist_suppressed") +
                                         HistoryText.Actor(pClaimant,
                                             pClaimant?.getName() ?? "");
                HistoryWriter.RecordPerson(supporter.data.id, pOriginal,
                    supporter.getName(),
                    "coup_loyalist_rebellion_suppressed", memberText,
                    ChronicleCategory.WAR,
                    HistoryTarget.Actor(pClaimant));
            }
        }

        private static HistoryText BuildStartedText(Actor pSupporter,
            Actor pClaimant, City pSeat, Kingdom pRebel, int pScore)
        {
            return HistoryText.Actor(pSupporter,
                       pSupporter?.getName() ?? "") +
                   H("aw_hist_coup_loyalist_supports") +
                   HistoryText.Actor(pClaimant) +
                   H("aw_hist_coup_loyalist_rose_at") +
                   HistoryText.City(pSeat, pRebel) +
                   H("aw_hist_coup_loyalist_score") +
                   HistoryText.PlainText(pScore.ToString());
        }

        private static void RecordStalemate(Kingdom pOriginal,
            Kingdom pRebel, Actor pClaimant)
        {
            if (pOriginal?.data == null) return;
            HistoryText text = HistoryText.Actor(pClaimant,
                                   pClaimant?.getName() ?? "") +
                               H("aw_hist_coup_loyalist_stalemate") +
                               HistoryText.Kingdom(pRebel);
            HistoryWriter.RecordKingdom(pOriginal,
                "coup_loyalist_rebellion_stalemate", text,
                pRebel?.data != null
                    ? HistoryTarget.Kingdom(pRebel)
                    : HistoryTarget.Kingdom(pOriginal));
        }
    }
}
