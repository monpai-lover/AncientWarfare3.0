using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class VassalService
    {
        private const float VASSAL_POWER_WEIGHT = 0.6f;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);
        private static string T(string pKey) => HistoryLocalizationRules.Text(pKey);

        internal sealed class KingdomDestroyWarCleanupState
        {
            public long destroyed_kingdom_id = -1L;
            public readonly List<long> vassal_ids = new List<long>();
            public readonly List<WarSideSnapshot> wars = new List<WarSideSnapshot>();
        }

        internal sealed class WarSideSnapshot
        {
            public War war;
            public bool destroyed_was_attacker;
            public bool destroyed_was_defender;
        }

        private sealed class ActiveRelationDetails
        {
            public long relation_id = -1;
            public long vassal_id = -1;
            public Kingdom vassal;
            public string relation_type = "";
            public int autonomy = 50;
            public int tribute_rate = 10;
            public int military_obligation = 50;
            public double start_time = -1;
            public long suzerain_id = -1;
            public string suzerain_name = "";
            public string suzerain_color = "";
        }

        public static bool IsVassalKingdom(Kingdom pKingdom)
        {
            return GetSuzerainId(pKingdom) >= 0;
        }

        public static bool IsSuzerain(Kingdom pKingdom)
        {
            return GetDirectVassalCount(pKingdom) > 0;
        }

        public static int GetDirectVassalCount(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            pKingdom.data.get(LineageKeys.VASSAL_DIRECT_COUNT, out int count, 0);
            return Math.Max(0, count);
        }

        public static string GetStatusShort(Kingdom pKingdom)
        {
            bool vassal = IsVassalKingdom(pKingdom);
            bool suzerain = IsSuzerain(pKingdom);
            if (vassal && suzerain) return "\u81E3+";
            if (vassal) return "\u81E3";
            if (suzerain) return "\u4E3B";
            return "\u72EC";
        }

        public static string GetStatusTooltip(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            var lines = new List<string>();
            lines.Add(BuildStatusTitle(pKingdom));
            Kingdom suzerain = GetSuzerain(pKingdom);
            if (suzerain?.data != null) lines.Add("\u5B97\u4E3B: " + suzerain.name);
            List<Kingdom> direct = GetVassals(pKingdom);
            List<Kingdom> total = GetVassals(pKingdom, pRecursive: true);
            if (direct.Count > 0) lines.Add("\u76F4\u5C5E\u9644\u5EB8: " + direct.Count);
            if (total.Count > direct.Count) lines.Add("\u9644\u5EB8\u4F53\u7CFB: " + total.Count);
            int years = GetYearsSinceRelationStarted(pKingdom);
            if (years >= 0) lines.Add("\u81E3\u5C5E\u5E74\u6570: " + years);
            return string.Join("\n", lines.ToArray());
        }

        public static List<VassalRelationInfo> GetRelationView(Kingdom pContext)
        {
            var result = new List<VassalRelationInfo>();
            if (pContext?.data == null || pContext.isRekt()) return result;

            result.Add(BuildRelationRow(pContext, BuildContextRoleLabel(pContext), 0, isContext: true,
                isChain: false, relationSubject: null));

            Kingdom current = pContext;
            int chainDepth = 1;
            var visited = new HashSet<long> { pContext.id };
            while (current?.data != null)
            {
                Kingdom suzerain = GetSuzerain(current);
                if (suzerain?.data == null || suzerain.isRekt() || !visited.Add(suzerain.id)) break;
                string role = chainDepth == 1 ? "\u76F4\u63A5\u5B97\u4E3B" : "\u4E0A\u7EA7\u5B97\u4E3B";
                result.Add(BuildRelationRow(suzerain, role, chainDepth, isContext: false,
                    isChain: true, relationSubject: current));
                current = suzerain;
                chainDepth++;
            }

            AddVassalRows(pContext, result, 1, new HashSet<long> { pContext.id });
            AttachContextActions(pContext, result);
            return result;
        }

        public static KingdomDestroyWarCleanupState CaptureKingdomDestroyWarCleanup(Kingdom pKingdom)
        {
            var state = new KingdomDestroyWarCleanupState();
            if (pKingdom?.data == null) return state;

            state.destroyed_kingdom_id = pKingdom.id;
            foreach (Kingdom vassal in GetVassals(pKingdom, pRecursive: true))
            {
                if (vassal?.data == null || vassal.isRekt()) continue;
                if (!state.vassal_ids.Contains(vassal.id)) state.vassal_ids.Add(vassal.id);
            }

            try
            {
                foreach (War war in pKingdom.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    bool attacker = false;
                    bool defender = false;
                    try { attacker = war.isAttacker(pKingdom); } catch { attacker = false; }
                    try { defender = war.isDefender(pKingdom); } catch { defender = false; }
                    if (!attacker && !defender) continue;
                    state.wars.Add(new WarSideSnapshot
                    {
                        war = war,
                        destroyed_was_attacker = attacker,
                        destroyed_was_defender = defender
                    });
                }
            }
            catch { }

            return state;
        }

        public static void CleanupWarsAfterKingdomDestroyed(KingdomDestroyWarCleanupState pState)
        {
            if (pState == null || pState.vassal_ids.Count == 0 || pState.wars.Count == 0) return;

            try
            {
                foreach (WarSideSnapshot snapshot in pState.wars)
                {
                    War war = snapshot?.war;
                    if (war?.data == null || war.hasEnded()) continue;

                    foreach (long vassalId in pState.vassal_ids.ToList())
                    {
                        if (war.hasEnded()) break;
                        Kingdom vassal = FindKingdom(vassalId);
                        if (vassal?.data == null || vassal.isRekt()) continue;
                        if (!WarContainsOnDestroyedSide(war, vassal, snapshot)) continue;
                        LeaveWarPeacefully(war, vassal);
                    }
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("VassalService.CleanupWarsAfterKingdomDestroyed: " + e.Message);
            }
        }

        public static long GetSuzerainId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1L;
            pKingdom.data.get(LineageKeys.VASSAL_SUZERAIN_ID, out long dataId, -1L);
            return dataId;
        }

        public static Kingdom GetSuzerain(Kingdom pKingdom)
        {
            return FindKingdom(GetSuzerainId(pKingdom));
        }

        public static Kingdom GetRootSuzerain(Kingdom pKingdom)
        {
            Kingdom current = pKingdom;
            var visited = new HashSet<long>();
            while (current?.data != null && visited.Add(current.id))
            {
                Kingdom next = GetSuzerain(current);
                if (next == null) return current;
                current = next;
            }

            return current;
        }

        public static List<Kingdom> GetVassals(Kingdom pSuzerain, bool pRecursive = false)
        {
            var result = new List<Kingdom>();
            if (pSuzerain?.data == null || World.world?.kingdoms == null) return result;

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom == pSuzerain || kingdom.isRekt() || !kingdom.isCiv()) continue;
                if (GetSuzerainId(kingdom) != pSuzerain.id) continue;
                result.Add(kingdom);
                if (pRecursive)
                    AddVassalsRecursive(kingdom, result, new HashSet<long> { pSuzerain.id, kingdom.id });
            }

            return result;
        }

        public static bool CanAbsorbVassalByDecision(Kingdom pSuzerain, Kingdom pVassal, out string pReason)
        {
            bool suzerainValid = pSuzerain?.data != null &&
                                  !pSuzerain.isRekt() &&
                                  pSuzerain.isCiv();
            bool directVassal = pVassal?.data != null &&
                                !pVassal.isRekt() &&
                                pVassal.isCiv() &&
                                pVassal != pSuzerain &&
                                GetSuzerainId(pVassal) == pSuzerain?.id;
            bool suzerainAtWar = HasActiveWars(pSuzerain);
            bool vassalAtWar = HasActiveWars(pVassal);
            bool baseAllowed = VassalAnnexDecisionRules.CanStart(suzerainValid, directVassal,
                suzerainAtWar, vassalAtWar, out pReason);
            if (!baseAllowed) return false;

            return VassalRelationRules.CanAbsorbVassal(true,
                MandateRebelService.IsRebelKingdom(pSuzerain),
                MandateRebelService.IsRebelKingdom(pVassal),
                out pReason);
        }

        public static Kingdom FindBestAbsorbVassalTarget(Kingdom pSuzerain)
        {
            if (pSuzerain?.data == null || pSuzerain.isRekt()) return null;
            foreach (Kingdom vassal in GetVassals(pSuzerain)
                         .OrderBy(v => GetPowerScore(v, pIncludeVassals: true))
                         .ThenBy(v => v.name))
            {
                if (CanAbsorbVassalByDecision(pSuzerain, vassal, out _))
                    return vassal;
            }

            return null;
        }

        public static bool CanSetVassal(Kingdom pVassal, Kingdom pSuzerain)
        {
            bool basicValid = HasValidVassalParticipants(pVassal, pSuzerain);
            if (!basicValid) return false;

            bool titleAbove = KingdomTitleService.GetTitle(pSuzerain) > KingdomTitleService.GetTitle(pVassal);
            bool cycleDetected = WouldCreateCycle(pVassal, pSuzerain);
            bool directlyAdjacent = KingdomAdjacency.AreDirectNeighbors(pVassal, pSuzerain);
            return VassalRelationRules.CanSetVassal(
                basicValid,
                MandateRebelService.IsRebelKingdom(pVassal),
                MandateRebelService.IsRebelKingdom(pSuzerain),
                titleAbove,
                cycleDetected,
                directlyAdjacent,
                out _);
        }

        private static bool CanEnforceVassalWarVictory(Kingdom pVassal, Kingdom pSuzerain)
        {
            bool basicValid = HasValidVassalParticipants(pVassal, pSuzerain);
            if (!basicValid) return false;
            bool cycleDetected = basicValid && WouldCreateCycle(pVassal, pSuzerain);
            return VassalRelationRules.CanEnforceWarVictory(
                basicValid,
                MandateRebelService.IsRebelKingdom(pVassal),
                MandateRebelService.IsRebelKingdom(pSuzerain),
                cycleDetected);
        }

        private static bool HasValidVassalParticipants(Kingdom pVassal, Kingdom pSuzerain)
        {
            return pVassal?.data != null &&
                   pSuzerain?.data != null &&
                   pVassal != pSuzerain &&
                   !pVassal.isRekt() &&
                   !pSuzerain.isRekt() &&
                   pVassal.isCiv() &&
                   pSuzerain.isCiv() &&
                   pVassal.hasCities();
        }

        private static bool WouldCreateCycle(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pVassal?.data == null || pSuzerain?.data == null) return true;
            Kingdom root = GetRootSuzerain(pSuzerain);
            if (root == pVassal) return true;
            Kingdom current = pSuzerain;
            var visited = new HashSet<long>();
            while (current?.data != null && visited.Add(current.id))
            {
                if (current == pVassal) return true;
                current = GetSuzerain(current);
            }

            return false;
        }

        public static bool SetVassal(Kingdom pVassal, Kingdom pSuzerain, string pReason = "manual",
            long pWarId = -1, bool pEnforceWarVictory = false)
        {
            bool allowed = pEnforceWarVictory
                ? CanEnforceVassalWarVictory(pVassal, pSuzerain)
                : CanSetVassal(pVassal, pSuzerain);
            if (!allowed) return false;
            if (!Ready) return false;

            long currentSuzerain = GetSuzerainId(pVassal);
            if (currentSuzerain == pSuzerain.id) return false;
            if (currentSuzerain >= 0) EndVassal(pVassal, "replaced");

            long relationId = TableIdAllocator.Next(DB, VassalRelationTableItem.GetTableName(), "RELATION_ID");
            double now = LineageService.CurTime();
            DB.Insert(VassalRelationTableItem.GetTableName(),
                ColumnVal.Create("RELATION_ID", relationId),
                ColumnVal.Create("VASSAL_ID", pVassal.id),
                ColumnVal.Create("VASSAL_NAME", pVassal.name ?? ""),
                ColumnVal.Create("VASSAL_COLOR", HistoryColors.FromKingdom(pVassal)),
                ColumnVal.Create("SUZERAIN_ID", pSuzerain.id),
                ColumnVal.Create("SUZERAIN_NAME", pSuzerain.name ?? ""),
                ColumnVal.Create("SUZERAIN_COLOR", HistoryColors.FromKingdom(pSuzerain)),
                ColumnVal.Create("RELATION_TYPE", pReason ?? "vassal"),
                ColumnVal.Create("AUTONOMY", 50),
                ColumnVal.Create("TRIBUTE_RATE", 10),
                ColumnVal.Create("MILITARY_OBLIGATION", 50),
                ColumnVal.Create("CREATED_BY_WAR_ID", pWarId),
                ColumnVal.Create("START_TIME", now),
                ColumnVal.Create("END_TIME", -1.0),
                ColumnVal.Create("ACTIVE", 1),
                ColumnVal.Create("ABSORBED", 0),
                ColumnVal.Create("END_REASON", ""));

            pVassal.data.set(LineageKeys.VASSAL_SUZERAIN_ID, pSuzerain.id);
            pVassal.data.set(LineageKeys.VASSAL_RELATION_ID, relationId);
            AdjustDirectVassalCount(pSuzerain, 1);
            RecordVassalSet(pVassal, pSuzerain, pReason);
            DirtyVassalMap();
            PullVassalIntoSuzerainWars(pVassal, pSuzerain);
            return true;
        }

        public static bool EndVassal(Kingdom pVassal, string pReason = "ended")
        {
            if (pVassal?.data == null || !Ready) return false;
            long suzerainId = GetSuzerainId(pVassal);
            long relationId = GetRelationId(pVassal);
            Kingdom suzerain = FindKingdom(suzerainId);
            if (relationId < 0) relationId = ReadActiveRelationId(pVassal.id);
            if (relationId < 0) return false;

            if (!CloseRelation(relationId, pReason ?? "ended", absorbed: false)) return false;
            pVassal.data.set(LineageKeys.VASSAL_SUZERAIN_ID, -1L);
            pVassal.data.set(LineageKeys.VASSAL_RELATION_ID, -1L);
            RecordVassalEnd(pVassal, suzerain, pReason);
            DirtyVassalMap();
            return true;
        }

        public static bool MarkAbsorbed(Kingdom pVassal, Kingdom pSuzerain)
        {
            return TryAbsorbVassal(pSuzerain, pVassal, "absorbed");
        }

        public static bool TryAbsorbVassal(Kingdom pSuzerain, Kingdom pVassal, string pReason = "absorbed")
        {
            if (pSuzerain?.data == null || pVassal?.data == null || !Ready) return false;
            if (!VassalRelationRules.CanAbsorbVassal(true,
                    MandateRebelService.IsRebelKingdom(pSuzerain),
                    MandateRebelService.IsRebelKingdom(pVassal),
                    out _))
                return false;
            if (GetSuzerainId(pVassal) != pSuzerain.id) return false;

            long relationId = GetRelationId(pVassal);
            if (relationId < 0) relationId = ReadActiveRelationId(pVassal.id);
            if (relationId < 0) return false;

            foreach (Kingdom child in GetVassals(pVassal).ToList())
                SetVassal(child, pSuzerain, "absorbed_reparent");

            List<City> cities = pVassal.getCities().Where(c => c?.data != null && !c.isRekt()).ToList();
            foreach (City city in cities)
                city.joinAnotherKingdom(pSuzerain);

            CloseRelation(relationId, pReason ?? "absorbed", absorbed: true);
            pVassal.data.set(LineageKeys.VASSAL_SUZERAIN_ID, -1L);
            pVassal.data.set(LineageKeys.VASSAL_RELATION_ID, -1L);

            HistoryWriter.RecordKingdom(pSuzerain, "vassal_absorb",
                KingdomLabel(pSuzerain) + H("aw_hist_vassal_absorb_mid") + KingdomLabel(pVassal),
                HistoryTarget.Kingdom(pVassal));
            HistoryWriter.RecordKingdom(pVassal, "vassal_absorbed",
                KingdomLabel(pVassal) + H("aw_hist_vassal_absorbed_mid") +
                KingdomLabel(pSuzerain) + H("aw_hist_vassal_absorbed_suffix"),
                HistoryTarget.Kingdom(pSuzerain));
            DirtyVassalMap();
            return true;
        }

        internal static void EnforceNoVassalRelationsForRebel(Kingdom pKingdom, string pReason = "rebel_government")
        {
            if (pKingdom?.data == null || !MandateRebelService.IsRebelKingdom(pKingdom)) return;

            if (IsVassalKingdom(pKingdom))
                EndVassal(pKingdom, "rebel_no_vassal");

            foreach (Kingdom child in GetVassals(pKingdom).ToList())
            {
                if (child?.data == null || child.isRekt()) continue;
                EndVassal(child, "rebel_no_suzerain");
            }
        }

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null) return;
            string type = GetWarType(pWar);
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null) return;

            if (type == "independence_war")
            {
                BeginIndependenceSuspension(pWar, attacker, defender);
                LeaveSuzerainWarsForIndependence(pWar, attacker, defender);
                Dictionary<long, List<ActiveRelationDetails>> independenceRelations =
                    BuildRelationAdjacency(ReadAllActiveRelations());
                JoinObligatedNetwork(pWar, GetRootSuzerain(defender) ?? defender,
                    defender, attacker, attackers: false, independenceRelations,
                    pAllowNewDecisions: true);
                return;
            }

            Kingdom attackerRoot = GetRootSuzerain(attacker);
            Kingdom defenderRoot = GetRootSuzerain(defender);
            if (attackerRoot != null && defenderRoot != null && attackerRoot == defenderRoot) return;

            Dictionary<long, List<ActiveRelationDetails>> relations =
                BuildRelationAdjacency(ReadAllActiveRelations());
            JoinObligatedNetwork(pWar, attackerRoot ?? attacker, attacker, defender,
                attackers: true, relations, pAllowNewDecisions: true);
            JoinObligatedNetwork(pWar, defenderRoot ?? defender, defender, attacker,
                attackers: false, relations, pAllowNewDecisions: true);
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null) return;
            string type = GetWarType(pWar);
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();

            if (type == "independence_war")
                EndIndependenceSuspension(pWar, attacker);

            if (type == "vassal_war" && pWinner == WarWinner.Attackers)
            {
                SetVassal(defender, attacker, "vassal_war", pWar.data.id,
                    pEnforceWarVictory: true);
                return;
            }

            if (type == "independence_war" && pWinner == WarWinner.Attackers)
            {
                EndIndependenceWar(attacker, defender);
                return;
            }

            if (type == "reclaim" && pWinner == WarWinner.Attackers && !WarTerritoryService.HasWarGoal(pWar.data.id))
                RoyalClaimService.OnReclaimWarWon(attacker, defender, pWar.data.id);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral()) return;
            try
            {
                if (GetRootSuzerain(pKingdom) != pKingdom || GetDirectVassalCount(pKingdom) <= 0) return;
                Dictionary<long, List<ActiveRelationDetails>> relations =
                    BuildRelationAdjacency(ReadAllActiveRelations());
                foreach (War war in pKingdom.getWars())
                    RepairObligatedNetwork(war, pKingdom, relations);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("VassalService.OnKingdomYear: " + e.Message);
            }
        }

        public static void SettleAnnualTribute(Kingdom pSuzerain)
        {
            if (pSuzerain?.data == null || pSuzerain.isRekt() || !Ready) return;
            if (GetDirectVassalCount(pSuzerain) <= 0) return;

            int year = Date.getCurrentYear();
            pSuzerain.data.get(LineageKeys.VASSAL_TRIBUTE_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pSuzerain.data.set(LineageKeys.VASSAL_TRIBUTE_LAST_YEAR, year);

            List<ActiveRelationDetails> relations = ReadDirectRelations(pSuzerain);
            if (relations.Count == 0) return;
            CentralizationEffects effects = CentralizationService.ReadSnapshot(pSuzerain).effects;
            float politicalTransferred = 0f;
            int goldTransferred = 0;

            foreach (ActiveRelationDetails relation in relations)
            {
                Kingdom vassal = relation.vassal;
                if (vassal?.data == null || vassal.isRekt()) continue;
                VassalEffectiveTerms terms = GetEffectiveRelationTerms(relation, effects);
                CityEconomyService.TryGetLatestCachedTaxContribution(vassal, out float annualTax);
                float requestedPolitical = VassalFiscalRules.PoliticalTribute(annualTax,
                    terms.TributeRate, KingdomPolicyService.GetPoliticalPoints(vassal),
                    KingdomPolicyService.GetPoliticalPoints(pSuzerain),
                    VassalFiscalRules.MaximumPoliticalBalance);
                politicalTransferred += KingdomPolicyService.TransferPoliticalPoints(
                    vassal, pSuzerain, requestedPolitical);

                int availableGold = GetCapitalGold(vassal);
                int requestedGold = VassalFiscalRules.GoldTribute(
                    annualTax, terms.TributeRate, availableGold);
                goldTransferred += TransferCapitalGold(vassal, pSuzerain, requestedGold);
            }

            if (politicalTransferred <= 0f && goldTransferred <= 0) return;
            HistoryWriter.RecordKingdom(pSuzerain, "vassal_tribute",
                KingdomLabel(pSuzerain) + H("aw_hist_vassal_tribute_text") +
                HistoryText.PlainText(Math.Round(politicalTransferred, 1) + " / " + goldTransferred));
        }

        public static VassalEffectiveTerms GetEffectiveRelationTerms(Kingdom pVassal)
        {
            ActiveRelationDetails relation = ReadActiveRelationDetails(pVassal?.id ?? -1L);
            Kingdom suzerain = FindKingdom(relation?.suzerain_id ?? -1L);
            CentralizationEffects effects = CentralizationService.ReadSnapshot(suzerain).effects;
            return relation == null
                ? VassalFiscalRules.EffectiveTerms(100, 0, 0, effects)
                : GetEffectiveRelationTerms(relation, effects);
        }

        public static CentralPowerVassalSummary ReadCentralPowerSummary(Kingdom pSuzerain)
        {
            var summary = new CentralPowerVassalSummary();
            if (pSuzerain?.data == null || pSuzerain.isRekt() || !Ready) return summary;
            List<ActiveRelationDetails> relations = ReadDirectRelations(pSuzerain);
            CentralizationEffects effects = CentralizationService.ReadSnapshot(pSuzerain).effects;
            float projectedSuzerainPoints = KingdomPolicyService.GetPoliticalPoints(pSuzerain);
            float autonomyTotal = 0f;
            float obligationTotal = 0f;

            foreach (ActiveRelationDetails relation in relations)
            {
                Kingdom vassal = relation.vassal;
                if (vassal?.data == null || vassal.isRekt()) continue;
                VassalEffectiveTerms terms = GetEffectiveRelationTerms(relation, effects);
                CityEconomyService.TryGetLatestCachedTaxContribution(vassal, out float annualTax);
                float political = VassalFiscalRules.PoliticalTribute(annualTax,
                    terms.TributeRate, KingdomPolicyService.GetPoliticalPoints(vassal),
                    projectedSuzerainPoints, VassalFiscalRules.MaximumPoliticalBalance);
                int gold = VassalFiscalRules.GoldTribute(
                    annualTax, terms.TributeRate, GetCapitalGold(vassal));
                projectedSuzerainPoints += political;

                summary.vassals.Add(BuildCentralPowerVassalInfo(
                    relation, terms, annualTax, political, gold));
                summary.forecast_political_tribute += political;
                summary.forecast_gold_tribute += gold;
                autonomyTotal += terms.Autonomy;
                obligationTotal += terms.MilitaryObligation;
            }

            summary.direct_vassal_count = summary.vassals.Count;
            if (summary.direct_vassal_count > 0)
            {
                summary.average_effective_autonomy = autonomyTotal / summary.direct_vassal_count;
                summary.average_effective_military_obligation = obligationTotal / summary.direct_vassal_count;
            }
            return summary;
        }

        public static void OnKingdomDestroyed(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || !Ready) return;

            try
            {
                Kingdom upperSuzerain = GetSuzerain(pKingdom);
                bool canReparentToUpper = upperSuzerain?.data != null &&
                                          !upperSuzerain.isRekt() &&
                                          upperSuzerain != pKingdom;

                long ownRelationId = GetRelationId(pKingdom);
                if (ownRelationId < 0) ownRelationId = ReadActiveRelationId(pKingdom.id);
                if (ownRelationId >= 0)
                {
                    CloseRelation(ownRelationId, "kingdom_fell", absorbed: false);
                    pKingdom.data.set(LineageKeys.VASSAL_SUZERAIN_ID, -1L);
                    pKingdom.data.set(LineageKeys.VASSAL_RELATION_ID, -1L);
                    RecordVassalFell(pKingdom, upperSuzerain);
                }

                foreach (Kingdom child in GetVassals(pKingdom).ToList())
                {
                    if (child?.data == null || child.isRekt()) continue;

                    long childRelationId = GetRelationId(child);
                    if (childRelationId < 0) childRelationId = ReadActiveRelationId(child.id);
                    if (childRelationId >= 0)
                        CloseRelation(childRelationId,
                            canReparentToUpper ? "suzerain_fell_reparent" : "suzerain_fell",
                            absorbed: false);

                    child.data.set(LineageKeys.VASSAL_SUZERAIN_ID, -1L);
                    child.data.set(LineageKeys.VASSAL_RELATION_ID, -1L);

                    if (canReparentToUpper && CanSetVassal(child, upperSuzerain))
                    {
                        SetVassal(child, upperSuzerain, "suzerain_fell_reparent");
                    }
                    else
                    {
                        RecordVassalFreedBySuzerainFall(child, pKingdom);
                    }
                }

                DirtyVassalMap();
            }
            catch (Exception e)
            {
                ModClass.LogWarning("VassalService.OnKingdomDestroyed: " + e.Message);
            }
        }

        public static void ResolveIndependenceWarWon(Kingdom pRebel, Kingdom pOldSuzerain)
        {
            EndIndependenceWar(pRebel, pOldSuzerain);
        }

        private static void EndIndependenceWar(Kingdom pRebel, Kingdom pOldSuzerain)
        {
            if (pRebel?.data == null) return;
            Kingdom upper = GetSuzerain(pOldSuzerain);
            if (upper?.data != null && !upper.isRekt() && CanSetVassal(pRebel, upper))
            {
                EndVassal(pRebel, "independence_war_transfer");
                SetVassal(pRebel, upper, "independence_war_transfer");
                return;
            }

            EndVassal(pRebel, "independence_war");
        }

        public static int GetYearsSinceRelationStarted(Kingdom pVassal)
        {
            double start = GetRelationStartTime(pVassal);
            if (start < 0) return -1;
            try { return Mathf.Max(0, Date.getCurrentYear() - Date.getYear(start)); }
            catch { return -1; }
        }

        public static float GetPowerScore(Kingdom pKingdom, bool pIncludeVassals)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0f;
            float score = CountWarriors(pKingdom) * 2f + CountCities(pKingdom) * 5f + pKingdom.countZones() * 0.02f;
            if (!pIncludeVassals) return score;

            foreach (Kingdom vassal in GetVassals(pKingdom, pRecursive: true))
                score += GetPowerScore(vassal, pIncludeVassals: false) * VASSAL_POWER_WEIGHT;
            return score;
        }

        public static int GetNetworkArmy(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            int count = CountWarriors(pKingdom);
            foreach (Kingdom vassal in GetVassals(pKingdom, pRecursive: true))
                count += CountWarriors(vassal);
            return count;
        }

        public static ColorAsset GetMapColor(Kingdom pKingdom, ColorAsset pFallback)
        {
            Kingdom root = GetRootSuzerain(pKingdom);
            if (root?.data == null) return pFallback;
            return DirectKingdomColor(root, pFallback);
        }

        private static ColorAsset DirectKingdomColor(Kingdom pKingdom, ColorAsset pFallback)
        {
            if (pKingdom?.data == null) return pFallback;
            try
            {
                int colorId = pKingdom.data.color_id;
                if (colorId >= 0)
                    return AssetManager.kingdom_colors_library.getColorByIndex(colorId) ?? pFallback;
            }
            catch { }
            return pFallback;
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            var lines = new List<string>();
            Kingdom suzerain = GetSuzerain(pKingdom);
            Kingdom root = GetRootSuzerain(pKingdom);
            List<Kingdom> direct = GetVassals(pKingdom);
            List<Kingdom> total = GetVassals(pKingdom, pRecursive: true);

            if (suzerain?.data != null)
            {
                lines.Add("\u9644\u5EB8\u56FD");
                lines.Add("\u5B97\u4E3B: " + suzerain.name);
                if (root?.data != null && root != suzerain) lines.Add("\u6839\u5B97\u4E3B: " + root.name);
                int years = GetYearsSinceRelationStarted(pKingdom);
                if (years >= 0) lines.Add("\u81E3\u5C5E\u5E74\u6570: " + years);
            }
            else
            {
                lines.Add("\u72EC\u7ACB\u56FD\u5BB6");
                if (root?.data != null && root != pKingdom) lines.Add("\u6839\u5B97\u4E3B: " + root.name);
            }

            if (direct.Count > 0)
                lines.Add("\u76F4\u5C5E\u9644\u5EB8: " + direct.Count);
            if (total.Count > direct.Count)
                lines.Add("\u9644\u5EB8\u4F53\u7CFB\u603B\u6570: " + total.Count);
            lines.Add("\u4F53\u7CFB\u519B\u529B: " + GetNetworkArmy(root ?? pKingdom));
            return string.Join("\n", lines.ToArray());
        }

        private static void AddVassalsRecursive(Kingdom pKingdom, List<Kingdom> pResult, HashSet<long> pVisited)
        {
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv()) continue;
                if (!pVisited.Add(kingdom.id)) continue;
                if (GetSuzerainId(kingdom) != pKingdom.id) continue;
                pResult.Add(kingdom);
                AddVassalsRecursive(kingdom, pResult, pVisited);
            }
        }

        private static void AddVassalRows(Kingdom pSuzerain, List<VassalRelationInfo> pRows, int pDepth,
            HashSet<long> pVisited)
        {
            foreach (Kingdom child in GetVassals(pSuzerain).OrderBy(k => k.name))
            {
                if (child?.data == null || child.isRekt()) continue;
                if (!pVisited.Add(child.id)) continue;

                string role = pDepth == 1 ? "\u76F4\u5C5E\u9644\u5EB8" : "\u9644\u5EB8";
                pRows.Add(BuildRelationRow(child, role, pDepth, isContext: false,
                    isChain: false, relationSubject: child));
                AddVassalRows(child, pRows, pDepth + 1, pVisited);
            }
        }

        private static void AttachContextActions(Kingdom pContext, List<VassalRelationInfo> pRows)
        {
            if (pContext?.data == null || pRows == null) return;
            foreach (VassalRelationInfo row in pRows)
            {
                if (row == null) continue;
                row.context_kingdom_id = pContext.id;
                Kingdom target = FindKingdom(row.kingdom_id);
                row.can_absorb_by_context = CanAbsorbVassalByDecision(pContext, target, out string reason);
                row.absorb_reason = reason ?? "";
            }
        }

        private static VassalRelationInfo BuildRelationRow(Kingdom pKingdom, string pRole, int pDepth,
            bool isContext, bool isChain, Kingdom relationSubject)
        {
            var row = new VassalRelationInfo
            {
                kingdom_id = pKingdom?.id ?? -1,
                kingdom_name = pKingdom?.name ?? "",
                color_text = HistoryColors.FromKingdom(pKingdom),
                color_id = pKingdom?.data?.color_id ?? -1,
                banner_icon_id = pKingdom?.data?.banner_icon_id ?? 0,
                banner_background_id = pKingdom?.data?.banner_background_id ?? 0,
                banner_id = pKingdom?.getActorAsset()?.banner_id ?? "",
                depth = pDepth,
                is_context = isContext,
                is_chain_row = isChain,
                is_vassal_row = !isContext && !isChain,
                role_label = pRole ?? "",
                cities = CountCities(pKingdom),
                army = CountWarriors(pKingdom),
                direct_vassals = GetVassals(pKingdom).Count,
                total_vassals = GetVassals(pKingdom, pRecursive: true).Count
            };

            Kingdom relationKingdom = relationSubject ?? (IsVassalKingdom(pKingdom) ? pKingdom : null);
            ActiveRelationDetails relation = ReadActiveRelationDetails(relationKingdom?.id ?? -1);
            if (relation != null)
            {
                CentralizationEffects effects = CentralizationService.ReadSnapshot(
                    FindKingdom(relation.suzerain_id)).effects;
                VassalEffectiveTerms effective = GetEffectiveRelationTerms(relation, effects);
                row.suzerain_id = relation.suzerain_id;
                row.suzerain_name = relation.suzerain_name;
                row.suzerain_color = relation.suzerain_color;
                row.relation_type = relation.relation_type;
                row.relation_reason_label = VassalGetReasonLabel(relation.relation_type);
                row.autonomy = effective.Autonomy;
                row.tribute_rate = effective.TributeRate;
                row.military_obligation = effective.MilitaryObligation;
                row.start_time = relation.start_time;
                row.years = YearsSince(relation.start_time);
                row.relation_subject_name = relationKingdom?.name ?? "";
            }

            return row;
        }

        private static string BuildContextRoleLabel(Kingdom pKingdom)
        {
            bool vassal = IsVassalKingdom(pKingdom);
            bool suzerain = IsSuzerain(pKingdom);
            if (vassal && suzerain) return "\u672C\u56FD\u00B7\u9644\u5EB8\u5B97\u4E3B";
            if (vassal) return "\u672C\u56FD\u00B7\u9644\u5EB8";
            if (suzerain) return "\u672C\u56FD\u00B7\u5B97\u4E3B";
            return "\u672C\u56FD\u00B7\u72EC\u7ACB";
        }

        private static string BuildStatusTitle(Kingdom pKingdom)
        {
            bool vassal = IsVassalKingdom(pKingdom);
            bool suzerain = IsSuzerain(pKingdom);
            if (vassal && suzerain) return "\u9644\u5EB8\u56FD\uFF0C\u5E76\u62E5\u6709\u4E0B\u7EA7\u9644\u5EB8";
            if (vassal) return "\u9644\u5EB8\u56FD";
            if (suzerain) return "\u5B97\u4E3B\u56FD";
            return "\u72EC\u7ACB\u56FD\u5BB6";
        }

        private static List<ActiveRelationDetails> ReadDirectRelations(Kingdom pSuzerain)
        {
            var result = new List<ActiveRelationDetails>();
            if (!Ready || pSuzerain?.data == null) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT RELATION_ID,VASSAL_ID,RELATION_TYPE,AUTONOMY,TRIBUTE_RATE," +
                    $"MILITARY_OBLIGATION,START_TIME,SUZERAIN_ID,SUZERAIN_NAME,SUZERAIN_COLOR " +
                    $"FROM {VassalRelationTableItem.GetTableName()} " +
                    "WHERE SUZERAIN_ID=@s AND ACTIVE=1 AND END_TIME<0 ORDER BY START_TIME";
                cmd.Parameters.AddWithValue("@s", pSuzerain.id);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    long vassalId = reader.IsDBNull(1) ? -1L : reader.GetInt64(1);
                    Kingdom vassal = FindKingdom(vassalId);
                    if (vassal?.data == null || vassal.isRekt()) continue;
                    result.Add(new ActiveRelationDetails
                    {
                        relation_id = reader.IsDBNull(0) ? -1L : reader.GetInt64(0),
                        vassal_id = vassalId,
                        vassal = vassal,
                        relation_type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        autonomy = reader.IsDBNull(3) ? 50 : (int)reader.GetInt64(3),
                        tribute_rate = reader.IsDBNull(4) ? 10 : (int)reader.GetInt64(4),
                        military_obligation = reader.IsDBNull(5) ? 50 : (int)reader.GetInt64(5),
                        start_time = reader.IsDBNull(6) ? -1 : reader.GetDouble(6),
                        suzerain_id = reader.IsDBNull(7) ? -1L : reader.GetInt64(7),
                        suzerain_name = reader.IsDBNull(8) ? "" : reader.GetString(8),
                        suzerain_color = reader.IsDBNull(9) ? "" : reader.GetString(9)
                    });
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("VassalService.ReadDirectRelations: " + e.Message);
            }
            return result;
        }

        private static List<ActiveRelationDetails> ReadAllActiveRelations()
        {
            var result = new List<ActiveRelationDetails>();
            if (!Ready) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT RELATION_ID,VASSAL_ID,RELATION_TYPE,AUTONOMY,TRIBUTE_RATE," +
                    $"MILITARY_OBLIGATION,START_TIME,SUZERAIN_ID,SUZERAIN_NAME,SUZERAIN_COLOR " +
                    $"FROM {VassalRelationTableItem.GetTableName()} " +
                    "WHERE ACTIVE=1 AND END_TIME<0 ORDER BY SUZERAIN_ID,START_TIME";
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    long vassalId = reader.IsDBNull(1) ? -1L : reader.GetInt64(1);
                    Kingdom vassal = FindKingdom(vassalId);
                    if (vassal?.data == null || vassal.isRekt()) continue;
                    result.Add(new ActiveRelationDetails
                    {
                        relation_id = reader.IsDBNull(0) ? -1L : reader.GetInt64(0),
                        vassal_id = vassalId,
                        vassal = vassal,
                        relation_type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        autonomy = reader.IsDBNull(3) ? 50 : (int)reader.GetInt64(3),
                        tribute_rate = reader.IsDBNull(4) ? 10 : (int)reader.GetInt64(4),
                        military_obligation = reader.IsDBNull(5) ? 50 : (int)reader.GetInt64(5),
                        start_time = reader.IsDBNull(6) ? -1 : reader.GetDouble(6),
                        suzerain_id = reader.IsDBNull(7) ? -1L : reader.GetInt64(7),
                        suzerain_name = reader.IsDBNull(8) ? "" : reader.GetString(8),
                        suzerain_color = reader.IsDBNull(9) ? "" : reader.GetString(9)
                    });
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("VassalService.ReadAllActiveRelations: " + e.Message);
            }
            return result;
        }

        private static Dictionary<long, List<ActiveRelationDetails>> BuildRelationAdjacency(
            List<ActiveRelationDetails> pRelations)
        {
            var result = new Dictionary<long, List<ActiveRelationDetails>>();
            if (pRelations == null) return result;
            foreach (ActiveRelationDetails relation in pRelations)
            {
                if (relation == null || relation.suzerain_id < 0 || relation.vassal?.data == null) continue;
                if (!result.TryGetValue(relation.suzerain_id, out List<ActiveRelationDetails> children))
                {
                    children = new List<ActiveRelationDetails>();
                    result[relation.suzerain_id] = children;
                }
                children.Add(relation);
            }
            return result;
        }

        private static VassalEffectiveTerms GetEffectiveRelationTerms(
            ActiveRelationDetails pRelation, CentralizationEffects pEffects)
        {
            return pRelation == null
                ? VassalFiscalRules.EffectiveTerms(100, 0, 0, pEffects)
                : VassalFiscalRules.EffectiveTerms(pRelation.autonomy, pRelation.tribute_rate,
                    pRelation.military_obligation, pEffects);
        }

        private static CentralPowerVassalInfo BuildCentralPowerVassalInfo(
            ActiveRelationDetails pRelation, VassalEffectiveTerms pTerms, float pAnnualTax,
            float pPolitical, int pGold)
        {
            Kingdom vassal = pRelation?.vassal;
            return new CentralPowerVassalInfo
            {
                relation_id = pRelation?.relation_id ?? -1L,
                kingdom_id = vassal?.id ?? -1L,
                kingdom_name = vassal?.name ?? "",
                kingdom_color = HistoryColors.FromKingdom(vassal),
                color_id = vassal?.data?.color_id ?? -1,
                banner_icon_id = vassal?.data?.banner_icon_id ?? 0,
                banner_background_id = vassal?.data?.banner_background_id ?? 0,
                banner_id = vassal?.getActorAsset()?.banner_id ?? "",
                relation_type = pRelation?.relation_type ?? "",
                base_autonomy = pRelation?.autonomy ?? 0,
                base_tribute_rate = pRelation?.tribute_rate ?? 0,
                base_military_obligation = pRelation?.military_obligation ?? 0,
                effective_autonomy = pTerms.Autonomy,
                effective_tribute_rate = pTerms.TributeRate,
                effective_military_obligation = pTerms.MilitaryObligation,
                annual_tax = pAnnualTax,
                forecast_political_tribute = pPolitical,
                forecast_gold_tribute = pGold
            };
        }

        private static ActiveRelationDetails ReadActiveRelationDetails(long pVassalId)
        {
            if (!Ready || pVassalId < 0) return null;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT RELATION_TYPE, AUTONOMY, TRIBUTE_RATE, MILITARY_OBLIGATION, START_TIME, " +
                    $"SUZERAIN_ID, SUZERAIN_NAME, SUZERAIN_COLOR FROM {VassalRelationTableItem.GetTableName()} " +
                    "WHERE VASSAL_ID=@v AND ACTIVE=1 AND END_TIME<0 ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@v", pVassalId);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return new ActiveRelationDetails
                {
                    vassal_id = pVassalId,
                    vassal = FindKingdom(pVassalId),
                    relation_type = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    autonomy = reader.IsDBNull(1) ? 50 : (int)reader.GetInt64(1),
                    tribute_rate = reader.IsDBNull(2) ? 10 : (int)reader.GetInt64(2),
                    military_obligation = reader.IsDBNull(3) ? 50 : (int)reader.GetInt64(3),
                    start_time = reader.IsDBNull(4) ? -1 : reader.GetDouble(4),
                    suzerain_id = reader.IsDBNull(5) ? -1 : reader.GetInt64(5),
                    suzerain_name = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    suzerain_color = reader.IsDBNull(7) ? "" : reader.GetString(7)
                };
            }
            catch { return null; }
        }

        private static int YearsSince(double pStartTime)
        {
            if (pStartTime < 0) return -1;
            try { return Mathf.Max(0, Date.getCurrentYear() - Date.getYear(pStartTime)); }
            catch { return -1; }
        }

        private static void JoinObligatedNetwork(War pWar, Kingdom pRoot, Kingdom pMain,
            Kingdom pEnemy, bool attackers,
            Dictionary<long, List<ActiveRelationDetails>> pRelations,
            bool pAllowNewDecisions)
        {
            if (pWar?.data == null || pWar.hasEnded() || pRoot?.data == null || pRelations == null) return;
            if (pRoot != pMain && pRoot != pEnemy) JoinSide(pWar, pRoot, attackers);

            pWar.data.get(LineageKeys.VASSAL_OBLIGATION_DECISIONS,
                out string decisions, "");
            string initialDecisions = decisions;
            var effectsBySuzerain = new Dictionary<long, CentralizationEffects>();
            var queue = new Queue<Kingdom>();
            var visited = new HashSet<long>();
            if (IsOnWarSide(pWar, pRoot, attackers)) queue.Enqueue(pRoot);
            if (pMain?.data != null && pMain != pRoot && IsOnWarSide(pWar, pMain, attackers))
                queue.Enqueue(pMain);

            while (queue.Count > 0)
            {
                Kingdom suzerain = queue.Dequeue();
                if (suzerain?.data == null || !visited.Add(suzerain.id)) continue;
                if (!pRelations.TryGetValue(suzerain.id,
                        out List<ActiveRelationDetails> children)) continue;

                if (!effectsBySuzerain.TryGetValue(suzerain.id,
                        out CentralizationEffects effects))
                {
                    effects = CentralizationService.ReadSnapshot(suzerain).effects;
                    effectsBySuzerain[suzerain.id] = effects;
                }

                foreach (ActiveRelationDetails relation in children)
                {
                    Kingdom vassal = relation?.vassal;
                    if (vassal?.data == null || vassal.isRekt() || vassal == pEnemy) continue;
                    bool helping = IsOnWarSide(pWar, vassal, attackers);
                    bool opposing = IsOnWarSide(pWar, vassal, !attackers);
                    bool suspended = HasActiveIndependenceSuspension(vassal, suzerain);
                    if (suspended)
                    {
                        if (helping) LeaveWarPeacefully(pWar, vassal);
                        continue;
                    }
                    if (vassal == pMain || helping)
                    {
                        queue.Enqueue(vassal);
                        continue;
                    }

                    bool accepted;
                    if (pAllowNewDecisions)
                    {
                        VassalEffectiveTerms terms = GetEffectiveRelationTerms(relation, effects);
                        VassalObligationDecisionCodec.Resolve(decisions, pWar.data.id,
                            suzerain.id, vassal.id, terms.MilitaryObligation,
                            out accepted, out decisions);
                    }
                    else if (!VassalObligationDecisionCodec.TryGet(decisions,
                                 suzerain.id, vassal.id, out accepted))
                    {
                        continue;
                    }

                    if (!VassalWarSupportRules.ShouldPullIntoSuzerainWar(
                            pSuzerainInWar: true,
                            pVassalAlreadyHelping: false,
                            pVassalAlreadyInWar: opposing,
                            pVassalOpposesSuzerain: opposing,
                            independenceSuspended: false,
                            pObligationAccepted: accepted))
                        continue;
                    JoinSide(pWar, vassal, attackers);
                    if (IsOnWarSide(pWar, vassal, attackers)) queue.Enqueue(vassal);
                }
            }

            if (!string.Equals(initialDecisions, decisions, StringComparison.Ordinal))
                pWar.data.set(LineageKeys.VASSAL_OBLIGATION_DECISIONS, decisions);
        }

        private static void RepairObligatedNetwork(War pWar, Kingdom pRoot,
            Dictionary<long, List<ActiveRelationDetails>> pRelations)
        {
            if (pWar?.data == null || pWar.hasEnded() || pRoot?.data == null) return;
            bool attackers = IsOnWarSide(pWar, pRoot, true);
            bool defenders = IsOnWarSide(pWar, pRoot, false);
            if (!attackers && !defenders) return;
            Kingdom main = attackers ? pWar.getMainAttacker() : pWar.getMainDefender();
            Kingdom enemy = attackers ? pWar.getMainDefender() : pWar.getMainAttacker();
            JoinObligatedNetwork(pWar, pRoot, main, enemy, attackers, pRelations,
                pAllowNewDecisions: false);
        }

        private static bool IsOnWarSide(War pWar, Kingdom pKingdom, bool attackers)
        {
            if (pWar?.data == null || pKingdom?.data == null) return false;
            try { return attackers ? pWar.isAttacker(pKingdom) : pWar.isDefender(pKingdom); }
            catch { return false; }
        }

        private static void PullVassalIntoSuzerainWars(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pVassal?.data == null || pSuzerain?.data == null || pSuzerain.isRekt()) return;
            try
            {
                Dictionary<long, List<ActiveRelationDetails>> relations =
                    BuildRelationAdjacency(ReadAllActiveRelations());
                Kingdom root = GetRootSuzerain(pSuzerain) ?? pSuzerain;
                foreach (War war in pSuzerain.getWars())
                {
                    bool attackers = IsOnWarSide(war, pSuzerain, true);
                    bool defenders = IsOnWarSide(war, pSuzerain, false);
                    if (!attackers && !defenders) continue;
                    Kingdom main = attackers ? war.getMainAttacker() : war.getMainDefender();
                    Kingdom enemy = attackers ? war.getMainDefender() : war.getMainAttacker();
                    JoinObligatedNetwork(war, root, main, enemy, attackers, relations,
                        pAllowNewDecisions: true);
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("VassalService.PullVassalIntoSuzerainWars: " + e.Message);
            }
        }

        private static void JoinSide(War pWar, Kingdom pKingdom, bool attackers)
        {
            if (pWar == null || pKingdom?.data == null || pKingdom.isRekt()) return;
            try { if (pWar.hasKingdom(pKingdom)) return; } catch { }
            try
            {
                if (attackers) pWar.joinAttackers(pKingdom);
                else pWar.joinDefenders(pKingdom);
            }
            catch { }
        }

        private static void BeginIndependenceSuspension(War pWar, Kingdom pRebel, Kingdom pSuzerain)
        {
            if (pWar?.data == null || pRebel?.data == null || pSuzerain?.data == null) return;
            pRebel.data.set(LineageKeys.VASSAL_INDEPENDENCE_WAR_ID, pWar.data.id);
            pRebel.data.set(LineageKeys.VASSAL_INDEPENDENCE_SUZERAIN_ID, pSuzerain.id);
        }

        private static void EndIndependenceSuspension(War pWar, Kingdom pRebel)
        {
            if (pWar?.data == null || pRebel?.data == null) return;
            pRebel.data.get(LineageKeys.VASSAL_INDEPENDENCE_WAR_ID, out long warId, -1L);
            if (warId != pWar.data.id) return;
            ClearIndependenceSuspension(pRebel);
        }

        private static void ClearIndependenceSuspension(Kingdom pRebel)
        {
            if (pRebel?.data == null) return;
            pRebel.data.set(LineageKeys.VASSAL_INDEPENDENCE_WAR_ID, -1L);
            pRebel.data.set(LineageKeys.VASSAL_INDEPENDENCE_SUZERAIN_ID, -1L);
        }

        private static void LeaveSuzerainWarsForIndependence(War pIndependenceWar,
            Kingdom pRebel, Kingdom pSuzerain)
        {
            if (pIndependenceWar?.data == null || pRebel?.data == null || pSuzerain?.data == null) return;

            List<War> activeWars;
            try { activeWars = pRebel.getWars().ToList(); }
            catch { return; }

            foreach (War war in activeWars)
            {
                if (war?.data == null || war.hasEnded()) continue;
                bool isIndependenceWar = war == pIndependenceWar ||
                                         war.data.id == pIndependenceWar.data.id;
                bool rebelInWar = false;
                bool suzerainInWar = false;
                bool sameSide = false;
                try
                {
                    rebelInWar = war.hasKingdom(pRebel);
                    suzerainInWar = war.hasKingdom(pSuzerain);
                    sameSide = rebelInWar && suzerainInWar && war.onTheSameSide(pRebel, pSuzerain);
                }
                catch { }

                if (!VassalWarSupportRules.ShouldLeaveForIndependence(
                        isIndependenceWar, rebelInWar, suzerainInWar, sameSide))
                    continue;
                LeaveWarPeacefully(war, pRebel);
            }
        }

        private static bool HasActiveIndependenceSuspension(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pVassal?.data == null || pSuzerain?.data == null) return false;
            pVassal.data.get(LineageKeys.VASSAL_INDEPENDENCE_WAR_ID, out long warId, -1L);
            pVassal.data.get(LineageKeys.VASSAL_INDEPENDENCE_SUZERAIN_ID, out long recordedSuzerainId, -1L);
            if (warId < 0 || recordedSuzerainId < 0) return false;

            War war = null;
            try { war = World.world?.wars?.get(warId); }
            catch { }
            bool warActive = war?.data != null && !war.hasEnded();
            Kingdom recordedSuzerain = FindKingdom(recordedSuzerainId);
            bool rebelOpposesRecordedSuzerain = false;
            if (warActive && recordedSuzerain?.data != null)
            {
                try { rebelOpposesRecordedSuzerain = war.isInWarWith(pVassal, recordedSuzerain); }
                catch { }
            }

            bool recordedStateActive = VassalWarSupportRules.HasActiveIndependenceSuspension(
                markerMatches: true,
                warActive: warActive,
                rebelOpposesSuzerain: rebelOpposesRecordedSuzerain);
            if (!recordedStateActive)
            {
                ClearIndependenceSuspension(pVassal);
                return false;
            }

            return VassalWarSupportRules.HasActiveIndependenceSuspension(
                markerMatches: recordedSuzerainId == pSuzerain.id,
                warActive: true,
                rebelOpposesSuzerain: true);
        }

        private static bool WarContainsOnDestroyedSide(War pWar, Kingdom pVassal, WarSideSnapshot pSnapshot)
        {
            try
            {
                if (!pWar.hasKingdom(pVassal)) return false;
            }
            catch { return false; }

            if (pSnapshot.destroyed_was_attacker)
            {
                try { if (pWar.isAttacker(pVassal)) return true; } catch { }
            }

            if (pSnapshot.destroyed_was_defender)
            {
                try { if (pWar.isDefender(pVassal)) return true; } catch { }
            }

            return false;
        }

        private static bool HasActiveWars(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            try
            {
                foreach (War war in pKingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) return true;
            }
            catch { }
            try { return pKingdom.hasEnemies(); }
            catch { return false; }
        }

        private static void LeaveWarPeacefully(War pWar, Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null || pWar.hasEnded()) return;
            try { pWar.removeFromWar(pKingdom, pInPeace: true); }
            catch (Exception e)
            {
                ModClass.LogWarning("VassalService.LeaveWarPeacefully: " + e.Message);
            }
        }

        private static int CountWarriors(Kingdom pKingdom)
        {
            try { return pKingdom?.countTotalWarriors() ?? 0; }
            catch { return 0; }
        }

        private static int CountCities(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }

        private static int Opinion(Kingdom pMain, Kingdom pTarget)
        {
            try { return World.world.diplomacy.getOpinion(pMain, pTarget).total; }
            catch { return 0; }
        }

        private static void RecordVassalSet(Kingdom pVassal, Kingdom pSuzerain, string pReason)
        {
            string reason = VassalSetReasonLabel(pReason);
            HistoryWriter.RecordKingdom(pVassal, "vassal_set",
                KingdomLabel(pVassal) + " " + HistoryText.PlainText(reason) +
                KingdomLabel(pSuzerain) + H("aw_hist_vassal_became_suffix"),
                HistoryTarget.Kingdom(pSuzerain));
            HistoryWriter.RecordKingdom(pSuzerain, "vassal_get",
                KingdomLabel(pSuzerain) + H("aw_hist_vassal_get_mid") + KingdomLabel(pVassal) +
                H("aw_hist_vassal_get_suffix") + H("aw_hist_paren_open") +
                HistoryText.PlainText(VassalGetReasonLabel(pReason)) + H("aw_hist_paren_close"),
                HistoryTarget.Kingdom(pVassal));
        }

        private static void RecordVassalEnd(Kingdom pVassal, Kingdom pSuzerain, string pReason)
        {
            string verb = pReason == "independence_war"
                ? T("aw_hist_vassal_independence_war_verb")
                : T("aw_hist_vassal_left_verb");
            HistoryWriter.RecordKingdom(pVassal, "vassal_end",
                KingdomLabel(pVassal) + " " + verb + " " +
                KingdomLabel(pSuzerain, T("aw_hist_vassal_suzerain_fallback")) +
                H("aw_hist_vassal_independent_suffix"),
                HistoryTarget.Kingdom(pSuzerain));
            if (pSuzerain?.data != null)
                HistoryWriter.RecordKingdom(pSuzerain, "vassal_lost",
                    KingdomLabel(pSuzerain) + H("aw_hist_vassal_lost_mid") + KingdomLabel(pVassal),
                    HistoryTarget.Kingdom(pVassal));
        }

        private static void RecordVassalFell(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pSuzerain?.data == null) return;
            HistoryWriter.RecordKingdom(pSuzerain, "vassal_lost",
                KingdomLabel(pSuzerain) + H("aw_hist_vassal_lost_mid") +
                KingdomLabel(pVassal) + H("aw_hist_vassal_fell_suffix"),
                HistoryTarget.Kingdom(pVassal));
        }

        private static void RecordVassalFreedBySuzerainFall(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pVassal?.data == null) return;
            HistoryWriter.RecordKingdom(pVassal, "vassal_end",
                KingdomLabel(pVassal) + H("aw_hist_vassal_suzerain_fell_mid") +
                KingdomLabel(pSuzerain, T("aw_hist_vassal_suzerain_fallback")) +
                H("aw_hist_vassal_suzerain_fell_suffix"),
                HistoryTarget.Kingdom(pSuzerain));
        }

        private static HistoryText KingdomLabel(Kingdom pKingdom, string pFallbackName = "")
        {
            string name = pKingdom?.name ?? pFallbackName ?? "";
            return HistoryText.Colored(name, HistoryColors.FromKingdom(pKingdom));
        }

        private static string VassalSetReasonLabel(string pReason)
        {
            switch (pReason ?? "")
            {
                case "active_vassal": return T("aw_hist_vassal_set_reason_active");
                case "vassal_war": return T("aw_hist_vassal_set_reason_war");
                case "absorbed_reparent": return T("aw_hist_vassal_set_reason_reparent");
                case "suzerain_fell_reparent": return T("aw_hist_vassal_set_reason_suzerain_fell");
                case "manual": return T("aw_hist_vassal_set_reason_manual");
                default: return T("aw_hist_vassal_set_reason_manual");
            }
        }

        private static string VassalGetReasonLabel(string pReason)
        {
            switch (pReason ?? "")
            {
                case "active_vassal": return T("aw_hist_vassal_get_reason_active");
                case "vassal_war": return T("aw_hist_vassal_get_reason_war");
                case "absorbed_reparent": return T("aw_hist_vassal_get_reason_reparent");
                case "suzerain_fell_reparent": return T("aw_hist_vassal_get_reason_suzerain_fell");
                case "manual": return T("aw_hist_vassal_get_reason_manual");
                default: return T("aw_hist_vassal_get_reason_generic");
            }
        }

        private static bool CloseRelation(long pRelationId, string pReason, bool absorbed)
        {
            long suzerainId = ReadRelationSuzerainIfActive(pRelationId);
            if (suzerainId < 0) return false;
            DB.UpdateValue(VassalRelationTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("RELATION_ID", pRelationId) },
                ColumnVal.Create("END_TIME", LineageService.CurTime()),
                ColumnVal.Create("ACTIVE", 0),
                ColumnVal.Create("ABSORBED", absorbed ? 1 : 0),
                ColumnVal.Create("END_REASON", pReason ?? ""));
            AdjustDirectVassalCount(FindKingdom(suzerainId), -1);
            return true;
        }

        private static long ReadRelationSuzerainIfActive(long pRelationId)
        {
            if (!Ready || pRelationId < 0) return -1L;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT SUZERAIN_ID FROM {VassalRelationTableItem.GetTableName()} " +
                                  "WHERE RELATION_ID=@r AND ACTIVE=1 AND END_TIME<0 LIMIT 1";
                cmd.Parameters.AddWithValue("@r", pRelationId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value);
            }
            catch
            {
                return -1L;
            }
        }

        private static void AdjustDirectVassalCount(Kingdom pSuzerain, int pDelta)
        {
            if (pSuzerain?.data == null || pDelta == 0) return;
            int current = GetDirectVassalCount(pSuzerain);
            pSuzerain.data.set(LineageKeys.VASSAL_DIRECT_COUNT, Math.Max(0, current + pDelta));
        }

        private static int GetCapitalGold(Kingdom pKingdom)
        {
            try
            {
                return Math.Max(0, pKingdom?.capital?.getResourcesAmount("gold") ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        private static int TransferCapitalGold(Kingdom pSource, Kingdom pTarget, int pRequested)
        {
            if (pRequested <= 0 || pSource?.capital == null || pTarget?.capital == null) return 0;
            try
            {
                int actual = Math.Min(pRequested, GetCapitalGold(pSource));
                if (actual <= 0) return 0;
                pSource.capital.takeResource("gold", actual);
                pTarget.capital.addResourcesToRandomStockpile("gold", actual);
                return actual;
            }
            catch
            {
                return 0;
            }
        }

        private static long GetRelationId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1L;
            pKingdom.data.get(LineageKeys.VASSAL_RELATION_ID, out long relationId, -1L);
            return relationId;
        }

        private static double GetRelationStartTime(Kingdom pVassal)
        {
            if (!Ready || pVassal?.data == null) return -1.0;
            long relationId = GetRelationId(pVassal);
            try
            {
                using var cmd = new SQLiteCommand(DB);
                if (relationId >= 0)
                {
                    cmd.CommandText = $"SELECT START_TIME FROM {VassalRelationTableItem.GetTableName()} WHERE RELATION_ID=@r LIMIT 1";
                    cmd.Parameters.AddWithValue("@r", relationId);
                }
                else
                {
                    cmd.CommandText =
                        $"SELECT START_TIME FROM {VassalRelationTableItem.GetTableName()} " +
                        "WHERE VASSAL_ID=@v AND ACTIVE=1 AND END_TIME<0 ORDER BY START_TIME DESC LIMIT 1";
                    cmd.Parameters.AddWithValue("@v", pVassal.id);
                }

                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? -1.0 : Convert.ToDouble(value);
            }
            catch { return -1.0; }
        }

        private static long ReadActiveSuzerainId(long pVassalId)
        {
            if (!Ready || pVassalId < 0) return -1L;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT SUZERAIN_ID FROM {VassalRelationTableItem.GetTableName()} " +
                    "WHERE VASSAL_ID=@v AND ACTIVE=1 AND END_TIME<0 ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@v", pVassalId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value);
            }
            catch { return -1L; }
        }

        private static long ReadActiveRelationId(long pVassalId)
        {
            if (!Ready || pVassalId < 0) return -1L;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT RELATION_ID FROM {VassalRelationTableItem.GetTableName()} " +
                    "WHERE VASSAL_ID=@v AND ACTIVE=1 AND END_TIME<0 ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@v", pVassalId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value);
            }
            catch { return -1L; }
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

        private static string GetWarType(War pWar)
        {
            try { return pWar?.getAsset()?.id ?? ""; }
            catch { return ""; }
        }

        private static void DirtyVassalMap()
        {
            try { VassalMapModeService.DirtyMapIfActive(); }
            catch { }
        }
    }
}
