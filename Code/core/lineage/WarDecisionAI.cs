using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class WarDecisionAI
    {
        private const string LAST_CHECK_YEAR = "aw_war_ai_last_check_year";
        private const string LAST_ACTION_YEAR = "aw_war_ai_last_action_year";
        private const string CLAIM_TARGET_ID =
            WarClaimPreparationService.TargetKey;
        private const int CHECK_INTERVAL = 6;
        private const int ACTION_COOLDOWN = 18;
        private static readonly Random Rng = new Random();
        private static readonly Dictionary<long, long> AsyncAdmissionLeases =
            new Dictionary<long, long>();
        private static long _nextAsyncAdmissionLeaseId;

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            _ = RunAuthoritativeYear(pKingdom);
        }

        internal static AsyncStrategyAuthorityTrace RunAuthoritativeYear(
            Kingdom pKingdom)
        {
            if (!CanRunFor(pKingdom))
                return AsyncStrategyAuthorityTrace.Skipped("ineligible");

            int year = Date.getCurrentYear();
            pKingdom.data.get(LAST_CHECK_YEAR, out int lastCheck, -99999);
            if (year - lastCheck < CHECK_INTERVAL)
                return AsyncStrategyAuthorityTrace.Skipped("cadence");
            pKingdom.data.set(LAST_CHECK_YEAR, year);
            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);

            if (TryDeclarePreparedWar(pKingdom, court))
            {
                pKingdom.data.set(LAST_ACTION_YEAR, year);
                return AsyncStrategyAuthorityTrace.Skipped("prepared_war");
            }

            pKingdom.data.get(LAST_ACTION_YEAR, out int lastAction, -99999);
            if (year - lastAction < ACTION_COOLDOWN)
                return AsyncStrategyAuthorityTrace.Skipped("cooldown");

            Kingdom target = PickNormalWarTarget(pKingdom, court,
                out IReadOnlyList<AsyncStrategyCandidate> traceCandidates);
            string trace = AsyncStrategyShadowRules.SummarizeDecisions(
                traceCandidates);
            if (target?.data == null)
                return AsyncStrategyAuthorityTrace.Planned(trace);
            WarStrategyCandidateKind selectedKind =
                traceCandidates.Count > 0
                    ? traceCandidates[0].WarKind
                    : WarStrategyCandidateKind.None;
            bool shouldIssue = selectedKind ==
                               WarStrategyCandidateKind.Zhulu
                ? ZhuluWarRules.ShouldIssueDiplomaticDeclaration(
                    Rng.NextDouble())
                : Chance(0.28f * WarMultiplier(pKingdom, target, court));
            if (!shouldIssue)
                return AsyncStrategyAuthorityTrace.Planned(trace);
            if (selectedKind == WarStrategyCandidateKind.Zhulu)
            {
                if (DiplomaticWarDeclarationService.IssueZhulu(
                        pKingdom, target))
                    pKingdom.data.set(LAST_ACTION_YEAR, year);
                return AsyncStrategyAuthorityTrace.Planned(trace);
            }
            WarTerritoryService.WarTargetOption option =
                PickBestImmediateOption(pKingdom, target);
            if (option != null && DiplomaticWarDeclarationService.Issue(
                    pKingdom, option))
            {
                pKingdom.data.set(CLAIM_TARGET_ID, target.id);
                pKingdom.data.set(LAST_ACTION_YEAR, year);
                return AsyncStrategyAuthorityTrace.Planned(trace);
            }

            if (!WarClaimPreparationService.TryBeginWeakClaim(pKingdom,
                    target))
                return AsyncStrategyAuthorityTrace.Planned(trace);
            pKingdom.data.set(CLAIM_TARGET_ID, target.id);
            pKingdom.data.set(LAST_ACTION_YEAR, year);
            return AsyncStrategyAuthorityTrace.Planned(trace);
        }

        internal static bool TryBeginAsyncYear(Kingdom pKingdom, int pYear,
            out AsyncStrategyAdmissionToken pToken)
        {
            pToken = default;
            if (!CanRunFor(pKingdom) || pYear != Date.getCurrentYear())
                return false;
            pKingdom.data.get(LAST_CHECK_YEAR, out int lastCheck, -99999);
            if (pYear - lastCheck < CHECK_INTERVAL) return false;
            pKingdom.data.get(LAST_ACTION_YEAR, out int lastAction, -99999);
            if (pYear - lastAction < ACTION_COOLDOWN) return false;
            long leaseId = NextAsyncAdmissionLeaseId();
            if (!AsyncStrategyAdmissionToken.TryCreateCadence(leaseId,
                    lastCheck, pYear, out pToken))
                return false;
            pKingdom.data.set(LAST_CHECK_YEAR, pToken.ReservedMarker);
            AsyncAdmissionLeases[pKingdom.id] = leaseId;
            return true;
        }

        internal static bool TryRollbackAsyncYear(Kingdom pKingdom,
            AsyncStrategyAdmissionToken pToken)
        {
            if (pKingdom?.data == null || !pToken.IsValid ||
                !AsyncAdmissionLeases.TryGetValue(pKingdom.id,
                    out long leaseId))
                return false;
            pKingdom.data.get(LAST_CHECK_YEAR, out int currentMarker,
                -99999);
            if (!pToken.TryRollback(leaseId, ref currentMarker))
                return false;
            pKingdom.data.set(LAST_CHECK_YEAR, currentMarker);
            AsyncAdmissionLeases.Remove(pKingdom.id);
            return true;
        }

        internal static bool TryCompleteAsyncYear(Kingdom pKingdom,
            int pYear, AsyncStrategyAdmissionToken pToken)
        {
            if (pKingdom?.data == null || !pToken.IsValid ||
                !AsyncAdmissionLeases.TryGetValue(pKingdom.id,
                    out long leaseId) || leaseId != pToken.LeaseId)
                return false;
            pKingdom.data.get(LAST_CHECK_YEAR, out int currentMarker,
                -99999);
            if (currentMarker != pToken.ReservedMarker) return false;
            AsyncAdmissionLeases.Remove(pKingdom.id);
            if (!CanRunFor(pKingdom) || pYear != Date.getCurrentYear())
                return false;
            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
            if (TryDeclarePreparedWar(pKingdom, court))
            {
                pKingdom.data.set(LAST_ACTION_YEAR, pYear);
                return false;
            }
            pKingdom.data.get(LAST_ACTION_YEAR, out int lastAction, -99999);
            return pYear - lastAction >= ACTION_COOLDOWN;
        }

        internal static void ClearAsyncAdmissionRuntime()
        {
            AsyncAdmissionLeases.Clear();
        }

        private static long NextAsyncAdmissionLeaseId()
        {
            _nextAsyncAdmissionLeaseId = _nextAsyncAdmissionLeaseId ==
                                         long.MaxValue
                ? 1L
                : _nextAsyncAdmissionLeaseId + 1L;
            return _nextAsyncAdmissionLeaseId;
        }

        internal static bool TryCaptureAsyncPlan(Kingdom pKingdom,
            int pYear, out KingdomStrategyFacts pSource,
            out StrategyTargetFacts[] pTargets)
        {
            pSource = default;
            pTargets = Array.Empty<StrategyTargetFacts>();
            if (!CanRunFor(pKingdom) || pYear != Date.getCurrentYear())
                return false;

            pKingdom.data.get(LAST_CHECK_YEAR, out int lastCheck, -99999);
            if (pYear - lastCheck < CHECK_INTERVAL) return false;

            pKingdom.data.get(LAST_ACTION_YEAR, out int lastAction, -99999);
            if (pYear - lastAction < ACTION_COOLDOWN) return false;
            return TryCaptureCurrentWarFacts(pKingdom, out pSource,
                out pTargets);
        }

        private static bool TryCaptureCurrentWarFacts(Kingdom pKingdom,
            out KingdomStrategyFacts pSource,
            out StrategyTargetFacts[] pTargets)
        {
            pSource = default;
            pTargets = Array.Empty<StrategyTargetFacts>();
            if (!CanRunFor(pKingdom)) return false;
            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
            float ownPower = Math.Max(1f, VassalService.GetWarPowerScore(
                pKingdom, pIncludeVassals: true));
            Kingdom sourceRoot = VassalService.GetRootSuzerain(pKingdom);
            pSource = new KingdomStrategyFacts(pKingdom.id, ownPower,
                court?.war ?? .5f, court?.peace ?? .5f,
                court?.aggression ?? .5f, sourceRoot?.id ?? pKingdom.id,
                court?.livelihood ?? .5f);

            var targets = new List<StrategyTargetFacts>();
            MandateReport mandateReport = MandateService.ReadReportReadOnly();
            Kingdom mandate = MandateService.GetCurrentMandateKingdomReadOnly(
                mandateReport);
            float sourceAlliancePower = pKingdom == mandate
                ? WarTerritoryService.GetAllianceSystemPower(pKingdom)
                : 0f;
            if (!AsyncWarCandidateProducer.TryCapture(
                    CandidateKingdomsReadOnly(pKingdom, mandateReport),
                    other => other?.data == null ? -1L : other.id,
                    AsyncStrategyRevisionSet.MaximumCandidateKingdoms,
                    out Kingdom[] candidateKingdoms))
                return false;
            for (int index = 0; index < candidateKingdoms.Length; index++)
            {
                Kingdom other = candidateKingdoms[index];
                if (other?.data == null || other == pKingdom) continue;
                targets.Add(BuildTargetFacts(pKingdom, other, mandate,
                    mandateReport, sourceRoot, sourceAlliancePower));
            }
            pTargets = targets.ToArray();
            return pTargets.Length > 0;
        }

        internal static bool TryCommitAsyncPlan(AsyncStrategyPlan pPlan,
            long pCurrentTick, bool pShadowOnly)
        {
            if (pPlan == null) return false;
            Kingdom source = FindKingdom(pPlan.SourceKingdomId);
            Kingdom target = FindKingdom(pPlan.TargetKingdomId);
            bool sourceAlive = CanRunFor(source);
            bool targetAlive = target?.data != null && !target.isRekt() &&
                               target.isCiv() && !target.isNeutral();
            bool alreadyAtWar = false;
            try { alreadyAtWar = source?.isEnemy(target) == true; }
            catch { }
            bool truceBlocked = sourceAlive && targetAlive &&
                DiplomacyProposalService.HasActiveWarBlocker(source, target);
            if (!AsyncStrategyPlanRules.AcceptWar(pPlan,
                    AncientWarfare3.core.asyncwork.AWAsyncRuntime.WorldGeneration,
                    KingdomStrategyRevisionService.Current,
                    Date.getCurrentYear(), pCurrentTick,
                    maxAgeTicks: 600L, sourceAlive, targetAlive, alreadyAtWar,
                    truceBlocked)) return false;
            if (pPlan.FactFingerprint == null ||
                !TryCaptureCurrentWarFacts(source,
                    out KingdomStrategyFacts sourceFacts,
                    out StrategyTargetFacts[] targetFacts) ||
                !pPlan.FactFingerprint.MatchesWar(sourceFacts, targetFacts))
                return false;
            CourtSnapshot court = CourtService.GetSnapshot(source);
            StrategyTargetFacts selectedTargetFacts = default;
            bool foundTargetFacts = false;
            for (int index = 0; index < targetFacts.Length; index++)
                if (targetFacts[index].TargetId == target.id)
                {
                    selectedTargetFacts = targetFacts[index];
                    foundTargetFacts = true;
                    break;
                }
            if (!foundTargetFacts || !WarStrategyCandidateRules.TryEvaluate(
                    sourceFacts, selectedTargetFacts,
                    out WarStrategyCandidate liveCandidate) ||
                !WarStrategyCandidateRules.MatchesKind(pPlan.WarKind,
                    liveCandidate.Kind))
                return false;
            if (pShadowOnly) return false;
            bool shouldIssue = pPlan.WarKind ==
                               WarStrategyCandidateKind.Zhulu
                ? ZhuluWarRules.ShouldIssueDiplomaticDeclaration(pPlan.Roll)
                : pPlan.Roll < Math.Max(0f, Math.Min(1f,
                    .28f * WarMultiplier(source, target, court)));
            if (!shouldIssue) return false;

            if (pPlan.WarKind == WarStrategyCandidateKind.Zhulu)
            {
                if (!DiplomaticWarDeclarationService.IssueZhulu(
                        source, target))
                    return false;
                source.data.set(LAST_ACTION_YEAR, pPlan.CaptureYear);
                KingdomStrategyRevisionService.MarkChanged(source.id,
                    target.id);
                return true;
            }

            WarTerritoryService.WarTargetOption option =
                PickBestImmediateOption(source, target);
            bool started = option != null &&
                           DiplomaticWarDeclarationService.Issue(source,
                               option);
            if (!started && pPlan.WarKind == WarStrategyCandidateKind.Normal)
            {
                started = WarClaimPreparationService.TryBeginWeakClaim(
                    source, target);
            }
            if (!started) return false;
            source.data.set(CLAIM_TARGET_ID, target.id);
            source.data.set(LAST_ACTION_YEAR, pPlan.CaptureYear);
            KingdomStrategyRevisionService.MarkChanged(source.id, target.id);
            return true;
        }

        public static bool TryQueueFromVanillaWarPlot(Actor pActor, Kingdom pPreferredTarget)
        {
            Kingdom kingdom = pActor?.kingdom;
            if (!CanRunForPlotRedirect(kingdom)) return false;

            bool supportsPolicySystem = KingdomPolicyService
                .CanUsePolicySystem(kingdom);
            if (supportsPolicySystem &&
                !KingdomPolicyService.IsPolicyEnabledForKingdom(kingdom) &&
                !KingdomPolicyService.SetPolicyEnabled(kingdom, true))
                return false;

            CourtSnapshot court = CourtService.GetSnapshot(kingdom);
            Kingdom target = IsUsableRedirectTarget(kingdom, pPreferredTarget)
                ? pPreferredTarget
                : PickNormalWarTarget(kingdom, court, out _);
            if (target?.data == null) return false;
            if (DiplomaticWarDeclarationService.HasPendingForPair(kingdom,
                    target)) return true;

            WarTerritoryService.WarTargetOption option =
                PickBestImmediateOption(kingdom, target,
                    pAllowNoCb: !supportsPolicySystem);
            if (option != null)
            {
                bool queued = DiplomaticWarDeclarationService.Issue(
                    kingdom, option);
                if (queued) kingdom.data.set(CLAIM_TARGET_ID, target.id);
                return queued;
            }

            bool started = WarClaimPreparationService.TryBeginWeakClaim(
                kingdom, target);
            if (started) kingdom.data.set(CLAIM_TARGET_ID, target.id);
            return started;
        }

        private static bool TryDeclarePreparedWar(Kingdom pKingdom, CourtSnapshot pCourt)
        {
            pKingdom.data.get(CLAIM_TARGET_ID, out long targetId, -1L);
            Kingdom target = FindKingdom(targetId);
            if (target?.data == null)
            {
                target = WarTerritoryService.FindBestClaimWarTarget(pKingdom);
                if (target?.data != null) pKingdom.data.set(CLAIM_TARGET_ID, target.id);
            }
            if (target?.data == null || target.isRekt() || pKingdom.hasEnemies() || target.hasEnemies())
            {
                if (targetId >= 0) pKingdom.data.set(CLAIM_TARGET_ID, -1L);
                return false;
            }
            if (WarTerritoryService.IsVassalDecisionOnlyTarget(pKingdom, target))
            {
                pKingdom.data.set(CLAIM_TARGET_ID, -1L);
                return false;
            }

            if (!WarDecisionService.HasValidCasusBelli(pKingdom, target, WarDecisionService.WAR_NORMAL))
            {
                if (!WarTerritoryService.HasActiveProjectAgainst(pKingdom, target))
                    pKingdom.data.set(CLAIM_TARGET_ID, -1L);
                return false;
            }

            if (!StillWantsWar(pKingdom, target, pCourt)) return false;
            if (DiplomaticWarDeclarationService.HasPendingForPair(pKingdom,
                    target))
                return false;

            WarTerritoryService.WarTargetOption option = PickBestImmediateOption(pKingdom, target);
            bool started = option != null &&
                           DiplomaticWarDeclarationService.Issue(pKingdom,
                               option);
            if (started) pKingdom.data.set(CLAIM_TARGET_ID, -1L);
            return started;
        }

        private static Kingdom PickNormalWarTarget(Kingdom pKingdom,
            CourtSnapshot pCourt,
            out IReadOnlyList<AsyncStrategyCandidate> pTraceCandidates)
        {
            float own = Math.Max(1f, VassalService.GetWarPowerScore(pKingdom,
                pIncludeVassals: true));
            Kingdom sourceRoot = VassalService.GetRootSuzerain(pKingdom);
            var sourceFacts = new KingdomStrategyFacts(pKingdom.id, own,
                pCourt?.war ?? .5f, pCourt?.peace ?? .5f,
                pCourt?.aggression ?? .5f,
                sourceRoot?.id ?? pKingdom.id,
                pCourt?.livelihood ?? .5f);
            MandateReport mandateReport = MandateService.ReadReport();
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            float sourceAlliancePower = pKingdom == mandate
                ? WarTerritoryService.GetAllianceSystemPower(pKingdom)
                : 0f;
            var targetFacts = new List<StrategyTargetFacts>();
            var targetsById = new Dictionary<long, Kingdom>();
            foreach (Kingdom other in CandidateKingdoms(pKingdom))
            {
                if (other?.data == null || other == pKingdom) continue;
                targetFacts.Add(BuildTargetFacts(pKingdom, other, mandate,
                    mandateReport, sourceRoot, sourceAlliancePower));
                targetsById[other.id] = other;
            }
            IReadOnlyList<WarStrategyCandidate> ranked =
                WarStrategyCandidateRules.RankCandidates(sourceFacts,
                    targetFacts);
            var trace = new List<AsyncStrategyCandidate>(ranked.Count);
            for (int index = 0; index < ranked.Count; index++)
            {
                WarStrategyCandidate candidate = ranked[index];
                trace.Add(new AsyncStrategyCandidate(
                    candidate.TargetKingdomId,
                    AsyncStrategyAction.DeclareWar,
                    AsyncDiplomacyProposalKind.None, candidate.Score, 0d,
                    candidate.Kind));
            }
            pTraceCandidates = trace;
            return ranked.Count > 0 && targetsById.TryGetValue(
                ranked[0].TargetKingdomId, out Kingdom best) ? best : null;
        }

        private static StrategyTargetFacts BuildTargetFacts(Kingdom pSource,
            Kingdom pTarget, Kingdom pMandate, MandateReport pMandateReport,
            Kingdom pSourceRoot, float pSourceAlliancePower)
        {
            bool isZhuluAge = World.world?.map_stats?.world_age_id ==
                              ZhuluAgeRules.AgeId;
            bool targetIsMandate = pTarget == pMandate;
            bool zhuluEligible = ZhuluWarService.CanDeclare(pSource,
                pTarget, out _);
            WarStrategyCandidateKind preferredKind = zhuluEligible
                ? WarStrategyCandidateKind.Zhulu
                : targetIsMandate
                ? WarStrategyCandidateKind.TakeMandate
                : pSource == pMandate
                    ? WarStrategyCandidateKind.MandateConquest
                    : WarStrategyCandidateKind.Normal;
            float targetPower = Math.Max(1f, VassalService.GetWarPowerScore(
                pTarget, pIncludeVassals: true));
            bool targetAtWar = pTarget.hasEnemies();
            bool sameRoot = VassalService.GetRootSuzerain(pTarget) ==
                            pSourceRoot;
            bool vassalBlocked = WarTerritoryService
                .IsVassalDecisionOnlyTarget(pSource, pTarget);
            bool warBlocked = DiplomacyProposalService.HasActiveWarBlocker(
                pSource, pTarget);
            bool blocked = sameRoot || vassalBlocked || warBlocked;
            bool sameAlliance = false;
            float targetAlliancePower = 0f;
            bool neighbor = false;
            float capitalDistance = CapitalDistance(pSource, pTarget);
            int opinion = 0;
            bool needsFabrication = preferredKind ==
                                    WarStrategyCandidateKind.Normal;
            if (targetAtWar || blocked) needsFabrication = false;
            if (preferredKind == WarStrategyCandidateKind.Normal &&
                !targetAtWar && !blocked)
            {
                neighbor = AreNeighbors(pSource, pTarget);
                opinion = Opinion(pSource, pTarget);
            }
            if (preferredKind == WarStrategyCandidateKind.MandateConquest)
            {
                if (!blocked && !targetAtWar)
                {
                    neighbor = AreNeighbors(pSource, pTarget);
                    sameAlliance = WarTerritoryService.AreInSameAlliance(
                        pSource, pTarget);
                    targetAlliancePower = WarTerritoryService
                        .GetAllianceSystemPower(pTarget);
                    bool conquestEligible = MandateConquestRules
                        .CanUseMandateConquest(
                            pAttackerIsCurrentMandate: true,
                            pVassalBlocked: vassalBlocked,
                            pSameAlliance: sameAlliance,
                            pAttackerSystemPower: pSourceAlliancePower,
                            pDefenderAlliancePower: targetAlliancePower);
                    needsFabrication = !conquestEligible;
                    if (needsFabrication)
                        opinion = Opinion(pSource, pTarget);
                }
            }
            if (preferredKind == WarStrategyCandidateKind.Zhulu)
            {
                neighbor = AreNeighbors(pSource, pTarget);
                sameAlliance = WarTerritoryService.AreInSameAlliance(
                    pSource, pTarget);
            }
            bool fabricationAvailable = false;
            if (needsFabrication)
                fabricationAvailable = WarTerritoryService
                    .FindFirstFabricationTargetCity(pSource, pTarget)?.data !=
                    null;
            return new StrategyTargetFacts(pTarget.id, targetPower,
                opinion, neighbor, targetAtWar, warBlocked, preferredKind,
                sameRoot: sameRoot, vassalBlocked: vassalBlocked,
                fabricationAvailable: fabricationAvailable,
                sameAlliance: sameAlliance,
                sourceAlliancePower: pSourceAlliancePower,
                targetAlliancePower: targetAlliancePower,
                mandateValue: pMandateReport?.mandate_value ?? 0,
                mandateCoreControl: pMandateReport?.core_control ?? 1f,
                zhuluEligible: zhuluEligible,
                capitalDistance: capitalDistance,
                zhuluAge: isZhuluAge);
        }

        private static WarTerritoryService.WarTargetOption
            PickBestImmediateOption(Kingdom pKingdom, Kingdom pTarget,
                bool pAllowNoCb = false)
        {
            WarTerritoryService.WarTargetOption best = null;
            int bestScore = int.MinValue;
            WarTerritoryService.WarTargetOption noCbFallback = null;
            int noCbFallbackScore = int.MinValue;
            WarAiPeopleRelation relation = ResolvePeopleRelation(
                pKingdom, pTarget);
            SamePeopleWarRoute route = SamePeopleWarIntentRules.Resolve(
                relation, pKingdom.id, pTarget.id, Date.getCurrentYear(),
                WarClaimPreparationService.IsLockedTo(pKingdom, pTarget));
            WarAiGoalContext context = BuildGoalContext(pKingdom, pTarget);
            foreach (WarTerritoryService.WarTargetOption option in WarTerritoryService.BuildTargetOptions(pKingdom, pTarget))
            {
                if (option == null || (!pAllowNoCb &&
                    option.goal_type == WarTerritoryService.GOAL_NO_CB))
                    continue;
                if (SamePeopleWarIntentRules.ShouldSuppressSubjugation(
                        route, option.goal_type)) continue;
                if (!DiplomaticWarDeclarationService.CanIssue(pKingdom,
                        option, out _)) continue;
                if (option.goal_type == WarTerritoryService.GOAL_RESTORE_KINGDOM &&
                    AutonomousRestorationService.ShouldPreferSelfRestoration(
                        option.restoration_claim_id)) continue;
                int strategicScore = WarAiGoalSelectionRules.StrategicScore(
                    option.goal_type, option.score, relation, context,
                    ObjectiveUrgency(option));
                if (strategicScore == int.MinValue) continue;
                if (option.goal_type == WarTerritoryService.GOAL_NO_CB)
                {
                    if (strategicScore <= noCbFallbackScore) continue;
                    noCbFallbackScore = strategicScore;
                    noCbFallback = option;
                    continue;
                }
                if (strategicScore <= bestScore) continue;
                bestScore = strategicScore;
                best = option;
            }
            return best ?? noCbFallback;
        }

        private static bool ShouldPrepareTerritorialClaim(Kingdom pSource,
            Kingdom pTarget,
            WarTerritoryService.WarTargetOption pImmediateOption,
            WarAiPeopleRelation pRelation, WarAiGoalContext pContext)
        {
            string goal = pImmediateOption?.goal_type ?? "";
            if (goal != WarTerritoryService.GOAL_FORCE_VASSAL &&
                goal != WarTerritoryService.GOAL_FORCE_TRIBUTARY)
                return false;
            City city = WarTerritoryService.FindFirstFabricationTargetCity(
                pSource, pTarget);
            if (city?.data == null) return false;
            int population = 0;
            try { population = city.getPopulationPeople(); }
            catch { }
            int prospectiveScore = WarTargetSelectionRules.ScoreTarget(
                WarTerritoryService.GOAL_PRESS_CLAIM_CITY,
                pHasCore: false, pHasStrongClaim: false,
                pHasWeakClaim: true, pRestorationStrength: 0,
                pPopulation: population);
            return WarAiGoalSelectionRules.ShouldPreferTerritorialPreparation(
                pRelation, pContext, prospectiveScore,
                new WarAiGoalCandidate(goal, pImmediateOption.score,
                    ObjectiveUrgency(pImmediateOption)));
        }

        private static WarAiGoalContext BuildGoalContext(Kingdom pSource,
            Kingdom pTarget)
        {
            float sourcePower = Math.Max(1f, VassalService.GetWarPowerScore(
                pSource, pIncludeVassals: true));
            float targetPower = Math.Max(1f, VassalService.GetWarPowerScore(
                pTarget, pIncludeVassals: true));
            CourtSnapshot court = CourtService.GetSnapshot(pSource);
            float expansionism = Math.Max(0f, Math.Min(1f,
                ((court?.war ?? .5f) + (court?.aggression ?? .5f) -
                 (court?.peace ?? .5f)) * .5f));
            int centralization = CentralizationService.ReadSnapshot(pSource)
                .effective_level;
            int targetCities = 0;
            try { targetCities = Math.Max(0, pTarget?.countCities() ?? 0); }
            catch { }
            Kingdom sourceSuzerain = VassalService.GetDiplomaticSuzerain(
                pSource);
            int currentSubjectCount = VassalService.GetVassals(pSource,
                pRecursive: true).Count;
            int subjectSoftCap = CourtInstitutionEffectService.Read(pSource)
                .VassalSoftCap;
            return new WarAiGoalContext(AreNeighbors(pSource, pTarget),
                sourceSuzerain?.data != null,
                VassalService.GetDiplomaticSuzerain(pTarget)?.data == null,
                DiplomacyProposalService.HasActiveWarBlocker(
                    pSource, pTarget), sourcePower / targetPower,
                targetCities, centralization, expansionism,
                court?.war ?? .5f, court?.peace ?? .5f,
                currentSubjectCount: currentSubjectCount,
                subjectSoftCap: subjectSoftCap,
                independenceTargetIsSuzerain: sourceSuzerain == pTarget,
                opposedSuccessionBranches:
                    SuccessionDisputeService.ReadOpposedCourtOpinion(
                        pSource, pTarget) < 0,
                attackerTitleRank: (int)KingdomTitleService.GetTitle(
                    pSource),
                targetTitleRank: (int)KingdomTitleService.GetTitle(
                    pTarget));
        }

        private static int ObjectiveUrgency(
            WarTerritoryService.WarTargetOption pOption)
        {
            return WarAiGoalSelectionRules.ObjectiveUrgency(
                pOption?.goal_type, pOption?.score ?? 0);
        }

        private static WarAiPeopleRelation ResolvePeopleRelation(
            Kingdom pSource, Kingdom pTarget)
        {
            string sourceSpecies = "";
            string targetSpecies = "";
            try { sourceSpecies = pSource?.getActorAsset()?.id ?? ""; }
            catch { }
            try { targetSpecies = pTarget?.getActorAsset()?.id ?? ""; }
            catch { }
            return WarAiGoalSelectionRules.ResolvePeopleRelation(
                sourceSpecies, targetSpecies,
                pSource?.culture?.data?.id ?? -1L,
                pTarget?.culture?.data?.id ?? -1L,
                LineageService.IsXiaKingdom(pSource),
                LineageService.IsXiaKingdom(pTarget));
        }

        private static bool IsUsableRedirectTarget(Kingdom pKingdom, Kingdom pTarget)
        {
            if (pKingdom?.data == null || pTarget?.data == null || pTarget == pKingdom ||
                pTarget.isRekt() || !pTarget.isCiv() || pTarget.isNeutral())
                return false;

            Kingdom suzerain = VassalService.GetDiplomaticSuzerain(pKingdom);
            if (suzerain == pTarget) return true;
            return !WarTerritoryService.IsVassalDecisionOnlyTarget(pKingdom, pTarget);
        }

        private static bool StillWantsWar(Kingdom pKingdom, Kingdom pTarget, CourtSnapshot pCourt)
        {
            if (pKingdom?.data == null || pTarget?.data == null) return false;
            float own = VassalService.GetWarPowerScore(pKingdom, pIncludeVassals: true);
            float target = Math.Max(1f, VassalService.GetWarPowerScore(pTarget, pIncludeVassals: true));
            if (WarTerritoryService.CanUseMandateConquest(pKingdom, pTarget)) return true;
            float multiplier = WarMultiplier(pKingdom, pTarget, pCourt);
            return own >= target * (1.15f / multiplier) &&
                   (AreNeighbors(pKingdom, pTarget) || Opinion(pKingdom, pTarget) <= -55);
        }

        private static float WarMultiplier(Kingdom pKingdom, Kingdom pTarget, CourtSnapshot pCourt)
        {
            bool protectedWar = pTarget?.data != null &&
                (WarTerritoryService.CanUseMandateConquest(pKingdom, pTarget) ||
                 MandateService.GetCurrentMandateKingdom() == pTarget);
            return CourtDirectionRules.OffensiveWarMultiplier(
                pCourt?.aggression ?? 0.5f,
                pCourt?.peace ?? 0.5f,
                pCourt?.livelihood ?? 0.5f,
                pCourt?.war ?? 0.5f,
                protectedWar);
        }

        private static IEnumerable<Kingdom> CandidateKingdoms(Kingdom pKingdom)
        {
            bool isZhuluAge = World.world?.map_stats?.world_age_id ==
                              ZhuluAgeRules.AgeId;
            return CandidateKingdomsWithMandate(pKingdom,
                    MandateService.GetCurrentMandateKingdom())
                .Take(ZhuluAgeRules.WarCandidateLimit(isZhuluAge));
        }

        private static IEnumerable<Kingdom> CandidateKingdomsReadOnly(
            Kingdom pKingdom, MandateReport pMandateReport)
        {
            return CandidateKingdomsWithMandate(pKingdom,
                MandateService.GetCurrentMandateKingdomReadOnly(
                    pMandateReport));
        }

        private static IEnumerable<Kingdom> CandidateKingdomsWithMandate(
            Kingdom pKingdom, Kingdom pMandate)
        {
            var seen = new HashSet<long>();

            Kingdom mandate = pMandate;
            if (mandate?.data != null && mandate != pKingdom &&
                !mandate.isRekt() && mandate.isCiv() &&
                !mandate.isNeutral() && seen.Add(mandate.id))
            {
                yield return mandate;
            }

            IEnumerable<City> cities;
            try { cities = pKingdom?.getCities() ?? Enumerable.Empty<City>(); }
            catch { yield break; }
            foreach (City city in cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                foreach (Kingdom other in city.neighbours_kingdoms)
                {
                    if (other?.data == null || other == pKingdom ||
                        other.isRekt() || !other.isCiv() ||
                        other.isNeutral() || !seen.Add(other.id)) continue;
                    yield return other;
                }
            }

            bool isZhuluAge = World.world?.map_stats?.world_age_id ==
                              ZhuluAgeRules.AgeId;
            if (!ZhuluAgeRules.ShouldIncludeDistantTargets(isZhuluAge,
                    MandatePhaseService.CurrentPhase,
                    XiaizationService.CanUseMandateSystem(pKingdom)))
                yield break;
            var distant = new List<Kingdom>();
            try
            {
                foreach (Kingdom other in World.world.kingdoms)
                {
                    if (other?.data == null || other == pKingdom ||
                        other.isRekt() || !other.isCiv() ||
                        other.isNeutral() || seen.Contains(other.id) ||
                        !ZhuluAgeRules.IsEligibleDistantTarget(isZhuluAge,
                            XiaizationService.CanUseMandateSystem(other)))
                        continue;
                    distant.Add(other);
                }
            }
            catch { yield break; }
            distant.Sort((left, right) => CapitalDistance(pKingdom, left)
                .CompareTo(CapitalDistance(pKingdom, right)));
            for (int index = 0; index < distant.Count; index++)
            {
                Kingdom other = distant[index];
                if (seen.Add(other.id)) yield return other;
            }
        }

        private static float CapitalDistance(Kingdom pSource,
            Kingdom pTarget)
        {
            try
            {
                WorldTile source = pSource?.capital?.getTile();
                WorldTile target = pTarget?.capital?.getTile();
                return source == null || target == null
                    ? float.MaxValue
                    : Toolbox.DistTile(source, target);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        private static bool AreNeighbors(Kingdom pA, Kingdom pB)
        {
            try
            {
                foreach (City city in pA.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    foreach (Kingdom neighbor in city.neighbours_kingdoms)
                        if (neighbor == pB) return true;
                }
            }
            catch { }
            return false;
        }

        private static Kingdom FindKingdom(long pId)
        {
            if (pId < 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom byId = World.world.kingdoms.get(pId);
                if (byId?.data != null) return byId;
            }
            catch { }
            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && kingdom.id == pId) return kingdom;
            return null;
        }

        private static bool CanRunFor(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return false;
            if (!pKingdom.hasKing() || pKingdom.hasEnemies()) return false;
            bool isZhuluAge = World.world?.map_stats?.world_age_id ==
                              ZhuluAgeRules.AgeId;
            bool hasVassalSuzerain = VassalService.GetSuzerain(pKingdom)
                                          ?.data != null;
            bool hasDiplomaticSuzerain = VassalService
                                              .GetDiplomaticSuzerain(pKingdom)
                                          ?.data != null;
            if (!ZhuluAgeRules.IsIndependentAiAttacker(isZhuluAge,
                    hasVassalSuzerain, hasDiplomaticSuzerain))
                return false;
            bool normalWarAiSupported = KingdomPolicyService
                                            .CanUsePolicySystem(pKingdom) ||
                                        LineageService.IsXiaKingdom(pKingdom);
            return ZhuluAgeRules.CanUseUnificationWarAi(isZhuluAge,
                normalWarAiSupported);
        }

        private static bool CanRunForPlotRedirect(Kingdom pKingdom)
        {
            bool validCivilizedRealm = pKingdom?.data != null &&
                                        !pKingdom.isRekt() &&
                                        pKingdom.isCiv() &&
                                        !pKingdom.isNeutral();
            bool attackerIsSubject = VassalService.GetDiplomaticSuzerain(
                pKingdom)?.data != null;
            return WarAiGoalSelectionRules.CanRedirectVanillaWarIntent(
                validCivilizedRealm, pKingdom?.hasKing() == true,
                pKingdom?.hasEnemies() == true, attackerIsSubject);
        }

        private static int Opinion(Kingdom pMain, Kingdom pTarget)
        {
            try { return World.world.diplomacy.getOpinion(pMain, pTarget).total; }
            catch { return 0; }
        }

        private static bool Chance(float pChance)
        {
            return Rng.NextDouble() < pChance;
        }
    }
}
