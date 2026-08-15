using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal enum PolicyNodeStatus
    {
        Locked,
        Available,
        Current,
        Completed
    }

    internal sealed class KingdomPolicySnapshot
    {
        public string profile_id = "";
        public string government_state = "default";
        public int royal_authority;
        public int migration_version;
        public string obsolete_node_ids = "";
        public string class_state = "";
        public string army_state = "";
        public string name_state = "";
        public string enfeoffment_state = "";
        public float policy_points;
        public float tech_points;
        public string current_policy = "";
        public float policy_progress;
        public string current_tech = "";
        public float tech_progress;
        public string current_decision = "";
        public float decision_progress;
        public long decision_target_kingdom_id = -1L;
        public string decision_target_kingdom_name = "";
        public string decision_queue = "";
        public long core_fab_current_city_id = -1L;
        public string core_fab_current_city_name = "";
        public float core_fab_progress;
        public string core_fab_queue = "";
        public string completed_policies = "";
        public string completed_techs = "";
        public string completed_decisions = "";
        public string locked_nodes = "";
    }

    internal sealed class TechLevelReport
    {
        public float score;
        public float max_score;
        public int level;
        public int max_level;
        public int completed_count;
        public int total_count;
        public string current_name = "";
        public float current_fraction;
    }

    internal static class KingdomPolicyService
    {
        private const float MAX_POINTS = 999f;
        public const float MAX_YEARLY_SPEND = 18f;
        private static int _techFrontierCacheYear = int.MinValue;
        private static int _techFrontierMaxLevel = 1;

        public static void ClearRuntime()
        {
            _techFrontierCacheYear = int.MinValue;
            _techFrontierMaxLevel = 1;
            KingdomPolicyEffectService.ClearRuntime();
        }

        internal static int EnsureWorldInitialized()
        {
            int initialized = 0;
            if (World.world?.kingdoms == null) return initialized;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                KingdomPolicyProfileId profile =
                    KingdomPolicyProfileService.EnsureAssigned(kingdom);
                if (!KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                        profile)) continue;
                EnsureInitialized(kingdom);
                initialized++;
            }
            return initialized;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return;
            EnsureInitialized(pKingdom);

            long courtBenchmark = UpdateAgeBenchmark.Begin();
            try { CourtService.OnKingdomYear(pKingdom); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtYearTickIndex, courtBenchmark); }

            int currentYear = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.POLICY_LAST_YEAR, out int lastYear, int.MinValue);
            int elapsedYears = KingdomAnnualProgressRules.ResolveElapsedYears(
                lastYear, currentYear);
            if (elapsedYears <= 0) return;
            pKingdom.data.set(LineageKeys.POLICY_LAST_YEAR, currentYear);

            EraChangeTriggerService.TryProcessAnnualAi(pKingdom);

            TryStartCoreFabrication(pKingdom);
            StartNextQueuedDecisionIfEmpty(pKingdom);
            long benchmark = UpdateAgeBenchmark.Begin();
            try { KingdomPolicyAI.TryFillEmptySlots(pKingdom); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomPolicyAiIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { AdvanceCurrent(pKingdom, PolicyNodeKind.Tech, elapsedYears); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomPolicyAdvanceTechIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { AdvanceCurrent(pKingdom, PolicyNodeKind.Social, elapsedYears); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomPolicyAdvanceSocialIndex, benchmark); }

            TryStartCoreFabrication(pKingdom);
            StartNextQueuedDecisionIfEmpty(pKingdom);
            benchmark = UpdateAgeBenchmark.Begin();
            try { KingdomPolicyAI.TryFillEmptySlots(pKingdom); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomPolicyAiIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { UpsertSnapshot(pKingdom); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomPolicySnapshotIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                TechMapModeService.DirtyMapIfActive();
                DevelopmentMapModeService.DirtyMapIfActive();
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomPolicyMapDirtyIndex, benchmark); }
        }

        internal static void OnKingdomDecisionMonth(Kingdom pKingdom,
            int pMonthKey)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return;
            EnsureInitialized(pKingdom);
            pKingdom.data.get(LineageKeys.POLICY_LAST_DECISION_MONTH,
                out int lastMonthKey, int.MinValue);
            if (!KingdomDecisionMonthlyRules.ShouldProcessMonth(pMonthKey,
                    lastMonthKey)) return;
            pKingdom.data.set(LineageKeys.POLICY_LAST_DECISION_MONTH,
                pMonthKey);

            AddMonthlyPoints(pKingdom);
            TryStartCoreFabrication(pKingdom);
            AdvanceCoreFabrication(pKingdom,
                KingdomDecisionMonthlyRules.MonthlyYearFraction);
            StartNextQueuedDecisionIfEmpty(pKingdom);
            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                AdvanceCurrent(pKingdom, PolicyNodeKind.Decision,
                    KingdomDecisionMonthlyRules.MonthlyYearFraction);
            }
            finally
            {
                UpdateAgeBenchmark.End(
                    UpdateAgeBenchmarkRules.KingdomPolicyAdvanceDecisionIndex,
                    benchmark);
            }
            TryStartCoreFabrication(pKingdom);
            StartNextQueuedDecisionIfEmpty(pKingdom);
            UpsertSnapshot(pKingdom);
        }

        public static bool CanUsePolicySystem(Kingdom pKingdom)
        {
            return IsSupportedPolicyKingdom(pKingdom);
        }

        public static KingdomPolicyProfileId GetPolicyProfile(
            Kingdom pKingdom)
        {
            return KingdomPolicyProfileService.TryGet(pKingdom,
                out KingdomPolicyProfileId profileId)
                ? profileId
                : KingdomPolicyProfileId.None;
        }

        public static IReadOnlyList<KingdomPolicyDef> GetNodes(
            Kingdom pKingdom, PolicyNodeKind pKind)
        {
            return KingdomPolicyDefs.GetNodes(GetPolicyProfile(pKingdom),
                pKind);
        }

        public static IReadOnlyList<KingdomPolicyDef> GetResearchNodes(
            Kingdom pKingdom)
        {
            return KingdomPolicyDefs.GetResearchNodes(
                GetPolicyProfile(pKingdom));
        }

        public static KingdomPolicyDef GetDefinition(Kingdom pKingdom,
            string pNodeId)
        {
            return KingdomPolicyDefs.Get(GetPolicyProfile(pKingdom),
                pNodeId);
        }

        public static bool IsPolicyEnabledForKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            if (!IsSupportedPolicyKingdom(pKingdom)) return false;

            bool defaultEnabled = KingdomPolicyProfileRules.
                IsResolvableKingdomProfile(GetPolicyProfile(pKingdom));
            pKingdom.data.get(LineageKeys.POLICY_ENABLED, out bool enabled, defaultEnabled);
            return enabled;
        }

        public static bool IsPolicyAIEnabled(Kingdom pKingdom)
        {
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            bool defaultEnabled = KingdomPolicyProfileRules.
                IsResolvableKingdomProfile(GetPolicyProfile(pKingdom));
            pKingdom.data.get(LineageKeys.POLICY_AI_ENABLED, out bool enabled, defaultEnabled);
            return enabled;
        }

        public static bool SetPolicyEnabled(Kingdom pKingdom, bool pEnabled)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            if (pEnabled && !IsSupportedPolicyKingdom(pKingdom)) return false;

            pKingdom.data.set(LineageKeys.POLICY_ENABLED, pEnabled);
            if (!pEnabled)
                pKingdom.data.set(LineageKeys.POLICY_AI_ENABLED, false);
            else
                EnsureInitialized(pKingdom);

            if (pEnabled) UpsertSnapshot(pKingdom);
            return true;
        }

        public static bool SetPolicyAIEnabled(Kingdom pKingdom, bool pEnabled)
        {
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            pKingdom.data.set(LineageKeys.POLICY_AI_ENABLED, pEnabled);
            return true;
        }

        public static void EnsureInitialized(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (!KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                    KingdomPolicyProfileService.EnsureAssigned(pKingdom)))
                return;
            EnsureState(pKingdom, LineageKeys.POLICY_GOVERNMENT_STATE,
                "default");
            pKingdom.data.get(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                out int royalAuthority, 0);
            if (royalAuthority < 0 || royalAuthority >
                WesternRoyalAuthorityRules.MaximumConsolidatedAuthority)
            {
                pKingdom.data.set(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                    Math.Max(0, Math.Min(
                        WesternRoyalAuthorityRules.
                            MaximumConsolidatedAuthority,
                        royalAuthority)));
            }
            pKingdom.data.get(LineageKeys.POLICY_OBSOLETE_NODE_IDS,
                out string obsoleteNodeIds, "");
            if (obsoleteNodeIds == null)
                pKingdom.data.set(LineageKeys.POLICY_OBSOLETE_NODE_IDS, "");
            pKingdom.data.get(LineageKeys.POLICY_CLASS_STATE, out string classState, "");
            if (string.IsNullOrEmpty(classState))
                pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, KingdomPolicyDefs.ClassDefault);
            EnsureState(pKingdom, LineageKeys.POLICY_ARMY_STATE, KingdomPolicyDefs.ArmyDefault);
            EnsureState(pKingdom, LineageKeys.POLICY_NAME_STATE, KingdomPolicyDefs.NameDefault);
            EnsureState(pKingdom, LineageKeys.POLICY_ENFEOFFMENT_STATE, KingdomPolicyDefs.EnfeoffmentDefault);
            pKingdom.data.get(LineageKeys.POLICY_CURRENT, out string currentPolicy, "");
            pKingdom.data.get(LineageKeys.TECH_CURRENT, out string currentTech, "");
            pKingdom.data.get(LineageKeys.DECISION_CURRENT, out string currentDecision, "");
            pKingdom.data.get(LineageKeys.DECISION_QUEUE, out string decisionQueue, "");
            pKingdom.data.get(LineageKeys.CORE_FAB_CURRENT_CITY_ID, out long currentCoreCityId, -1L);
            pKingdom.data.get(LineageKeys.CORE_FAB_CURRENT_CITY_NAME, out string currentCoreCityName, "");
            pKingdom.data.get(LineageKeys.CORE_FAB_QUEUE, out string coreQueue, "");
            pKingdom.data.get(LineageKeys.POLICY_LOCKED_NODES, out string lockedNodes, "");
            if (currentPolicy == null) pKingdom.data.set(LineageKeys.POLICY_CURRENT, "");
            if (currentTech == null) pKingdom.data.set(LineageKeys.TECH_CURRENT, "");
            if (currentDecision == null) pKingdom.data.set(LineageKeys.DECISION_CURRENT, "");
            if (decisionQueue == null) pKingdom.data.set(LineageKeys.DECISION_QUEUE, "");
            if (currentCoreCityId < -1L) pKingdom.data.set(LineageKeys.CORE_FAB_CURRENT_CITY_ID, -1L);
            if (currentCoreCityName == null) pKingdom.data.set(LineageKeys.CORE_FAB_CURRENT_CITY_NAME, "");
            if (coreQueue == null) pKingdom.data.set(LineageKeys.CORE_FAB_QUEUE, "");
            if (lockedNodes == null) pKingdom.data.set(LineageKeys.POLICY_LOCKED_NODES, "");
            MigrateCurrentCoreDecisionToDedicatedSlot(pKingdom);
            MigrateHotPolicyState(pKingdom);
        }

        public static string GetLockedNodesRaw(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.POLICY_LOCKED_NODES, out string raw, "");
            return raw ?? "";
        }

        public static bool IsNodeLocked(Kingdom pKingdom, string pNodeId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pNodeId)) return false;
            return PolicyNodeLockRules.IsLocked(GetLockedNodesRaw(pKingdom), pNodeId);
        }

        public static bool SetNodeLocked(Kingdom pKingdom, string pNodeId, bool pLocked)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pNodeId)) return false;
            if (GetDefinition(pKingdom, pNodeId) == null) return false;
            EnsureInitialized(pKingdom);

            string raw = GetLockedNodesRaw(pKingdom);
            string next = PolicyNodeLockRules.SetLocked(raw, pNodeId, pLocked);
            pKingdom.data.set(LineageKeys.POLICY_LOCKED_NODES, next);
            if (pLocked) CleanLockedNodeSideEffects(pKingdom, pNodeId);
            UpsertSnapshot(pKingdom);
            return true;
        }

        public static bool ToggleNodeLocked(Kingdom pKingdom, string pNodeId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pNodeId)) return false;
            return SetNodeLocked(pKingdom, pNodeId, !IsNodeLocked(pKingdom, pNodeId));
        }

        private static void CleanLockedNodeSideEffects(Kingdom pKingdom, string pNodeId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pNodeId)) return;

            if (PolicyNodeLockRules.ShouldClearCurrent(pNodeId, GetCurrent(pKingdom, PolicyNodeKind.Tech)))
            {
                pKingdom.data.set(LineageKeys.TECH_CURRENT, "");
                pKingdom.data.set(LineageKeys.TECH_PROGRESS, 0f);
            }

            if (PolicyNodeLockRules.ShouldClearCurrent(pNodeId, GetCurrent(pKingdom, PolicyNodeKind.Social)))
            {
                pKingdom.data.set(LineageKeys.POLICY_CURRENT, "");
                pKingdom.data.set(LineageKeys.POLICY_PROGRESS, 0f);
            }

            if (PolicyNodeLockRules.ShouldClearCurrent(pNodeId, GetCurrent(pKingdom, PolicyNodeKind.Decision)))
            {
                pKingdom.data.set(LineageKeys.DECISION_CURRENT, "");
                pKingdom.data.set(LineageKeys.DECISION_PROGRESS, 0f);
                ClearDecisionTarget(pKingdom);
            }

            RemoveQueuedDecision(pKingdom, pNodeId);

            if (PolicyNodeLockRules.ShouldClearCoreFabrication(pNodeId))
            {
                ClearCoreFabricationCurrent(pKingdom);
                WriteCoreFabricationQueue(pKingdom, new List<KingdomDecisionQueueItem>());
            }
        }

        public static bool StartResearch(Kingdom pKingdom, string pNodeId)
        {
            if (pKingdom?.data == null) return false;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            KingdomPolicyDef def = GetDefinition(pKingdom, pNodeId);
            if (def == null) return false;
            if (!CanAccessPolicyNode(pKingdom, def)) return false;
            if (IsNodeLocked(pKingdom, def.Id)) return false;
            if (def.Kind == PolicyNodeKind.Decision && def.Id == "aw_decision_absorb_vassal")
                return StartDecisionWithTarget(pKingdom, def.Id, VassalService.FindBestAbsorbVassalTarget(pKingdom),
                    pForceReplace: true);
            if (def.Kind == PolicyNodeKind.Decision && IsTargetedFabricationDecision(def.Id))
                return StartTargetedFabricationDecision(pKingdom, def.Id, pForceReplace: true);
            if (def.Kind == PolicyNodeKind.Decision && def.Id == "aw_decision_seek_suzerain")
                return false;
            EnsureInitialized(pKingdom);
            if (GetStatus(pKingdom, def) != PolicyNodeStatus.Available) return false;

            if (def.Kind == PolicyNodeKind.Decision &&
                TitleUpgradeDecisionRules.ShouldCompleteImmediately(def.Id, HasValidSuzerain(pKingdom)))
                return CompleteImmediateDecision(pKingdom, def);
            if (def.Kind == PolicyNodeKind.Decision && def.Id == "aw_decision_year_name")
                return CompleteImmediateDecision(pKingdom, def);

            if (def.Kind == PolicyNodeKind.Decision &&
                DecisionQueueRules.ShouldQueueDecisionWhenBusy(GetCurrent(pKingdom, PolicyNodeKind.Decision), def.Id))
            {
                EnqueueDecisionBack(pKingdom, CreateSimpleDecisionItem(def.Id, 0f));
                UpsertSnapshot(pKingdom);
                return true;
            }

            string currentKey = CurrentKey(def.Kind);
            string progressKey = ProgressKey(def.Kind);
            pKingdom.data.set(currentKey, def.Id);
            pKingdom.data.set(progressKey, 0f);
            if (def.Kind == PolicyNodeKind.Decision) ClearDecisionTarget(pKingdom);
            UpsertSnapshot(pKingdom);
            return true;
        }

        public static bool ForceStartResearch(Kingdom pKingdom, string pNodeId)
        {
            if (pKingdom?.data == null) return false;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            KingdomPolicyDef def = GetDefinition(pKingdom, pNodeId);
            if (def == null || IsCompleted(pKingdom, def)) return false;
            if (!CanAccessPolicyNode(pKingdom, def)) return false;
            if (IsNodeLocked(pKingdom, def.Id)) return false;
            if (def.Kind == PolicyNodeKind.Decision && def.Id == "aw_decision_absorb_vassal")
                return StartDecisionWithTarget(pKingdom, def.Id, VassalService.FindBestAbsorbVassalTarget(pKingdom));
            if (def.Kind == PolicyNodeKind.Decision && IsTargetedFabricationDecision(def.Id))
                return StartTargetedFabricationDecision(pKingdom, def.Id);
            if (def.Kind == PolicyNodeKind.Decision && def.Id == "aw_decision_seek_suzerain")
                return false;
            EnsureInitialized(pKingdom);
            if (GetCurrent(pKingdom, def.Kind) == def.Id) return false;

            if (def.Kind == PolicyNodeKind.Decision &&
                TitleUpgradeDecisionRules.ShouldCompleteImmediately(def.Id, HasValidSuzerain(pKingdom)))
                return CompleteImmediateDecision(pKingdom, def);
            if (def.Kind == PolicyNodeKind.Decision && def.Id == "aw_decision_year_name")
                return CompleteImmediateDecision(pKingdom, def);

            pKingdom.data.set(CurrentKey(def.Kind), def.Id);
            pKingdom.data.set(ProgressKey(def.Kind), 0f);
            if (def.Kind == PolicyNodeKind.Decision) ClearDecisionTarget(pKingdom);
            UpsertSnapshot(pKingdom);
            return true;
        }

        public static bool StartDecisionWithTarget(Kingdom pKingdom, string pNodeId, Kingdom pTarget,
            bool pForceReplace = false)
        {
            if (pKingdom?.data == null || pTarget?.data == null) return false;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            KingdomPolicyDef def = GetDefinition(pKingdom, pNodeId);
            if (def == null || def.Kind != PolicyNodeKind.Decision || IsCompleted(pKingdom, def)) return false;
            if (!CanAccessPolicyNode(pKingdom, def)) return false;
            if (IsNodeLocked(pKingdom, def.Id)) return false;
            EnsureInitialized(pKingdom);

            if (def.Id == "aw_decision_absorb_vassal" &&
                !VassalService.CanAbsorbVassalByDecision(pKingdom, pTarget, out _))
                return false;
            if (def.Id == "aw_decision_seek_suzerain" &&
                !VassalService.CanSetVassal(pKingdom, pTarget))
                return false;
            if (GetStatus(pKingdom, def) == PolicyNodeStatus.Locked) return false;
            if (HasPendingTargetedDecision(pKingdom, def.Id, pTarget.id))
                return true;

            var item = CreateSimpleDecisionItem(def.Id, 0f);
            FillDecisionTarget(item, pTarget);
            if (!pForceReplace &&
                DecisionQueueRules.ShouldQueueDecisionWhenBusy(GetCurrent(pKingdom, PolicyNodeKind.Decision), def.Id))
            {
                EnqueueDecisionBack(pKingdom, item);
                UpsertSnapshot(pKingdom);
                return true;
            }

            pKingdom.data.set(LineageKeys.DECISION_CURRENT, def.Id);
            pKingdom.data.set(LineageKeys.DECISION_PROGRESS, 0f);
            ClearDecisionTarget(pKingdom);
            SetDecisionTarget(pKingdom, pTarget);
            UpsertSnapshot(pKingdom);
            return true;
        }

        public static bool StartFabricationDecision(Kingdom pKingdom, Kingdom pTarget, City pTargetCity,
            string pProjectType, bool pForceReplace = false)
        {
            if (pKingdom?.data == null) return false;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            string defId = FabricationDecisionId(pProjectType);
            KingdomPolicyDef def = GetDefinition(pKingdom, defId);
            if (def == null || def.Kind != PolicyNodeKind.Decision) return false;
            if (!CanAccessPolicyNode(pKingdom, def)) return false;
            if (IsNodeLocked(pKingdom, def.Id)) return false;
            EnsureInitialized(pKingdom);

            City city = pTargetCity;
            Kingdom target = pTarget;
            if (pProjectType == WarTerritoryService.PROJECT_CORE)
            {
                if (city?.data == null) city = WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
                target = pKingdom;
                if (!WarTerritoryService.CanFabricateCoreProject(pKingdom, city, out _)) return false;
                return StartCoreFabrication(pKingdom, city);
            }
            else
            {
                if (target?.data == null) return false;
                if (city?.data == null) city = WarTerritoryService.FindFirstFabricationTargetCity(pKingdom, target);
                if (!WarTerritoryService.CanFabricateAgainst(pKingdom, target, city, out _)) return false;
            }

            var item = CreateFabricationDecisionItem(def.Id, target, city, pProjectType, def.FallbackName ?? "", 0f);
            string current = GetCurrent(pKingdom, PolicyNodeKind.Decision);
            if (pProjectType == WarTerritoryService.PROJECT_CORE &&
                DecisionQueueRules.ShouldPreemptCurrentDecisionForCore(current, coreDecisionAvailable: true))
            {
                EnqueueCurrentDecisionFront(pKingdom);
            }
            else if (!pForceReplace && DecisionQueueRules.ShouldQueueDecisionWhenBusy(current, def.Id))
            {
                EnqueueDecisionBack(pKingdom, item);
                UpsertSnapshot(pKingdom);
                return true;
            }

            pKingdom.data.set(LineageKeys.DECISION_CURRENT, def.Id);
            pKingdom.data.set(LineageKeys.DECISION_PROGRESS, 0f);
            ClearDecisionTarget(pKingdom);
            SetDecisionTarget(pKingdom, target);
            pKingdom.data.set(LineageKeys.DECISION_PROJECT_TYPE, pProjectType ?? "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_TARGET_CITY_ID, city?.data?.id ?? -1L);
            pKingdom.data.set(LineageKeys.DECISION_WAR_TARGET_CITY_NAME, city?.data?.name ?? "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_REASON_LABEL, def.FallbackName ?? "");
            UpsertSnapshot(pKingdom);
            return true;
        }

        private static bool IsTargetedFabricationDecision(string pDefId)
        {
            return pDefId == "aw_decision_fabricate_core" ||
                   pDefId == "aw_decision_fabricate_weak_claim" ||
                   pDefId == "aw_decision_fabricate_strong_claim";
        }

        private static bool HasTargetedFabricationTarget(Kingdom pKingdom, string pDefId)
        {
            bool hasCoreTarget = WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom)?.data != null;
            bool hasClaimTarget = WarTerritoryService.FindFirstFabricationTargetKingdom(pKingdom)?.data != null;
            return WarFabricationRules.CanExposeFabricationDecision(pDefId, hasCoreTarget, hasClaimTarget);
        }

        private static bool StartTargetedFabricationDecision(Kingdom pKingdom, string pDefId,
            bool pForceReplace = false)
        {
            if (pDefId == "aw_decision_fabricate_core")
            {
                City city = WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
                return KingdomPolicyService.StartFabricationDecision(pKingdom, pKingdom, city,
                    WarTerritoryService.PROJECT_CORE, pForceReplace);
            }

            if (pDefId != "aw_decision_fabricate_weak_claim" &&
                pDefId != "aw_decision_fabricate_strong_claim")
                return false;

            Kingdom target = WarTerritoryService.FindFirstFabricationTargetKingdom(pKingdom);
            City targetCity = WarTerritoryService.FindFirstFabricationTargetCity(pKingdom, target);
            string projectType = pDefId == "aw_decision_fabricate_strong_claim"
                ? WarTerritoryService.PROJECT_STRONG_CLAIM
                : WarTerritoryService.PROJECT_WEAK_CLAIM;
            return KingdomPolicyService.StartFabricationDecision(pKingdom, target, targetCity, projectType,
                pForceReplace);
        }

        private static string FabricationDecisionId(string pProjectType)
        {
            switch (pProjectType ?? "")
            {
                case WarTerritoryService.PROJECT_CORE:
                    return "aw_decision_fabricate_core";
                case WarTerritoryService.PROJECT_STRONG_CLAIM:
                    return "aw_decision_fabricate_strong_claim";
                case WarTerritoryService.PROJECT_WEAK_CLAIM:
                    return "aw_decision_fabricate_weak_claim";
                default:
                    return "";
            }
        }

        public static Kingdom GetDecisionTargetKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            return FindKingdom(GetDecisionTargetKingdomId(pKingdom));
        }

        private static Kingdom GetFrozenAnnexDecisionTargetKingdom(
            Kingdom pKingdom)
        {
            long targetId = GetDecisionTargetKingdomId(pKingdom);
            if (targetId < 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom target = World.world.kingdoms.get(targetId);
                return target?.data != null && !target.isRekt()
                    ? target
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static long GetDecisionTargetKingdomId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1L;
            pKingdom.data.get(LineageKeys.DECISION_TARGET_KINGDOM_ID,
                out long targetId, -1L);
            return targetId;
        }

        public static string GetDecisionTargetName(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.DECISION_TARGET_KINGDOM_NAME, out string targetName, "");
            if (!string.IsNullOrEmpty(targetName)) return targetName;
            Kingdom target = GetDecisionTargetKingdom(pKingdom);
            return target?.name ?? "";
        }

        public static string BuildDecisionTargetLine(Kingdom pKingdom)
        {
            if (IsTargetedFabricationDecision(GetCurrent(pKingdom, PolicyNodeKind.Decision)))
                return BuildFabricationDecisionTargetSummary(pKingdom);
            return DecisionTargetTextRules.TargetLine(GetDecisionTargetName(pKingdom));
        }

        private static string BuildFabricationDecisionTargetSummary(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.DECISION_PROJECT_TYPE, out string projectType, "");
            pKingdom.data.get(LineageKeys.DECISION_WAR_TARGET_CITY_NAME, out string cityName, "");
            string label = WarTerritoryService.ProjectLabel(projectType);
            string targetName = GetDecisionTargetName(pKingdom);
            string target = string.IsNullOrEmpty(targetName) ? "" : DecisionTargetTextRules.TargetLine(targetName);
            string city = string.IsNullOrEmpty(cityName) ? "" : "\u76ee\u6807\u57ce\uff1a" + cityName;
            if (string.IsNullOrEmpty(target)) return label + (string.IsNullOrEmpty(city) ? "" : "\n" + city);
            return label + "\n" + target + (string.IsNullOrEmpty(city) ? "" : "\n" + city);
        }

        private static void SetDecisionTarget(Kingdom pKingdom, Kingdom pTarget)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.DECISION_TARGET_KINGDOM_ID, pTarget?.id ?? -1L);
            pKingdom.data.set(LineageKeys.DECISION_TARGET_KINGDOM_NAME, pTarget?.name ?? "");
        }

        private static void ClearDecisionTarget(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.DECISION_TARGET_KINGDOM_ID, -1L);
            pKingdom.data.set(LineageKeys.DECISION_TARGET_KINGDOM_NAME, "");
            pKingdom.data.set(LineageKeys.DECISION_PROJECT_TYPE, "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_TYPE, "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_GOAL_TYPE, "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_REASON_KEY, "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_REASON_LABEL, "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_TARGET_CITY_ID, -1L);
            pKingdom.data.set(LineageKeys.DECISION_WAR_TARGET_CITY_NAME, "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_SOURCE_CLAIM_ID, -1L);
            pKingdom.data.set(LineageKeys.DECISION_WAR_SOURCE_CORE_ID, -1L);
            pKingdom.data.set(LineageKeys.DECISION_WAR_RESTORATION_CLAIM_ID, -1L);
            pKingdom.data.set(LineageKeys.DECISION_WAR_CLAIMANT_ACTOR_ID, -1L);
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_SIGNATURE, "");
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_YEAR, -1);
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_EARLIEST_YEAR, -1);
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_FORCED_YEAR, -1);
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_RECORDED, false);
        }

        private static void MigrateCurrentCoreDecisionToDedicatedSlot(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (IsNodeLocked(pKingdom, DecisionQueueRules.FabricateCoreDecisionId)) return;
            pKingdom.data.get(LineageKeys.DECISION_CURRENT, out string current, "");
            if (current != DecisionQueueRules.FabricateCoreDecisionId) return;
            pKingdom.data.get(LineageKeys.DECISION_PROJECT_TYPE, out string projectType, "");
            if (!string.IsNullOrEmpty(projectType) && projectType != WarTerritoryService.PROJECT_CORE) return;

            pKingdom.data.get(LineageKeys.DECISION_WAR_TARGET_CITY_ID, out long cityId, -1L);
            City city = FindCity(cityId) ?? WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
            float progress = GetProgress(pKingdom, PolicyNodeKind.Decision);
            if (city?.data != null)
            {
                if (GetCoreFabricationCityId(pKingdom) < 0)
                    SetCoreFabricationCurrent(pKingdom, city, progress);
                else if (!HasCoreFabricationProjectForCity(pKingdom, city.data.id))
                    EnqueueCoreFabrication(pKingdom, city);
            }

            pKingdom.data.set(LineageKeys.DECISION_CURRENT, "");
            pKingdom.data.set(LineageKeys.DECISION_PROGRESS, 0f);
            ClearDecisionTarget(pKingdom);
        }

        public static bool ForceSetClassState(Kingdom pKingdom, string pClassId)
        {
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            return PeasantRebelGovernmentTransitionService.TrySetClassState(
                pKingdom, pClassId);
        }

        internal static bool ApplyClassStateDirect(Kingdom pKingdom,
            string pClassId)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying ||
                pKingdom?.data == null || pKingdom.isRekt() ||
                !KingdomPolicyDefs.ClassStates.Contains(pClassId))
                return false;
            if (!KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                    KingdomPolicyProfileService.EnsureAssigned(pKingdom)))
                return false;
            EnsureInitialized(pKingdom);
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, pClassId);
            ApplyClassStateEffects(pKingdom, pClassId);
            UpsertSnapshot(pKingdom);
            return true;
        }

        public static PolicyNodeStatus GetStatus(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return PolicyNodeStatus.Locked;
            if (!CanAccessPolicyNode(pKingdom, pDef)) return PolicyNodeStatus.Locked;
            if (IsCompleted(pKingdom, pDef)) return PolicyNodeStatus.Completed;
            if (IsNodeLocked(pKingdom, pDef.Id)) return PolicyNodeStatus.Locked;
            if (GetCurrent(pKingdom, pDef.Kind) == pDef.Id) return PolicyNodeStatus.Current;
            return AreRequirementsMet(pKingdom, pDef) ? PolicyNodeStatus.Available : PolicyNodeStatus.Locked;
        }

        public static string GetClassId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return KingdomPolicyDefs.ClassDefault;
            if (MandateRebelService.IsRebelKingdom(pKingdom)) return KingdomPolicyDefs.ClassRebel;
            EnsureInitialized(pKingdom);
            pKingdom.data.get(LineageKeys.POLICY_CLASS_STATE, out string value, KingdomPolicyDefs.ClassDefault);
            return string.IsNullOrEmpty(value) ? KingdomPolicyDefs.ClassDefault : value;
        }

        public static string GetClassFallbackName(string pClassId)
        {
            if (pClassId == KingdomPolicyDefs.ClassRebel) return "\u519C\u6C11\u4E49\u519B";
            if (pClassId == KingdomPolicyDefs.ClassRepublic) return "\u5171\u548C\u653F\u4F53";
            return pClassId switch
            {
                KingdomPolicyDefs.ClassSlaveOwner => "奴隶制",
                KingdomPolicyDefs.ClassHalfAristocrat => "半贵族制",
                KingdomPolicyDefs.ClassAristocrat => "封建贵族",
                KingdomPolicyDefs.ClassReform => "改革制",
                _ => "部落制"
            };
        }

        public static string GetClassLocaleKey(string pClassId)
        {
            return "aw_policy_class_" + (string.IsNullOrEmpty(pClassId) ? KingdomPolicyDefs.ClassDefault : pClassId);
        }

        public static string GetArmyState(Kingdom pKingdom)
        {
            return GetState(pKingdom, LineageKeys.POLICY_ARMY_STATE, KingdomPolicyDefs.ArmyDefault);
        }

        public static string GetNameState(Kingdom pKingdom)
        {
            return GetState(pKingdom, LineageKeys.POLICY_NAME_STATE, KingdomPolicyDefs.NameDefault);
        }

        public static string GetEnfeoffmentState(Kingdom pKingdom)
        {
            return GetState(pKingdom, LineageKeys.POLICY_ENFEOFFMENT_STATE, KingdomPolicyDefs.EnfeoffmentDefault);
        }

        public static bool IsEnfeoffmentActive(Kingdom pKingdom)
        {
            string state = GetEnfeoffmentState(pKingdom);
            return state == KingdomPolicyDefs.EnfeoffmentBase ||
                   state == KingdomPolicyDefs.EnfeoffmentLimit ||
                   state == KingdomPolicyDefs.EnfeoffmentUnlimit ||
                   IsCompleted(pKingdom, PolicyNodeKind.Social,
                       "aw_policy_base_enfeoffment") ||
                   KingdomPolicyEffectService.Read(pKingdom)
                       .VassalAdministrationUnlocked;
        }

        public static float GetPoliticalPoints(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            pKingdom.data.get(LineageKeys.POLICY_POINTS, out float value, 0f);
            return value;
        }

        public static bool TrySpendPoliticalPoints(Kingdom pKingdom, float pCost,
            float pReserve = 0f)
        {
            if (pKingdom?.data == null || pCost < 0f || pReserve < 0f) return false;
            float current = Mathf.Clamp(GetPoliticalPoints(pKingdom), 0f, MAX_POINTS);
            if (current + 0.001f < pCost + pReserve) return false;
            pKingdom.data.set(LineageKeys.POLICY_POINTS,
                Mathf.Clamp(current - pCost, 0f, MAX_POINTS));
            UpsertSnapshot(pKingdom);
            return true;
        }

        internal static void RestorePoliticalPoints(Kingdom pKingdom,
            float pValue)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.POLICY_POINTS,
                Mathf.Clamp(pValue, 0f, MAX_POINTS));
            UpsertSnapshot(pKingdom);
        }

        public static float TransferPoliticalPoints(Kingdom pSource, Kingdom pTarget,
            float pRequested)
        {
            if (pSource?.data == null || pTarget?.data == null ||
                pSource == pTarget || pRequested <= 0f) return 0f;
            float sourcePoints = Mathf.Clamp(GetPoliticalPoints(pSource), 0f, MAX_POINTS);
            float targetPoints = Mathf.Clamp(GetPoliticalPoints(pTarget), 0f, MAX_POINTS);
            float actual = Mathf.Min(pRequested, sourcePoints, MAX_POINTS - targetPoints);
            if (actual <= 0f) return 0f;
            pSource.data.set(LineageKeys.POLICY_POINTS, sourcePoints - actual);
            pTarget.data.set(LineageKeys.POLICY_POINTS, targetPoints + actual);
            UpsertSnapshot(pSource);
            UpsertSnapshot(pTarget);
            return actual;
        }

        public static float GetTechPoints(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            pKingdom.data.get(LineageKeys.TECH_POINTS, out float value, 0f);
            return value;
        }

        public static float GetPoliticalPointGain(Kingdom pKingdom)
        {
            return pKingdom?.data == null ? 0f : CalcPoliticalGain(pKingdom);
        }

        public static float GetTechPointGain(Kingdom pKingdom)
        {
            return pKingdom?.data == null ? 0f : CalcTechGain(pKingdom);
        }

        public static string GetCurrent(Kingdom pKingdom, PolicyNodeKind pKind)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(CurrentKey(pKind), out string value, "");
            return value ?? "";
        }

        public static float GetProgress(Kingdom pKingdom, PolicyNodeKind pKind)
        {
            if (pKingdom?.data == null) return 0f;
            pKingdom.data.get(ProgressKey(pKind), out float value, 0f);
            return value;
        }

        public static float GetProgressFraction(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pDef == null || pDef.Cost <= 0f) return 0f;
            return Mathf.Clamp01(GetProgress(pKingdom, pDef.Kind) / pDef.Cost);
        }

        public static KingdomPolicySnapshot ReadSnapshot(Kingdom pKingdom)
        {
            var snapshot = new KingdomPolicySnapshot();
            if (pKingdom?.data == null) return snapshot;
            EnsureInitialized(pKingdom);

            snapshot.profile_id = KingdomPolicyProfileRules.ToPersistedId(
                GetPolicyProfile(pKingdom));
            pKingdom.data.get(LineageKeys.POLICY_GOVERNMENT_STATE,
                out snapshot.government_state, "default");
            pKingdom.data.get(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                out snapshot.royal_authority, 0);
            pKingdom.data.get(LineageKeys.POLICY_MIGRATION_VERSION,
                out snapshot.migration_version, 0);
            pKingdom.data.get(LineageKeys.POLICY_OBSOLETE_NODE_IDS,
                out snapshot.obsolete_node_ids, "");
            snapshot.class_state = GetClassId(pKingdom);
            snapshot.army_state = GetArmyState(pKingdom);
            snapshot.name_state = GetNameState(pKingdom);
            snapshot.enfeoffment_state = GetEnfeoffmentState(pKingdom);
            snapshot.policy_points = GetPoliticalPoints(pKingdom);
            snapshot.tech_points = GetTechPoints(pKingdom);
            snapshot.current_policy = GetCurrent(pKingdom, PolicyNodeKind.Social);
            snapshot.policy_progress = GetProgress(pKingdom, PolicyNodeKind.Social);
            snapshot.current_tech = GetCurrent(pKingdom, PolicyNodeKind.Tech);
            snapshot.tech_progress = GetProgress(pKingdom, PolicyNodeKind.Tech);
            snapshot.current_decision = GetCurrent(pKingdom, PolicyNodeKind.Decision);
            snapshot.decision_progress = GetProgress(pKingdom, PolicyNodeKind.Decision);
            pKingdom.data.get(LineageKeys.DECISION_TARGET_KINGDOM_ID,
                out snapshot.decision_target_kingdom_id, -1L);
            pKingdom.data.get(LineageKeys.DECISION_TARGET_KINGDOM_NAME,
                out snapshot.decision_target_kingdom_name, "");
            snapshot.decision_queue = GetDecisionQueueRaw(pKingdom);
            snapshot.core_fab_current_city_id = GetCoreFabricationCityId(pKingdom);
            snapshot.core_fab_current_city_name = GetCoreFabricationCityName(pKingdom);
            snapshot.core_fab_progress = GetCoreFabricationProgress(pKingdom);
            snapshot.core_fab_queue = GetCoreFabricationQueueRaw(pKingdom);
            snapshot.completed_policies = GetCompletedRaw(pKingdom, PolicyNodeKind.Social);
            snapshot.completed_techs = GetCompletedRaw(pKingdom, PolicyNodeKind.Tech);
            snapshot.completed_decisions = GetCompletedRaw(pKingdom, PolicyNodeKind.Decision);
            snapshot.locked_nodes = GetLockedNodesRaw(pKingdom);
            return snapshot;
        }

        public static void ApplySnapshot(Kingdom pKingdom, KingdomPolicySnapshot pSnapshot, bool pIncludeDecision)
        {
            if (pKingdom?.data == null || pSnapshot == null) return;
            EnsureInitialized(pKingdom);
            KingdomPolicyProfileMigrationState migrated =
                SanitizeSnapshotNodes(pKingdom, pSnapshot);
            string migratedDecisionQueue = pSnapshot.decision_queue ?? "";
            if (pIncludeDecision)
            {
                migratedDecisionQueue = MigrateLegacyDecisionQueue(
                    migratedDecisionQueue, out bool replacedLegacyDecision);
                if (replacedLegacyDecision)
                {
                    migrated.obsoleteNodeIds =
                        KingdomPolicyProfileMigrationRules.
                            AppendObsoleteNodeId(migrated.obsoleteNodeIds,
                                KingdomPolicyProfileMigrationRules.
                                    LegacyAppeaseXiaCitiesDecisionId);
                }
            }

            pKingdom.data.set(LineageKeys.POLICY_PROFILE_ID,
                migrated.profileId);
            pKingdom.data.set(LineageKeys.POLICY_GOVERNMENT_STATE,
                NonEmpty(pSnapshot.government_state, "default"));
            pKingdom.data.set(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                Math.Max(0, Math.Min(
                    WesternRoyalAuthorityRules.MaximumConsolidatedAuthority,
                    pSnapshot.royal_authority)));
            pKingdom.data.set(LineageKeys.POLICY_MIGRATION_VERSION,
                migrated.migrationVersion);
            pKingdom.data.set(LineageKeys.POLICY_OBSOLETE_NODE_IDS,
                migrated.obsoleteNodeIds);

            SetState(pKingdom, LineageKeys.POLICY_CLASS_STATE,
                NonEmpty(pSnapshot.class_state, KingdomPolicyDefs.ClassDefault));
            SetState(pKingdom, LineageKeys.POLICY_ARMY_STATE,
                NonEmpty(pSnapshot.army_state, KingdomPolicyDefs.ArmyDefault));
            SetState(pKingdom, LineageKeys.POLICY_NAME_STATE,
                NonEmpty(pSnapshot.name_state, KingdomPolicyDefs.NameDefault));
            SetState(pKingdom, LineageKeys.POLICY_ENFEOFFMENT_STATE,
                NonEmpty(pSnapshot.enfeoffment_state, KingdomPolicyDefs.EnfeoffmentDefault));

            pKingdom.data.set(LineageKeys.POLICY_POINTS, Mathf.Clamp(pSnapshot.policy_points, 0f, MAX_POINTS));
            pKingdom.data.set(LineageKeys.TECH_POINTS, Mathf.Clamp(pSnapshot.tech_points, 0f, MAX_POINTS));
            pKingdom.data.set(LineageKeys.POLICY_CURRENT,
                migrated.currentPolicy);
            pKingdom.data.set(LineageKeys.POLICY_PROGRESS,
                string.IsNullOrEmpty(migrated.currentPolicy)
                    ? 0f
                    : Mathf.Max(0f, pSnapshot.policy_progress));
            pKingdom.data.set(LineageKeys.TECH_CURRENT,
                migrated.currentTech);
            pKingdom.data.set(LineageKeys.TECH_PROGRESS,
                string.IsNullOrEmpty(migrated.currentTech)
                    ? 0f
                    : Mathf.Max(0f, pSnapshot.tech_progress));
            pKingdom.data.set(LineageKeys.POLICY_COMPLETED,
                migrated.completedPolicies);
            pKingdom.data.set(LineageKeys.TECH_COMPLETED,
                migrated.completedTechs);

            if (pIncludeDecision)
            {
                pKingdom.data.set(LineageKeys.DECISION_CURRENT,
                    migrated.currentDecision);
                pKingdom.data.set(LineageKeys.DECISION_PROGRESS,
                    string.IsNullOrEmpty(migrated.currentDecision)
                        ? 0f
                        : Mathf.Max(0f, pSnapshot.decision_progress));
                pKingdom.data.set(LineageKeys.DECISION_TARGET_KINGDOM_ID,
                    string.IsNullOrEmpty(migrated.currentDecision)
                        ? -1L
                        : pSnapshot.decision_target_kingdom_id);
                pKingdom.data.set(LineageKeys.DECISION_TARGET_KINGDOM_NAME,
                    string.IsNullOrEmpty(migrated.currentDecision)
                        ? ""
                        : pSnapshot.decision_target_kingdom_name ?? "");
                pKingdom.data.set(LineageKeys.DECISION_QUEUE,
                    migratedDecisionQueue);
                pKingdom.data.set(LineageKeys.CORE_FAB_CURRENT_CITY_ID, pSnapshot.core_fab_current_city_id);
                pKingdom.data.set(LineageKeys.CORE_FAB_CURRENT_CITY_NAME, pSnapshot.core_fab_current_city_name ?? "");
                pKingdom.data.set(LineageKeys.CORE_FAB_PROGRESS, Mathf.Max(0f, pSnapshot.core_fab_progress));
                pKingdom.data.set(LineageKeys.CORE_FAB_QUEUE, pSnapshot.core_fab_queue ?? "");
                pKingdom.data.set(LineageKeys.DECISION_COMPLETED,
                    migrated.completedDecisions);
            }

            pKingdom.data.set(LineageKeys.POLICY_LOCKED_NODES,
                migrated.lockedNodes);

            KingdomPolicyEffectService.Invalidate(pKingdom);
            UpsertSnapshot(pKingdom);
        }

        public static bool RestoreIdentityContinuity(Kingdom pKingdom)
        {
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || pKingdom?.data == null) return false;
            RestorationInstitutionState fallen;
            try
            {
                using var cmd = new System.Data.SQLite.SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT CLASS_STATE, ARMY_STATE, NAME_STATE, ENFEOFFMENT_STATE, " +
                    $"POLICY_POINTS, TECH_POINTS, CURRENT_POLICY, POLICY_PROGRESS, CURRENT_TECH, " +
                    $"TECH_PROGRESS, CURRENT_DECISION, DECISION_PROGRESS, DECISION_QUEUE, " +
                    $"CORE_FAB_CURRENT_CITY_ID, CORE_FAB_CURRENT_CITY_NAME, CORE_FAB_PROGRESS, " +
                    $"CORE_FAB_QUEUE, COMPLETED_POLICIES, COMPLETED_TECHS, COMPLETED_DECISIONS, " +
                    $"LOCKED_NODES, PROFILE_ID, GOVERNMENT_STATE, MIGRATION_VERSION, " +
                    $"OBSOLETE_NODE_IDS, ROYAL_AUTHORITY FROM {KingdomPolicyStateTableItem.GetTableName()} " +
                    "WHERE KINGDOM_ID=@k LIMIT 1";
                cmd.Parameters.AddWithValue("@k", pKingdom.id);
                using var reader = (System.Data.SQLite.SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return false;
                fallen = new RestorationInstitutionState
                {
                    classState = DbString(reader, 0),
                    armyState = DbString(reader, 1),
                    nameState = DbString(reader, 2),
                    enfeoffmentState = DbString(reader, 3),
                    policyPoints = DbFloat(reader, 4),
                    techPoints = DbFloat(reader, 5),
                    currentPolicy = DbString(reader, 6),
                    policyProgress = DbFloat(reader, 7),
                    currentTech = DbString(reader, 8),
                    techProgress = DbFloat(reader, 9),
                    currentDecision = DbString(reader, 10),
                    decisionProgress = DbFloat(reader, 11),
                    decisionQueue = DbString(reader, 12),
                    coreFabricationCityId = DbLong(reader, 13),
                    coreFabricationCityName = DbString(reader, 14),
                    coreFabricationProgress = DbFloat(reader, 15),
                    coreFabricationQueue = DbString(reader, 16),
                    completedPolicies = DbString(reader, 17),
                    completedTechs = DbString(reader, 18),
                    completedDecisions = DbString(reader, 19),
                    lockedNodes = DbString(reader, 20),
                    profileId = DbString(reader, 21),
                    governmentState = DbString(reader, 22),
                    migrationVersion = (int)DbLong(reader, 23),
                    obsoleteNodeIds = DbString(reader, 24),
                    royalAuthority = (int)DbLong(reader, 25)
                };
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Kingdom policy continuity read failed: " + e.Message);
                return false;
            }

            RestorationInstitutionState state =
                RestorationInstitutionRules.SanitizeForRevival(fallen);
            if (state == null) return false;
            pKingdom.data.set(LineageKeys.POLICY_ENABLED, true);
            pKingdom.data.set(LineageKeys.POLICY_AI_ENABLED, true);
            pKingdom.data.set(LineageKeys.POLICY_LAST_YEAR, Date.getCurrentYear());
            ApplySnapshot(pKingdom, new KingdomPolicySnapshot
            {
                profile_id = state.profileId,
                government_state = state.governmentState,
                royal_authority = state.royalAuthority,
                migration_version = state.migrationVersion,
                obsolete_node_ids = state.obsoleteNodeIds,
                class_state = state.classState,
                army_state = state.armyState,
                name_state = state.nameState,
                enfeoffment_state = state.enfeoffmentState,
                policy_points = state.policyPoints,
                tech_points = state.techPoints,
                current_policy = state.currentPolicy,
                policy_progress = state.policyProgress,
                current_tech = state.currentTech,
                tech_progress = state.techProgress,
                completed_policies = state.completedPolicies,
                completed_techs = state.completedTechs,
                completed_decisions = state.completedDecisions,
                locked_nodes = state.lockedNodes,
                current_decision = state.currentDecision,
                decision_progress = state.decisionProgress,
                decision_queue = state.decisionQueue,
                core_fab_current_city_id = state.coreFabricationCityId,
                core_fab_current_city_name =
                    state.coreFabricationCityName,
                core_fab_progress = state.coreFabricationProgress,
                core_fab_queue = state.coreFabricationQueue
            }, pIncludeDecision: true);
            return true;
        }

        private static string DbString(System.Data.SQLite.SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }

        private static float DbFloat(System.Data.SQLite.SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0f : Convert.ToSingle(pReader.GetValue(pIndex));
        }

        private static long DbLong(System.Data.SQLite.SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? -1L : Convert.ToInt64(pReader.GetValue(pIndex));
        }

        public static TechLevelReport GetTechLevelReport(Kingdom pKingdom)
        {
            var report = new TechLevelReport();
            IReadOnlyList<KingdomPolicyDef> techs = GetNodes(pKingdom,
                PolicyNodeKind.Tech);
            report.total_count = techs.Count;
            report.max_level = 5;

            float maxScore = 0f;
            float score = 0f;
            int completed = 0;
            foreach (KingdomPolicyDef tech in techs)
            {
                float cost = Mathf.Max(1f, tech.Cost);
                maxScore += cost;
                if (!IsCompleted(pKingdom, tech)) continue;
                completed++;
                score += cost;
            }

            KingdomPolicyDef current = GetDefinition(pKingdom,
                GetCurrent(pKingdom, PolicyNodeKind.Tech));
            if (current != null && current.Cost > 0f && !IsCompleted(pKingdom, current))
            {
                float fraction = GetProgressFraction(pKingdom, current);
                score += Mathf.Max(1f, current.Cost) * fraction;
                report.current_name = current.FallbackName;
                report.current_fraction = fraction;
            }

            report.score = score;
            report.max_score = Mathf.Max(1f, maxScore);
            report.completed_count = completed;
            report.level = Mathf.Clamp(1 + Mathf.FloorToInt(score / report.max_score * report.max_level), 1,
                report.max_level);
            if (completed >= report.total_count && report.total_count > 0) report.level = report.max_level;
            return report;
        }

        public static float GetTechFrontierMultiplier(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 1f;
            TechLevelReport report = GetTechLevelReport(pKingdom);
            return TechResearchPaceRules.FrontierMultiplier(
                pIsTech: true,
                pOwnTechLevel: report.level,
                pWorldMaxTechLevel: GetWorldMaxTechLevelForYear());
        }

        public static string GetCurrentSummary(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            var parts = new List<string>();
            KingdomPolicyDef tech = GetDefinition(pKingdom,
                GetCurrent(pKingdom, PolicyNodeKind.Tech));
            KingdomPolicyDef social = GetDefinition(pKingdom,
                GetCurrent(pKingdom, PolicyNodeKind.Social));
            if (tech != null) parts.Add(AW_L10n.Text(tech.NameKey, tech.FallbackName));
            if (social != null) parts.Add(AW_L10n.Text(social.NameKey, social.FallbackName));
            return parts.Count == 0
                ? AW_L10n.Text("aw_policy_idle", "Idle")
                : string.Join("/", parts.ToArray());
        }

        public static bool IsCompleted(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pDef == null) return false;
            if (pDef.Repeatable) return false;
            return IsCompleted(pKingdom, pDef.Kind, pDef.Id);
        }

        public static bool IsCompleted(Kingdom pKingdom, PolicyNodeKind pKind, string pId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pId)) return false;
            string raw = GetCompletedRaw(pKingdom, pKind);
            return Split(raw).Contains(pId);
        }

        public static IEnumerable<string> MissingRequirements(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pDef == null) yield break;
            foreach (string id in pDef.RequiredTechs ?? Array.Empty<string>())
            {
                if (ShouldIgnoreRequirement(pKingdom, pDef, PolicyNodeKind.Tech, id)) continue;
                if (!IsCompleted(pKingdom, PolicyNodeKind.Tech, id))
                    yield return id;
            }
            foreach (string id in pDef.RequiredPolicies ?? Array.Empty<string>())
            {
                if (ShouldIgnoreRequirement(pKingdom, pDef, PolicyNodeKind.Social, id)) continue;
                if (!IsCompleted(pKingdom, PolicyNodeKind.Social, id))
                    yield return id;
            }
        }

        private static bool ShouldIgnoreRequirement(Kingdom pKingdom, KingdomPolicyDef pDef,
            PolicyNodeKind pKind, string pRequirementId)
        {
            return XiaizationService.IsXiaizationPolicy(pDef) &&
                   !LineageService.IsXiaKingdom(pKingdom) &&
                   XiaizationService.GetLevel(pKingdom) >= XiaizationService.LevelPseudoDynasty &&
                   pKind == PolicyNodeKind.Tech &&
                   pRequirementId == "aw_tech_writing";
        }

        private static bool CanAccessPolicyNode(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return false;
            KingdomPolicyProfileId profileId = GetPolicyProfile(pKingdom);
            if (!KingdomPolicyCatalogRules.BelongsTo(pDef, profileId))
                return false;
            if (profileId == KingdomPolicyProfileId.WesternGeneral)
                return true;
            return XiaizationEligibilityRules.CanUsePolicyNode(
                XiaizationService.IsNativePolicyKingdom(pKingdom),
                XiaizationService.GetLevel(pKingdom),
                pDef.Id,
                XiaizationService.IsXiaizationPolicy(pDef));
        }

        private static bool IsHistoricalFigureKing(Kingdom pKingdom)
        {
            Actor king = pKingdom?.king;
            return king?.data != null && (king.hasTrait("first") || king.hasTrait("figure"));
        }

        private static void AddMonthlyPoints(Kingdom pKingdom)
        {
            float political = GetPoliticalPoints(pKingdom);
            float tech = GetTechPoints(pKingdom);
            pKingdom.data.set(LineageKeys.POLICY_POINTS,
                Mathf.Clamp(political +
                            KingdomDecisionMonthlyRules.MonthlyShare(
                                CalcPoliticalGain(pKingdom)),
                    0f, MAX_POINTS));
            pKingdom.data.set(LineageKeys.TECH_POINTS,
                Mathf.Clamp(tech +
                            KingdomDecisionMonthlyRules.MonthlyShare(
                                CalcTechGain(pKingdom)),
                    0f, MAX_POINTS));
        }

        private static float CalcPoliticalGain(Kingdom pKingdom)
        {
            float king = 0f;
            if (pKingdom.hasKing() && pKingdom.king?.stats != null)
                king = pKingdom.king.stats["stewardship"] * 0.05f + pKingdom.king.stats["diplomacy"] * 0.02f;
            float cityEconomy = CityEconomyService.GetPolicyContribution(pKingdom);
            return Mathf.Clamp(2f + king + CountCities(pKingdom) * 0.25f + CountUnits(pKingdom) * 0.008f + cityEconomy, 1f, 22f);
        }

        private static float CalcTechGain(Kingdom pKingdom)
        {
            float king = 0f;
            if (pKingdom.hasKing() && pKingdom.king?.stats != null)
                king = pKingdom.king.stats["intelligence"] * 0.06f + pKingdom.king.stats["stewardship"] * 0.015f;
            float cityEconomy = CityEconomyService.GetTechContribution(pKingdom);
            return Mathf.Clamp(1.5f + king + CountCities(pKingdom) * 0.18f + CountUnits(pKingdom) * 0.004f + cityEconomy, 1f, 20f);
        }

        private static void AdvanceCurrent(Kingdom pKingdom,
            PolicyNodeKind pKind, float pElapsedYears)
        {
            string current = GetCurrent(pKingdom, pKind);
            if (string.IsNullOrEmpty(current)) return;
            KingdomPolicyDef def = GetDefinition(pKingdom, current);
            if (def == null)
            {
                pKingdom.data.set(CurrentKey(pKind), "");
                pKingdom.data.set(ProgressKey(pKind), 0f);
                return;
            }
            if (IsNodeLocked(pKingdom, def.Id))
            {
                pKingdom.data.set(CurrentKey(pKind), "");
                pKingdom.data.set(ProgressKey(pKind), 0f);
                if (pKind == PolicyNodeKind.Decision) ClearDecisionTarget(pKingdom);
                UpsertSnapshot(pKingdom);
                return;
            }
            if (!CanAccessPolicyNode(pKingdom, def))
            {
                pKingdom.data.set(CurrentKey(pKind), "");
                pKingdom.data.set(ProgressKey(pKind), 0f);
                if (pKind == PolicyNodeKind.Decision) ClearDecisionTarget(pKingdom);
                UpsertSnapshot(pKingdom);
                return;
            }

            float progress = GetProgress(pKingdom, pKind);
            if (pKind == PolicyNodeKind.Decision &&
                current == "aw_decision_absorb_vassal")
            {
                VassalAnnexProgressState annexState =
                    VassalService.GetAnnexDecisionProgressState(
                        pKingdom,
                        GetFrozenAnnexDecisionTargetKingdom(pKingdom),
                        progress, def.Cost);
                if (annexState == VassalAnnexProgressState.Pause)
                    return;
                if (annexState == VassalAnnexProgressState.Cancel)
                {
                    ReleaseFailedDecision(pKingdom);
                    return;
                }
                if (annexState == VassalAnnexProgressState.Complete)
                {
                    Complete(pKingdom, def);
                    return;
                }
            }
            if (progress + 0.001f >= def.Cost)
            {
                Complete(pKingdom, def);
                return;
            }
            string pointKey = pKind == PolicyNodeKind.Tech ? LineageKeys.TECH_POINTS : LineageKeys.POLICY_POINTS;
            pKingdom.data.get(pointKey, out float points, 0f);
            if (points <= 0f) return;

            float remaining = def.Cost - progress;
            float spendLimit = Mathf.Max(0f,
                MAX_YEARLY_SPEND * pElapsedYears);
            float rawSpend = pKind == PolicyNodeKind.Tech
                ? Mathf.Min(points, spendLimit)
                : PoliticalPointSpendingRules.AutomaticSpend(points,
                    spendLimit);
            float progressMultiplier = 1f;
            if (pKind == PolicyNodeKind.Tech)
            {
                progressMultiplier = CityTechService.GetNeighborTechResearchBonus(pKingdom, def.Id);
                progressMultiplier *= GetTechFrontierMultiplier(pKingdom);
            }

            float effectiveProgress = Mathf.Min(remaining, rawSpend * progressMultiplier);
            float spend = progressMultiplier <= 0f ? effectiveProgress : effectiveProgress / progressMultiplier;
            if (spend <= 0f || effectiveProgress <= 0f) return;

            points -= spend;
            progress += effectiveProgress;
            pKingdom.data.set(pointKey, points);
            pKingdom.data.set(ProgressKey(pKind), progress);

            if (progress + 0.001f >= def.Cost) Complete(pKingdom, def);
        }

        private static void Complete(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            bool effectApplied = false;
            if (pDef.Kind == PolicyNodeKind.Decision)
            {
                effectApplied = ApplyEffect(pKingdom, pDef);
                if (KingdomDecisionPriorityRules.
                    ShouldReleaseFailedCompletion(
                        isDecision: true, effectApplied: effectApplied))
                {
                    ReleaseFailedDecision(pKingdom);
                    return;
                }
            }
            if (pDef.Kind == PolicyNodeKind.Decision)
                effectApplied = true;

            AddCompleted(pKingdom, pDef.Kind, pDef.Id);
            if (pDef.Kind == PolicyNodeKind.Tech)
                CivilServiceLegacyTransitionService.OnTechnologyCompleted(
                    pKingdom, pDef.Id);
            pKingdom.data.set(CurrentKey(pDef.Kind), "");
            pKingdom.data.set(ProgressKey(pDef.Kind), 0f);
            if (!string.IsNullOrEmpty(pDef.ClassAfter))
            {
                pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, pDef.ClassAfter);
                ApplyClassStateEffects(pKingdom, pDef.ClassAfter);
            }
            ApplyPolicyStateEffects(pKingdom, pDef);

            if (pDef.Kind != PolicyNodeKind.Decision)
                effectApplied = ApplyEffect(pKingdom, pDef);
            RecordWesternInstitutionTransition(pKingdom);
            if (pDef.Kind == PolicyNodeKind.Tech)
                CityTechService.OnNationalTechCompleted(pKingdom, pDef);
            if (effectApplied && EraNameRules.IsCentralReform(pDef.Id))
                EraChangeTriggerService.Mark(pKingdom,
                    EraChangeReason.CentralReform,
                    "reform:" + pDef.Id + ":" + Date.getCurrentYear());
            if (effectApplied && ShouldRecordGenericCompletion(pDef))
                RecordCompletion(pKingdom, pDef);
            if (pDef.Kind == PolicyNodeKind.Decision)
            {
                ClearDecisionTarget(pKingdom);
                StartNextQueuedDecisionIfEmpty(pKingdom);
            }
            UpsertSnapshot(pKingdom);
        }

        private static void ReleaseFailedDecision(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.DECISION_CURRENT, "");
            pKingdom.data.set(LineageKeys.DECISION_PROGRESS, 0f);
            ClearDecisionTarget(pKingdom);
            StartNextQueuedDecisionIfEmpty(pKingdom);
            UpsertSnapshot(pKingdom);
        }

        private static bool CompleteImmediateDecision(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef?.Kind != PolicyNodeKind.Decision) return false;
            if (!ApplyEffect(pKingdom, pDef)) return false;

            AddCompleted(pKingdom, pDef.Kind, pDef.Id);
            if (ShouldRecordGenericCompletion(pDef)) RecordCompletion(pKingdom, pDef);
            UpsertSnapshot(pKingdom);
            return true;
        }

        private static void RecordWesternInstitutionTransition(
            Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                GetPolicyProfile(pKingdom) !=
                KingdomPolicyProfileId.WesternGeneral) return;
            CourtInstitutionService.Refresh(pKingdom, pRecordHistory: true);
        }

        private static bool HasValidSuzerain(Kingdom pKingdom)
        {
            Kingdom suzerain = VassalService.GetSuzerain(pKingdom);
            return suzerain?.data != null && !suzerain.isRekt();
        }

        private static int GetWorldMaxTechLevelForYear()
        {
            int year = Date.getCurrentYear();
            if (_techFrontierCacheYear == year) return _techFrontierMaxLevel;
            _techFrontierCacheYear = year;
            _techFrontierMaxLevel = 1;

            try
            {
                if (World.world?.kingdoms == null) return _techFrontierMaxLevel;
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) continue;
                    if (!CanUsePolicySystem(kingdom)) continue;
                    int level = GetTechLevelReport(kingdom).level;
                    if (level > _techFrontierMaxLevel) _techFrontierMaxLevel = level;
                }
            }
            catch
            {
                _techFrontierMaxLevel = Math.Max(1, _techFrontierMaxLevel);
            }

            return _techFrontierMaxLevel;
        }

        private static bool ApplyEffect(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return false;
            switch (pDef.Id)
            {
                case "aw_tech_official_court":
                case "aw_tech_three_departments":
                case "aw_tech_song_court":
                    CourtInstitutionService.Refresh(pKingdom, pRecordHistory: true);
                    return true;
                case "aw_policy_start_slavery":
                case "aw_policy_control_slaves":
                    SlaveService.SetSlaveryEnabled(pKingdom, true);
                    SlaveService.EnforceSlaveControl(pKingdom);
                    return true;
                case "aw_policy_slave_army":
                    SlaveService.SetSlaveArmyEnabled(pKingdom, true);
                    return true;
                case "aw_policy_name_integration":
                    LineageService.ApplyNameIntegration(pKingdom);
                    return true;
                case "aw_policy_abolish_slavery":
                    SlaveService.SetSlaveryEnabled(pKingdom, false);
                    return true;
                case "aw_decision_year_name":
                    if (!pKingdom.hasKing()) return false;
                    return YearNameService.TryChangeEra(pKingdom, pKingdom.king,
                        "", EraChangeKind.Voluntary,
                        EraChangeReason.PlayerRequested).Success;
                case "aw_decision_claim_mandate":
                    return MandateService.TryDeclareMandate(pKingdom, "decision");
                case "aw_decision_title_upgrade":
                    if (!CanPromoteTitle(pKingdom)) return false;
                    KingdomTitleService.PromoteTitle(pKingdom);
                    return true;
                case "aw_decision_royal_expansion":
                    if (!RoyalExpansionDecisionService.Execute(pKingdom)) return false;
                    pKingdom.data.set(LineageKeys.POLICY_AI_LAST_ROYAL_EXPANSION_YEAR, Date.getCurrentYear());
                    return true;
                case "aw_decision_change_capital":
                    return ChangeCapital(pKingdom);
                case "aw_decision_control_slaves":
                    SlaveService.SetSlaveryEnabled(pKingdom, true);
                    SlaveService.EnforceSlaveControl(pKingdom);
                    return true;
                case "aw_west_decision_consolidate_royal_authority":
                    return KingdomPolicyEffectService.ApplyRoyalAuthorityDecision(
                        pKingdom);
                case "aw_decision_absorb_vassal":
                    Kingdom target = GetDecisionTargetKingdom(pKingdom);
                    return target != null &&
                           VassalService.CanCompleteAbsorbVassalByDecision(
                               pKingdom, target, out _) &&
                           VassalService.TryAbsorbVassal(pKingdom, target, "absorb_vassal_decision");
                case "aw_decision_seek_suzerain":
                    Kingdom suzerain = GetDecisionTargetKingdom(pKingdom);
                    return suzerain != null &&
                           VassalService.SetVassal(pKingdom, suzerain, "active_vassal_decision");
                case "aw_decision_fabricate_core":
                    return ExecuteFabricationDecision(pKingdom, WarTerritoryService.PROJECT_CORE);
                case "aw_decision_fabricate_weak_claim":
                    return ExecuteFabricationDecision(pKingdom, WarTerritoryService.PROJECT_WEAK_CLAIM);
                case "aw_decision_fabricate_strong_claim":
                    return ExecuteFabricationDecision(pKingdom, WarTerritoryService.PROJECT_STRONG_CLAIM);
                default:
                    if (XiaizationService.ApplyPolicyEffect(pKingdom, pDef)) return true;
                    return true;
            }
        }

        private static bool ShouldRecordGenericCompletion(KingdomPolicyDef pDef)
        {
            if (pDef?.Kind != PolicyNodeKind.Decision) return true;
            return pDef.Id != "aw_decision_royal_expansion" &&
                   pDef.Id != "aw_decision_change_capital" &&
                   pDef.Id != "aw_decision_absorb_vassal" &&
                   pDef.Id != "aw_decision_seek_suzerain" &&
                   pDef.Id != "aw_decision_fabricate_core" &&
                   pDef.Id != "aw_decision_fabricate_weak_claim" &&
                   pDef.Id != "aw_decision_fabricate_strong_claim";
        }

        private static void ApplyPolicyStateEffects(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (!string.IsNullOrEmpty(pDef.ArmyStateAfter))
            {
                SetState(pKingdom, LineageKeys.POLICY_ARMY_STATE, pDef.ArmyStateAfter);
                if (pDef.ArmyStateAfter == KingdomPolicyDefs.ArmySlaveSoldier)
                    SlaveService.SetSlaveArmyEnabled(pKingdom, true);
            }

            if (!string.IsNullOrEmpty(pDef.NameStateAfter))
                SetState(pKingdom, LineageKeys.POLICY_NAME_STATE, pDef.NameStateAfter);

            if (!string.IsNullOrEmpty(pDef.EnfeoffmentStateAfter))
                SetState(pKingdom, LineageKeys.POLICY_ENFEOFFMENT_STATE, pDef.EnfeoffmentStateAfter);

            if (!string.IsNullOrEmpty(pDef.GovernmentStateAfter))
                SetState(pKingdom,
                    LineageKeys.POLICY_GOVERNMENT_STATE, pDef.GovernmentStateAfter);
        }

        private static void ApplyClassStateEffects(Kingdom pKingdom, string pClassId)
        {
            switch (pClassId)
            {
                case KingdomPolicyDefs.ClassSlaveOwner:
                    SlaveService.SetSlaveryEnabled(pKingdom, true);
                    break;
                case KingdomPolicyDefs.ClassRebel:
                case KingdomPolicyDefs.ClassBandit:
                    break;
                case KingdomPolicyDefs.ClassDefault:
                case KingdomPolicyDefs.ClassRepublic:
                case KingdomPolicyDefs.ClassReform:
                    SlaveService.SetSlaveryEnabled(pKingdom, false);
                    break;
            }
        }

        private static void EnsureState(Kingdom pKingdom, string pKey, string pDefault)
        {
            pKingdom.data.get(pKey, out string value, "");
            if (string.IsNullOrEmpty(value))
                pKingdom.data.set(pKey, pDefault);
        }

        private static string GetState(Kingdom pKingdom, string pKey, string pDefault)
        {
            if (pKingdom?.data == null) return pDefault;
            EnsureInitialized(pKingdom);
            pKingdom.data.get(pKey, out string value, pDefault);
            return string.IsNullOrEmpty(value) ? pDefault : value;
        }

        private static void SetState(Kingdom pKingdom, string pKey, string pValue)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pValue)) return;
            pKingdom.data.set(pKey, pValue);
            if (pKey == LineageKeys.POLICY_GOVERNMENT_STATE)
                KingdomPolicyEffectService.Invalidate(pKingdom);
        }

        private static void RecordCompletion(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return;
            string kindKey = pDef.Kind == PolicyNodeKind.Tech
                ? "aw_hist_policy_kind_tech"
                : pDef.Kind == PolicyNodeKind.Decision
                    ? "aw_hist_policy_kind_decision"
                    : "aw_hist_policy_kind_policy";
            HistoryText kind = HistoryLocalizationRules.H(kindKey);
            HistoryText definitionName = HistoryText.PlainText(
                AncientWarfare3.ui.AW_L10n.Text(pDef.NameKey, pDef.FallbackName));
            string eventType = pDef.Kind == PolicyNodeKind.Tech
                ? KingdomEvent.TECH_COMPLETED
                : KingdomEvent.POLICY_COMPLETED;
            HistoryWriter.RecordKingdom(pKingdom, eventType,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_policy_completed_mid") + kind +
                HistoryLocalizationRules.H("aw_hist_policy_kind_separator") + definitionName,
                HistoryTarget.Kingdom(pKingdom));

            Actor king = pKingdom.hasKing() ? pKingdom.king : null;
            if (ChronicleGate.IsNobleActor(king))
                HistoryWriter.RecordPerson(king.data.id, pKingdom, king.getName(), eventType,
                    HistoryText.Actor(king) +
                    HistoryLocalizationRules.H("aw_hist_policy_presided_mid") + kind +
                    HistoryLocalizationRules.H("aw_hist_policy_kind_separator") + definitionName,
                    ChronicleCategory.HONOR,
                    HistoryTarget.Kingdom(pKingdom));
        }

        private static bool AreRequirementsMet(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pDef == null) return false;
            return !MissingRequirements(pKingdom, pDef).Any() && IsSpecialRequirementMet(pKingdom, pDef);
        }

        private static bool IsSpecialRequirementMet(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return false;
            switch (pDef.Id)
            {
                case "aw_decision_year_name":
                    return pKingdom.hasKing() &&
                           KingdomTitleService.IsEmperor(pKingdom) &&
                           !RepublicGovernmentService.IsRepublic(pKingdom) &&
                           VassalService.GetSuzerain(pKingdom) == null &&
                           KingdomPolicyService.GetPoliticalPoints(pKingdom) >=
                           YearNameService.VoluntaryChangeCost;
                case "aw_decision_claim_mandate":
                    return MandateService.CanDeclareMandate(pKingdom, out _);
                case "aw_decision_title_upgrade":
                    return CanPromoteTitle(pKingdom);
                case "aw_decision_royal_expansion":
                    return YearsSince(pKingdom, LineageKeys.POLICY_AI_LAST_ROYAL_EXPANSION_YEAR, -99999) >= 15 &&
                           RoyalExpansionDecisionService.CanExecute(pKingdom);
                case "aw_decision_change_capital":
                    return FindNewCapital(pKingdom) != null;
                case "aw_decision_control_slaves":
                    return SlaveService.IsSlaveryEnabled(pKingdom) ||
                           IsCompleted(pKingdom, PolicyNodeKind.Social, "aw_policy_start_slavery");
                case "aw_west_decision_consolidate_royal_authority":
                    return KingdomPolicyEffectService.
                        CanConsolidateRoyalAuthority(pKingdom);
                case "aw_decision_absorb_vassal":
                    return VassalService.FindBestAbsorbVassalTarget(pKingdom) != null;
                case "aw_decision_seek_suzerain":
                    return true;
                case "aw_decision_fabricate_core":
                case "aw_decision_fabricate_weak_claim":
                case "aw_decision_fabricate_strong_claim":
                    return HasTargetedFabricationTarget(pKingdom, pDef.Id);
                default:
                    if (XiaizationService.IsXiaizationPolicy(pDef))
                        return XiaizationService.SpecialRequirementMet(pKingdom, pDef.Id);
                    return true;
            }
        }

        private static void AddCompleted(Kingdom pKingdom, PolicyNodeKind pKind, string pId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pId)) return;
            var set = new HashSet<string>(Split(GetCompletedRaw(pKingdom, pKind)));
            if (!set.Add(pId)) return;
            pKingdom.data.set(CompletedKey(pKind), string.Join(";", set.ToArray()));
            KingdomPolicyEffectService.Invalidate(pKingdom);
        }

        private static string GetCompletedRaw(Kingdom pKingdom, PolicyNodeKind pKind)
        {
            pKingdom.data.get(CompletedKey(pKind), out string raw, "");
            return raw ?? "";
        }

        private static string GetDecisionQueueRaw(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.DECISION_QUEUE, out string raw, "");
            return raw ?? "";
        }

        public static long GetCoreFabricationCityId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1L;
            pKingdom.data.get(LineageKeys.CORE_FAB_CURRENT_CITY_ID, out long value, -1L);
            return value;
        }

        public static string GetCoreFabricationCityName(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.CORE_FAB_CURRENT_CITY_NAME, out string value, "");
            return value ?? "";
        }

        public static float GetCoreFabricationProgress(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            pKingdom.data.get(LineageKeys.CORE_FAB_PROGRESS, out float value, 0f);
            return Mathf.Max(0f, value);
        }

        public static float GetCoreFabricationCost(Kingdom pKingdom)
        {
            KingdomPolicyDef def = GetDefinition(pKingdom,
                DecisionQueueRules.FabricateCoreDecisionId);
            return Mathf.Max(1f, def?.Cost ?? 80f);
        }

        public static float GetCoreFabricationProgressFraction(Kingdom pKingdom)
        {
            return Mathf.Clamp01(GetCoreFabricationProgress(pKingdom) /
                                 GetCoreFabricationCost(pKingdom));
        }

        public static bool TryGetCoreFabricationProject(Kingdom pKingdom, long pCityId,
            out float pProgress, out float pCost)
        {
            pProgress = 0f;
            pCost = GetCoreFabricationCost(pKingdom);
            if (pKingdom?.data == null || pCityId < 0) return false;
            if (GetCoreFabricationCityId(pKingdom) != pCityId) return false;
            pProgress = GetCoreFabricationProgress(pKingdom);
            return true;
        }

        public static bool HasCoreFabricationProjectForCity(Kingdom pKingdom, long pCityId)
        {
            if (pKingdom?.data == null || pCityId < 0) return false;
            if (GetCoreFabricationCityId(pKingdom) == pCityId) return true;
            foreach (KingdomDecisionQueueItem item in ReadCoreFabricationQueue(pKingdom))
                if (item.war_target_city_id == pCityId) return true;
            return false;
        }

        public static int CountCoreFabricationProjects(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            int count = GetCoreFabricationCityId(pKingdom) >= 0 ? 1 : 0;
            count += ReadCoreFabricationQueue(pKingdom).Count;
            return count;
        }

        private static string GetCoreFabricationQueueRaw(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.CORE_FAB_QUEUE, out string raw, "");
            return raw ?? "";
        }

        private static List<KingdomDecisionQueueItem> ReadCoreFabricationQueue(Kingdom pKingdom)
        {
            return KingdomDecisionQueueCodec.Decode(GetCoreFabricationQueueRaw(pKingdom));
        }

        private static void WriteCoreFabricationQueue(Kingdom pKingdom, List<KingdomDecisionQueueItem> pItems)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.CORE_FAB_QUEUE, KingdomDecisionQueueCodec.Encode(pItems));
        }

        private static bool StartCoreFabrication(Kingdom pKingdom, City pCity)
        {
            if (pKingdom?.data == null) return false;
            if (IsNodeLocked(pKingdom, DecisionQueueRules.FabricateCoreDecisionId)) return false;
            City city = pCity ?? WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
            if (city?.data == null) return false;

            long currentCityId = GetCoreFabricationCityId(pKingdom);
            if (currentCityId == city.data.id) return true;
            if (IsCoreFabricationQueued(pKingdom, city.data.id)) return true;
            if (!WarTerritoryService.CanFabricateCoreProject(pKingdom, city, out _)) return false;

            if (CoreFabricationSlotRules.ShouldStartWhenEmpty(currentCityId, hasAvailableCoreTarget: true))
            {
                SetCoreFabricationCurrent(pKingdom, city, 0f);
                UpsertSnapshot(pKingdom);
                return true;
            }

            if (CoreFabricationSlotRules.ShouldQueueWhenBusy(currentCityId, hasAvailableCoreTarget: true))
            {
                EnqueueCoreFabrication(pKingdom, city);
                UpsertSnapshot(pKingdom);
                return true;
            }

            return false;
        }

        private static bool IsCoreFabricationQueued(Kingdom pKingdom, long pCityId)
        {
            foreach (KingdomDecisionQueueItem item in ReadCoreFabricationQueue(pKingdom))
                if (item.war_target_city_id == pCityId) return true;
            return false;
        }

        private static void EnqueueCoreFabrication(Kingdom pKingdom, City pCity)
        {
            if (pKingdom?.data == null || pCity?.data == null) return;
            List<KingdomDecisionQueueItem> queue = ReadCoreFabricationQueue(pKingdom);
            if (queue.Count >= KingdomDecisionQueueCodec.MaxQueueSize) return;
            queue.Add(CreateFabricationDecisionItem(DecisionQueueRules.FabricateCoreDecisionId, pKingdom, pCity,
                WarTerritoryService.PROJECT_CORE, "\u5236\u9020\u6838\u5FC3", 0f));
            WriteCoreFabricationQueue(pKingdom, queue);
        }

        private static void SetCoreFabricationCurrent(Kingdom pKingdom, City pCity, float pProgress)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.CORE_FAB_CURRENT_CITY_ID, pCity?.data?.id ?? -1L);
            pKingdom.data.set(LineageKeys.CORE_FAB_CURRENT_CITY_NAME, pCity?.data?.name ?? "");
            pKingdom.data.set(LineageKeys.CORE_FAB_PROGRESS, Mathf.Max(0f, pProgress));
        }

        private static void ClearCoreFabricationCurrent(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.CORE_FAB_CURRENT_CITY_ID, -1L);
            pKingdom.data.set(LineageKeys.CORE_FAB_CURRENT_CITY_NAME, "");
            pKingdom.data.set(LineageKeys.CORE_FAB_PROGRESS, 0f);
        }

        private static void TryStartCoreFabrication(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (IsNodeLocked(pKingdom, DecisionQueueRules.FabricateCoreDecisionId)) return;
            if (GetCoreFabricationCityId(pKingdom) < 0 && StartNextQueuedCoreFabrication(pKingdom))
                return;
            City city = WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
            if (city?.data == null) return;
            StartCoreFabrication(pKingdom, city);
        }

        private static void AdvanceCoreFabrication(Kingdom pKingdom,
            float pElapsedYears)
        {
            if (pKingdom?.data == null) return;
            if (IsNodeLocked(pKingdom, DecisionQueueRules.FabricateCoreDecisionId))
            {
                ClearCoreFabricationCurrent(pKingdom);
                WriteCoreFabricationQueue(pKingdom, new List<KingdomDecisionQueueItem>());
                UpsertSnapshot(pKingdom);
                return;
            }

            long cityId = GetCoreFabricationCityId(pKingdom);
            if (cityId < 0) return;

            City city = FindCity(cityId);
            if (city?.data == null || city.kingdom != pKingdom || !WarTerritoryService.IsOwnedNonCore(pKingdom, city))
            {
                ClearCoreFabricationCurrent(pKingdom);
                StartNextQueuedCoreFabrication(pKingdom);
                UpsertSnapshot(pKingdom);
                return;
            }

            pKingdom.data.get(LineageKeys.POLICY_POINTS, out float points, 0f);
            if (points <= 0f) return;

            float progress = GetCoreFabricationProgress(pKingdom);
            float cost = GetCoreFabricationCost(pKingdom);
            float remaining = cost - progress;
            float spend = PoliticalPointSpendingRules.AutomaticSpend(points,
                Mathf.Min(MAX_YEARLY_SPEND * Mathf.Max(0f,
                    pElapsedYears), remaining));
            if (spend <= 0f) return;

            points -= spend;
            progress += spend;
            pKingdom.data.set(LineageKeys.POLICY_POINTS, points);
            pKingdom.data.set(LineageKeys.CORE_FAB_PROGRESS, progress);

            if (progress + 0.001f < cost) return;
            if (WarTerritoryService.EnsureCore(pKingdom, city, "fabricated", "\u5236\u9020\u6838\u5FC3") < 0)
                return;

            ClearCoreFabricationCurrent(pKingdom);
            StartNextQueuedCoreFabrication(pKingdom);
            UpsertSnapshot(pKingdom);
        }

        private static bool StartNextQueuedCoreFabrication(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || GetCoreFabricationCityId(pKingdom) >= 0) return false;
            if (IsNodeLocked(pKingdom, DecisionQueueRules.FabricateCoreDecisionId)) return false;
            List<KingdomDecisionQueueItem> queue = ReadCoreFabricationQueue(pKingdom);
            while (queue.Count > 0)
            {
                KingdomDecisionQueueItem item = queue[0];
                queue.RemoveAt(0);
                WriteCoreFabricationQueue(pKingdom, queue);
                City city = FindCity(item.war_target_city_id);
                if (city?.data == null || !WarTerritoryService.CanFabricateCoreProject(pKingdom, city, out _))
                    continue;
                SetCoreFabricationCurrent(pKingdom, city, Mathf.Max(0f, item.progress));
                return true;
            }

            WriteCoreFabricationQueue(pKingdom, queue);
            return false;
        }

        private static List<KingdomDecisionQueueItem> ReadDecisionQueue(Kingdom pKingdom)
        {
            return KingdomDecisionQueueCodec.Decode(GetDecisionQueueRaw(pKingdom));
        }

        private static bool HasPendingTargetedDecision(Kingdom pKingdom,
            string pDecisionId, long pTargetKingdomId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pDecisionId) ||
                pTargetKingdomId < 0) return false;

            pKingdom.data.get(LineageKeys.DECISION_TARGET_KINGDOM_ID,
                out long currentTargetId, -1L);
            if (DecisionQueueRules.IsSameTargetedDecision(
                    GetCurrent(pKingdom, PolicyNodeKind.Decision),
                    currentTargetId, pDecisionId, pTargetKingdomId))
                return true;

            foreach (KingdomDecisionQueueItem item in ReadDecisionQueue(pKingdom))
                if (item != null && DecisionQueueRules.IsSameTargetedDecision(
                        item.decision_id, item.target_kingdom_id,
                        pDecisionId, pTargetKingdomId))
                    return true;
            return false;
        }

        private static void WriteDecisionQueue(Kingdom pKingdom, List<KingdomDecisionQueueItem> pItems)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.DECISION_QUEUE, KingdomDecisionQueueCodec.Encode(pItems));
        }

        private static void RemoveQueuedDecision(Kingdom pKingdom, string pDecisionId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pDecisionId)) return;
            List<KingdomDecisionQueueItem> queue = ReadDecisionQueue(pKingdom);
            int before = queue.Count;
            queue.RemoveAll(p => p?.decision_id == pDecisionId);
            if (queue.Count != before) WriteDecisionQueue(pKingdom, queue);
        }

        private static void EnqueueDecisionBack(Kingdom pKingdom, KingdomDecisionQueueItem pItem)
        {
            if (pKingdom?.data == null || pItem == null || string.IsNullOrEmpty(pItem.decision_id)) return;
            List<KingdomDecisionQueueItem> queue = ReadDecisionQueue(pKingdom);
            if (queue.Count >= KingdomDecisionQueueCodec.MaxQueueSize) return;
            queue.Add(pItem);
            WriteDecisionQueue(pKingdom, queue);
        }

        private static void EnqueueDecisionFront(Kingdom pKingdom, KingdomDecisionQueueItem pItem)
        {
            if (pKingdom?.data == null || pItem == null || string.IsNullOrEmpty(pItem.decision_id)) return;
            List<KingdomDecisionQueueItem> queue = ReadDecisionQueue(pKingdom);
            queue.Insert(0, pItem);
            if (queue.Count > KingdomDecisionQueueCodec.MaxQueueSize)
                queue.RemoveAt(queue.Count - 1);
            WriteDecisionQueue(pKingdom, queue);
        }

        private static void EnqueueCurrentDecisionFront(Kingdom pKingdom)
        {
            KingdomDecisionQueueItem current = CaptureCurrentDecision(pKingdom);
            if (current == null) return;
            EnqueueDecisionFront(pKingdom, current);
        }

        private static KingdomDecisionQueueItem CaptureCurrentDecision(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            string current = GetCurrent(pKingdom, PolicyNodeKind.Decision);
            if (string.IsNullOrEmpty(current)) return null;

            var item = CreateSimpleDecisionItem(current, GetProgress(pKingdom, PolicyNodeKind.Decision));
            pKingdom.data.get(LineageKeys.DECISION_TARGET_KINGDOM_ID, out item.target_kingdom_id, -1L);
            pKingdom.data.get(LineageKeys.DECISION_TARGET_KINGDOM_NAME, out item.target_kingdom_name, "");
            pKingdom.data.get(LineageKeys.DECISION_PROJECT_TYPE, out item.project_type, "");
            pKingdom.data.get(LineageKeys.DECISION_WAR_TYPE, out item.war_type, "");
            pKingdom.data.get(LineageKeys.DECISION_WAR_GOAL_TYPE, out item.war_goal_type, "");
            pKingdom.data.get(LineageKeys.DECISION_WAR_REASON_KEY, out item.war_reason_key, "");
            pKingdom.data.get(LineageKeys.DECISION_WAR_REASON_LABEL, out item.war_reason_label, "");
            pKingdom.data.get(LineageKeys.DECISION_WAR_TARGET_CITY_ID, out item.war_target_city_id, -1L);
            pKingdom.data.get(LineageKeys.DECISION_WAR_TARGET_CITY_NAME, out item.war_target_city_name, "");
            pKingdom.data.get(LineageKeys.DECISION_WAR_SOURCE_CLAIM_ID, out item.war_source_claim_id, -1L);
            pKingdom.data.get(LineageKeys.DECISION_WAR_SOURCE_CORE_ID, out item.war_source_core_id, -1L);
            pKingdom.data.get(LineageKeys.DECISION_WAR_RESTORATION_CLAIM_ID, out item.war_restoration_claim_id, -1L);
            pKingdom.data.get(LineageKeys.DECISION_WAR_CLAIMANT_ACTOR_ID, out item.war_claimant_actor_id, -1L);
            pKingdom.data.get(LineageKeys.DECISION_NOTICE_SIGNATURE, out item.notice_signature, "");
            pKingdom.data.get(LineageKeys.DECISION_NOTICE_YEAR, out item.notice_year, -1);
            pKingdom.data.get(LineageKeys.DECISION_NOTICE_EARLIEST_YEAR, out item.earliest_war_year, -1);
            pKingdom.data.get(LineageKeys.DECISION_NOTICE_FORCED_YEAR, out item.forced_war_year, -1);
            pKingdom.data.get(LineageKeys.DECISION_NOTICE_RECORDED, out item.notice_recorded, false);
            return item;
        }

        private static KingdomDecisionQueueItem CreateSimpleDecisionItem(string pDecisionId, float pProgress)
        {
            return new KingdomDecisionQueueItem
            {
                decision_id = pDecisionId ?? "",
                progress = Mathf.Max(0f, pProgress)
            };
        }

        private static KingdomDecisionQueueItem CreateFabricationDecisionItem(string pDecisionId, Kingdom pTarget,
            City pCity, string pProjectType, string pReasonLabel, float pProgress)
        {
            var item = CreateSimpleDecisionItem(pDecisionId, pProgress);
            FillDecisionTarget(item, pTarget);
            item.project_type = pProjectType ?? "";
            item.war_target_city_id = pCity?.data?.id ?? -1L;
            item.war_target_city_name = pCity?.data?.name ?? "";
            item.war_reason_label = pReasonLabel ?? "";
            return item;
        }

        private static void FillDecisionTarget(KingdomDecisionQueueItem pItem, Kingdom pTarget)
        {
            if (pItem == null) return;
            pItem.target_kingdom_id = pTarget?.id ?? -1L;
            pItem.target_kingdom_name = pTarget?.name ?? "";
        }

        private static void TryPromoteCoreDecision(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (GetCurrent(pKingdom, PolicyNodeKind.Decision) == DecisionQueueRules.FabricateCoreDecisionId) return;
            City city = WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
            if (city?.data == null) return;
            StartFabricationDecision(pKingdom, pKingdom, city, WarTerritoryService.PROJECT_CORE);
        }

        private static bool StartNextQueuedDecisionIfEmpty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            if (!string.IsNullOrEmpty(GetCurrent(pKingdom, PolicyNodeKind.Decision))) return false;

            List<KingdomDecisionQueueItem> queue = ReadDecisionQueue(pKingdom);
            while (queue.Count > 0)
            {
                KingdomDecisionQueueItem item = queue[0];
                queue.RemoveAt(0);
                WriteDecisionQueue(pKingdom, queue);
                if (IsNodeLocked(pKingdom, item.decision_id)) continue;
                if (item.decision_id == DecisionQueueRules.FabricateCoreDecisionId)
                {
                    StartCoreFabrication(pKingdom, FindCity(item.war_target_city_id));
                    continue;
                }
                if (!CanStartQueuedDecision(pKingdom, item)) continue;
                ApplyQueuedDecision(pKingdom, item);
                return true;
            }

            WriteDecisionQueue(pKingdom, queue);
            return false;
        }

        private static bool CanStartQueuedDecision(Kingdom pKingdom, KingdomDecisionQueueItem pItem)
        {
            if (pKingdom?.data == null || pItem == null || string.IsNullOrEmpty(pItem.decision_id)) return false;
            KingdomPolicyDef def = GetDefinition(pKingdom,
                pItem.decision_id);
            if (def == null || def.Kind != PolicyNodeKind.Decision || IsCompleted(pKingdom, def)) return false;
            if (IsNodeLocked(pKingdom, def.Id)) return false;

            switch (pItem.decision_id)
            {
                case "aw_decision_absorb_vassal":
                    return VassalService.CanAbsorbVassalByDecision(pKingdom, FindKingdom(pItem.target_kingdom_id), out _);
                case "aw_decision_seek_suzerain":
                    return VassalService.CanSetVassal(pKingdom, FindKingdom(pItem.target_kingdom_id));
                case "aw_decision_fabricate_core":
                    return false;
                case "aw_decision_fabricate_weak_claim":
                case "aw_decision_fabricate_strong_claim":
                    return WarTerritoryService.CanFabricateAgainst(pKingdom, FindKingdom(pItem.target_kingdom_id),
                        FindCity(pItem.war_target_city_id), out _);
                default:
                    return GetStatus(pKingdom, def) == PolicyNodeStatus.Available;
            }
        }

        private static void ApplyQueuedDecision(Kingdom pKingdom, KingdomDecisionQueueItem pItem)
        {
            ClearDecisionTarget(pKingdom);
            pKingdom.data.set(LineageKeys.DECISION_CURRENT, pItem.decision_id ?? "");
            pKingdom.data.set(LineageKeys.DECISION_PROGRESS, Mathf.Max(0f, pItem.progress));
            pKingdom.data.set(LineageKeys.DECISION_TARGET_KINGDOM_ID, pItem.target_kingdom_id);
            pKingdom.data.set(LineageKeys.DECISION_TARGET_KINGDOM_NAME, pItem.target_kingdom_name ?? "");
            pKingdom.data.set(LineageKeys.DECISION_PROJECT_TYPE, pItem.project_type ?? "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_TYPE, pItem.war_type ?? "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_GOAL_TYPE, pItem.war_goal_type ?? "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_REASON_KEY, pItem.war_reason_key ?? "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_REASON_LABEL, pItem.war_reason_label ?? "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_TARGET_CITY_ID, pItem.war_target_city_id);
            pKingdom.data.set(LineageKeys.DECISION_WAR_TARGET_CITY_NAME, pItem.war_target_city_name ?? "");
            pKingdom.data.set(LineageKeys.DECISION_WAR_SOURCE_CLAIM_ID, pItem.war_source_claim_id);
            pKingdom.data.set(LineageKeys.DECISION_WAR_SOURCE_CORE_ID, pItem.war_source_core_id);
            pKingdom.data.set(LineageKeys.DECISION_WAR_RESTORATION_CLAIM_ID, pItem.war_restoration_claim_id);
            pKingdom.data.set(LineageKeys.DECISION_WAR_CLAIMANT_ACTOR_ID, pItem.war_claimant_actor_id);
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_SIGNATURE, pItem.notice_signature ?? "");
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_YEAR, pItem.notice_year);
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_EARLIEST_YEAR, pItem.earliest_war_year);
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_FORCED_YEAR, pItem.forced_war_year);
            pKingdom.data.set(LineageKeys.DECISION_NOTICE_RECORDED, pItem.notice_recorded);
            UpsertSnapshot(pKingdom);
        }

        private static IEnumerable<string> Split(string pRaw)
        {
            if (string.IsNullOrEmpty(pRaw)) return Array.Empty<string>();
            return pRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static KingdomPolicyProfileMigrationState
            SanitizeSnapshotNodes(Kingdom pKingdom,
                KingdomPolicySnapshot pSnapshot)
        {
            KingdomPolicyProfileId profileId = GetPolicyProfile(pKingdom);
            string persistedProfileId = KingdomPolicyProfileRules.
                ToPersistedId(profileId);
            if (KingdomPolicyProfileRules.TryParsePersisted(
                    pSnapshot?.profile_id,
                    out KingdomPolicyProfileId snapshotProfileId) &&
                snapshotProfileId == profileId)
            {
                persistedProfileId = pSnapshot.profile_id;
            }

            var migrationState = new KingdomPolicyProfileMigrationState
            {
                profileId = persistedProfileId,
                migrationVersion = pSnapshot?.migration_version ?? 0,
                currentPolicy = pSnapshot?.current_policy ?? "",
                currentTech = pSnapshot?.current_tech ?? "",
                currentDecision = pSnapshot?.current_decision ?? "",
                completedPolicies = pSnapshot?.completed_policies ?? "",
                completedTechs = pSnapshot?.completed_techs ?? "",
                completedDecisions = pSnapshot?.completed_decisions ?? "",
                lockedNodes = pSnapshot?.locked_nodes ?? "",
                obsoleteNodeIds = pSnapshot?.obsolete_node_ids ?? ""
            };

            return KingdomPolicyProfileMigrationRules.Sanitize(
                migrationState,
                policyAllowed: id => NodeHasKind(profileId, id,
                    PolicyNodeKind.Social),
                techAllowed: id => NodeHasKind(profileId, id,
                    PolicyNodeKind.Tech),
                decisionAllowed: id => NodeHasKind(profileId, id,
                    PolicyNodeKind.Decision));
        }

        private static void MigrateHotPolicyState(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            KingdomPolicyProfileId profileId = GetPolicyProfile(pKingdom);
            if (!KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                    profileId)) return;

            pKingdom.data.get(LineageKeys.POLICY_CURRENT,
                out string currentPolicy, "");
            pKingdom.data.get(LineageKeys.TECH_CURRENT,
                out string currentTech, "");
            pKingdom.data.get(LineageKeys.DECISION_CURRENT,
                out string currentDecision, "");
            pKingdom.data.get(LineageKeys.DECISION_QUEUE,
                out string decisionQueue, "");
            pKingdom.data.get(LineageKeys.POLICY_COMPLETED,
                out string completedPolicies, "");
            pKingdom.data.get(LineageKeys.TECH_COMPLETED,
                out string completedTechs, "");
            pKingdom.data.get(LineageKeys.DECISION_COMPLETED,
                out string completedDecisions, "");
            pKingdom.data.get(LineageKeys.POLICY_LOCKED_NODES,
                out string lockedNodes, "");
            pKingdom.data.get(LineageKeys.POLICY_OBSOLETE_NODE_IDS,
                out string obsoleteNodeIds, "");
            pKingdom.data.get(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                out int royalAuthority, 0);
            pKingdom.data.get(LineageKeys.POLICY_MIGRATION_VERSION,
                out int migrationVersion, 0);

            KingdomPolicyProfileMigrationState migrated =
                KingdomPolicyProfileMigrationRules.Sanitize(
                    new KingdomPolicyProfileMigrationState
                    {
                        profileId = KingdomPolicyProfileRules.ToPersistedId(
                            profileId),
                        migrationVersion = migrationVersion,
                        currentPolicy = currentPolicy,
                        currentTech = currentTech,
                        currentDecision = currentDecision,
                        completedPolicies = completedPolicies,
                        completedTechs = completedTechs,
                        completedDecisions = completedDecisions,
                        lockedNodes = lockedNodes,
                        obsoleteNodeIds = obsoleteNodeIds
                    },
                    policyAllowed: id => NodeHasKind(profileId, id,
                        PolicyNodeKind.Social),
                    techAllowed: id => NodeHasKind(profileId, id,
                        PolicyNodeKind.Tech),
                    decisionAllowed: id => NodeHasKind(profileId, id,
                        PolicyNodeKind.Decision));
            string migratedDecisionQueue = MigrateLegacyDecisionQueue(
                decisionQueue, out bool replacedLegacyDecision);
            if (replacedLegacyDecision)
            {
                migrated.obsoleteNodeIds = KingdomPolicyProfileMigrationRules.
                    AppendObsoleteNodeId(migrated.obsoleteNodeIds,
                        KingdomPolicyProfileMigrationRules.
                            LegacyAppeaseXiaCitiesDecisionId);
            }

            pKingdom.data.set(LineageKeys.POLICY_CURRENT,
                migrated.currentPolicy);
            if (string.IsNullOrEmpty(migrated.currentPolicy))
                pKingdom.data.set(LineageKeys.POLICY_PROGRESS, 0f);
            pKingdom.data.set(LineageKeys.TECH_CURRENT,
                migrated.currentTech);
            if (string.IsNullOrEmpty(migrated.currentTech))
                pKingdom.data.set(LineageKeys.TECH_PROGRESS, 0f);
            pKingdom.data.set(LineageKeys.DECISION_CURRENT,
                migrated.currentDecision);
            if (string.IsNullOrEmpty(migrated.currentDecision))
            {
                pKingdom.data.set(LineageKeys.DECISION_PROGRESS, 0f);
                ClearDecisionTarget(pKingdom);
            }
            pKingdom.data.set(LineageKeys.POLICY_COMPLETED,
                migrated.completedPolicies);
            pKingdom.data.set(LineageKeys.TECH_COMPLETED,
                migrated.completedTechs);
            pKingdom.data.set(LineageKeys.DECISION_COMPLETED,
                migrated.completedDecisions);
            pKingdom.data.set(LineageKeys.DECISION_QUEUE,
                migratedDecisionQueue);
            pKingdom.data.set(LineageKeys.POLICY_LOCKED_NODES,
                migrated.lockedNodes);
            pKingdom.data.set(LineageKeys.POLICY_OBSOLETE_NODE_IDS,
                migrated.obsoleteNodeIds);
            pKingdom.data.set(LineageKeys.POLICY_MIGRATION_VERSION,
                migrated.migrationVersion);
            KingdomPolicyEffectService.Invalidate(pKingdom);
        }

        private static bool NodeHasKind(KingdomPolicyProfileId pProfileId,
            string pNodeId, PolicyNodeKind pKind)
        {
            KingdomPolicyDef definition = KingdomPolicyDefs.Get(pProfileId,
                pNodeId);
            return definition != null && definition.Kind == pKind;
        }

        private static string MigrateLegacyDecisionQueue(string pRaw,
            out bool pReplacedLegacyDecision)
        {
            return KingdomDecisionQueueCodec.MigrateDecisionIds(pRaw,
                KingdomPolicyProfileMigrationRules.MapLegacyDecisionId,
                out pReplacedLegacyDecision);
        }

        private static string NonEmpty(string pValue, string pFallback)
        {
            return string.IsNullOrEmpty(pValue) ? pFallback : pValue;
        }

        private static string CurrentKey(PolicyNodeKind pKind)
        {
            if (pKind == PolicyNodeKind.Tech) return LineageKeys.TECH_CURRENT;
            if (pKind == PolicyNodeKind.Decision) return LineageKeys.DECISION_CURRENT;
            return LineageKeys.POLICY_CURRENT;
        }

        private static string ProgressKey(PolicyNodeKind pKind)
        {
            if (pKind == PolicyNodeKind.Tech) return LineageKeys.TECH_PROGRESS;
            if (pKind == PolicyNodeKind.Decision) return LineageKeys.DECISION_PROGRESS;
            return LineageKeys.POLICY_PROGRESS;
        }

        private static string CompletedKey(PolicyNodeKind pKind)
        {
            if (pKind == PolicyNodeKind.Tech) return LineageKeys.TECH_COMPLETED;
            if (pKind == PolicyNodeKind.Decision) return LineageKeys.DECISION_COMPLETED;
            return LineageKeys.POLICY_COMPLETED;
        }

        private static bool CanPromoteTitle(Kingdom pKingdom)
        {
            return CanPromoteTitle(pKingdom, out _);
        }

        public static bool CanPromoteTitle(Kingdom pKingdom, out string pReason)
        {
            pReason = "";
            if (pKingdom?.data == null)
            {
                pReason = "invalid_kingdom";
                return false;
            }
            if (IsVassalKingdom(pKingdom) && !HasOverlordApprovalForTitleUpgrade(pKingdom))
            {
                pReason = "requires_overlord_approval";
                return false;
            }
            KingdomTitle title = KingdomTitleService.GetTitle(pKingdom);
            if (title >= KingdomTitle.Emperor)
            {
                pReason = "maximum_title";
                return false;
            }
            if (title == KingdomTitle.King &&
                !MandateRitesService.CanPromoteToEmperor(pKingdom, out pReason))
                return false;

            int cities = pKingdom.countCities();
            int zones = pKingdom.countZones();
            bool eligible = KingdomTitleProgressionRules.
                MeetsTerritoryRequirement((int)title, cities, zones);
            if (eligible) return true;
            pReason = "territory_requirement";
            return false;
        }

        private static int YearsSince(Kingdom pKingdom, string pKey, int pFallback)
        {
            pKingdom.data.get(pKey, out int lastYear, pFallback);
            return Date.getCurrentYear() - lastYear;
        }

        private static bool IsVassalKingdom(Kingdom pKingdom)
        {
            return VassalService.IsVassalKingdom(pKingdom);
        }

        private static bool HasOverlordApprovalForTitleUpgrade(Kingdom pKingdom)
        {
            Kingdom suzerain = VassalService.GetSuzerain(pKingdom);
            if (suzerain?.data == null) return false;
            KingdomTitle suzerainTitle = KingdomTitleService.GetTitle(suzerain);
            KingdomTitle vassalTitle = KingdomTitleService.GetTitle(pKingdom);
            return KingdomTitleUpgradeRules.CanVassalUpgradeUnderSuzerain(
                (int)suzerainTitle,
                (int)vassalTitle,
                out _);
        }

        private static City FindNewCapital(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.capital == null) return null;
            try
            {
                if (pKingdom.getWars().Any()) return null;
            }
            catch { }

            City current = pKingdom.capital;
            float currentScore = CapitalScore(current, current, pKingdom);
            City best = null;
            float bestScore = currentScore;
            foreach (City city in pKingdom.getCities())
            {
                if (!CapitalMoveCandidateService.CanConsider(city, pKingdom, current)) continue;
                float score = CapitalScore(city, current, pKingdom);
                if (score <= bestScore) continue;
                best = city;
                bestScore = score;
            }

            return best != null && CapitalMoveRules.ShouldMoveCapital(currentScore, bestScore) ? best : null;
        }

        private static float CapitalScore(City pCity, City pCurrent, Kingdom pKingdom)
        {
            if (pCity?.data == null || !pCity.isAlive()) return 0f;
            int population = SafePopulation(pCity);
            int currentPopulation = SafePopulation(pCurrent);
            int zones = SafeZones(pCity);
            int currentZones = SafeZones(pCurrent);
            float age = SafeAge(pCity);
            float currentAge = SafeAge(pCurrent);
            int ownNeighbors = CountOwnNeighbors(pCity, pKingdom);
            float centrality = CapitalCentralityScore(pCity, pKingdom);
            return CapitalMoveRules.ScoreCity(age, currentAge, population, currentPopulation, zones, currentZones,
                ownNeighbors, centrality);
        }

        private static bool ChangeCapital(Kingdom pKingdom)
        {
            City next = FindNewCapital(pKingdom);
            if (next == null) return false;
            City old = pKingdom.capital;
            TransferCapitalGold(old, next);
            pKingdom.setCapital(next);
            if (pKingdom.king != null && pKingdom.king.city != next)
            {
                if (pKingdom.king.hasArmy())
                    pKingdom.king.removeFromArmy();
                pKingdom.king.joinCity(next);
            }

            string oldName = old?.data?.name ?? "";
            string newName = next.data?.name ?? "";
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.CAPITAL_MOVED,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_capital_moved_from") +
                HistoryText.PlainText(oldName) +
                HistoryLocalizationRules.H("aw_hist_capital_moved_to") +
                HistoryText.PlainText(newName),
                HistoryTarget.Kingdom(pKingdom));
            EraChangeTriggerService.Mark(pKingdom,
                EraChangeReason.CapitalRelocated,
                "capital:" + next.data.id + ":" + Date.getCurrentYear());
            return true;
        }

        private static bool ExecuteFabricationDecision(Kingdom pKingdom, string pProjectType)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.DECISION_WAR_TARGET_CITY_ID, out long cityId, -1L);
            City city = FindCity(cityId);
            if (pProjectType == WarTerritoryService.PROJECT_CORE)
            {
                if (city?.data == null) city = WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
                if (!WarTerritoryService.CanFabricateCoreProject(pKingdom, city, out _)) return false;
                return WarTerritoryService.EnsureCore(pKingdom, city, "fabricated", "\u5236\u9020\u6838\u5fc3") >= 0;
            }

            Kingdom target = GetDecisionTargetKingdom(pKingdom);
            if (target?.data == null) return false;
            if (city?.data == null) city = WarTerritoryService.FindFirstFabricationTargetCity(pKingdom, target);
            if (!WarTerritoryService.CanFabricateAgainst(pKingdom, target, city, out _)) return false;

            bool strong = pProjectType == WarTerritoryService.PROJECT_STRONG_CLAIM;
            WarDecisionService.CreateClaim(pKingdom, target, city,
                strong ? WarTerritoryService.CLAIM_STRONG : WarTerritoryService.CLAIM_WEAK,
                WarDecisionService.WAR_NORMAL,
                strong ? "strong_claim_decision" : "weak_claim_decision",
                strong ? 45 : 20);
            return true;
        }

        private static void TransferCapitalGold(City pOld, City pNext)
        {
            if (pOld?.data == null || pNext?.data == null || pOld == pNext) return;
            int gold = pOld.getResourcesAmount("gold");
            if (gold <= 0) return;

            int transfer = Mathf.FloorToInt(gold * 0.7f);
            if (transfer <= 0) return;
            pOld.takeResource("gold", transfer);
            pNext.addResourcesToRandomStockpile("gold", transfer);
        }

        private static int CountOwnNeighbors(City pCity, Kingdom pKingdom)
        {
            if (pCity?.data == null || pKingdom?.data == null) return 0;
            int count = 0;
            try
            {
                foreach (City other in pCity.neighbours_cities)
                    if (other?.data != null && other.kingdom == pKingdom) count++;
            }
            catch { }
            return count;
        }

        private static float CapitalCentralityScore(City pCity, Kingdom pKingdom)
        {
            if (pCity?.data == null || pKingdom?.data == null) return 0f;
            float distance = 0f;
            int count = 0;
            try
            {
                foreach (City other in pKingdom.getCities())
                {
                    if (other?.data == null || other == pCity) continue;
                    WorldTile a = pCity.getTile();
                    WorldTile b = other.getTile();
                    if (a == null || b == null) continue;
                    distance += Toolbox.DistVec2(a.pos, b.pos);
                    count++;
                }
            }
            catch { }
            return count <= 0 ? 0f : 60f / (1f + distance / count);
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int SafeZones(City pCity)
        {
            try { return pCity?.countZones() ?? 0; }
            catch { return 0; }
        }

        private static float SafeAge(City pCity)
        {
            try { return pCity?.getAge() ?? 0f; }
            catch { return 0f; }
        }

        private static int CountCities(Kingdom pKingdom)
        {
            int count = 0;
            foreach (City city in pKingdom.getCities())
                if (city?.data != null && !city.isRekt()) count++;
            return count;
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom direct = World.world.kingdoms.get(pKingdomId);
                if (direct?.data != null && !direct.isRekt()) return direct;
            }
            catch { }

            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && !kingdom.isRekt() && kingdom.id == pKingdomId)
                    return kingdom;
            return null;
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0 || World.world?.cities == null) return null;
            try
            {
                City direct = World.world.cities.get(pCityId);
                if (direct?.data != null && !direct.isRekt()) return direct;
            }
            catch { }

            foreach (City city in World.world.cities)
                if (city?.data != null && !city.isRekt() && city.data.id == pCityId)
                    return city;
            return null;
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0 || World.world?.units == null) return null;
            try
            {
                Actor direct = World.world.units.get(pActorId);
                if (direct?.data != null && !direct.isRekt()) return direct;
            }
            catch { }

            foreach (Actor actor in World.world.units)
                if (actor?.data != null && !actor.isRekt() && actor.data.id == pActorId)
                    return actor;
            return null;
        }

        private static int CountUnits(Kingdom pKingdom)
        {
            try { return Math.Max(0, pKingdom?.getPopulationTotal() ?? 0); }
            catch { return 0; }
        }

        private static bool IsSupportedPolicyKingdom(Kingdom pKingdom)
        {
            return KingdomPolicyProfileService.TryGet(pKingdom,
                out KingdomPolicyProfileId profileId) &&
                   KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                       profileId);
        }

        private static bool IsHumanKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            if (pKingdom.asset?.id == "human") return true;

            ActorAsset actorAsset = null;
            try { actorAsset = pKingdom.getActorAsset(); }
            catch { actorAsset = null; }
            return actorAsset?.id == "human" || actorAsset?.banner_id == "human";
        }

        private static void UpsertSnapshot(Kingdom pKingdom)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.POLICY_GOVERNMENT_STATE,
                out string governmentState, "default");
            pKingdom.data.get(LineageKeys.POLICY_MIGRATION_VERSION,
                out int migrationVersion,
                KingdomPolicyProfileMigrationRules.CurrentVersion);
            pKingdom.data.get(LineageKeys.POLICY_OBSOLETE_NODE_IDS,
                out string obsoleteNodeIds, "");
            pKingdom.data.get(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                out int royalAuthority, 0);

            string table = KingdomPolicyStateTableItem.GetTableName();
            var values = new[]
            {
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("PROFILE_ID",
                    KingdomPolicyProfileRules.ToPersistedId(
                        GetPolicyProfile(pKingdom))),
                ColumnVal.Create("GOVERNMENT_STATE",
                    NonEmpty(governmentState, "default")),
                ColumnVal.Create("ROYAL_AUTHORITY",
                    Math.Max(0, Math.Min(
                        WesternRoyalAuthorityRules.
                            MaximumConsolidatedAuthority,
                        royalAuthority))),
                ColumnVal.Create("MIGRATION_VERSION", migrationVersion),
                ColumnVal.Create("OBSOLETE_NODE_IDS",
                    obsoleteNodeIds ?? ""),
                ColumnVal.Create("CLASS_STATE", GetClassId(pKingdom)),
                ColumnVal.Create("ARMY_STATE", GetArmyState(pKingdom)),
                ColumnVal.Create("NAME_STATE", GetNameState(pKingdom)),
                ColumnVal.Create("ENFEOFFMENT_STATE", GetEnfeoffmentState(pKingdom)),
                ColumnVal.Create("POLICY_POINTS", (double)GetPoliticalPoints(pKingdom)),
                ColumnVal.Create("TECH_POINTS", (double)GetTechPoints(pKingdom)),
                ColumnVal.Create("CURRENT_POLICY", GetCurrent(pKingdom, PolicyNodeKind.Social)),
                ColumnVal.Create("POLICY_PROGRESS", (double)GetProgress(pKingdom, PolicyNodeKind.Social)),
                ColumnVal.Create("CURRENT_TECH", GetCurrent(pKingdom, PolicyNodeKind.Tech)),
                ColumnVal.Create("TECH_PROGRESS", (double)GetProgress(pKingdom, PolicyNodeKind.Tech)),
                ColumnVal.Create("CURRENT_DECISION", GetCurrent(pKingdom, PolicyNodeKind.Decision)),
                ColumnVal.Create("DECISION_PROGRESS", (double)GetProgress(pKingdom, PolicyNodeKind.Decision)),
                ColumnVal.Create("DECISION_TARGET_KINGDOM_ID",
                    GetDecisionTargetKingdomId(pKingdom)),
                ColumnVal.Create("DECISION_TARGET_KINGDOM_NAME",
                    GetDecisionTargetName(pKingdom)),
                ColumnVal.Create("DECISION_QUEUE", GetDecisionQueueRaw(pKingdom)),
                ColumnVal.Create("CORE_FAB_CURRENT_CITY_ID", GetCoreFabricationCityId(pKingdom)),
                ColumnVal.Create("CORE_FAB_CURRENT_CITY_NAME", GetCoreFabricationCityName(pKingdom)),
                ColumnVal.Create("CORE_FAB_PROGRESS", (double)GetCoreFabricationProgress(pKingdom)),
                ColumnVal.Create("CORE_FAB_QUEUE", GetCoreFabricationQueueRaw(pKingdom)),
                ColumnVal.Create("COMPLETED_POLICIES", GetCompletedRaw(pKingdom, PolicyNodeKind.Social)),
                ColumnVal.Create("COMPLETED_TECHS", GetCompletedRaw(pKingdom, PolicyNodeKind.Tech)),
                ColumnVal.Create("COMPLETED_DECISIONS", GetCompletedRaw(pKingdom, PolicyNodeKind.Decision)),
                ColumnVal.Create("LOCKED_NODES", GetLockedNodesRaw(pKingdom)),
                ColumnVal.Create("UPDATED_TIME", LineageService.CurTime())
            };

            try
            {
                if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id)))
                {
                    db.UpdateValue(table,
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id) },
                        values);
                    return;
                }

                var insert = new List<ColumnVal> { ColumnVal.Create("KINGDOM_ID", pKingdom.id) };
                insert.AddRange(values);
                db.Insert(table, insert.ToArray());
            }
            catch (Exception e)
            {
                ModClass.LogWarning("KingdomPolicyState upsert failed: " + e.Message);
            }
        }
    }
}
