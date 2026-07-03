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

        public static bool IsVassalKingdom(Kingdom pKingdom)
        {
            return GetSuzerainId(pKingdom) >= 0;
        }

        public static bool IsSuzerain(Kingdom pKingdom)
        {
            return GetVassals(pKingdom).Count > 0;
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

        public static bool CanSetVassal(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pVassal?.data == null || pSuzerain?.data == null) return false;
            if (pVassal == pSuzerain) return false;
            if (pVassal.isRekt() || pSuzerain.isRekt()) return false;
            if (!pVassal.isCiv() || !pSuzerain.isCiv()) return false;

            Kingdom root = GetRootSuzerain(pSuzerain);
            if (root == pVassal) return false;

            Kingdom current = pSuzerain;
            var visited = new HashSet<long>();
            while (current?.data != null && visited.Add(current.id))
            {
                if (current == pVassal) return false;
                current = GetSuzerain(current);
            }

            return true;
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
            RecordVassalSet(pVassal, pSuzerain);
            DirtyVassalMap();
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
                HistoryText.Kingdom(pSuzerain) + " \u541E\u5E76\u9644\u5EB8 " + HistoryText.Kingdom(pVassal),
                HistoryTarget.Kingdom(pVassal));
            HistoryWriter.RecordKingdom(pVassal, "vassal_absorbed",
                HistoryText.Kingdom(pVassal) + " \u88AB " + HistoryText.Kingdom(pSuzerain) + " \u541E\u5E76",
                HistoryTarget.Kingdom(pSuzerain));
            DirtyVassalMap();
            return true;
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
                EndVassal(attacker, "independence_war");
                return;
            }

            if (type == "reclaim" && pWinner == WarWinner.Attackers)
                RoyalClaimService.OnReclaimWarWon(attacker, defender, pWar.data.id);
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
            return root.getColor() ?? pFallback;
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

        private static void RecordVassalSet(Kingdom pVassal, Kingdom pSuzerain)
        {
            HistoryWriter.RecordKingdom(pVassal, "vassal_set",
                HistoryText.Kingdom(pVassal) + " \u81E3\u5C5E\u4E8E " + HistoryText.Kingdom(pSuzerain),
                HistoryTarget.Kingdom(pSuzerain));
            HistoryWriter.RecordKingdom(pSuzerain, "vassal_get",
                HistoryText.Kingdom(pSuzerain) + " \u6536 " + HistoryText.Kingdom(pVassal) + " \u4E3A\u9644\u5EB8",
                HistoryTarget.Kingdom(pVassal));
        }

        private static void RecordVassalEnd(Kingdom pVassal, Kingdom pSuzerain, string pReason)
        {
            string verb = pReason == "independence_war" ? "\u901A\u8FC7\u72EC\u7ACB\u6218\u4E89\u8131\u79BB" : "\u8131\u79BB";
            HistoryWriter.RecordKingdom(pVassal, "vassal_end",
                HistoryText.Kingdom(pVassal) + " " + verb + " " +
                HistoryText.Kingdom(pSuzerain, "\u5B97\u4E3B\u56FD") + " \u72EC\u7ACB",
                HistoryTarget.Kingdom(pSuzerain));
            if (pSuzerain?.data != null)
                HistoryWriter.RecordKingdom(pSuzerain, "vassal_lost",
                    HistoryText.Kingdom(pSuzerain) + " \u5931\u53BB\u9644\u5EB8 " + HistoryText.Kingdom(pVassal),
                    HistoryTarget.Kingdom(pVassal));
        }

        private static void RecordVassalFell(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pSuzerain?.data == null) return;
            HistoryWriter.RecordKingdom(pSuzerain, "vassal_lost",
                HistoryText.Kingdom(pSuzerain) + " \u5931\u53BB\u9644\u5EB8 " +
                HistoryText.Kingdom(pVassal) + "\uFF08\u4EA1\u56FD\uFF09",
                HistoryTarget.Kingdom(pVassal));
        }

        private static void RecordVassalFreedBySuzerainFall(Kingdom pVassal, Kingdom pSuzerain)
        {
            if (pVassal?.data == null) return;
            HistoryWriter.RecordKingdom(pVassal, "vassal_end",
                HistoryText.Kingdom(pVassal) + " \u56E0\u5B97\u4E3B " +
                HistoryText.Kingdom(pSuzerain, "\u5B97\u4E3B\u56FD") +
                " \u706D\u4EA1\u800C\u6062\u590D\u72EC\u7ACB",
                HistoryTarget.Kingdom(pSuzerain));
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
