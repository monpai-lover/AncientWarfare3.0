using System.Collections.Generic;
using AncientWarfare3.core.policy;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal sealed class MandateDecisionDef
    {
        public string Id;
        public string NameKey;
        public string DescKey;
        public string FallbackName;
        public string FallbackDesc;
        public string IconPath;
        public float Cost;
        public MandateSacrificeLevel? SacrificeLevel;
    }

    internal static class MandateDecisionService
    {
        private static readonly MandateDecisionDef[] _all =
        {
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_border_defense",
                NameKey = "aw_mandate_decision_border_defense",
                DescKey = "aw_mandate_decision_border_defense_desc",
                FallbackName = "\u6574\u5907\u8FB9\u9632",
                FallbackDesc = "\u4EE5\u5929\u671D\u51B3\u8BAE\u6574\u5907\u6CD5\u7406\u8FB9\u754C\uFF0C\u4FEE\u7B51\u8FB9\u5899\u5E76\u5728\u5175\u529B\u5145\u8DB3\u65F6\u62BD\u8C03\u8FB9\u519B\u3002",
                IconPath = "ui/icons/iconArmor",
                Cost = 70f
            },
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_sacrifice_gamble",
                NameKey = "aw_mandate_decision_sacrifice_gamble",
                DescKey = "aw_mandate_decision_sacrifice_gamble_desc",
                FallbackName = "\u8D4C\u5927\u658B\u6212",
                FallbackDesc = "A high-risk grand sacrifice with the greatest upside and a chance of an ominous result.",
                IconPath = "ui/Icons/traits/iconTianming",
                Cost = MandateSacrificeService.GetCost(MandateSacrificeLevel.Gamble),
                SacrificeLevel = MandateSacrificeLevel.Gamble
            },
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_sacrifice_moderate",
                NameKey = "aw_mandate_decision_sacrifice_moderate",
                DescKey = "aw_mandate_decision_sacrifice_moderate_desc",
                FallbackName = "\u4E2D\u5EB8\u658B\u6212",
                FallbackDesc = "A lower-cost grand sacrifice with restrained gains and manageable risk.",
                IconPath = "ui/Icons/traits/iconTianming",
                Cost = MandateSacrificeService.GetCost(MandateSacrificeLevel.Moderate),
                SacrificeLevel = MandateSacrificeLevel.Moderate
            },
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_sacrifice_conservative",
                NameKey = "aw_mandate_decision_sacrifice_conservative",
                DescKey = "aw_mandate_decision_sacrifice_conservative_desc",
                FallbackName = "\u4FDD\u5B88\u658B\u6212",
                FallbackDesc = "A costly and cautious grand sacrifice that cannot produce an ominous result.",
                IconPath = "ui/Icons/traits/iconTianming",
                Cost = MandateSacrificeService.GetCost(MandateSacrificeLevel.Conservative),
                SacrificeLevel = MandateSacrificeLevel.Conservative
            }
        };

        public static IReadOnlyList<MandateDecisionDef> All => _all;

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate?.data == null || pKingdom?.data == null || mandate != pKingdom) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.MANDATE_DECISION_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_LAST_YEAR, year);

            if (string.IsNullOrEmpty(GetCurrent(pKingdom)))
                AutoSelect(pKingdom);
            AdvanceCurrent(pKingdom);
        }

        public static MandateDecisionDef Get(string pId)
        {
            if (string.IsNullOrEmpty(pId)) return null;
            foreach (MandateDecisionDef def in _all)
                if (def.Id == pId) return def;
            return null;
        }

        public static string GetCurrent(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.MANDATE_DECISION_CURRENT, out string value, "");
            return value ?? "";
        }

        public static MandateDecisionDef GetCurrentDef(Kingdom pKingdom)
        {
            return Get(GetCurrent(pKingdom));
        }

        public static float GetProgress(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            pKingdom.data.get(LineageKeys.MANDATE_DECISION_PROGRESS, out float value, 0f);
            return value;
        }

        public static float GetProgressFraction(Kingdom pKingdom)
        {
            MandateDecisionDef def = GetCurrentDef(pKingdom);
            if (def == null || def.Cost <= 0f) return 0f;
            return Mathf.Clamp01(GetProgress(pKingdom) / def.Cost);
        }

        public static bool ForceStart(Kingdom pKingdom, string pId)
        {
            MandateDecisionDef def = Get(pId);
            if (def == null || !CanRun(pKingdom, def)) return false;
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_CURRENT, def.Id);
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_PROGRESS, 0f);
            return true;
        }

        public static bool CycleCurrent(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            string current = GetCurrent(pKingdom);
            int start = -1;
            for (int i = 0; i < _all.Length; i++)
                if (_all[i].Id == current) start = i;

            for (int step = 1; step <= _all.Length; step++)
            {
                MandateDecisionDef next = _all[(start + step + _all.Length) % _all.Length];
                if (!CanRun(pKingdom, next)) continue;
                return ForceStart(pKingdom, next.Id);
            }

            pKingdom.data.set(LineageKeys.MANDATE_DECISION_CURRENT, "");
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_PROGRESS, 0f);
            return false;
        }

        public static bool CanRun(Kingdom pKingdom, MandateDecisionDef pDef)
        {
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (pKingdom?.data == null || pDef == null || mandate != pKingdom) return false;
            if (!pKingdom.hasKing()) return false;
            if (pDef.SacrificeLevel.HasValue)
                return MandateSacrificeService.CanExecute(pKingdom);

            switch (pDef.Id)
            {
                case "aw_mandate_decision_border_defense":
                    return true;
                default:
                    return false;
            }
        }

        public static float EstimateYearlyGain(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            MandateReport report = MandateService.ReadReport();
            float baseGain = 2.5f + KingdomPolicyService.GetPoliticalPointGain(pKingdom) * 0.75f;
            baseGain += Mathf.Clamp(report.imperial_authority, 0, 100) * 0.025f;
            baseGain += Mathf.Clamp(report.mandate_value, -30, 100) * 0.012f;
            return Mathf.Clamp(baseGain, 1f, KingdomPolicyService.MAX_YEARLY_SPEND);
        }

        public static float EstimateYearlyGain(Kingdom pKingdom, MandateDecisionDef pDef)
        {
            if (pDef?.SacrificeLevel == null) return EstimateYearlyGain(pKingdom);
            float progress = GetCurrent(pKingdom) == pDef.Id
                ? GetProgress(pKingdom)
                : 0f;
            return MandateSacrificeRules.SpendForYear(
                KingdomPolicyService.GetPoliticalPoints(pKingdom),
                pDef.Cost - progress, KingdomPolicyService.MAX_YEARLY_SPEND);
        }

        private static void AutoSelect(Kingdom pKingdom)
        {
            string preferredId =
                MandateSacrificeService.PreferredAiDecisionId(pKingdom);
            MandateDecisionDef preferred = Get(preferredId);
            if (preferred != null && CanRun(pKingdom, preferred))
            {
                ForceStart(pKingdom, preferred.Id);
                return;
            }

            foreach (MandateDecisionDef def in _all)
            {
                if (def.SacrificeLevel.HasValue) continue;
                if (!CanRun(pKingdom, def)) continue;
                ForceStart(pKingdom, def.Id);
                return;
            }
        }

        private static void AdvanceCurrent(Kingdom pKingdom)
        {
            MandateDecisionDef def = GetCurrentDef(pKingdom);
            if (def == null)
            {
                pKingdom.data.set(LineageKeys.MANDATE_DECISION_CURRENT, "");
                pKingdom.data.set(LineageKeys.MANDATE_DECISION_PROGRESS, 0f);
                return;
            }
            if (!CanRun(pKingdom, def)) return;

            float progress;
            if (def.SacrificeLevel.HasValue)
            {
                float currentProgress = GetProgress(pKingdom);
                float politicalPoints = KingdomPolicyService.GetPoliticalPoints(pKingdom);
                float spend = MandateSacrificeRules.SpendForYear(
                    politicalPoints, def.Cost - currentProgress,
                    KingdomPolicyService.MAX_YEARLY_SPEND);
                if (spend <= 0f) return;
                progress = Mathf.Min(def.Cost, currentProgress + spend);
                pKingdom.data.set(LineageKeys.POLICY_POINTS, politicalPoints - spend);
            }
            else
            {
                progress = Mathf.Min(def.Cost,
                    GetProgress(pKingdom) + EstimateYearlyGain(pKingdom));
            }
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_PROGRESS, progress);
            if (progress + 0.001f < def.Cost) return;

            bool applied = ApplyEffect(pKingdom, def);
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_CURRENT, "");
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_PROGRESS, 0f);
            if (!applied) AutoSelect(pKingdom);
        }

        private static bool ApplyEffect(Kingdom pKingdom, MandateDecisionDef pDef)
        {
            if (pDef.SacrificeLevel.HasValue)
                return MandateSacrificeService.Execute(pKingdom, pDef.SacrificeLevel.Value);

            switch (pDef.Id)
            {
                case "aw_mandate_decision_border_defense":
                    return MandateBorderDefenseService.ExecuteDecision(pKingdom);
                default:
                    return false;
            }
        }
    }
}
