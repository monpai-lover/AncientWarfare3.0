using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.schools;

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
            public string state;
            public long seedCityId;
            public long rollbackSeedOwnerId;
            public long rollbackPreviousClaimantKingdomId;
            public long rollbackPreviousClaimantCityId;
            public int rollbackAttempts;
        }

        private sealed class SeedSelection
        {
            public City City;
            public long CityId;
            public Kingdom Owner;
            public long OwnerId;
            public List<long> SupporterIds;
            public int Defenders;
        }

        private enum PendingInitializationResult
        {
            StillPending = 0,
            Completed = 1,
            RolledBack = 2
        }

        private static readonly Dictionary<long, HashSet<long>> CoreIdsByCampaign =
            new Dictionary<long, HashSet<long>>();

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static int _lastWorldYear = -1;
        [ThreadStatic] private static bool _rollingBackProvisional;
        [ThreadStatic] private static long _rollingBackProvisionalKingdomId;
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
                    pClaimId, pPlayerRequested,
                    pRebellionTriggered: false, pRequiredSeed: null,
                    RestorationRebellionSeedMode.Core,
                    out _, out _, out pError);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Self restoration start failed: " + e.Message);
                pError = "restoration_internal_error";
                return false;
            }
        }

        internal static RestorationRebellionStartOutcome
            TryStartSelfRestorationFromRebellion(long pClaimId,
                Actor pClaimant, City pRequiredSeed, out string pError)
        {
            return TryStartSelfRestorationFromRequiredSeed(pClaimId,
                pClaimant, pRequiredSeed,
                RestorationRebellionSeedMode.Core, out _, out pError);
        }

        internal static RestorationRebellionStartOutcome
            TryStartSelfRestorationFromExternalBandit(long pClaimId,
                Actor pClaimant, City pExternalSeed,
                out Kingdom pRestored, out string pError)
        {
            return TryStartSelfRestorationFromRequiredSeed(pClaimId,
                pClaimant, pExternalSeed,
                RestorationRebellionSeedMode.ExternalBandit,
                out pRestored, out pError);
        }

        private static RestorationRebellionStartOutcome
            TryStartSelfRestorationFromRequiredSeed(long pClaimId,
                Actor pClaimant, City pRequiredSeed,
                RestorationRebellionSeedMode pSeedMode,
                out Kingdom pRestored, out string pError)
        {
            bool committed = false;
            pRestored = null;
            try
            {
                if (pClaimant?.data == null || pRequiredSeed?.data == null ||
                    pClaimId < 0)
                {
                    pError = "restoration_rebellion_invalid_context";
                    return RestorationRebellionStartOutcome.NotStarted;
                }
                bool started = TryStartSelfRestorationCore(
                    pClaimId, pPlayerRequested: false,
                    pRebellionTriggered: true, pRequiredSeed,
                    pSeedMode, out committed, out pRestored, out pError);
                return RestorationRebellionRedirectRules.ResolveOutcome(
                    started, committed);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Restoration rebellion start failed: " + e.Message);
                pError = "restoration_rebellion_internal_error";
                return RestorationRebellionRedirectRules.ResolveOutcome(
                    started: false, identityCreationCommitted: committed);
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
                if (!RoyalRestorationRules.IsAutonomousYearEligible(
                        year, claim.earliestAutonomousYear)) return false;
                bool cooldownReady = RestorationCampaignRules.CooldownReady(
                    year, ReadClaimLastAttemptYear(claim.claimId),
                    playerRequested: false);
                Actor claimant = FindActor(claim.claimantId);
                bool claimantValid =
                    RoyalClaimService.IsAvailableRestorationLeader(claimant);
                bool oldKingdomDead =
                    !IsLiveKingdom(FindKingdom(claim.originalKingdomId));
                SeedSelection seed = claimantValid && oldKingdomDead
                    ? FindSeedSelection(claimant,
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
                    hasEligibleSeed: seed?.City?.data != null,
                    cooldownReady);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryStartSelfRestorationCore(long pClaimId,
            bool pPlayerRequested, bool pRebellionTriggered,
            City pRequiredSeed, RestorationRebellionSeedMode pSeedMode,
            out bool pIdentityCreationCommitted, out Kingdom pRestored,
            out string pError)
        {
            pIdentityCreationCommitted = false;
            pRestored = null;
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
            if (!RoyalRestorationRules.IsAutonomousYearEligible(
                    year, claim.earliestAutonomousYear))
            {
                pError = "restoration_treaty_guard";
                return false;
            }
            bool cooldownReady = RestorationCampaignRules.CooldownReady(year,
                ReadClaimLastAttemptYear(claim.claimId),
                pPlayerRequested || pRebellionTriggered);
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
                else if (!pPlayerRequested && !pRebellionTriggered)
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

            List<long> allCoreIds = pRequiredSeed == null
                ? null
                : ReadOldCoreIds(claim,
                    RestorationCampaignRules.MaxPersistedCoreIds);
            bool seedIsPersistedCore = pRequiredSeed != null &&
                allCoreIds.Contains(pRequiredSeed.id);
            if (pRequiredSeed != null &&
                !RestorationRebellionRedirectRules.CanUseRequiredSeed(
                    pSeedMode, oldKingdomDead,
                    pRequiredSeed.id == claim.originalCapitalCityId,
                    seedIsPersistedCore))
            {
                pError = "restoration_rebellion_seed_not_allowed";
                return false;
            }

            List<long> seedCandidateIds = pRequiredSeed == null
                ? ReadOldCoreIds(claim,
                    RoyalRestorationRules.MaxCoreCandidates)
                : new List<long> { pRequiredSeed.id };
            SeedSelection seedSelection = FindSeedSelection(
                claimant, seedCandidateIds,
                claim.originalCapitalCityId, pRebellionTriggered,
                pSeedAllowed: true);
            if (pRequiredSeed != null &&
                seedSelection?.City != pRequiredSeed)
            {
                pError = "restoration_rebellion_seed_invalid";
                return false;
            }
            City seed = seedSelection?.City;
            bool hasSeed = seed?.data != null;
            if (!RoyalRestorationRules.CanStartAutonomousCampaign(
                    mandateExists: false,
                    chaosPhase: MandatePhaseService.CanLaunchAutonomousRestoration,
                    playerRequested: pPlayerRequested,
                    claimStrength: claim.strength,
                    claimantValid: true,
                    oldKingdomDead: true,
                    hasEligibleSeed: hasSeed,
                    cooldownReady: cooldownReady,
                    rebellionTriggered: pRebellionTriggered))
            {
                if (!pPlayerRequested && !pRebellionTriggered)
                    RoyalClaimService.MarkAutonomousAttempt(claim.claimId, year);
                pError = hasSeed ? "restoration_claim_too_weak" : "restoration_no_eligible_core";
                return false;
            }

            allCoreIds ??= ReadOldCoreIds(
                claim, RestorationCampaignRules.MaxPersistedCoreIds);
            seedIsPersistedCore = allCoreIds.Contains(seed.data.id);
            allCoreIds = FilterLivingCoreIds(allCoreIds);
            if (pSeedMode == RestorationRebellionSeedMode.ExternalBandit)
                SortCoreIdsByDistance(allCoreIds, seed);
            else if (!allCoreIds.Contains(seed.data.id))
                allCoreIds.Add(seed.data.id);
            string encodedCoreIds = RestorationCampaignRules.EncodeCoreIds(allCoreIds);
            allCoreIds = RestorationCampaignRules.DecodeCoreIds(encodedCoreIds);
            if (allCoreIds.Count == 0)
            {
                if (!pPlayerRequested && !pRebellionTriggered)
                    RoyalClaimService.MarkAutonomousAttempt(claim.claimId,
                        year);
                pError = "restoration_no_living_core";
                return false;
            }

            if (!RevalidateSeedSelection(seedSelection, claimant,
                    pRebellionTriggered, pSeedAllowed: true))
            {
                if (!pPlayerRequested && !pRebellionTriggered)
                    RoyalClaimService.MarkAutonomousAttempt(claim.claimId,
                        year);
                pError = "restoration_seed_changed";
                return false;
            }
            Kingdom seedOwner = seedSelection.Owner;

            int controlledCoreCount =
                RestorationRebellionRedirectRules.ShouldCountSeedAsCore(
                    pSeedMode, seedIsPersistedCore) ? 1 : 0;
            long campaignId = RoyalClaimService.BeginSelfCampaign(
                claim, claimant, seed, encodedCoreIds,
                pControlled: controlledCoreCount,
                pTotal: allCoreIds.Count, pYear: year);
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
            Kingdom previousClaimantKingdom = claimant.kingdom;
            City previousClaimantCity = claimant.city;
            Kingdom restored = KingdomIdentityContinuityService.RestoreFromCity(
                seed, claimant, request, out string restoreError);
            pRestored = restored;
            if (!IsLiveKingdom(restored))
            {
                pIdentityCreationCommitted =
                    IsLiveKingdom(seed?.kingdom) &&
                    seed.kingdom != seedOwner;
                RoyalClaimService.FailSelfCampaign(campaignId,
                    claim.originalKingdomId, year, "creation_failed");
                pError = string.IsNullOrEmpty(restoreError)
                    ? "restoration_creation_failed"
                    : restoreError;
                return false;
            }
            pIdentityCreationCommitted = true;

            SetCampaignRuntime(restored, campaignId, claim.claimId,
                claim.originalMandatePeriodId, year);
            CoreIdsByCampaign[campaignId] = new HashSet<long>(allCoreIds);
            int postCreationDefenders = Math.Max(seedSelection.Defenders,
                CountSeedDefenders(seed));
            int requiredSupporters =
                RoyalRestorationRules.MinimumRequiredSupporters(
                    postCreationDefenders);
            List<long> postCreationSupporterIds =
                RestorationUprisingMobilizationService
                    .RevalidateInitialSupporterIds(seed, restored,
                        seedSelection.SupporterIds, claimant.data.id);
            bool postCreationSeedValid = RoyalRestorationRules.CanUseSeedCity(
                cityValid: seed?.data != null && !seed.isRekt(),
                oldCore: true,
                peacefulHostCity: false,
                ownerValid: IsLiveKingdom(restored) && seed?.kingdom == restored,
                activeOrFrozenCapture: HasActiveOrFrozenCapture(seed),
                population: ReadSeedPopulation(seed),
                supporters: postCreationSupporterIds.Count,
                defenders: postCreationDefenders);
            bool postCreationContextValid =
                claimant?.data != null && claimant.isAlive() &&
                !claimant.isRekt() && IsLiveKingdom(restored) &&
                seed?.data != null && !seed.isRekt() &&
                seed.kingdom == restored;
            bool cohortStarted = postCreationSeedValid &&
                RestorationUprisingMobilizationService
                    .TryStartWithInitialCohort(
                        restored, seed, campaignId,
                        postCreationSupporterIds, requiredSupporters);
            if (!cohortStarted)
            {
                if (RestorationRebellionRedirectRules
                    .ShouldRetryCommittedInitialization(
                        pSeedMode, pIdentityCreationCommitted,
                        postCreationContextValid))
                {
                    restored.data.set(
                        LineageKeys.RESTORATION_INITIALIZATION_PENDING,
                        true);
                    RoyalClaimService.MarkSelfCampaignInitializationPending(
                        campaignId, claim.originalKingdomId,
                        seedOwner?.id ?? -1L,
                        previousClaimantKingdom?.id ?? -1L,
                        previousClaimantCity?.id ?? -1L, year);
                    pError = "restoration_initialization_pending";
                    return false;
                }
                bool rollbackCompleted = RollbackProvisionalRestoration(
                    restored, seed,
                    seedSelection.Owner, campaignId, claim.originalKingdomId,
                    year, claimant, previousClaimantKingdom,
                    previousClaimantCity);
                pError = rollbackCompleted
                    ? "restoration_initial_cohort_failed"
                    : "restoration_rollback_pending";
                return false;
            }
            CompleteLaunchInitialization(campaignId, restored, seed,
                claimant, seedOwner, year);
            return true;
        }

        private static void CompleteLaunchInitialization(long pCampaignId,
            Kingdom pRestored, City pSeed, Actor pClaimant,
            Kingdom pSeedOwner, int pYear)
        {
            if (pRestored?.data == null || pClaimant?.data == null) return;
            HistoryText uprising = HistoryText.Actor(pClaimant,
                pClaimant.getName()) + H("aw_hist_restoration_uprising_at") +
                HistoryText.City(pSeed, pRestored) +
                H("aw_hist_restoration_uprising_suffix");
            HistoryWriter.RecordKingdom(pRestored,
                KingdomEvent.RESTORATION_UPRISING, uprising,
                HistoryTarget.Actor(pClaimant));
            HistoryWriter.RecordPerson(pClaimant.data.id, pRestored,
                pClaimant.getName(), PersonEvent.RESTORATION_UPRISING,
                uprising, ChronicleCategory.HONOR,
                HistoryTarget.Kingdom(pRestored));

            CampaignRow started = ReadActiveCampaignById(pCampaignId);
            if (started.campaignId < 0) return;
            bool coreWarStarted = false;
            if (!RoyalRestorationRules.HasRecoveredCoreThreshold(
                    started.controlledCoreCount, started.totalCoreCount))
                coreWarStarted = TryStartNextCoreWar(
                    started, pRestored, pYear,
                    pSeedOwner?.id ?? -1L);
            if (!coreWarStarted)
                TryStartFormerOwnerWar(started, pRestored, pSeedOwner,
                    pSeed, pYear);
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
            if (!RoyalRestorationRules.CanAdvanceRestorationCampaign(
                    campaign.state)) return;
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
            TryCompleteStableCampaign(campaign, restored);
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
            TryCompleteStableCampaign(campaign, restored);
        }

        public static bool OnKingdomDestroying(Kingdom pKingdom)
        {
            if (_rollingBackProvisional && pKingdom?.data != null &&
                pKingdom.id == _rollingBackProvisionalKingdomId)
                return true;
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
            if (campaign.state == "rollback_pending") return true;

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
            if (!RoyalRestorationRules.CanAdvanceRestorationCampaign(
                    pCampaign.state))
            {
                if (pCampaign.state == "rollback_pending")
                    MaintainRollbackPendingCampaign(pCampaign, pYear);
                return;
            }
            Kingdom restored = FindKingdom(pCampaign.originalKingdomId);
            if (IsLiveKingdom(restored))
            {
                restored.data.get(
                    LineageKeys.RESTORATION_INITIALIZATION_PENDING,
                    out bool initializationPending, false);
                if (initializationPending)
                {
                    PendingInitializationResult pendingResult =
                        TryResumePendingInitialization(
                            pCampaign, restored, pYear);
                    if (pendingResult ==
                        PendingInitializationResult.Completed)
                        CompleteLaunchInitialization(
                            pCampaign.campaignId, restored,
                            FindCity(pCampaign.seedCityId),
                            FindActor(pCampaign.claimantActorId), null,
                            pYear);
                    return;
                }
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
                if (TryCompleteStableCampaign(pCampaign, restored)) return;
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

        private static PendingInitializationResult
            TryResumePendingInitialization(CampaignRow pCampaign,
                Kingdom pRestored, int pYear)
        {
            City seed = FindCity(pCampaign.seedCityId);
            Actor claimant = FindActor(pCampaign.claimantActorId);
            if (!IsLiveKingdom(pRestored) || claimant?.data == null ||
                !claimant.isAlive() || claimant.isRekt() ||
                seed?.data == null || seed.isRekt() ||
                seed.kingdom != pRestored)
            {
                bool rolledBack = RollbackProvisionalRestoration(
                    pRestored, seed,
                    FindKingdom(pCampaign.rollbackSeedOwnerId),
                    pCampaign.campaignId, pCampaign.originalKingdomId,
                    pYear, claimant,
                    FindKingdom(pCampaign.rollbackPreviousClaimantKingdomId),
                    FindCity(pCampaign.rollbackPreviousClaimantCityId));
                return rolledBack
                    ? PendingInitializationResult.RolledBack
                    : PendingInitializationResult.StillPending;
            }

            int defenders = CountSeedDefenders(seed);
            List<long> candidates =
                RestorationUprisingMobilizationService.CollectInitialSupporterIds(
                    seed, RoyalRestorationRules.MaxSeedResidentsInspected,
                    claimant.data.id, out _);
            List<long> supporters =
                RestorationUprisingMobilizationService.RevalidateInitialSupporterIds(
                    seed, pRestored, candidates, claimant.data.id);
            bool seedValid = RoyalRestorationRules.CanUseSeedCity(
                cityValid: true, oldCore: true,
                peacefulHostCity: false, ownerValid: true,
                activeOrFrozenCapture: HasActiveOrFrozenCapture(seed),
                population: ReadSeedPopulation(seed),
                supporters: supporters.Count, defenders: defenders);
            int required = RoyalRestorationRules.MinimumRequiredSupporters(
                defenders);
            if (!seedValid ||
                !RestorationUprisingMobilizationService
                    .TryStartWithInitialCohort(
                        pRestored, seed, pCampaign.campaignId,
                        supporters, required))
                return PendingInitializationResult.StillPending;

            pRestored.data.set(
                LineageKeys.RESTORATION_INITIALIZATION_PENDING, false);
            RoyalClaimService.ClearSelfCampaignInitializationPending(
                pCampaign.campaignId, pCampaign.originalKingdomId);
            return PendingInitializationResult.Completed;
        }

        private static void MaintainRollbackPendingCampaign(
            CampaignRow pCampaign, int pYear)
        {
            City seed = FindCity(pCampaign.seedCityId);
            Kingdom originalOwner = FindKingdom(
                pCampaign.rollbackSeedOwnerId);
            Kingdom restored = FindKingdom(pCampaign.originalKingdomId);
            Actor claimant = FindActor(pCampaign.claimantActorId);
            Kingdom previousClaimantKingdom = FindKingdom(
                pCampaign.rollbackPreviousClaimantKingdomId);
            City previousClaimantCity = FindCity(
                pCampaign.rollbackPreviousClaimantCityId);
            bool seedReturned = SeedReturnedToOriginalOwner(seed,
                originalOwner);
            if (seedReturned && !IsLiveKingdom(restored))
            {
                CoreIdsByCampaign.Remove(pCampaign.campaignId);
                RoyalClaimService.FailSelfCampaign(pCampaign.campaignId,
                    pCampaign.originalKingdomId, pYear,
                    "initial_cohort_failed");
                return;
            }
            if (!IsLiveKingdom(restored)) return;
            RollbackProvisionalRestoration(restored, seed, originalOwner,
                pCampaign.campaignId, pCampaign.originalKingdomId, pYear,
                claimant, previousClaimantKingdom, previousClaimantCity);
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
                // 邀请「复国支持者」参战:与被复国方同宗族、
                // 或有旧领土诉求的他国势力可以加入攻方。
                TryInviteRestorationSupporters(war, pRestored,
                    pCampaign.originalKingdomId);
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
            War war = WarDecisionService.TryStartInternalSystemWar(
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
            pRestored.data.set(
                LineageKeys.RESTORATION_INITIALIZATION_PENDING, false);
            pRestored.data.set(LineageKeys.RESTORATION_MODE, "self_restoration");
            pRestored.data.set(LineageKeys.RESTORATION_COMPLETED, true);
            pRestored.data.set(LineageKeys.RESTORATION_ORIGINAL_MANDATE_PERIOD_ID,
                pCampaign.originalMandatePeriodId);
            pRestored.data.set(LineageKeys.RESTORATION_REFUNDER_ELIGIBLE,
                pCampaign.originalMandatePeriodId >= 0);
            pRestored.data.set(LineageKeys.RESTORATION_LAST_YEAR, Date.getCurrentYear());
            RulerTitleRestorationStateService.MarkAutonomousRestorationCompleted(pRestored);
            EraChangeTriggerService.Mark(pRestored,
                EraChangeReason.AutonomousRestoration,
                "restoration:" + pCampaign.campaignId);
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

        private static bool TryCompleteStableCampaign(CampaignRow pCampaign,
            Kingdom pRestored)
        {
            bool live = IsLiveKingdom(pRestored);
            bool hasCity = false;
            if (live)
            {
                try { hasCity = pRestored.countCities() > 0; }
                catch { hasCity = pRestored.hasCities(); }
            }
            bool recovered = RoyalRestorationRules.HasRecoveredCoreThreshold(
                pCampaign.controlledCoreCount, pCampaign.totalCoreCount);
            if (!RoyalRestorationRules.CanCompleteCampaign(recovered, live,
                    hasCity, HasActiveWar(pRestored))) return false;
            CompleteCampaign(pCampaign, pRestored);
            return true;
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
            pKingdom.data.set(
                LineageKeys.RESTORATION_INITIALIZATION_PENDING, false);
        }

        private static City FindSeedCity(Actor pClaimant, List<long> pCoreIds,
            long pOriginalCapitalCityId)
        {
            return FindSeedSelection(pClaimant, pCoreIds,
                pOriginalCapitalCityId)?.City;
        }

        private static SeedSelection FindSeedSelection(Actor pClaimant,
            List<long> pCoreIds, long pOriginalCapitalCityId,
            bool pRebellionTriggered = false, bool pSeedAllowed = true)
        {
            if (pCoreIds == null) return null;
            Kingdom peacefulHost = pClaimant?.kingdom;
            City best = null;
            Kingdom bestOwner = null;
            List<long> bestSupporters = null;
            int bestDefenders = 0;
            RestorationSeedScore bestScore = default;
            bool hasBest = false;
            foreach (long cityId in pCoreIds)
            {
                int candidateInspectionBudget =
                    RoyalRestorationRules.MaxSeedResidentsInspected;
                City city = FindCity(cityId);
                Kingdom owner = city?.kingdom;
                bool valid = city?.data != null && !city.isRekt();
                bool ownerValid = IsLiveKingdom(owner);
                bool peacefulHostCity =
                    RestorationRebellionRedirectRules.IsPeacefulHostCity(
                        ownerValid && owner == peacefulHost,
                        pRebellionTriggered);
                int population = ReadSeedPopulation(city);
                int defenders = CountSeedDefenders(city);
                bool activeOrFrozenCapture = HasActiveOrFrozenCapture(city);
                if (!valid || !ownerValid || peacefulHostCity ||
                    activeOrFrozenCapture ||
                    population < RoyalRestorationRules.MinimumSeedPopulation ||
                    RoyalRestorationRules.MinimumPreflightSupporters(defenders) >
                    candidateInspectionBudget) continue;
                List<long> supporters =
                    RestorationUprisingMobilizationService
                        .CollectInitialSupporterIds(city,
                            candidateInspectionBudget,
                            pClaimant?.data?.id ?? -1L,
                            out int inspectedResidents);
                if (!RoyalRestorationRules.CanUseSeedCity(valid,
                        oldCore: pSeedAllowed, peacefulHostCity, ownerValid,
                        activeOrFrozenCapture,
                        population, supporters: supporters.Count,
                        defenders) ||
                    !RoyalRestorationRules.HasRequiredPreflightSupporters(
                        supporters.Count, defenders)) continue;
                RestorationSeedScore score = ScoreSeedCity(
                    pClaimant, city, pOriginalCapitalCityId);
                if (hasBest && RestorationUprisingRules.CompareSeeds(
                        score, bestScore) >= 0) continue;
                best = city;
                bestOwner = owner;
                bestSupporters = supporters;
                bestDefenders = defenders;
                bestScore = score;
                hasBest = true;
            }
            return best?.data == null
                ? null
                : new SeedSelection
                {
                    City = best,
                    CityId = best.id,
                    Owner = bestOwner,
                    OwnerId = bestOwner?.id ?? -1L,
                    SupporterIds = bestSupporters ?? new List<long>(),
                    Defenders = bestDefenders
                };
        }

        private static bool RevalidateSeedSelection(SeedSelection pSelection,
            Actor pClaimant, bool pRebellionTriggered = false,
            bool pSeedAllowed = true)
        {
            bool peacefulHostCity =
                RestorationRebellionRedirectRules.IsPeacefulHostCity(
                    pSelection?.Owner == pClaimant?.kingdom,
                    pRebellionTriggered);
            if (pSelection?.City?.data == null ||
                pSelection.Owner?.data == null ||
                pSelection.City.id != pSelection.CityId ||
                pSelection.Owner.id != pSelection.OwnerId ||
                FindCity(pSelection.CityId) != pSelection.City ||
                pSelection.City.kingdom != pSelection.Owner ||
                !IsLiveKingdom(pSelection.Owner) ||
                peacefulHostCity)
                return false;
            int population = ReadSeedPopulation(pSelection.City);
            int defenders = Math.Max(pSelection.Defenders,
                CountSeedDefenders(pSelection.City));
            List<long> supporters = RestorationUprisingMobilizationService
                .RevalidateInitialSupporterIds(pSelection.City,
                    pSelection.Owner, pSelection.SupporterIds,
                    pClaimant?.data?.id ?? -1L);
            if (!RoyalRestorationRules.CanUseSeedCity(
                    cityValid: !pSelection.City.isRekt(),
                    oldCore: pSeedAllowed,
                    peacefulHostCity, ownerValid: true,
                    activeOrFrozenCapture:
                    HasActiveOrFrozenCapture(pSelection.City),
                    population, supporters.Count, defenders) ||
                !RoyalRestorationRules.HasRequiredPreflightSupporters(
                    supporters.Count, defenders)) return false;
            pSelection.SupporterIds = supporters;
            pSelection.Defenders = defenders;
            return true;
        }

        private static int ReadSeedPopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static int CountSeedDefenders(City pCity)
        {
            try
            {
                return pCity != null && pCity.hasArmy()
                    ? Math.Max(0, pCity.getArmy()?.countUnits() ?? 0)
                    : 0;
            }
            catch { return 0; }
        }

        private static bool HasActiveOrFrozenCapture(City pCity)
        {
            if (pCity?.data == null) return true;
            try { if (pCity.isGettingCaptured()) return true; }
            catch { return true; }
            try { return WarScoreService.ShouldHoldFrozenOccupation(pCity); }
            catch { return true; }
        }

        private static bool RollbackProvisionalRestoration(Kingdom pRestored,
            City pSeed, Kingdom pOriginalOwner, long pCampaignId,
            long pOriginalKingdomId, int pYear, Actor pClaimant,
            Kingdom pPreviousClaimantKingdom, City pPreviousClaimantCity)
        {
            if (!PersistRollbackPending(pCampaignId, pOriginalKingdomId,
                    pSeed, pOriginalOwner, pPreviousClaimantKingdom,
                    pPreviousClaimantCity, pYear, pAttempts: 0))
                return false;
            bool physicalCleanupComplete =
                RestorationUprisingMobilizationService
                    .TryCleanupForRollback(pRestored, pCampaignId);
            ProvisionalRollbackAction action = RoyalRestorationRules
                .ResolveProvisionalRollbackAction(
                    rollbackPending: true, physicalCleanupComplete,
                    SeedReturnedToOriginalOwner(pSeed, pOriginalOwner));
            if (action == ProvisionalRollbackAction.RetryCleanup)
            {
                PersistRollbackPending(pCampaignId, pOriginalKingdomId,
                    pSeed, pOriginalOwner, pPreviousClaimantKingdom,
                    pPreviousClaimantCity, pYear, pAttempts: 1);
                return false;
            }
            _rollingBackProvisional = true;
            _rollingBackProvisionalKingdomId = pRestored?.id ?? -1L;
            bool seedReturned = SeedReturnedToOriginalOwner(
                pSeed, pOriginalOwner);
            for (int attempt = 0; attempt < 2 && !seedReturned; attempt++)
            {
                try
                {
                    if (pSeed?.data != null && pOriginalOwner?.data != null)
                        pSeed.joinAnotherKingdom(pOriginalOwner,
                            pCaptured: false, pRebellion: false);
                }
                catch (Exception e)
                {
                    ModClass.LogWarning(
                        "Provisional restoration city rollback failed: " +
                        e.Message);
                }
                seedReturned = SeedReturnedToOriginalOwner(
                    pSeed, pOriginalOwner);
            }
            if (!RoyalRestorationRules.CanFinalizeProvisionalRollback(
                    seedReturned))
            {
                _rollingBackProvisional = false;
                _rollingBackProvisionalKingdomId = -1L;
                PersistRollbackPending(pCampaignId, pOriginalKingdomId,
                    pSeed, pOriginalOwner, pPreviousClaimantKingdom,
                    pPreviousClaimantCity, pYear, pAttempts: 2);
                return false;
            }
            try
            {
                City claimantDestination = pPreviousClaimantCity?.data != null &&
                                           !pPreviousClaimantCity.isRekt() &&
                                           pPreviousClaimantCity.kingdom ==
                                           pPreviousClaimantKingdom
                    ? pPreviousClaimantCity
                    : pPreviousClaimantKingdom?.capital;
                if (pClaimant?.data != null &&
                    IsLiveKingdom(pPreviousClaimantKingdom) &&
                    claimantDestination?.data != null)
                {
                    using (FormalAffiliationTransferScope.Open(
                               pClaimant.data.id,
                               pPreviousClaimantKingdom.id,
                               claimantDestination.id))
                        pClaimant.joinCity(claimantDestination);
                    try { pClaimant.ai?.setJob(pClaimant.getNextJob()); }
                    catch { }
                    try { HeirService.RefreshHeir(pPreviousClaimantKingdom); }
                    catch { }
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Provisional restoration claimant rollback failed: " +
                                    e.Message);
            }
            bool provisionalRemoved = false;
            try
            {
                Kingdom registered = pRestored?.data == null
                    ? null
                    : World.world?.kingdoms?.get(pRestored.id);
                if (registered == pRestored)
                    World.world.kingdoms.removeObject(pRestored);
                provisionalRemoved = pRestored?.data == null ||
                                     World.world?.kingdoms?.get(
                                         pRestored.id) != pRestored;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Provisional restoration kingdom rollback failed: " +
                                    e.Message);
            }
            finally
            {
                _rollingBackProvisional = false;
                _rollingBackProvisionalKingdomId = -1L;
            }
            if (!provisionalRemoved)
            {
                PersistRollbackPending(pCampaignId, pOriginalKingdomId,
                    pSeed, pOriginalOwner, pPreviousClaimantKingdom,
                    pPreviousClaimantCity, pYear, pAttempts: 1);
                return false;
            }
            ClearCampaignRuntime(pRestored);
            CoreIdsByCampaign.Remove(pCampaignId);
            RoyalClaimService.FailSelfCampaign(pCampaignId,
                pOriginalKingdomId, pYear, "initial_cohort_failed");
            return true;
        }

        private static bool PersistRollbackPending(long pCampaignId,
            long pOriginalKingdomId, City pSeed, Kingdom pOriginalOwner,
            Kingdom pPreviousClaimantKingdom, City pPreviousClaimantCity,
            int pYear, int pAttempts)
        {
            return RoyalClaimService.MarkSelfCampaignRollbackPending(
                pCampaignId, pOriginalKingdomId,
                pOriginalOwner?.id ?? -1L,
                pPreviousClaimantKingdom?.id ?? -1L,
                pPreviousClaimantCity?.id ?? -1L,
                pYear, pAttempts);
        }

        private static bool SeedReturnedToOriginalOwner(City pSeed,
            Kingdom pOriginalOwner)
        {
            return pSeed?.data != null && pOriginalOwner?.data != null &&
                   pSeed.kingdom == pOriginalOwner;
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

        private static void SortCoreIdsByDistance(List<long> pCoreIds,
            City pSeed)
        {
            if (pCoreIds == null || pCoreIds.Count < 2 ||
                pSeed?.getTile() == null) return;
            WorldTile seedTile = pSeed.getTile();
            var distanceByCity = new Dictionary<long, int>();
            foreach (long cityId in pCoreIds)
            {
                int distance = int.MaxValue;
                City city = FindCity(cityId);
                try
                {
                    WorldTile targetTile = city?.getTile();
                    if (targetTile != null)
                        distance = Toolbox.SquaredDistTile(seedTile,
                            targetTile);
                }
                catch { }
                distanceByCity[cityId] = distance;
            }
            pCoreIds.Sort((left, right) =>
                RestorationRebellionRedirectRules.CompareCoreTargets(
                    distanceByCity[left], left,
                    distanceByCity[right], right));
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
                              " WHERE STATE IN ('uprising','rollback_pending') " +
                              "ORDER BY LAST_ATTEMPT_YEAR ASC, CAMPAIGN_ID ASC LIMIT @lim";
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
                              " WHERE CAMPAIGN_ID=@id AND STATE IN " +
                              "('uprising','rollback_pending') LIMIT 1";
            cmd.Parameters.AddWithValue("@id", pCampaignId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            return reader.Read() ? ReadCampaign(reader) : InvalidCampaign();
        }

        private static CampaignRow ReadActiveCampaignForKingdom(long pKingdomId)
        {
            if (!Ready || pKingdomId < 0) return InvalidCampaign();
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = CampaignSelect() +
                              " WHERE ORIGINAL_KINGDOM_ID=@k AND STATE IN " +
                              "('uprising','rollback_pending') " +
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
                   $"LAST_ATTEMPT_YEAR, STATE, SEED_CITY_ID, ROLLBACK_SEED_OWNER_ID, " +
                   $"ROLLBACK_PREVIOUS_CLAIMANT_KINGDOM_ID, " +
                   $"ROLLBACK_PREVIOUS_CLAIMANT_CITY_ID, ROLLBACK_ATTEMPTS " +
                   $"FROM {RestorationCampaignTableItem.GetTableName()}";
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
                lastAttemptYear = pReader.IsDBNull(12) ? -1 : pReader.GetInt32(12),
                state = pReader.IsDBNull(13) ? "uprising" : pReader.GetString(13),
                seedCityId = pReader.IsDBNull(14) ? -1L : pReader.GetInt64(14),
                rollbackSeedOwnerId = pReader.IsDBNull(15) ? -1L : pReader.GetInt64(15),
                rollbackPreviousClaimantKingdomId = pReader.IsDBNull(16)
                    ? -1L : pReader.GetInt64(16),
                rollbackPreviousClaimantCityId = pReader.IsDBNull(17)
                    ? -1L : pReader.GetInt64(17),
                rollbackAttempts = pReader.IsDBNull(18) ? 0 : pReader.GetInt32(18)
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

        /// <summary>
        ///     复国战争开打后，邀请有意愿的他国势力加入攻方。
        ///
        ///     「支持者」满足以下所有条件：
        ///     <list type="number">
        ///     <item>与被复国方同宗族（LINEAGE_ID 相同）或共享原王国 ID</item>
        ///     <item>与防守方（当前占着那座城的国家）处于敌对状态，
        ///           或对防守方持有强烈负面看法（opinion ≤ −40）</item>
        ///     <item>与被复国方之间没有活跃战争</item>
        ///     <item>没有在别的战争里</item>
        ///     </list>
        ///
        ///     最多邀请 <see cref="MaxRestorationSupporters"/> 个国家，
        ///     防止一场复国战争变成天下围攻。
        /// </summary>
        private static void TryInviteRestorationSupporters(War pWar,
            Kingdom pRestored, long pOriginalKingdomId)
        {
            const int MaxRestorationSupporters = 2;
            if (pWar?.data == null || !IsLiveKingdom(pRestored)) return;
            try
            {
                Kingdom defender = pWar.getMainDefender();
                if (defender?.data == null) return;

                // 取被复国方的宗族 ID，用来匹配同宗支持者。
                pRestored.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                    out long restoredLineageId, -1L);

                int invited = 0;
                foreach (Kingdom candidate in World.world?.kingdoms ??
                    System.Linq.Enumerable.Empty<Kingdom>())
                {
                    if (invited >= MaxRestorationSupporters) break;
                    if (!IsLiveKingdom(candidate)) continue;
                    if (candidate == pRestored || candidate == defender) continue;
                    // 已在这场战争里——跳过。
                    try { if (pWar.hasKingdom(candidate)) continue; } catch { }
                    // 不能与被复国方有活跃战争。
                    try
                    {
                        if (World.world.wars.getWar(pRestored, candidate, false) != null)
                            continue;
                    }
                    catch { }
                    // 正在打别的战争的国家精力有限，不参与。
                    if (candidate.hasEnemies()) continue;

                    // 同宗族判定。
                    bool sameLineage = false;
                    if (restoredLineageId >= 0)
                    {
                        candidate.data.get(
                            LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                            out long candidateLineage, -1L);
                        sameLineage = candidateLineage == restoredLineageId;
                    }
                    if (!sameLineage &&
                        candidate.id != pOriginalKingdomId) continue;

                    // 需要对防守方有负面立场，或曾与防守方交战。
                    bool hostile;
                    try { hostile = candidate.isEnemy(defender); }
                    catch { hostile = false; }
                    if (!hostile)
                    {
                        try
                        {
                            int opinion = World.world.diplomacy
                                .getOpinion(candidate, defender).total;
                            hostile = opinion <= -40;
                        }
                        catch { hostile = false; }
                    }
                    if (!hostile) continue;

                    using (WarParticipantEntrySourceScope.Open(pWar,
                        candidate, WarParticipantEntrySourceKind.ScriptedJoin,
                        pRestored))
                    {
                        try { pWar.joinAttackers(candidate); }
                        catch { continue; }
                    }
                    invited++;
                    ModClass.LogInfo("[AW3] 复国支持者参战: " +
                        (candidate.name ?? "?") + " 支持 " +
                        (pRestored.name ?? "?") + " 对抗 " +
                        (defender.name ?? "?"));
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("[AW3] 复国支持者邀请失败: " + error.Message);
            }
        }
    }
}