using System;
using System.Data.SQLite;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateSacrificeService
    {
        private const int MinimumQualifiedIntelligence = 12;

        private static readonly System.Random _random = new System.Random();

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance.InitializeSuccessful;

        public static bool CanExecute(Kingdom pKingdom)
        {
            return CanExecute(pKingdom, out _);
        }

        public static bool CanExecute(Kingdom pKingdom, out string pReason)
        {
            pReason = "";
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                !pKingdom.isCiv() || pKingdom.isNeutral())
            {
                pReason = "invalid";
                return false;
            }

            if (!MandateService.IsMandateKingdom(pKingdom))
            {
                pReason = "not_mandate";
                return false;
            }

            Actor king = pKingdom.king;
            if (!pKingdom.hasKing() || king?.data == null ||
                !king.isAlive() || king.isRekt())
            {
                pReason = "no_king";
                return false;
            }

            try
            {
                if (pKingdom.hasEnemies())
                {
                    pReason = "at_war";
                    return false;
                }
            }
            catch
            {
                pReason = "war_state";
                return false;
            }

            if (!KingdomPolicyService.IsCompleted(pKingdom,
                    PolicyNodeKind.Social, "aw_policy_mandate_rites"))
            {
                pReason = "missing_mandate_rites";
                return false;
            }

            if (!KingdomPolicyService.IsCompleted(pKingdom,
                    PolicyNodeKind.Tech, "aw_tech_rites_music"))
            {
                pReason = "missing_rites_music";
                return false;
            }

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.MANDATE_SACRIFICE_LAST_YEAR,
                out int lastYear, int.MinValue);
            if (!MandateSacrificeRules.CooldownReady(year, lastYear))
            {
                pReason = "cooldown";
                return false;
            }

            return true;
        }

        public static bool IsQualified(Kingdom pKingdom)
        {
            Actor king = pKingdom?.king;
            if (king?.data == null || !king.isAlive() || king.isRekt()) return false;
            try
            {
                if (king.stats["intelligence"] >= MinimumQualifiedIntelligence)
                    return true;
            }
            catch { }

            City capital = pKingdom.capital;
            if (capital?.data == null || capital.isRekt() || capital.buildings == null)
                return false;
            try
            {
                foreach (Building building in capital.buildings)
                {
                    if (building?.asset == null ||
                        !building.asset.id.StartsWith("temple_", StringComparison.Ordinal))
                        continue;
                    if (building.isUsable() && !building.isAbandoned() &&
                        !building.isUnderConstruction()) return true;
                }
            }
            catch { }
            return false;
        }

        public static bool Execute(Kingdom pKingdom, MandateSacrificeLevel pLevel)
        {
            if (!CanExecute(pKingdom)) return false;

            int year = Date.getCurrentYear();
            bool qualified = IsQualified(pKingdom);
            int roll = _random.Next(10000);
            MandateSacrificeOutcome outcome = MandateSacrificeRules.ResolveOutcome(
                pLevel, qualified, roll);
            MandateSacrificeEffects effects = MandateSacrificeRules.Effects(
                pLevel, outcome);
            string eventType = EventType(outcome);
            string historyContent = BuildHistoryContent(
                pKingdom, pLevel, qualified, outcome, effects);

            if (!MandateService.ApplySacrificeOutcome(
                    pKingdom, effects, eventType, historyContent)) return false;

            pKingdom.data.get(LineageKeys.MANDATE_RITUAL_COMPLETENESS,
                out int ritualCompleteness, 0);
            if (outcome == MandateSacrificeOutcome.Auspicious)
                ritualCompleteness = Math.Min(10, Math.Max(0, ritualCompleteness) + 1);
            else
                ritualCompleteness = Math.Max(0, Math.Min(10, ritualCompleteness));

            int buffUntilYear = year + MandateSacrificeRules.BuffYears;
            pKingdom.data.set(LineageKeys.MANDATE_SACRIFICE_LAST_YEAR, year);
            pKingdom.data.set(LineageKeys.MANDATE_SACRIFICE_BUFF_UNTIL,
                buffUntilYear);
            pKingdom.data.set(LineageKeys.MANDATE_SACRIFICE_BUFF_DELTA,
                effects.AnnualMandateDelta);
            pKingdom.data.set(LineageKeys.MANDATE_RITUAL_COMPLETENESS,
                ritualCompleteness);

            Actor emperor = pKingdom.king;
            MandateReport report = MandateService.ReadReport();
            HistoryWriter.RecordKingdom(pKingdom, eventType,
                HistoryText.PlainText(historyContent), HistoryTarget.Actor(emperor));
            PersistRecord(pKingdom, emperor, report, pLevel, qualified, roll,
                outcome, effects, buffUntilYear, ritualCompleteness);
            if (outcome == MandateSacrificeOutcome.Auspicious)
                EraChangeTriggerService.Mark(pKingdom,
                    EraChangeReason.GrandSacrificeBlessing,
                    "sacrifice:" + (report?.period_id ?? -1L) + ":" + year);
            return true;
        }

        public static int GetCost(MandateSacrificeLevel pLevel)
        {
            return MandateSacrificeRules.Cost(pLevel);
        }

        public static string GetDecisionId(MandateSacrificeLevel pLevel)
        {
            return pLevel switch
            {
                MandateSacrificeLevel.Gamble =>
                    "aw_mandate_decision_sacrifice_gamble",
                MandateSacrificeLevel.Moderate =>
                    "aw_mandate_decision_sacrifice_moderate",
                MandateSacrificeLevel.Conservative =>
                    "aw_mandate_decision_sacrifice_conservative",
                _ => ""
            };
        }

        public static bool TryGetLevel(string pDecisionId,
            out MandateSacrificeLevel pLevel)
        {
            foreach (MandateSacrificeLevel level in Enum.GetValues(
                         typeof(MandateSacrificeLevel)))
            {
                if (GetDecisionId(level) != pDecisionId) continue;
                pLevel = level;
                return true;
            }

            pLevel = default;
            return false;
        }

        public static string PreferredAiDecisionId(Kingdom pKingdom)
        {
            if (!CanExecute(pKingdom)) return "";
            MandateReport report = MandateService.ReadReport();
            MandatePhase phase = MandatePhaseService.CurrentPhase;
            if (!MandateSacrificeRules.ShouldAutoSacrifice(
                    phase, report.mandate_value, MandatePhaseService.CatalystScore))
                return "";
            return GetDecisionId(MandateSacrificeRules.PreferredAiLevel(
                phase, IsQualified(pKingdom)));
        }

        private static string EventType(MandateSacrificeOutcome pOutcome)
        {
            return pOutcome switch
            {
                MandateSacrificeOutcome.Auspicious =>
                    "mandate_sacrifice_auspicious",
                MandateSacrificeOutcome.Ominous =>
                    "mandate_sacrifice_ominous",
                _ => "mandate_sacrifice_neutral"
            };
        }

        private static string BuildHistoryContent(Kingdom pKingdom,
            MandateSacrificeLevel pLevel, bool pQualified,
            MandateSacrificeOutcome pOutcome, MandateSacrificeEffects pEffects)
        {
            string qualificationKey = pQualified
                ? "aw_mandate_sacrifice_qualified"
                : "aw_mandate_sacrifice_unqualified";
            return (pKingdom?.name ?? "") +
                   HistoryLocalizationRules.Text("aw_hist_sacrifice_performed") +
                   HistoryLocalizationRules.Text(GetDecisionId(pLevel)) +
                   HistoryLocalizationRules.Text("aw_hist_sacrifice_qualification_mid") +
                   HistoryLocalizationRules.Text(qualificationKey) +
                   HistoryLocalizationRules.Text("aw_hist_sacrifice_result_mid") +
                   HistoryLocalizationRules.Text(OutcomeLocalizationKey(pOutcome)) +
                   HistoryLocalizationRules.Text("aw_hist_sacrifice_mandate_mid") +
                   Signed(pEffects.MandateDelta);
        }

        private static string OutcomeLocalizationKey(
            MandateSacrificeOutcome pOutcome)
        {
            return pOutcome switch
            {
                MandateSacrificeOutcome.Auspicious =>
                    "aw_mandate_sacrifice_outcome_auspicious",
                MandateSacrificeOutcome.Ominous =>
                    "aw_mandate_sacrifice_outcome_ominous",
                _ => "aw_mandate_sacrifice_outcome_neutral"
            };
        }

        private static string Signed(int pValue)
        {
            return pValue >= 0 ? "+" + pValue : pValue.ToString();
        }

        private static void PersistRecord(Kingdom pKingdom, Actor pEmperor,
            MandateReport pReport, MandateSacrificeLevel pLevel, bool pQualified,
            int pRoll, MandateSacrificeOutcome pOutcome,
            MandateSacrificeEffects pEffects, int pBuffUntilYear,
            int pRitualCompleteness)
        {
            try
            {
                if (!Ready)
                    throw new InvalidOperationException(
                        "lineage archive is unavailable");
                long recordId = TableIdAllocator.Next(DB,
                    SacrificeRecordTableItem.GetTableName(), "RECORD_ID");
                double now = LineageService.CurTime();
                DB.Insert(SacrificeRecordTableItem.GetTableName(),
                    ColumnVal.Create("RECORD_ID", recordId),
                    ColumnVal.Create("PERIOD_ID", pReport?.period_id ?? -1L),
                    ColumnVal.Create("KINGDOM_ID", pKingdom?.id ?? -1L),
                    ColumnVal.Create("KINGDOM_NAME", pKingdom?.name ?? ""),
                    ColumnVal.Create("KINGDOM_COLOR",
                        HistoryColors.FromKingdom(pKingdom)),
                    ColumnVal.Create("EMPEROR_ACTOR_ID",
                        pEmperor?.data?.id ?? -1L),
                    ColumnVal.Create("EMPEROR_NAME", pEmperor?.getName() ?? ""),
                    ColumnVal.Create("CHOICE",
                        pLevel.ToString().ToLowerInvariant()),
                    ColumnVal.Create("QUALIFIED", pQualified ? 1 : 0),
                    ColumnVal.Create("ROLL_BASIS_POINTS", pRoll),
                    ColumnVal.Create("OUTCOME",
                        pOutcome.ToString().ToLowerInvariant()),
                    ColumnVal.Create("MANDATE_DELTA", pEffects.MandateDelta),
                    ColumnVal.Create("AUTHORITY_DELTA", pEffects.AuthorityDelta),
                    ColumnVal.Create("PRESTIGE_DELTA", pEffects.PrestigeDelta),
                    ColumnVal.Create("ANNUAL_MANDATE_DELTA",
                        pEffects.AnnualMandateDelta),
                    ColumnVal.Create("BUFF_UNTIL_YEAR", pBuffUntilYear),
                    ColumnVal.Create("RITUAL_COMPLETENESS",
                        pRitualCompleteness),
                    ColumnVal.Create("WORLD_TIME", now),
                    ColumnVal.Create("YEAR_PREFIX",
                        HistoryWriter.BuildYearPrefix(now, pKingdom)));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Grand sacrifice record failed: " + e.Message);
            }
        }
    }
}
