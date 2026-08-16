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
        public int CentralizationTargetLevel;
    }

    internal static class MandateDecisionService
    {
        private static readonly MandateDecisionDef[] _all =
        {
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_centralize_1",
                NameKey = "aw_mandate_decision_centralize_1",
                DescKey = "aw_mandate_decision_centralize_1_desc",
                FallbackName = "Establish Central Administration",
                FallbackDesc = "Build the first level of central administration for the Mandate realm.",
                IconPath = "ui/icons/iconKingdomList",
                Cost = 45f,
                CentralizationTargetLevel = 1
            },
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_centralize_2",
                NameKey = "aw_mandate_decision_centralize_2",
                DescKey = "aw_mandate_decision_centralize_2_desc",
                FallbackName = "Consolidate Central Authority",
                FallbackDesc = "Advance the Mandate realm to the second level of central administration.",
                IconPath = "ui/icons/iconKingdomList",
                Cost = 75f,
                CentralizationTargetLevel = 2
            },
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_centralize_3",
                NameKey = "aw_mandate_decision_centralize_3",
                DescKey = "aw_mandate_decision_centralize_3_desc",
                FallbackName = "Perfect Central Authority",
                FallbackDesc = "Complete the highest level of central administration for the Mandate realm.",
                IconPath = "ui/icons/iconKingdomList",
                Cost = 110f,
                CentralizationTargetLevel = 3
            },
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
                Id = "aw_mandate_decision_great_enfeoffment",
                NameKey = "aw_mandate_decision_great_enfeoffment",
                DescKey = "aw_mandate_decision_great_enfeoffment_desc",
                FallbackName = "Enfeoff the Princes",
                FallbackDesc = "Establish frontier feudatories outside the imperial core while ordinary governors continue to administer their cities.",
                IconPath = "ui/Icons/traits/iconzhuhou",
                Cost = 80f
            },
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_grant_royal_titles",
                NameKey = "aw_mandate_decision_grant_royal_titles",
                DescKey = "aw_mandate_decision_grant_royal_titles_desc",
                FallbackName = "Great Grant of Royal Titles",
                FallbackDesc = "Grant hereditary titular ranks to the royal clan within five degrees without transferring land or military command.",
                IconPath = "ui/Icons/traits/iconzhuhou",
                Cost = 65f
            },
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_favor_order",
                NameKey = "aw_mandate_decision_favor_order",
                DescKey = "aw_mandate_decision_favor_order_desc",
                FallbackName = "Proclaim the Favor Order",
                FallbackDesc = "Raise centralization by one level and reclaim one non-seat city at each future feudatory succession.",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 0f
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
            float cost = GetCost(pKingdom, def);
            if (def == null || cost <= 0f) return 0f;
            return Mathf.Clamp01(GetProgress(pKingdom) / cost);
        }

        public static float GetCost(Kingdom pKingdom,
            MandateDecisionDef pDef)
        {
            if (pDef == null) return 0f;
            if (pDef.Id == "aw_mandate_decision_favor_order")
            {
                CentralizationSnapshot snapshot =
                    CentralizationService.ReadSnapshot(pKingdom);
                return CentralizationRules.ReformCost(
                    snapshot.next_target_level);
            }
            return pDef.Cost;
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
            if (pDef.CentralizationTargetLevel > 0)
                return CentralizationService.CanStartMandateReform(pKingdom,
                    pDef.CentralizationTargetLevel, out _);

            switch (pDef.Id)
            {
                case "aw_mandate_decision_border_defense":
                    {
                        MandateReport report = MandateService.ReadReport();
                        if (!MandateBorderDecisionRules.CanExecute(
                                MandateBorderDecisionUsageService.ReadUses(
                                    report.period_id), report.mandate_value))
                            return false;
                    }
                    if (CityEconomyService.TryGetLatestCachedForeignLandBorder(
                            pKingdom, out bool hasBorder) && !hasBorder)
                        return false;
                    break;
                case "aw_mandate_decision_great_enfeoffment":
                    if (!FeudatorySelectionService.CanExecuteGreatEnfeoffment(pKingdom))
                        return false;
                    break;
                case "aw_mandate_decision_grant_royal_titles":
                    if (!NobleRankService.CanExecuteGreatRoyalGrant(pKingdom))
                        return false;
                    break;
                case "aw_mandate_decision_favor_order":
                    return FeudatoryService.CanEnableFavorOrder(pKingdom,
                        out _);
                default:
                    return false;
            }
            int cooldown = MandateDecisionAiRules.CooldownYears(pDef.Id);
            if (cooldown <= 0) return true;
            pKingdom.data.get(LastSuccessKey(pDef.Id), out int lastSuccess,
                -1);
            return MandateDecisionAiRules.IsCooldownReady(
                Date.getCurrentYear(), lastSuccess, cooldown);
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
            float cost = GetCost(pKingdom, pDef);
            return MandateSacrificeRules.SpendForYear(
                PoliticalPointSpendingRules.AutomaticSpend(
                    KingdomPolicyService.GetPoliticalPoints(pKingdom),
                    KingdomPolicyService.MAX_YEARLY_SPEND),
                cost - progress, KingdomPolicyService.MAX_YEARLY_SPEND);
        }

        private static void AutoSelect(Kingdom pKingdom)
        {
            string preferredId =
                MandateSacrificeService.PreferredAiDecisionId(pKingdom);
            MandateDecisionDef selected = null;
            int selectedScore = int.MinValue;
            foreach (MandateDecisionDef def in _all)
            {
                if (!CanRun(pKingdom, def)) continue;
                int score = MandateDecisionAiRules.Score(def.Id,
                    def.Id == preferredId);
                if (selected != null && score <= selectedScore) continue;
                selected = def;
                selectedScore = score;
            }
            if (selected != null) ForceStart(pKingdom, selected.Id);
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
            if (!CanRun(pKingdom, def))
            {
                ClearCurrent(pKingdom);
                AutoSelect(pKingdom);
                return;
            }
            float cost = GetCost(pKingdom, def);
            if (cost <= 0f) return;

            float progress;
            if (def.SacrificeLevel.HasValue)
            {
                float currentProgress = GetProgress(pKingdom);
                float politicalPoints = KingdomPolicyService.GetPoliticalPoints(pKingdom);
                float spend = MandateSacrificeRules.SpendForYear(
                    PoliticalPointSpendingRules.AutomaticSpend(
                        politicalPoints,
                        KingdomPolicyService.MAX_YEARLY_SPEND),
                    cost - currentProgress,
                    KingdomPolicyService.MAX_YEARLY_SPEND);
                if (spend <= 0f) return;
                progress = Mathf.Min(cost, currentProgress + spend);
                pKingdom.data.set(LineageKeys.POLICY_POINTS, politicalPoints - spend);
            }
            else
            {
                progress = Mathf.Min(cost,
                    GetProgress(pKingdom) + EstimateYearlyGain(pKingdom));
            }
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_PROGRESS, progress);
            if (progress + 0.001f < cost) return;

            bool applied = ApplyEffect(pKingdom, def);
            if (applied &&
                MandateDecisionAiRules.CooldownYears(def.Id) > 0)
                pKingdom.data.set(LastSuccessKey(def.Id),
                    Date.getCurrentYear());
            ClearCurrent(pKingdom);
            if (!applied) AutoSelect(pKingdom);
        }

        private static void ClearCurrent(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_CURRENT, "");
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_PROGRESS, 0f);
        }

        private static string LastSuccessKey(string pDecisionId)
        {
            return LineageKeys.MANDATE_DECISION_LAST_SUCCESS_PREFIX +
                   (pDecisionId ?? "");
        }

        private static bool ApplyEffect(Kingdom pKingdom, MandateDecisionDef pDef)
        {
            if (pDef.SacrificeLevel.HasValue)
                return MandateSacrificeService.Execute(pKingdom, pDef.SacrificeLevel.Value);
            if (pDef.CentralizationTargetLevel > 0)
                return CentralizationService.TryCompleteMandateReform(pKingdom,
                    pDef.CentralizationTargetLevel, out _);

            switch (pDef.Id)
            {
                case "aw_mandate_decision_border_defense":
                    return MandateBorderDefenseService.ExecuteDecision(pKingdom);
                case "aw_mandate_decision_great_enfeoffment":
                    return FeudatorySelectionService.ExecuteGreatEnfeoffment(pKingdom) > 0;
                case "aw_mandate_decision_grant_royal_titles":
                    return NobleRankService.ExecuteGreatRoyalGrant(pKingdom) > 0;
                case "aw_mandate_decision_favor_order":
                    return FeudatoryService.EnableFavorOrder(pKingdom,
                        out _);
                default:
                    return false;
            }
        }
    }
}
