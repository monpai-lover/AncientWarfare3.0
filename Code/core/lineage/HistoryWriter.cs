using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     编年史统一写入:拼「通用年 + 国家年号」前缀快照,分配自增 event_id,写对应表。
    ///     年份前缀格式:"{Date.getYear}年" + (年号非空 ? " {年号}" : "")。无年号只显通用年。
    ///     前缀写入当时快照 → 日后改年号不影响旧事件显示。
    ///     DB 不可用(OperatingDB==null)则静默跳过 + LogWarning,不崩。
    /// </summary>
    public static class HistoryWriter
    {
        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, string pContent)
        {
            Insert(PersonBiographyTableItem.GetTableName(), pContextKingdom, pEventType, pContent, pSubjectName,
                ColumnVal.Create("ACTOR_ID", pActorId));
        }

        public static void RecordKingdom(Kingdom pKingdom, string pEventType, string pContent)
        {
            if (pKingdom == null) return;
            Insert(KingdomHistoryTableItem.GetTableName(), pKingdom, pEventType, pContent, pKingdom.name,
                ColumnVal.Create("KINGDOM_ID", pKingdom.id));
        }

        public static void RecordCity(City pCity, Kingdom pContextKingdom, string pEventType, string pContent)
        {
            if (pCity == null || pCity.data == null) return;
            Insert(CityHistoryTableItem.GetTableName(), pContextKingdom, pEventType, pContent, pCity.data.name,
                ColumnVal.Create("CITY_ID", pCity.id));
        }

        internal static string BuildYearPrefix(double pTime, Kingdom pKingdom)
        {
            string year = Date.getYear(pTime) + "年";
            string era = pKingdom != null ? YearNameService.GetYearName(pKingdom) : "";
            return string.IsNullOrEmpty(era) ? year : year + " " + era;
        }

        // 公共写入:取时间、拼前缀、分配 event_id、Insert(关联列由调用方传 pKeyCol)。
        private static void Insert(string pTable, Kingdom pContextKingdom,
            string pEventType, string pContent, string pSubjectName, ColumnVal pKeyCol)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null)
            {
                ModClass.LogWarning("HistoryWriter: DB 不可用,事件未记录(" + pTable + "/" + pEventType + ")");
                return;
            }

            double t = World.world.getCurWorldTime();
            string prefix = BuildYearPrefix(t, pContextKingdom);
            long eventId = NextEventId(db, pTable);

            try
            {
                db.Insert(pTable,
                    ColumnVal.Create("EVENT_ID", eventId),
                    pKeyCol,
                    ColumnVal.Create("WORLD_TIME", t),
                    ColumnVal.Create("YEAR_PREFIX", prefix ?? ""),
                    ColumnVal.Create("SUBJECT_NAME", pSubjectName ?? ""),
                    ColumnVal.Create("CONTENT", pContent ?? ""),
                    ColumnVal.Create("EVENT_TYPE", pEventType ?? ""));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("HistoryWriter.Insert 失败(" + pTable + "):" + e.Message);
            }
        }

        // 自增 event_id:取表内 MAX(EVENT_ID)+1。空表返回 1。原生 SQLiteCommand(同 FigureStateStore.Load 模式)。
        private static long NextEventId(SQLiteConnection pDb, string pTable)
        {
            try
            {
                using var cmd = new SQLiteCommand(pDb);
                cmd.CommandText = "SELECT IFNULL(MAX(EVENT_ID), 0) FROM " + pTable;
                object result = cmd.ExecuteScalar();
                long max = (result == null || result == DBNull.Value) ? 0L : Convert.ToInt64(result);
                return max + 1;
            }
            catch
            {
                return 1; // 表尚未建立/异常 → 从 1 起(极早期不会走到写入)
            }
        }
    }
}
