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
                Id = "aw_mandate_decision_ritual",
                NameKey = "aw_mandate_decision_ritual",
                DescKey = "aw_mandate_decision_ritual_desc",
                FallbackName = "\u796D\u5929\u6574\u987F",
                FallbackDesc = "\u7531\u5929\u5B50\u4E3B\u6301\u796D\u5929\u548C\u671D\u5EF7\u6574\u987F\uFF0C\u5C0F\u5E45\u6062\u590D\u5929\u547D\u3001\u7687\u6743\u4E0E\u738B\u671D\u5A01\u671B\u3002",
                IconPath = "ui/Icons/traits/iconTianming",
                Cost = 55f
            },
            new MandateDecisionDef
            {
                Id = "aw_mandate_decision_year_name",
                NameKey = "aw_mandate_decision_year_name",
                DescKey = "aw_mandate_decision_year_name_desc",
                FallbackName = "\u6539\u5143",
                FallbackDesc = "\u5728\u5927\u4E8B\u540E\u9881\u5E03\u65B0\u5E74\u53F7\uFF0C\u5E76\u5199\u5165\u5929\u547D\u53F2\u548C\u56FD\u53F2\u3002",
                IconPath = "ui/policy/change_name",
                Cost = 25f
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

            switch (pDef.Id)
            {
                case "aw_mandate_decision_ritual":
                    return MandateService.CanStabilizeMandate(pKingdom);
                case "aw_mandate_decision_year_name":
                    return true;
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

        private static void AutoSelect(Kingdom pKingdom)
        {
            foreach (MandateDecisionDef def in _all)
            {
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

            float progress = Mathf.Min(def.Cost, GetProgress(pKingdom) + EstimateYearlyGain(pKingdom));
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_PROGRESS, progress);
            if (progress + 0.001f < def.Cost) return;

            bool applied = ApplyEffect(pKingdom, def);
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_CURRENT, "");
            pKingdom.data.set(LineageKeys.MANDATE_DECISION_PROGRESS, 0f);
            if (!applied) AutoSelect(pKingdom);
        }

        private static bool ApplyEffect(Kingdom pKingdom, MandateDecisionDef pDef)
        {
            switch (pDef.Id)
            {
                case "aw_mandate_decision_border_defense":
                    return MandateBorderDefenseService.ExecuteDecision(pKingdom);
                case "aw_mandate_decision_ritual":
                    return MandateService.ApplySacrificeOutcome(pKingdom,
                        new MandateSacrificeEffects(8, 4, 3, 0),
                        "mandate_ritual");
                case "aw_mandate_decision_year_name":
                    YearNameService.ChangeYearName(pKingdom);
                    MandateReport report = MandateService.ReadReport();
                    MandateService.RecordMandateEvent("mandate_year_name", pKingdom, pKingdom.king, null,
                        0, report.mandate_value, (pKingdom.name ?? "") + " \u6539\u5143");
                    return true;
                default:
                    return false;
            }
        }
    }
}
