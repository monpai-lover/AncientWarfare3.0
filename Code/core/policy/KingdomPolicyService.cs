using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
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
        public string completed_policies = "";
        public string completed_techs = "";
        public string completed_decisions = "";
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

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return;
            EnsureInitialized(pKingdom);

            int currentYear = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.POLICY_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == currentYear) return;
            pKingdom.data.set(LineageKeys.POLICY_LAST_YEAR, currentYear);

            AddYearlyPoints(pKingdom);
            KingdomPolicyAI.TryFillEmptySlots(pKingdom);
            AdvanceCurrent(pKingdom, PolicyNodeKind.Tech);
            AdvanceCurrent(pKingdom, PolicyNodeKind.Social);
            AdvanceCurrent(pKingdom, PolicyNodeKind.Decision);
            KingdomPolicyAI.TryFillEmptySlots(pKingdom);
            UpsertSnapshot(pKingdom);
            TechMapModeService.DirtyMapIfActive();
        }

        public static bool CanUsePolicySystem(Kingdom pKingdom)
        {
            return IsSupportedPolicyKingdom(pKingdom);
        }

        public static bool IsPolicyEnabledForKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            if (!IsSupportedPolicyKingdom(pKingdom)) return false;

            bool defaultEnabled = LineageService.IsXiaKingdom(pKingdom);
            pKingdom.data.get(LineageKeys.POLICY_ENABLED, out bool enabled, defaultEnabled);
            return enabled;
        }

        public static bool IsPolicyAIEnabled(Kingdom pKingdom)
        {
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            bool defaultEnabled = LineageService.IsXiaKingdom(pKingdom);
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
            pKingdom.data.get(LineageKeys.POLICY_CLASS_STATE, out string classState, "");
            if (string.IsNullOrEmpty(classState))
                pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, KingdomPolicyDefs.ClassDefault);
            EnsureState(pKingdom, LineageKeys.POLICY_ARMY_STATE, KingdomPolicyDefs.ArmyDefault);
            EnsureState(pKingdom, LineageKeys.POLICY_NAME_STATE, KingdomPolicyDefs.NameDefault);
            EnsureState(pKingdom, LineageKeys.POLICY_ENFEOFFMENT_STATE, KingdomPolicyDefs.EnfeoffmentDefault);
            pKingdom.data.get(LineageKeys.POLICY_CURRENT, out string currentPolicy, "");
            pKingdom.data.get(LineageKeys.TECH_CURRENT, out string currentTech, "");
            pKingdom.data.get(LineageKeys.DECISION_CURRENT, out string currentDecision, "");
            if (currentPolicy == null) pKingdom.data.set(LineageKeys.POLICY_CURRENT, "");
            if (currentTech == null) pKingdom.data.set(LineageKeys.TECH_CURRENT, "");
            if (currentDecision == null) pKingdom.data.set(LineageKeys.DECISION_CURRENT, "");
        }

        public static bool StartResearch(Kingdom pKingdom, string pNodeId)
        {
            if (pKingdom?.data == null) return false;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            KingdomPolicyDef def = KingdomPolicyDefs.Get(pNodeId);
            if (def == null) return false;
            EnsureInitialized(pKingdom);
            if (GetStatus(pKingdom, def) != PolicyNodeStatus.Available) return false;

            string currentKey = CurrentKey(def.Kind);
            string progressKey = ProgressKey(def.Kind);
            pKingdom.data.set(currentKey, def.Id);
            pKingdom.data.set(progressKey, 0f);
            UpsertSnapshot(pKingdom);
            return true;
        }

        public static bool ForceStartResearch(Kingdom pKingdom, string pNodeId)
        {
            if (pKingdom?.data == null) return false;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            KingdomPolicyDef def = KingdomPolicyDefs.Get(pNodeId);
            if (def == null || IsCompleted(pKingdom, def)) return false;
            EnsureInitialized(pKingdom);
            if (GetCurrent(pKingdom, def.Kind) == def.Id) return false;

            pKingdom.data.set(CurrentKey(def.Kind), def.Id);
            pKingdom.data.set(ProgressKey(def.Kind), 0f);
            UpsertSnapshot(pKingdom);
            return true;
        }

        public static bool ForceSetClassState(Kingdom pKingdom, string pClassId)
        {
            if (pKingdom?.data == null || !KingdomPolicyDefs.ClassStates.Contains(pClassId)) return false;
            if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
            EnsureInitialized(pKingdom);
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, pClassId);
            ApplyClassStateEffects(pKingdom, pClassId);
            UpsertSnapshot(pKingdom);
            return true;
        }

        public static PolicyNodeStatus GetStatus(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return PolicyNodeStatus.Locked;
            if (IsCompleted(pKingdom, pDef)) return PolicyNodeStatus.Completed;
            if (GetCurrent(pKingdom, pDef.Kind) == pDef.Id) return PolicyNodeStatus.Current;
            return AreRequirementsMet(pKingdom, pDef) ? PolicyNodeStatus.Available : PolicyNodeStatus.Locked;
        }

        public static string GetClassId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return KingdomPolicyDefs.ClassDefault;
            EnsureInitialized(pKingdom);
            pKingdom.data.get(LineageKeys.POLICY_CLASS_STATE, out string value, KingdomPolicyDefs.ClassDefault);
            return string.IsNullOrEmpty(value) ? KingdomPolicyDefs.ClassDefault : value;
        }

        public static string GetClassFallbackName(string pClassId)
        {
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
                   IsCompleted(pKingdom, PolicyNodeKind.Social, "aw_policy_base_enfeoffment");
        }

        public static float GetPoliticalPoints(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            pKingdom.data.get(LineageKeys.POLICY_POINTS, out float value, 0f);
            return value;
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
            snapshot.completed_policies = GetCompletedRaw(pKingdom, PolicyNodeKind.Social);
            snapshot.completed_techs = GetCompletedRaw(pKingdom, PolicyNodeKind.Tech);
            snapshot.completed_decisions = GetCompletedRaw(pKingdom, PolicyNodeKind.Decision);
            return snapshot;
        }

        public static void ApplySnapshot(Kingdom pKingdom, KingdomPolicySnapshot pSnapshot, bool pIncludeDecision)
        {
            if (pKingdom?.data == null || pSnapshot == null) return;
            EnsureInitialized(pKingdom);

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
            pKingdom.data.set(LineageKeys.POLICY_CURRENT, pSnapshot.current_policy ?? "");
            pKingdom.data.set(LineageKeys.POLICY_PROGRESS, Mathf.Max(0f, pSnapshot.policy_progress));
            pKingdom.data.set(LineageKeys.TECH_CURRENT, pSnapshot.current_tech ?? "");
            pKingdom.data.set(LineageKeys.TECH_PROGRESS, Mathf.Max(0f, pSnapshot.tech_progress));
            pKingdom.data.set(LineageKeys.POLICY_COMPLETED, pSnapshot.completed_policies ?? "");
            pKingdom.data.set(LineageKeys.TECH_COMPLETED, pSnapshot.completed_techs ?? "");

            if (pIncludeDecision)
            {
                pKingdom.data.set(LineageKeys.DECISION_CURRENT, pSnapshot.current_decision ?? "");
                pKingdom.data.set(LineageKeys.DECISION_PROGRESS, Mathf.Max(0f, pSnapshot.decision_progress));
                pKingdom.data.set(LineageKeys.DECISION_COMPLETED, pSnapshot.completed_decisions ?? "");
            }

            UpsertSnapshot(pKingdom);
        }

        public static TechLevelReport GetTechLevelReport(Kingdom pKingdom)
        {
            var report = new TechLevelReport();
            report.total_count = KingdomPolicyDefs.Techs.Count();
            report.max_level = 5;

            float maxScore = 0f;
            float score = 0f;
            int completed = 0;
            foreach (KingdomPolicyDef tech in KingdomPolicyDefs.Techs)
            {
                float cost = Mathf.Max(1f, tech.Cost);
                maxScore += cost;
                if (!IsCompleted(pKingdom, tech)) continue;
                completed++;
                score += cost;
            }

            KingdomPolicyDef current = KingdomPolicyDefs.Get(GetCurrent(pKingdom, PolicyNodeKind.Tech));
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

        public static string GetCurrentSummary(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            var parts = new List<string>();
            KingdomPolicyDef tech = KingdomPolicyDefs.Get(GetCurrent(pKingdom, PolicyNodeKind.Tech));
            KingdomPolicyDef social = KingdomPolicyDefs.Get(GetCurrent(pKingdom, PolicyNodeKind.Social));
            if (tech != null) parts.Add(tech.FallbackName);
            if (social != null) parts.Add(social.FallbackName);
            return parts.Count == 0 ? "待定" : string.Join("/", parts.ToArray());
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
                if (!IsCompleted(pKingdom, PolicyNodeKind.Tech, id))
                    yield return id;
            foreach (string id in pDef.RequiredPolicies ?? Array.Empty<string>())
                if (!IsCompleted(pKingdom, PolicyNodeKind.Social, id))
                    yield return id;
        }

        private static void AddYearlyPoints(Kingdom pKingdom)
        {
            float political = GetPoliticalPoints(pKingdom);
            float tech = GetTechPoints(pKingdom);
            pKingdom.data.set(LineageKeys.POLICY_POINTS,
                Mathf.Clamp(political + CalcPoliticalGain(pKingdom), 0f, MAX_POINTS));
            pKingdom.data.set(LineageKeys.TECH_POINTS,
                Mathf.Clamp(tech + CalcTechGain(pKingdom), 0f, MAX_POINTS));
        }

        private static float CalcPoliticalGain(Kingdom pKingdom)
        {
            float king = 0f;
            if (pKingdom.hasKing() && pKingdom.king?.stats != null)
                king = pKingdom.king.stats["stewardship"] * 0.05f + pKingdom.king.stats["diplomacy"] * 0.02f;
            return Mathf.Clamp(2f + king + CountCities(pKingdom) * 0.35f + CountUnits(pKingdom) * 0.01f, 1f, 18f);
        }

        private static float CalcTechGain(Kingdom pKingdom)
        {
            float king = 0f;
            if (pKingdom.hasKing() && pKingdom.king?.stats != null)
                king = pKingdom.king.stats["intelligence"] * 0.06f + pKingdom.king.stats["stewardship"] * 0.015f;
            return Mathf.Clamp(1.5f + king + CountCities(pKingdom) * 0.25f + CountUnits(pKingdom) * 0.006f, 1f, 16f);
        }

        private static void AdvanceCurrent(Kingdom pKingdom, PolicyNodeKind pKind)
        {
            string current = GetCurrent(pKingdom, pKind);
            if (string.IsNullOrEmpty(current)) return;
            KingdomPolicyDef def = KingdomPolicyDefs.Get(current);
            if (def == null)
            {
                pKingdom.data.set(CurrentKey(pKind), "");
                pKingdom.data.set(ProgressKey(pKind), 0f);
                return;
            }

            string pointKey = pKind == PolicyNodeKind.Tech ? LineageKeys.TECH_POINTS : LineageKeys.POLICY_POINTS;
            pKingdom.data.get(pointKey, out float points, 0f);
            if (points <= 0f) return;

            float progress = GetProgress(pKingdom, pKind);
            float spend = Mathf.Min(points, Mathf.Min(MAX_YEARLY_SPEND, def.Cost - progress));
            if (spend <= 0f) return;

            points -= spend;
            progress += spend;
            pKingdom.data.set(pointKey, points);
            pKingdom.data.set(ProgressKey(pKind), progress);

            if (progress + 0.001f >= def.Cost)
                Complete(pKingdom, def);
        }

        private static void Complete(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            AddCompleted(pKingdom, pDef.Kind, pDef.Id);
            pKingdom.data.set(CurrentKey(pDef.Kind), "");
            pKingdom.data.set(ProgressKey(pDef.Kind), 0f);
            if (!string.IsNullOrEmpty(pDef.ClassAfter))
            {
                pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, pDef.ClassAfter);
                ApplyClassStateEffects(pKingdom, pDef.ClassAfter);
            }
            ApplyPolicyStateEffects(pKingdom, pDef);

            bool effectApplied = ApplyEffect(pKingdom, pDef);
            if (effectApplied && ShouldRecordGenericCompletion(pDef))
                RecordCompletion(pKingdom, pDef);
            UpsertSnapshot(pKingdom);
        }

        private static bool ApplyEffect(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return false;
            switch (pDef.Id)
            {
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
                    YearNameService.ChangeYearName(pKingdom);
                    return true;
                case "aw_decision_title_upgrade":
                    if (!CanPromoteTitle(pKingdom)) return false;
                    KingdomTitleService.PromoteTitle(pKingdom);
                    YearNameService.ChangeYearName(pKingdom);
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
                default:
                    return true;
            }
        }

        private static bool ShouldRecordGenericCompletion(KingdomPolicyDef pDef)
        {
            if (pDef?.Kind != PolicyNodeKind.Decision) return true;
            return pDef.Id != "aw_decision_royal_expansion" &&
                   pDef.Id != "aw_decision_change_capital";
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
        }

        private static void ApplyClassStateEffects(Kingdom pKingdom, string pClassId)
        {
            switch (pClassId)
            {
                case KingdomPolicyDefs.ClassSlaveOwner:
                    SlaveService.SetSlaveryEnabled(pKingdom, true);
                    break;
                case KingdomPolicyDefs.ClassDefault:
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
        }

        private static void RecordCompletion(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return;
            string kind = pDef.Kind == PolicyNodeKind.Tech
                ? "\u79D1\u6280"
                : pDef.Kind == PolicyNodeKind.Decision
                    ? "\u51B3\u7B56"
                    : "\u56FD\u7B56";
            string eventType = pDef.Kind == PolicyNodeKind.Tech
                ? KingdomEvent.TECH_COMPLETED
                : KingdomEvent.POLICY_COMPLETED;
            HistoryWriter.RecordKingdom(pKingdom, eventType,
                HistoryText.Kingdom(pKingdom) + " 完成" + kind + " " + HistoryText.PlainText(pDef.FallbackName),
                HistoryTarget.Kingdom(pKingdom));

            Actor king = pKingdom.hasKing() ? pKingdom.king : null;
            if (ChronicleGate.IsNobleActor(king))
                HistoryWriter.RecordPerson(king.data.id, pKingdom, king.getName(), eventType,
                    HistoryText.Actor(king) + " 主持完成" + kind + " " + HistoryText.PlainText(pDef.FallbackName),
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
                    return pKingdom.hasKing();
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
                default:
                    return true;
            }
        }

        private static void AddCompleted(Kingdom pKingdom, PolicyNodeKind pKind, string pId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pId)) return;
            var set = new HashSet<string>(Split(GetCompletedRaw(pKingdom, pKind)));
            if (!set.Add(pId)) return;
            pKingdom.data.set(CompletedKey(pKind), string.Join(";", set.ToArray()));
        }

        private static string GetCompletedRaw(Kingdom pKingdom, PolicyNodeKind pKind)
        {
            pKingdom.data.get(CompletedKey(pKind), out string raw, "");
            return raw ?? "";
        }

        private static IEnumerable<string> Split(string pRaw)
        {
            if (string.IsNullOrEmpty(pRaw)) return Array.Empty<string>();
            return pRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
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
            if (pKingdom?.data == null) return false;
            if (IsVassalKingdom(pKingdom) && !HasOverlordApprovalForTitleUpgrade(pKingdom)) return false;
            KingdomTitle title = KingdomTitleService.GetTitle(pKingdom);
            if (title >= KingdomTitle.Emperor) return false;

            int cities = pKingdom.countCities();
            int zones = pKingdom.countZones();
            switch (title)
            {
                case KingdomTitle.Baron:
                    return cities >= 2 || zones > 300;
                case KingdomTitle.Marquis:
                    return cities >= 4 || zones > 800;
                case KingdomTitle.Duke:
                    return cities >= 6 || zones > 1300;
                case KingdomTitle.King:
                    return cities >= 10 || zones > 2000;
                default:
                    return false;
            }
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
            return suzerainTitle > vassalTitle;
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
            float currentScore = CapitalScore(current);
            City best = null;
            float bestScore = currentScore;
            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || !city.isAlive() || city == current) continue;
                float score = CapitalScore(city);
                if (score <= bestScore) continue;
                best = city;
                bestScore = score;
            }

            return best;
        }

        private static float CapitalScore(City pCity)
        {
            if (pCity?.data == null || !pCity.isAlive()) return 0f;
            return pCity.countZones() * 2f + pCity.getPopulationPeople() + pCity.countBuildings() * 0.5f;
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
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.POLICY_COMPLETED,
                HistoryText.Kingdom(pKingdom) + " \u8FC1\u90FD\uFF0C\u7531" +
                HistoryText.PlainText(oldName) + "\u8FC1\u5F80" + HistoryText.PlainText(newName),
                HistoryTarget.Kingdom(pKingdom));
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

        private static int CountCities(Kingdom pKingdom)
        {
            int count = 0;
            foreach (City city in pKingdom.getCities())
                if (city?.data != null && !city.isRekt()) count++;
            return count;
        }

        private static int CountUnits(Kingdom pKingdom)
        {
            int count = 0;
            foreach (Actor unit in pKingdom.getUnits())
                if (unit?.data != null && !unit.isRekt()) count++;
            return count;
        }

        private static bool IsSupportedPolicyKingdom(Kingdom pKingdom)
        {
            return LineageService.IsXiaKingdom(pKingdom) || IsHumanKingdom(pKingdom);
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

            string table = KingdomPolicyStateTableItem.GetTableName();
            var values = new[]
            {
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
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
                ColumnVal.Create("COMPLETED_POLICIES", GetCompletedRaw(pKingdom, PolicyNodeKind.Social)),
                ColumnVal.Create("COMPLETED_TECHS", GetCompletedRaw(pKingdom, PolicyNodeKind.Tech)),
                ColumnVal.Create("COMPLETED_DECISIONS", GetCompletedRaw(pKingdom, PolicyNodeKind.Decision)),
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
