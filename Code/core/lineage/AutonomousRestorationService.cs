using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class AutonomousRestorationService
    {
        private struct CampaignRow
        {
            public long campaignId;
            public long claimId;
            public long originalKingdomId;
            public long claimantActorId;
            public long originalMandatePeriodId;
            public string coreCityIds;
            public int coreCursor;
            public int controlledCoreCount;
            public int totalCoreCount;
            public long activeWarId;
            public long targetCityId;
            public long targetKingdomId;
            public int lastAttemptYear;
        }

        private static readonly Dictionary<long, HashSet<long>> CoreIdsByCampaign =
            new Dictionary<long, HashSet<long>>();

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static int _lastWorldYear = -1;
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);

        public static void OnWorldYear()
        {
            if (!Ready || World.world == null) return;
            int year = Date.getCurrentYear();
            if (_lastWorldYear == year) return;
            _lastWorldYear = year;

            try
            {
                foreach (CampaignRow campaign in ReadActiveCampaigns(
                             RoyalRestorationRules.MaxCampaignsPerYear))
                    MaintainCampaign(campaign, year);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Autonomous restoration campaign update failed: " + e.Message);
            }

            if (MandateService.Exists) return;
            if (!MandatePhaseService.CanLaunchAutonomousRestoration) return;

            try
            {
                int starts = 0;
                foreach (RoyalClaimService.ClaimRow claim in RoyalClaimService.GetAutonomousCandidates(
                             year, RoyalRestorationRules.MaxAnnualCandidates))
                {
                    if (starts >= RoyalRestorationRules.MaxAnnualStarts) break;
                    if (TryStartSelfRestoration(claim.claimId, pPlayerRequested: false, out _)) starts++;
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Autonomous restoration scheduling failed: " + e.Message);
            }
        }

        public static bool TryStartSelfRestoration(long pClaimId,
            bool pPlayerRequested, out string pError)
        {
            try
            {
                return TryStartSelfRestorationCore(
                    pClaimId, pPlayerRequested, out pError);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Self restoration start failed: " + e.Message);
                pError = "restoration_internal_error";
                return false;
            }
        }

        public static bool ShouldPreferSelfRestoration(long pClaimId)
        {
            if (!Ready || World.world == null || pClaimId < 0 || MandateService.Exists)
                return false;
            if (!MandatePhaseService.CanLaunchAutonomousRestoration) return false;
            try
            {
                RoyalClaimService.ClaimRow claim =
                    RoyalClaimService.FindDormantClaim(pClaimId);
                if (claim.claimId < 0) return false;
                int year = Date.getCurrentYear();
                bool cooldownReady = RestorationCampaignRules.CooldownReady(
                    year, ReadClaimLastAttemptYear(claim.claimId),
                    playerRequested: false);
                Actor claimant = FindActor(claim.claimantId);
                bool claimantValid =
                    RoyalClaimService.IsAvailableRestorationLeader(claimant);
                bool oldKingdomDead =
                    !IsLiveKingdom(FindKingdom(claim.originalKingdomId));
                City seed = claimantValid && oldKingdomDead
                    ? FindSeedCity(claimant,
                        ReadOldCoreIds(claim, RoyalRestorationRules.MaxCoreCandidates),
                        claim.originalCapitalCityId)
                    : null;
                return RoyalRestorationRules.CanStartAutonomousCampaign(
                    mandateExists: false,
                    chaosPhase: MandatePhaseService.CanLaunchAutonomousRestoration,
                    playerRequested: false,
                    claimStrength: claim.strength,
                    claimantValid,
                    oldKingdomDead,
                    hasEligibleSeed: seed?.data != null,
                    cooldownReady);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryStartSelfRestorationCore(long pClaimId,
            bool pPlayerRequested, out string pError)
        {
            pError = "";
            if (!Ready || World.world == null)
            {
                pError = "restoration_not_ready";
                return false;
            }

            RoyalClaimService.ClaimRow claim = RoyalClaimService.FindDormantClaim(pClaimId);
            if (claim.claimId < 0)
            {
                pError = "restoration_claim_inactive";
                return false;
            }

            if (MandateService.Exists)
            {
                pError = "restoration_mandate_order";
                return false;
            }
            if (!MandatePhaseService.CanLaunchAutonomousRestoration)
            {
                pError = "restoration_phase_order";
                return false;
            }

            int year = Date.getCurrentYear();
            bool cooldownReady = RestorationCampaignRules.CooldownReady(year,
                ReadClaimLastAttemptYear(claim.claimId), pPlayerRequested);
            if (!cooldownReady)
            {
                pError = "restoration_cooldown";
                return false;
            }

            Actor claimant = FindActor(claim.claimantId);
            if (!RoyalClaimService.IsAvailableRestorationLeader(claimant))
            {
                if (!RoyalClaimService.IsEligibleRestorationClaimant(claimant))
                    RoyalClaimService.ResolveClaim(claim.claimId, "invalid_claimant");
                else
                    RoyalClaimService.MarkAutonomousAttempt(claim.claimId, year);
                pError = "restoration_claimant_unavailable";
                return false;
            }

            bool oldKingdomDead = !IsLiveKingdom(FindKingdom(claim.originalKingdomId));
            if (!oldKingdomDead)
            {
                RoyalClaimService.ResolveAllClaimsForKingdom(
                    claim.originalKingdomId, "kingdom_already_restored");
                pError = "restoration_kingdom_alive";
                return false;
            }

            List<long> seedCandidateIds = ReadOldCoreIds(
                claim, RoyalRestorationRules.MaxCoreCandidates);
            City seed = FindSeedCity(claimant, seedCandidateIds,
                claim.originalCapitalCityId);
            bool hasSeed = seed?.data != null;
            if (!RoyalRestorationRules.CanStartAutonomousCampaign(
                    mandateExists: false,
                    chaosPhase: MandatePhaseService.CanLaunchAutonomousRestoration,
                    playerRequested: pPlayerRequested,
                    claimStrength: claim.strength,
                    claimantValid: true,
                    oldKingdomDead: true,
                    hasEligibleSeed: hasSeed,
                    cooldownReady: cooldownReady))
            {
                if (!pPlayerRequested)
                    RoyalClaimService.MarkAutonomousAttempt(claim.claimId, year);
                pError = hasSeed ? "restoration_claim_too_weak" : "restoration_no_eligible_core";
                return false;
            }

            Kingdom seedOwner = seed.kingdom;

            List<long> allCoreIds = ReadOldCoreIds(
                claim, RestorationCampaignRules.MaxPersistedCoreIds);
            allCoreIds = FilterLivingCoreIds(allCoreIds);
            if (!allCoreIds.Contains(seed.data.id)) allCoreIds.Add(seed.data.id);
            string encodedCoreIds = RestorationCampaignRules.EncodeCoreIds(allCoreIds);
            allCoreIds = RestorationCampaignRules.DecodeCoreIds(encodedCoreIds);
            if (allCoreIds.Count == 0)
            {
                RoyalClaimService.MarkAutonomousAttempt(claim.claimId, year);
                pError = "restoration_no_living_core";
                return false;
            }

            long campaignId = RoyalClaimService.BeginSelfCampaign(
                claim, claimant, seed, encodedCoreIds,
                pControlled: 1, pTotal: allCoreIds.Count, pYear: year);
            if (campaignId < 0)
            {
                pError = "restoration_campaign_conflict";
                return false;
            }

            var request = new KingdomRestorationRequest
            {
                claim_id = claim.claimId,
                original_kingdom_id = claim.originalKingdomId,
                original_kingdom_name = claim.originalKingdomName,
                original_capital_city_id = claim.originalCapitalCityId,
                original_mandate_period_id = claim.originalMandatePeriodId,
                lineage_id = claim.lineageId,
                shi_id = claim.shiId,
                clan_name = claim.clanName,
                state_name = claim.shiId >= 0
                    ? StateNameService.GetBoundStateName(claim.shiId)
                    : "",
                mode = "self_restoration"
            };
            Kingdom restored = KingdomIdentityContinuityService.RestoreFromCity(
                seed, claimant, request, out string restoreError);
            if (!IsLiveKingdom(restored))
            {
                RoyalClaimService.FailSelfCampaign(campaignId,
                    claim.originalKingdomId, year, "creation_failed");
                pError = string.IsNullOrEmpty(restoreError)
                    ? "restoration_creation_failed"
                    : restoreError;
                return false;
            }

            SetCampaignRuntime(restored, campaignId, claim.claimId,
                claim.originalMandatePeriodId, year);
            CoreIdsByCampaign[campaignId] = new HashSet<long>(allCoreIds);
            RestorationUprisingMobilizationService.Start(restored, seed, campaignId);
            HistoryText uprising = HistoryText.Actor(claimant, claim.claimantName) +
                                   H("aw_hist_restoration_uprising_at") +
                                   HistoryText.City(seed, restored) +
                                   H("aw_hist_restoration_uprising_suffix");
            HistoryWriter.RecordKingdom(restored, KingdomEvent.RESTORATION_UPRISING,
                uprising, HistoryTarget.Actor(claimant));
            HistoryWriter.RecordPerson(claimant.data.id, restored, claimant.getName(),
                PersonEvent.RESTORATION_UPRISING, uprising, ChronicleCategory.HONOR,
                HistoryTarget.Kingdom(restored));

            CampaignRow started = ReadActiveCampaignById(campaignId);
            if (started.campaignId >= 0)
            {
                bool coreWarStarted = false;
                if (!RoyalRestorationRules.HasRecoveredCoreThreshold(
                        started.controlledCoreCount, started.totalCoreCount))
                    coreWarStarted = TryStartNextCoreWar(
                        started, restored, year, seedOwner?.id ?? -1L);
                if (!coreWarStarted)
                    TryStartFormerOwnerWar(
                        started, restored, seedOwner, seed, year);
            }
            return true;
        }

        public static void OnCityTransferred(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            try { OnCityTransferredCore(pCity, pOldKingdom, pNewKingdom); }
            catch (Exception e)
            {
                ModClass.LogWarning("Restoration city progress failed: " + e.Message);
            }
        }

        private static void OnCityTransferredCore(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (!Ready || pCity?.data == null) return;
            Kingdom restored = IsActiveCampaignKingdom(pNewKingdom)
                ? pNewKingdom
                : IsActiveCampaignKingdom(pOldKingdom)
                    ? pOldKingdom
                    : null;
            if (restored?.data == null) return;

            CampaignRow campaign = ReadActiveCampaignForKingdom(restored.id);
            if (campaign.campaignId < 0) return;
            bool isCore = GetCampaignCoreIds(campaign).Contains(pCity.data.id);
            int controlled = RestorationCampaignRules.AdjustControlledCoreCount(
                campaign.controlledCoreCount,
                campaign.totalCoreCount,
                isCore,
                pOldKingdom == restored,
                pNewKingdom == restored);
            if (controlled == campaign.controlledCoreCount) return;

            UpdateCampaignProgress(campaign.campaignId, controlled);
            campaign.controlledCoreCount = controlled;
            if (RoyalRestorationRules.HasRecoveredCoreThreshold(
                    controlled, campaign.totalCoreCount))
                CompleteCampaign(campaign, restored);
        }

        public static void OnWarEnded(War pWar)
        {
            try { OnWarEndedCore(pWar); }
            catch (Exception e)
            {
                ModClass.LogWarning("Restoration war completion failed: " + e.Message);
            }
        }

        private static void OnWarEndedCore(War pWar)
        {
            if (!Ready || pWar?.data == null) return;
            CampaignRow campaign = ReadActiveCampaignByWar(pWar.data.id);
            if (campaign.campaignId < 0) return;
            ClearCampaignWar(campaign.campaignId, Date.getCurrentYear());
            campaign.activeWarId = -1L;
            campaign.targetCityId = -1L;
            campaign.targetKingdomId = -1L;
            Kingdom restored = FindKingdom(campaign.originalKingdomId);
            if (IsLiveKingdom(restored) && RoyalRestorationRules.HasRecoveredCoreThreshold(
                    campaign.controlledCoreCount, campaign.totalCoreCount))
                CompleteCampaign(campaign, restored);
        }

        public static bool OnKingdomDestroying(Kingdom pKingdom)
        {
            if (!IsActiveCampaignKingdom(pKingdom)) return false;
            pKingdom.data.get(LineageKeys.RESTORATION_CAMPAIGN_ID,
                out long campaignId, -1L);
            CampaignRow campaign = campaignId >= 0
                ? ReadActiveCampaignById(campaignId)
                : ReadActiveCampaignForKingdom(pKingdom.id);
            if (campaign.campaignId < 0)
            {
                RestorationUprisingMobilizationService.Fail(pKingdom, campaignId);
                ClearCampaignRuntime(pKingdom);
                return RoyalClaimService.RecoverOrphanedSelfCampaignClaims(
                    pKingdom.id, Date.getCurrentYear());
            }

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RESTORATION_FAILED,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_restoration_failed_suffix"),
                HistoryTarget.Actor(campaign.claimantActorId));
            RestorationUprisingMobilizationService.Fail(
                pKingdom, campaign.campaignId);
            RoyalClaimService.FailSelfCampaign(campaign.campaignId,
                campaign.originalKingdomId, Date.getCurrentYear(),
                "restoration_regime_fell");
            ClearCampaignRuntime(pKingdom);
            CoreIdsByCampaign.Remove(campaign.campaignId);
            return true;
        }

        public static bool IsActiveCampaignKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.RESTORATION_CAMPAIGN_ACTIVE,
                out bool active, false);
            return active;
        }

        public static void ClearRuntime()
        {
            _lastWorldYear = -1;
            CoreIdsByCampaign.Clear();
            RestorationUprisingMobilizationService.ClearRuntime();
        }

        public static void RebuildRuntime()
        {
            _lastWorldYear = -1;
            CoreIdsByCampaign.Clear();
            RestorationUprisingMobilizationService.RebuildRuntime();
        }

        private static void MaintainCampaign(CampaignRow pCampaign, int pYear)
        {
            Kingdom restored = FindKingdom(pCampaign.originalKingdomId);
            if (IsLiveKingdom(restored))
            {
                EnsureCampaignRuntime(restored, pCampaign, pYear);
                RestorationUprisingMobilizationService.OnCampaignYear(
                    restored, pCampaign.campaignId);
            }
            RestorationCampaignAction action = RestorationCampaignRules.ResolveAction(
                IsLiveKingdom(restored),
                RoyalClaimService.IsEligibleRestorationClaimant(
                    FindActor(pCampaign.claimantActorId)),
                RoyalRestorationRules.HasRecoveredCoreThreshold(
                    pCampaign.controlledCoreCount, pCampaign.totalCoreCount));
            if (action == RestorationCampaignAction.Fail)
            {
                RestorationUprisingMobilizationService.Fail(
                    restored, pCampaign.campaignId);
                RoyalClaimService.FailSelfCampaign(pCampaign.campaignId,
                    pCampaign.originalKingdomId, pYear, "restoration_regime_missing");
                CoreIdsByCampaign.Remove(pCampaign.campaignId);
                return;
            }
            if (action == RestorationCampaignAction.Complete)
            {
                CompleteCampaign(pCampaign, restored);
                return;
            }

            if (pCampaign.activeWarId >= 0)
            {
                War activeWar = FindWar(pCampaign.activeWarId);
                if (activeWar?.data != null && !activeWar.hasEnded()) return;
                ClearCampaignWar(pCampaign.campaignId, pYear);
                pCampaign.activeWarId = -1L;
            }
            if (HasActiveWar(restored) || pCampaign.lastAttemptYear >= pYear) return;

            TryStartNextCoreWar(pCampaign, restored, pYear, -1L);
        }

        private static bool TryStartNextCoreWar(CampaignRow pCampaign,
            Kingdom pRestored, int pYear, long pPreferredDefenderId)
        {
            List<long> cores = RestorationCampaignRules.DecodeCoreIds(pCampaign.coreCityIds);
            if (cores.Count == 0)
            {
                RestorationUprisingMobilizationService.Fail(
                    pRestored, pCampaign.campaignId);
                RoyalClaimService.FailSelfCampaign(pCampaign.campaignId,
                    pCampaign.originalKingdomId, pYear, "restoration_no_core");
                ClearCampaignRuntime(pRestored);
                CoreIdsByCampaign.Remove(pCampaign.campaignId);
                return false;
            }

            int inspected = 0;
            int start = Math.Max(0, pCampaign.coreCursor) % cores.Count;
            int limit = Math.Min(RoyalRestorationRules.MaxCoreCandidates, cores.Count);
            Actor claimant = FindActor(pCampaign.claimantActorId) ?? pRestored.king;
            City fallback = null;
            for (int i = 0; i < limit; i++)
            {
                inspected++;
                City target = FindCity(cores[(start + i) % cores.Count]);
                Kingdom defender = target?.kingdom;
                if (target?.data == null || target.isRekt() ||
                    !IsLiveKingdom(defender) || defender == pRestored)
                    continue;
                if (pPreferredDefenderId >= 0 && defender.id != pPreferredDefenderId)
                {
                    if (fallback == null) fallback = target;
                    continue;
                }
                War war = WarTerritoryService.TryDeclareAutonomousRestorationCoreWar(
                    pRestored, target, pCampaign.claimId, claimant);
                if (war?.data == null) continue;
                int cursor = RestorationCampaignRules.NextCoreCursor(
                    pCampaign.coreCursor, inspected, cores.Count);
                UpdateCampaignWar(pCampaign.campaignId, war.data.id,
                    target.data.id, defender.id, cursor, pYear);
                return true;
            }

            if (fallback?.data != null && fallback.kingdom?.data != null &&
                fallback.kingdom != pRestored)
            {
                Kingdom fallbackDefender = fallback.kingdom;
                War war = WarTerritoryService.TryDeclareAutonomousRestorationCoreWar(
                    pRestored, fallback, pCampaign.claimId, claimant);
                if (war?.data != null)
                {
                    int cursor = RestorationCampaignRules.NextCoreCursor(
                        pCampaign.coreCursor, inspected, cores.Count);
                    UpdateCampaignWar(pCampaign.campaignId, war.data.id,
                        fallback.data.id, fallbackDefender.id, cursor, pYear);
                    return true;
                }
            }

            int nextCursor = RestorationCampaignRules.NextCoreCursor(
                pCampaign.coreCursor, inspected, cores.Count);
            UpdateCampaignCursor(pCampaign.campaignId, nextCursor, pYear);
            return false;
        }

        private static bool TryStartFormerOwnerWar(CampaignRow pCampaign,
            Kingdom pRestored, Kingdom pFormerOwner, City pSeed, int pYear)
        {
            if (!IsLiveKingdom(pRestored) || !IsLiveKingdom(pFormerOwner) ||
                pFormerOwner == pRestored || pSeed?.data == null ||
                HasActiveWar(pRestored)) return false;
            War war = WarDecisionService.TryStartSystemWar(
                pRestored, pFormerOwner, WarDecisionService.WAR_RESTORATION,
                "self_restoration_uprising");
            if (war?.data == null) return false;
            UpdateCampaignWar(pCampaign.campaignId, war.data.id,
                pSeed.id, pFormerOwner.id, pCampaign.coreCursor, pYear);
            return true;
        }

        private static void CompleteCampaign(CampaignRow pCampaign, Kingdom pRestored)
        {
            if (!RoyalClaimService.CompleteSelfCampaign(pCampaign.campaignId,
                    pCampaign.originalKingdomId, "self_restoration_completed"))
                return;
            RestorationUprisingMobilizationService.Complete(
                pRestored, pCampaign.campaignId);
            pRestored.data.set(LineageKeys.RESTORATION_CAMPAIGN_ACTIVE, false);
            pRestored.data.set(LineageKeys.RESTORATION_CAMPAIGN_ID, -1L);
            pRestored.data.set(LineageKeys.RESTORATION_CLAIM_ID, -1L);
            pRestored.data.set(LineageKeys.RESTORATION_MODE, "self_restoration");
            pRestored.data.set(LineageKeys.RESTORATION_COMPLETED, true);
            pRestored.data.set(LineageKeys.RESTORATION_ORIGINAL_MANDATE_PERIOD_ID,
                pCampaign.originalMandatePeriodId);
            pRestored.data.set(LineageKeys.RESTORATION_REFUNDER_ELIGIBLE,
                pCampaign.originalMandatePeriodId >= 0);
            pRestored.data.set(LineageKeys.RESTORATION_LAST_YEAR, Date.getCurrentYear());
            RulerTitleRestorationStateService.MarkAutonomousRestorationCompleted(pRestored);
            CoreIdsByCampaign.Remove(pCampaign.campaignId);

            Actor ruler = pRestored.king;
            HistoryText completed = HistoryText.Kingdom(pRestored) +
                                    H("aw_hist_restoration_completed_suffix");
            HistoryWriter.RecordKingdom(pRestored, KingdomEvent.RESTORATION_COMPLETED,
                completed, HistoryTarget.Actor(ruler));
            if (ruler?.data != null)
                HistoryWriter.RecordPerson(ruler.data.id, pRestored, ruler.getName(),
                    PersonEvent.RESTORATION_COMPLETED, completed, ChronicleCategory.HONOR,
                    HistoryTarget.Kingdom(pRestored));
        }

        private static void SetCampaignRuntime(Kingdom pKingdom, long pCampaignId,
            long pClaimId, long pMandatePeriodId, int pYear)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.RESTORATION_CAMPAIGN_ACTIVE, true);
            pKingdom.data.set(LineageKeys.RESTORATION_CAMPAIGN_ID, pCampaignId);
            pKingdom.data.set(LineageKeys.RESTORATION_CLAIM_ID, pClaimId);
            pKingdom.data.set(LineageKeys.RESTORATION_MODE, "restoration_uprising");
            pKingdom.data.set(LineageKeys.RESTORATION_ORIGINAL_MANDATE_PERIOD_ID,
                pMandatePeriodId);
            pKingdom.data.set(LineageKeys.RESTORATION_COMPLETED, false);
            pKingdom.data.set(LineageKeys.RESTORATION_REFUNDER_ELIGIBLE, false);
            pKingdom.data.set(LineageKeys.RESTORATION_LAST_YEAR, pYear);
        }

        private static void EnsureCampaignRuntime(Kingdom pKingdom,
            CampaignRow pCampaign, int pYear)
        {
            if (IsActiveCampaignKingdom(pKingdom)) return;
            SetCampaignRuntime(pKingdom, pCampaign.campaignId, pCampaign.claimId,
                pCampaign.originalMandatePeriodId, pYear);
        }

        private static void ClearCampaignRuntime(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.RESTORATION_CAMPAIGN_ACTIVE, false);
            pKingdom.data.set(LineageKeys.RESTORATION_CAMPAIGN_ID, -1L);
            pKingdom.data.set(LineageKeys.RESTORATION_CLAIM_ID, -1L);
        }

        private static City FindSeedCity(Actor pClaimant, List<long> pCoreIds,
            long pOriginalCapitalCityId)
        {
            if (pCoreIds == null) return null;
            Kingdom peacefulHost = pClaimant?.kingdom;
            City best = null;
            RestorationSeedScore bestScore = default;
            bool hasBest = false;
            foreach (long cityId in pCoreIds)
            {
                City city = FindCity(cityId);
                Kingdom owner = city?.kingdom;
                bool valid = city?.data != null && !city.isRekt();
                bool ownerValid = IsLiveKingdom(owner);
                bool peacefulHostCity = ownerValid && owner == peacefulHost;
                if (!RoyalRestorationRules.CanUseSeedCity(valid,
                        oldCore: true, peacefulHostCity, ownerValid)) continue;
                RestorationSeedScore score = ScoreSeedCity(
                    pClaimant, city, pOriginalCapitalCityId);
                if (hasBest && RestorationUprisingRules.CompareSeeds(
                        score, bestScore) >= 0) continue;
                best = city;
                bestScore = score;
                hasBest = true;
            }
            return best;
        }

        private static RestorationSeedScore ScoreSeedCity(Actor pClaimant,
            City pCity, long pOriginalCapitalCityId)
        {
            int distanceSquared = 1_000_000;
            float resentment = 0f;
            int population = 0;
            int defenders = 0;
            try
            {
                WorldTile claimantTile = pClaimant?.current_tile;
                WorldTile cityTile = pCity?.getTile();
                if (claimantTile != null && cityTile != null)
                    distanceSquared = Toolbox.SquaredDistTile(claimantTile, cityTile);
            }
            catch { }
            try { resentment = ForeignOccupationService.GetResentment(pCity); }
            catch { }
            try { population = pCity?.getPopulationPeople() ?? 0; }
            catch { }
            try
            {
                if (pCity != null && pCity.hasArmy())
                    defenders = pCity.getArmy()?.countUnits() ?? 0;
            }
            catch { }
            return new RestorationSeedScore(
                pCity?.id ?? -1L,
                pCity?.id == pOriginalCapitalCityId,
                pClaimant?.city == pCity,
                distanceSquared,
                resentment,
                population,
                defenders);
        }

        private static List<long> FilterLivingCoreIds(List<long> pCoreIds)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();
            if (pCoreIds == null) return result;
            foreach (long cityId in pCoreIds)
            {
                if (!seen.Add(cityId)) continue;
                City city = FindCity(cityId);
                if (city?.data == null || city.isRekt()) continue;
                result.Add(cityId);
            }
            return result;
        }

        private static List<long> ReadOldCoreIds(RoyalClaimService.ClaimRow pClaim,
            int pLimit)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();
            int limit = Math.Max(0, Math.Min(pLimit,
                RestorationCampaignRules.MaxPersistedCoreIds));
            if (!Ready || limit == 0) return result;
            AddCoreId(result, seen, pClaim.originalCapitalCityId, limit);
            if (pClaim.originalMandatePeriodId >= 0 && result.Count < limit)
            {
                using var mandate = new SQLiteCommand(DB);
                mandate.CommandText = $"SELECT CITY_ID FROM {MandateCoreCityTableItem.GetTableName()} " +
                                      "WHERE PERIOD_ID=@p AND ACTIVE=1 ORDER BY CORE_ID ASC LIMIT @lim";
                mandate.Parameters.AddWithValue("@p", pClaim.originalMandatePeriodId);
                mandate.Parameters.AddWithValue("@lim", limit - result.Count);
                using var reader = (SQLiteDataReader)mandate.ExecuteReader();
                while (reader.Read() && result.Count < limit)
                    AddCoreId(result, seen, reader.GetInt64(0), limit);
            }
            if (result.Count < limit)
            {
                using var core = new SQLiteCommand(DB);
                core.CommandText = $"SELECT CITY_ID FROM {KingdomCoreTableItem.GetTableName()} " +
                                   "WHERE KINGDOM_ID=@k AND ACTIVE=1 ORDER BY CREATED_TIME ASC, CORE_ID ASC LIMIT @lim";
                core.Parameters.AddWithValue("@k", pClaim.originalKingdomId);
                core.Parameters.AddWithValue("@lim", limit - result.Count);
                using var reader = (SQLiteDataReader)core.ExecuteReader();
                while (reader.Read() && result.Count < limit)
                    AddCoreId(result, seen, reader.GetInt64(0), limit);
            }
            return result;
        }

        private static void AddCoreId(List<long> pResult, HashSet<long> pSeen,
            long pCityId, int pLimit)
        {
            if (pCityId < 0 || pResult.Count >= pLimit || !pSeen.Add(pCityId)) return;
            pResult.Add(pCityId);
        }

        private static HashSet<long> GetCampaignCoreIds(CampaignRow pCampaign)
        {
            if (CoreIdsByCampaign.TryGetValue(pCampaign.campaignId, out HashSet<long> cached))
                return cached;
            cached = new HashSet<long>(
                RestorationCampaignRules.DecodeCoreIds(pCampaign.coreCityIds));
            CoreIdsByCampaign[pCampaign.campaignId] = cached;
            return cached;
        }

        private static List<CampaignRow> ReadActiveCampaigns(int pLimit)
        {
            var result = new List<CampaignRow>();
            if (!Ready || pLimit <= 0) return result;
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = CampaignSelect() +
                              " WHERE STATE='uprising' ORDER BY LAST_ATTEMPT_YEAR ASC, CAMPAIGN_ID ASC LIMIT @lim";
            cmd.Parameters.AddWithValue("@lim", pLimit);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read()) result.Add(ReadCampaign(reader));
            return result;
        }

        private static CampaignRow ReadActiveCampaignById(long pCampaignId)
        {
            if (!Ready || pCampaignId < 0) return InvalidCampaign();
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = CampaignSelect() +
                              " WHERE CAMPAIGN_ID=@id AND STATE='uprising' LIMIT 1";
            cmd.Parameters.AddWithValue("@id", pCampaignId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            return reader.Read() ? ReadCampaign(reader) : InvalidCampaign();
        }

        private static CampaignRow ReadActiveCampaignForKingdom(long pKingdomId)
        {
            if (!Ready || pKingdomId < 0) return InvalidCampaign();
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = CampaignSelect() +
                              " WHERE ORIGINAL_KINGDOM_ID=@k AND STATE='uprising' " +
                              "ORDER BY CAMPAIGN_ID DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@k", pKingdomId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            return reader.Read() ? ReadCampaign(reader) : InvalidCampaign();
        }

        private static CampaignRow ReadActiveCampaignByWar(long pWarId)
        {
            if (!Ready || pWarId < 0) return InvalidCampaign();
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = CampaignSelect() +
                              " WHERE ACTIVE_WAR_ID=@w AND STATE='uprising' LIMIT 1";
            cmd.Parameters.AddWithValue("@w", pWarId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            return reader.Read() ? ReadCampaign(reader) : InvalidCampaign();
        }

        private static string CampaignSelect()
        {
            return $"SELECT CAMPAIGN_ID, CLAIM_ID, ORIGINAL_KINGDOM_ID, CLAIMANT_ACTOR_ID, " +
                   $"ORIGINAL_MANDATE_PERIOD_ID, CORE_CITY_IDS, CORE_CURSOR, CONTROLLED_CORE_COUNT, " +
                   $"TOTAL_CORE_COUNT, ACTIVE_WAR_ID, TARGET_CITY_ID, TARGET_KINGDOM_ID, " +
                   $"LAST_ATTEMPT_YEAR FROM {RestorationCampaignTableItem.GetTableName()}";
        }

        private static CampaignRow ReadCampaign(SQLiteDataReader pReader)
        {
            return new CampaignRow
            {
                campaignId = pReader.GetInt64(0),
                claimId = pReader.GetInt64(1),
                originalKingdomId = pReader.GetInt64(2),
                claimantActorId = pReader.GetInt64(3),
                originalMandatePeriodId = pReader.IsDBNull(4) ? -1L : pReader.GetInt64(4),
                coreCityIds = pReader.IsDBNull(5) ? "" : pReader.GetString(5),
                coreCursor = pReader.IsDBNull(6) ? 0 : pReader.GetInt32(6),
                controlledCoreCount = pReader.IsDBNull(7) ? 0 : pReader.GetInt32(7),
                totalCoreCount = pReader.IsDBNull(8) ? 0 : pReader.GetInt32(8),
                activeWarId = pReader.IsDBNull(9) ? -1L : pReader.GetInt64(9),
                targetCityId = pReader.IsDBNull(10) ? -1L : pReader.GetInt64(10),
                targetKingdomId = pReader.IsDBNull(11) ? -1L : pReader.GetInt64(11),
                lastAttemptYear = pReader.IsDBNull(12) ? -1 : pReader.GetInt32(12)
            };
        }

        private static CampaignRow InvalidCampaign()
        {
            return new CampaignRow { campaignId = -1L };
        }

        private static int ReadClaimLastAttemptYear(long pClaimId)
        {
            if (!Ready || pClaimId < 0) return -1;
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = $"SELECT LAST_ATTEMPT_YEAR FROM {RoyalClaimTableItem.GetTableName()} " +
                              "WHERE CLAIM_ID=@c LIMIT 1";
            cmd.Parameters.AddWithValue("@c", pClaimId);
            object value = cmd.ExecuteScalar();
            return value == null || value == DBNull.Value ? -1 : Convert.ToInt32(value);
        }

        private static void UpdateCampaignProgress(long pCampaignId, int pControlled)
        {
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = $"UPDATE {RestorationCampaignTableItem.GetTableName()} " +
                              "SET CONTROLLED_CORE_COUNT=@count WHERE CAMPAIGN_ID=@id AND STATE='uprising'";
            cmd.Parameters.AddWithValue("@count", pControlled);
            cmd.Parameters.AddWithValue("@id", pCampaignId);
            cmd.ExecuteNonQuery();
        }

        private static void UpdateCampaignWar(long pCampaignId, long pWarId,
            long pCityId, long pKingdomId, int pCursor, int pYear)
        {
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = $"UPDATE {RestorationCampaignTableItem.GetTableName()} SET " +
                              "ACTIVE_WAR_ID=@war, TARGET_CITY_ID=@city, TARGET_KINGDOM_ID=@kingdom, " +
                              "CORE_CURSOR=@cursor, LAST_ATTEMPT_YEAR=@year " +
                              "WHERE CAMPAIGN_ID=@id AND STATE='uprising'";
            cmd.Parameters.AddWithValue("@war", pWarId);
            cmd.Parameters.AddWithValue("@city", pCityId);
            cmd.Parameters.AddWithValue("@kingdom", pKingdomId);
            cmd.Parameters.AddWithValue("@cursor", pCursor);
            cmd.Parameters.AddWithValue("@year", pYear);
            cmd.Parameters.AddWithValue("@id", pCampaignId);
            cmd.ExecuteNonQuery();
        }

        private static void UpdateCampaignCursor(long pCampaignId, int pCursor, int pYear)
        {
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = $"UPDATE {RestorationCampaignTableItem.GetTableName()} SET " +
                              "CORE_CURSOR=@cursor, LAST_ATTEMPT_YEAR=@year " +
                              "WHERE CAMPAIGN_ID=@id AND STATE='uprising'";
            cmd.Parameters.AddWithValue("@cursor", pCursor);
            cmd.Parameters.AddWithValue("@year", pYear);
            cmd.Parameters.AddWithValue("@id", pCampaignId);
            cmd.ExecuteNonQuery();
        }

        private static void ClearCampaignWar(long pCampaignId, int pYear)
        {
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = $"UPDATE {RestorationCampaignTableItem.GetTableName()} SET " +
                              "ACTIVE_WAR_ID=-1, TARGET_CITY_ID=-1, TARGET_KINGDOM_ID=-1, " +
                              "LAST_ATTEMPT_YEAR=@year WHERE CAMPAIGN_ID=@id AND STATE='uprising'";
            cmd.Parameters.AddWithValue("@year", pYear);
            cmd.Parameters.AddWithValue("@id", pCampaignId);
            cmd.ExecuteNonQuery();
        }

        private static bool HasActiveWar(Kingdom pKingdom)
        {
            if (!IsLiveKingdom(pKingdom)) return false;
            try
            {
                foreach (War war in pKingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) return true;
            }
            catch { }
            return false;
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0 || World.world?.kingdoms == null) return null;
            try { return World.world.kingdoms.get(pKingdomId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0 || World.world?.cities == null) return null;
            try { return World.world.cities.get(pCityId); }
            catch { return null; }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0 || World.world?.units == null) return null;
            try { return World.world.units.get(pActorId); }
            catch { return null; }
        }

        private static War FindWar(long pWarId)
        {
            if (pWarId < 0 || World.world?.wars == null) return null;
            try { return World.world.wars.get(pWarId); }
            catch { return null; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() && !pKingdom.isNeutral();
        }
    }
}
