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
            return GetVassals(pKingdom).Count > 0;
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
            if (dataId >= 0) return dataId;
            return ReadActiveSuzerainId(pKingdom.id);
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
            bool basicValid = pVassal?.data != null &&
                              pSuzerain?.data != null &&
                              pVassal != pSuzerain &&
                              !pVassal.isRekt() &&
                              !pSuzerain.isRekt() &&
                              pVassal.isCiv() &&
                              pSuzerain.isCiv();
            if (!basicValid) return false;

            bool titleAbove = KingdomTitleService.GetTitle(pSuzerain) > KingdomTitleService.GetTitle(pVassal);
            bool cycleDetected = WouldCreateCycle(pVassal, pSuzerain);
            return VassalRelationRules.CanSetVassal(
                basicValid,
                MandateRebelService.IsRebelKingdom(pVassal),
                MandateRebelService.IsRebelKingdom(pSuzerain),
                titleAbove,
                cycleDetected,
                out _);
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

        public static bool SetVassal(Kingdom pVassal, Kingdom pSuzerain, string pReason = "manual", long pWarId = -1)
        {
            if (!CanSetVassal(pVassal, pSuzerain)) return false;
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

            CloseRelation(relationId, pReason ?? "ended", absorbed: false);
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
                KingdomLabel(pSuzerain) + " \u541E\u5E76\u9644\u5EB8 " + KingdomLabel(pVassal),
                HistoryTarget.Kingdom(pVassal));
            HistoryWriter.RecordKingdom(pVassal, "vassal_absorbed",
                KingdomLabel(pVassal) + " \u88AB " + KingdomLabel(pSuzerain) + " \u541E\u5E76",
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
                JoinLoyalVassalsToDefenders(pWar, defender, attacker);
                return;
            }

            Kingdom attackerRoot = GetRootSuzerain(attacker);
            Kingdom defenderRoot = GetRootSuzerain(defender);
            if (attackerRoot != null && defenderRoot != null && attackerRoot == defenderRoot) return;

            JoinNetwork(pWar, attackerRoot ?? attacker, attacker, defender, attackers: true);
            JoinNetwork(pWar, defenderRoot ?? defender, defender, attacker, attackers: false);
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null) return;
            string type = GetWarType(pWar);
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();

            if (type == "vassal_war" && pWinner == WarWinner.Attackers)
            {
                SetVassal(defender, attacker, "vassal_war", pWar.data.id);
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
                List<Kingdom> vassals = GetVassals(pKingdom, pRecursive: true);
                if (vassals.Count == 0) return;

                foreach (War war in pKingdom.getWars())
                    PullVassalsIntoSuzerainWar(war, pKingdom, vassals);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("VassalService.OnKingdomYear: " + e.Message);
            }
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
                row.suzerain_id = relation.suzerain_id;
                row.suzerain_name = relation.suzerain_name;
                row.suzerain_color = relation.suzerain_color;
                row.relation_type = relation.relation_type;
                row.relation_reason_label = VassalGetReasonLabel(relation.relation_type);
                row.autonomy = relation.autonomy;
                row.tribute_rate = relation.tribute_rate;
                row.military_obligation = relation.military_obligation;
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

        private static void JoinLoyalVassalsToDefenders(War pWar, Kingdom pSuzerain, Kingdom pRebel)
        {
            foreach (Kingdom vassal in GetVassals(pSuzerain))
            {
                if (vassal == null || vassal == pRebel || vassal.isRekt() || vassal.hasEnemies()) continue;
                if (Opinion(vassal, pSuzerain) < -50) continue;
                JoinSide(pWar, vassal, attackers: false);
            }
        }

        private static void JoinNetwork(War pWar, Kingdom pRoot, Kingdom pMain, Kingdom pEnemy, bool attackers)
        {
            if (pRoot?.data == null) return;
            if (pRoot != pMain && pRoot != pEnemy) JoinSide(pWar, pRoot, attackers);
            foreach (Kingdom vassal in GetVassals(pRoot, pRecursive: true))
            {
                if (vassal == pMain || vassal == pEnemy || vassal.isRekt()) continue;
                JoinSide(pWar, vassal, attackers);
            }
        }

        private static void PullVassalsIntoSuzerainWar(War pWar, Kingdom pSuzerain, List<Kingdom> pVassals)
        {
            if (pWar?.data == null || pWar.hasEnded() || pSuzerain?.data == null || pVassals == null) return;

            bool suzerainAttacker = false;
            bool suzerainDefender = false;
            try { suzerainAttacker = pWar.isAttacker(pSuzerain); } catch { }
            try { suzerainDefender = pWar.isDefender(pSuzerain); } catch { }
            if (!suzerainAttacker && !suzerainDefender) return;

            foreach (Kingdom vassal in pVassals)
            {
                if (vassal?.data == null || vassal == pSuzerain || vassal.isRekt()) continue;

                bool vassalAttacker = false;
                bool vassalDefender = false;
                try { vassalAttacker = pWar.isAttacker(vassal); } catch { }
                try { vassalDefender = pWar.isDefender(vassal); } catch { }

                bool vassalHelping = suzerainAttacker ? vassalAttacker : vassalDefender;
                bool vassalOpposes = suzerainAttacker ? vassalDefender : vassalAttacker;
                bool vassalInWar = vassalAttacker || vassalDefender;
                if (!VassalWarSupportRules.ShouldPullIntoSuzerainWar(
                        pSuzerainInWar: true,
                        pVassalAlreadyHelping: vassalHelping,
                        pVassalAlreadyInWar: vassalInWar,
                        pVassalOpposesSuzerain: vassalOpposes))
                    continue;

                JoinSide(pWar, vassal, suzerainAttacker);
            }
        }

        private static void PullVassalIntoSuzerainWars(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pVassal?.data == null || pSuzerain?.data == null || pSuzerain.isRekt()) return;
            try
            {
                var vassals = new List<Kingdom> { pVassal };
                foreach (War war in pSuzerain.getWars())
                    PullVassalsIntoSuzerainWar(war, pSuzerain, vassals);
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
                KingdomLabel(pSuzerain) + "\uFF0C\u6210\u4E3A\u9644\u5EB8",
                HistoryTarget.Kingdom(pSuzerain));
            HistoryWriter.RecordKingdom(pSuzerain, "vassal_get",
                KingdomLabel(pSuzerain) + " \u6536 " + KingdomLabel(pVassal) +
                " \u4E3A\u9644\u5EB8\uFF08" + HistoryText.PlainText(VassalGetReasonLabel(pReason)) + "\uFF09",
                HistoryTarget.Kingdom(pVassal));
        }

        private static void RecordVassalEnd(Kingdom pVassal, Kingdom pSuzerain, string pReason)
        {
            string verb = pReason == "independence_war" ? "\u901A\u8FC7\u72EC\u7ACB\u6218\u4E89\u8131\u79BB" : "\u8131\u79BB";
            HistoryWriter.RecordKingdom(pVassal, "vassal_end",
                KingdomLabel(pVassal) + " " + verb + " " +
                KingdomLabel(pSuzerain, "\u5B97\u4E3B\u56FD") + " \u72EC\u7ACB",
                HistoryTarget.Kingdom(pSuzerain));
            if (pSuzerain?.data != null)
                HistoryWriter.RecordKingdom(pSuzerain, "vassal_lost",
                    KingdomLabel(pSuzerain) + " \u5931\u53BB\u9644\u5EB8 " + KingdomLabel(pVassal),
                    HistoryTarget.Kingdom(pVassal));
        }

        private static void RecordVassalFell(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pSuzerain?.data == null) return;
            HistoryWriter.RecordKingdom(pSuzerain, "vassal_lost",
                KingdomLabel(pSuzerain) + " \u5931\u53BB\u9644\u5EB8 " +
                KingdomLabel(pVassal) + "\uFF08\u4EA1\u56FD\uFF09",
                HistoryTarget.Kingdom(pVassal));
        }

        private static void RecordVassalFreedBySuzerainFall(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pVassal?.data == null) return;
            HistoryWriter.RecordKingdom(pVassal, "vassal_end",
                KingdomLabel(pVassal) + " \u56E0\u5B97\u4E3B " +
                KingdomLabel(pSuzerain, "\u5B97\u4E3B\u56FD") +
                " \u706D\u4EA1\u800C\u6062\u590D\u72EC\u7ACB",
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
                case "active_vassal": return "\u56E0\u5916\u90E8\u5A01\u80C1\u4E3B\u52A8\u81E3\u5C5E\u4E8E ";
                case "vassal_war": return "\u6218\u8D25\u540E\u81E3\u5C5E\u4E8E ";
                case "absorbed_reparent": return "\u56E0\u9644\u5EB8\u4F53\u7CFB\u91CD\u6574\u8F6C\u81E3\u5C5E\u4E8E ";
                case "suzerain_fell_reparent": return "\u56E0\u65E7\u5B97\u4E3B\u706D\u4EA1\u6539\u81E3\u5C5E\u4E8E ";
                case "manual": return "\u81E3\u5C5E\u4E8E ";
                default: return "\u81E3\u5C5E\u4E8E ";
            }
        }

        private static string VassalGetReasonLabel(string pReason)
        {
            switch (pReason ?? "")
            {
                case "active_vassal": return "\u4E3B\u52A8\u81E3\u5C5E";
                case "vassal_war": return "\u6218\u4E89\u81E3\u670D";
                case "absorbed_reparent": return "\u4F53\u7CFB\u91CD\u6574";
                case "suzerain_fell_reparent": return "\u6539\u6295\u5B97\u4E3B";
                case "manual": return "\u4E0A\u5E1D\u8BBE\u5B9A";
                default: return "\u81E3\u5C5E";
            }
        }

        private static void CloseRelation(long pRelationId, string pReason, bool absorbed)
        {
            DB.UpdateValue(VassalRelationTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("RELATION_ID", pRelationId) },
                ColumnVal.Create("END_TIME", LineageService.CurTime()),
                ColumnVal.Create("ACTIVE", 0),
                ColumnVal.Create("ABSORBED", absorbed ? 1 : 0),
                ColumnVal.Create("END_REASON", pReason ?? ""));
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
