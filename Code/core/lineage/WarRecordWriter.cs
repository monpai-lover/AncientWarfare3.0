using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     战争记录写入:newWar 时 INSERT 一行(end=-1),newKillAction 时内存累加,endWar 时 UPDATE。
    ///     内存活跃缓存 (_active) 随游戏会话存在;读档后调 BackfillActive 重建(kills 归零可接受)。
    ///     WarRecord 表供谥号战绩统计(该王在位期间 winner/loser 次数加权)。
    /// </summary>
    internal static class WarRecordWriter
    {
        // 活跃战争缓存:War 对象 → (war_id, attacker kingdom id, defender kingdom id)
        private struct WarLive
        {
            public long war_id;
            public long attacker_id;
            public long defender_id;
            public int  attacker_kills;
            public int  defender_kills;
        }

        private static readonly Dictionary<War, WarLive> _active = new Dictionary<War, WarLive>();

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        // ──────────────── 外部接口 ────────────────

        /// <summary>战争开始:立即 INSERT 一行(end=-1)并加入活跃缓存。</summary>
        public static void OnWarStart(War pWar)
        {
            if (pWar?.data == null || !Ready) return;

            Kingdom atk = pWar.getMainAttacker();
            Kingdom def = pWar.getMainDefender();
            if (atk == null || def == null) return;

            long warId = pWar.data.id;
            string atkName = atk.name ?? "";
            string defName = def.name ?? "";
            string atkColor = HistoryColors.FromKingdom(atk);
            string defColor = HistoryColors.FromKingdom(def);
            string warType = pWar.getAsset()?.id ?? "";
            int isRebellion = (pWar.getAsset()?.rebellion == true) ? 1 : 0;
            double now = World.world.getCurWorldTime();
            string prefix = HistoryWriter.BuildYearPrefix(now, atk);

            string table = WarRecordTableItem.GetTableName();
            try
            {
                DB.Insert(table,
                    ColumnVal.Create("WAR_ID",              warId),
                    ColumnVal.Create("ATTACKER_KINGDOM_ID", atk.id),
                    ColumnVal.Create("DEFENDER_KINGDOM_ID", def.id),
                    ColumnVal.Create("ATTACKER_NAME",       atkName),
                    ColumnVal.Create("ATTACKER_COLOR",      atkColor),
                    ColumnVal.Create("DEFENDER_NAME",       defName),
                    ColumnVal.Create("DEFENDER_COLOR",      defColor),
                    ColumnVal.Create("WAR_TYPE",            warType),
                    ColumnVal.Create("IS_REBELLION",        isRebellion),
                    ColumnVal.Create("START_TIME",          now),
                    ColumnVal.Create("END_TIME",            -1.0),
                    ColumnVal.Create("WINNER",              ""),
                    ColumnVal.Create("WINNER_COLOR",        ""),
                    ColumnVal.Create("ATTACKER_KILLS",      0),
                    ColumnVal.Create("DEFENDER_KILLS",      0),
                    ColumnVal.Create("YEAR_PREFIX",         prefix ?? ""));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarRecordWriter.OnWarStart 失败:" + e.Message);
                return;
            }

            _active[pWar] = new WarLive
            {
                war_id       = warId,
                attacker_id  = atk.id,
                defender_id  = def.id,
                attacker_kills = 0,
                defender_kills = 0
            };
        }

        /// <summary>击杀累加:由 AW_KillPatch 在 newKillAction 后调用。</summary>
        public static void AddKill(Actor pKiller, Actor pDead, Kingdom pDeadPrevKingdom)
        {
            if (pKiller?.data == null || pDead?.data == null) return;
            if (_active.Count == 0) return;
            Kingdom killerKingdom = pKiller.kingdom;
            if (killerKingdom == null || pDeadPrevKingdom == null) return;

            War matchedWar = null;
            WarLive matchedLive = default;
            foreach (var pair in _active)
            {
                War war = pair.Key;
                WarLive live = pair.Value;
                if (war == null) continue;

                if (killerKingdom.id == live.attacker_id && pDeadPrevKingdom.id == live.defender_id)
                {
                    live.attacker_kills++;
                    matchedWar = war;
                    matchedLive = live;
                    break;
                }

                if (killerKingdom.id == live.defender_id && pDeadPrevKingdom.id == live.attacker_id)
                {
                    live.defender_kills++;
                    matchedWar = war;
                    matchedLive = live;
                    break;
                }
            }

            if (matchedWar != null) _active[matchedWar] = matchedLive;
        }

        /// <summary>战争结束:UPDATE end/winner/kills,清缓存。</summary>
        public static void OnWarEnd(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null || !Ready) return;

            _active.TryGetValue(pWar, out WarLive live);
            long warId = live.war_id > 0 ? live.war_id : pWar.data.id;

            string table = WarRecordTableItem.GetTableName();
            string winnerStr = WinnerToString(pWinner);
            string winnerColor = WinnerColor(pWinner, pWar.getMainAttacker(), pWar.getMainDefender());
            double now = World.world.getCurWorldTime();

            try
            {
                DB.UpdateValue(table,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("WAR_ID", warId) },
                    ColumnVal.Create("END_TIME",       now),
                    ColumnVal.Create("WINNER",         winnerStr),
                    ColumnVal.Create("WINNER_COLOR",   winnerColor),
                    ColumnVal.Create("ATTACKER_KILLS", live.attacker_kills),
                    ColumnVal.Create("DEFENDER_KILLS", live.defender_kills));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarRecordWriter.OnWarEnd 失败:" + e.Message);
            }

            _active.Remove(pWar);
        }

        /// <summary>读档后重建活跃缓存(当前进行中的战争,kills 归零可接受)。</summary>
        public static void BackfillActive()
        {
            _active.Clear();
            if (!Ready || World.world?.wars == null) return;
            foreach (War war in World.world.wars)
            {
                if (war?.data == null) continue;
                Kingdom atk = war.getMainAttacker();
                Kingdom def = war.getMainDefender();
                if (atk == null || def == null) continue;
                _active[war] = new WarLive
                {
                    war_id      = war.data.id,
                    attacker_id = atk.id,
                    defender_id = def.id
                };
            }
        }

        // ──────────────── 谥号查询接口 ────────────────

        /// <summary>
        ///     查某王国在 [start, end) 期间的战绩(胜/败场次,攻守合计)。
        ///     供 PosthumousTitleService 综合国力评谥用。
        /// </summary>
        public static (int wins, int losses) GetWarRecord(long pKingdomId, double pStart, double pEnd)
        {
            if (!Ready) return (0, 0);
            int wins = 0, losses = 0;
            string table = WarRecordTableItem.GetTableName();
            try
            {
                string endCond = pEnd > 0 ? $"AND START_TIME < {pEnd}" : "";
                string sql =
                    $"SELECT ATTACKER_KINGDOM_ID, DEFENDER_KINGDOM_ID, WINNER " +
                    $"FROM {table} WHERE START_TIME >= {pStart} {endCond} AND END_TIME >= 0";
                using var cmd = new SQLiteCommand(sql, DB);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    long atkId  = reader.GetInt64(0);
                    long defId  = reader.GetInt64(1);
                    string w    = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    bool isAtk  = atkId == pKingdomId;
                    bool isDef  = defId == pKingdomId;
                    if (!isAtk && !isDef) continue;

                    bool won = (isAtk && w == "attackers") || (isDef && w == "defenders");
                    bool lost = (isAtk && w == "defenders") || (isDef && w == "attackers");
                    if (won)  wins++;
                    if (lost) losses++;
                }
            }
            catch { /* 查不到不崩 */ }
            return (wins, losses);
        }

        // ──────────────── 内部辅助 ────────────────

        private static War FindWarForKingdom(Kingdom pKingdom)
        {
            if (pKingdom == null || World.world?.wars == null) return null;
            foreach (War w in World.world.wars)
            {
                if (w == null) continue;
                if (w.isMainAttacker(pKingdom) || w.isMainDefender(pKingdom)) return w;
            }
            return null;
        }

        /// <summary>供 AW_WarPatch 生成国家史内容用的胜负描述（含国名）。</summary>
        public static string WinnerLabel(WarWinner pWinner, Kingdom pAtk, Kingdom pDef)
        {
            string atkName = pAtk?.name ?? "攻方";
            string defName = pDef?.name ?? "守方";
            switch (pWinner)
            {
                case WarWinner.Attackers: return atkName + " 胜";
                case WarWinner.Defenders: return defName + " 胜";
                case WarWinner.Peace:     return "议和";
                case WarWinner.Merged:    return "合并";
                default:                  return "不分胜负";
            }
        }

        public static HistoryText WinnerLabelRich(WarWinner pWinner, Kingdom pAtk, Kingdom pDef)
        {
            switch (pWinner)
            {
                case WarWinner.Attackers:
                    return HistoryText.Kingdom(pAtk, "攻方") + " 胜";
                case WarWinner.Defenders:
                    return HistoryText.Kingdom(pDef, "守方") + " 胜";
                case WarWinner.Peace:
                    return "议和";
                case WarWinner.Merged:
                    return "合并";
                default:
                    return "不分胜负";
            }
        }

        private static string WinnerColor(WarWinner pWinner, Kingdom pAtk, Kingdom pDef)
        {
            switch (pWinner)
            {
                case WarWinner.Attackers: return HistoryColors.FromKingdom(pAtk);
                case WarWinner.Defenders: return HistoryColors.FromKingdom(pDef);
                default: return "";
            }
        }

        private static string WinnerToString(WarWinner pWinner)
        {
            switch (pWinner)
            {
                case WarWinner.Attackers: return "attackers";
                case WarWinner.Defenders: return "defenders";
                case WarWinner.Peace:     return "peace";
                case WarWinner.Merged:    return "merged";
                default:                  return "nobody";
            }
        }
    }
}
