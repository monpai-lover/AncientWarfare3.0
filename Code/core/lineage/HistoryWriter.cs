using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    public readonly struct HistoryTarget
    {
        public readonly string type;
        public readonly long id;

        private HistoryTarget(string pType, long pId)
        {
            type = pType ?? "";
            id = pId;
        }

        public static HistoryTarget Actor(Actor pActor)
        {
            return new HistoryTarget("actor", pActor?.data?.id ?? -1L);
        }

        public static HistoryTarget Actor(long pActorId)
        {
            return new HistoryTarget("actor", pActorId);
        }

        public static HistoryTarget Kingdom(Kingdom pKingdom)
        {
            return new HistoryTarget("kingdom", pKingdom?.id ?? -1L);
        }

        public static HistoryTarget City(City pCity)
        {
            return new HistoryTarget("city", pCity?.id ?? -1L);
        }

        public static HistoryTarget From(string pType, long pId)
        {
            return new HistoryTarget(pType, pId);
        }

        public bool IsValid => !string.IsNullOrEmpty(type) && id >= 0;
    }

    /// <summary>
    ///     编年史统一写入:拼「通用年 + 国家年号」前缀快照,分配自增 event_id,写对应表。
    ///     年份前缀格式:有年号时 "{年号}({通用年}年{月}月{日}日)",无年号时 "{通用年}年{月}月{日}日"。
    ///     前缀写入当时快照 → 日后改年号不影响旧事件显示。
    ///     DB 不可用(OperatingDB==null)则静默跳过 + LogWarning,不崩。
    /// </summary>
    public static class HistoryWriter
    {
        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, string pContent)
        {
            RecordPerson(pActorId, pContextKingdom, pSubjectName, pEventType, HistoryText.PlainText(pContent), "");
        }

        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, HistoryText pContent)
        {
            RecordPerson(pActorId, pContextKingdom, pSubjectName, pEventType, pContent, "");
        }

        /// <summary>带分类(category)的人物事件写入。分类供 UI 筛选(life/honor/clan/war/bond)。</summary>
        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, string pContent, string pCategory)
        {
            RecordPerson(pActorId, pContextKingdom, pSubjectName, pEventType,
                HistoryText.PlainText(pContent), pCategory);
        }

        /// <summary>带分类(category)的人物事件写入。分类供 UI 筛选(life/honor/clan/war/bond)。</summary>
        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, HistoryText pContent, string pCategory)
        {
            RecordPerson(pActorId, pContextKingdom, pSubjectName, pEventType, pContent, pCategory,
                HistoryTarget.Actor(pActorId));
        }

        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, HistoryText pContent, string pCategory, HistoryTarget pTarget)
        {
            Insert(PersonBiographyTableItem.GetTableName(), pContextKingdom, pEventType, pContent, pSubjectName,
                pTarget.IsValid ? pTarget : HistoryTarget.Actor(pActorId),
                ColumnVal.Create("ACTOR_ID", pActorId),
                ColumnVal.Create("CATEGORY", pCategory ?? ""));
        }

        public static void RecordKingdom(Kingdom pKingdom, string pEventType, string pContent)
        {
            RecordKingdom(pKingdom, pEventType, HistoryText.PlainText(pContent));
        }

        public static void RecordKingdom(Kingdom pKingdom, string pEventType, HistoryText pContent)
        {
            RecordKingdom(pKingdom, pEventType, pContent, HistoryTarget.Kingdom(pKingdom));
        }

        public static void RecordKingdom(Kingdom pKingdom, string pEventType, HistoryText pContent, HistoryTarget pTarget)
        {
            if (pKingdom == null) return;
            Insert(KingdomHistoryTableItem.GetTableName(), pKingdom, pEventType, pContent, pKingdom.name,
                pTarget.IsValid ? pTarget : HistoryTarget.Kingdom(pKingdom),
                ColumnVal.Create("KINGDOM_ID", pKingdom.id));
        }

        public static void RecordCity(City pCity, Kingdom pContextKingdom, string pEventType, string pContent)
        {
            RecordCity(pCity, pContextKingdom, pEventType, HistoryText.PlainText(pContent));
        }

        public static void RecordCity(City pCity, Kingdom pContextKingdom, string pEventType, HistoryText pContent)
        {
            RecordCity(pCity, pContextKingdom, pEventType, pContent, HistoryTarget.City(pCity));
        }

        public static void RecordCity(City pCity, Kingdom pContextKingdom, string pEventType, HistoryText pContent, HistoryTarget pTarget)
        {
            if (pCity == null || pCity.data == null) return;
            // 额外写 KINGDOM_NAME 快照(该事件时城市所属国名),供城市史"归属期"分段切段用。
            string kingdomName = pContextKingdom != null ? pContextKingdom.name : "";
            Insert(CityHistoryTableItem.GetTableName(), pContextKingdom, pEventType, pContent, pCity.data.name,
                pTarget.IsValid ? pTarget : HistoryTarget.City(pCity),
                ColumnVal.Create("CITY_ID", pCity.id),
                ColumnVal.Create("KINGDOM_NAME", kingdomName ?? ""),
                ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pContextKingdom)));
        }

        internal static string BuildYearPrefix(double pTime, Kingdom pKingdom)
        {
            string date = FormatDate(pTime);
            string era = pKingdom != null ? YearNameService.GetYearName(pKingdom) : "";
            return string.IsNullOrEmpty(era) ? date : era + "(" + date + ")";
        }

        internal static string BuildYearPrefixRich(double pTime, Kingdom pKingdom)
        {
            string prefix = BuildYearPrefix(pTime, pKingdom);
            string color = HistoryColors.FromKingdom(pKingdom);
            return string.IsNullOrEmpty(color)
                ? HistoryColors.EscapeRich(prefix)
                : HistoryText.Colored(prefix, color).Rich;
        }

        internal static string FormatDate(double pTime)
        {
            int[] raw = Date.getRawDate(pTime); // [day, month, year]
            return raw[2] + "年" + raw[1] + "月" + raw[0] + "日";
        }

        internal static string NormalizeYearPrefix(string pYearPrefixSnapshot, double pTime)
        {
            string date = FormatDate(pTime);
            if (string.IsNullOrEmpty(pYearPrefixSnapshot)) return date;
            if (pYearPrefixSnapshot.Contains("(")) return pYearPrefixSnapshot;

            string trimmed = pYearPrefixSnapshot.Trim();
            string yearOnly = Date.getYear(pTime) + "年";
            if (trimmed == yearOnly) return date;

            int space = trimmed.LastIndexOf(' ');
            if (space >= 0 && space < trimmed.Length - 1)
            {
                string era = trimmed.Substring(space + 1);
                return era + "(" + date + ")";
            }
            return trimmed + "(" + date + ")";
        }

        // 公共写入:取时间、拼前缀、分配 event_id、Insert(关联列 + 表专属额外列由调用方传 pExtraCols)。
        private static void Insert(string pTable, Kingdom pContextKingdom,
            string pEventType, HistoryText pContent, string pSubjectName, HistoryTarget pTarget, params ColumnVal[] pExtraCols)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null)
            {
                ModClass.LogWarning("HistoryWriter: DB 不可用,事件未记录(" + pTable + "/" + pEventType + ")");
                return;
            }

            double t = World.world.getCurWorldTime();
            string prefix = BuildYearPrefix(t, pContextKingdom);
            string prefixRich = BuildYearPrefixRich(t, pContextKingdom);
            long eventId = NextEventId(db, pTable);
            string contextName = pContextKingdom != null ? pContextKingdom.name : "";
            string contextColor = HistoryColors.FromKingdom(pContextKingdom);
            HistoryTarget target = ResolveTarget(pTarget, pContent);

            try
            {
                var cols = new System.Collections.Generic.List<ColumnVal>
                {
                    ColumnVal.Create("EVENT_ID", eventId),
                    ColumnVal.Create("WORLD_TIME", t),
                    ColumnVal.Create("YEAR_PREFIX", prefix ?? ""),
                    ColumnVal.Create("YEAR_PREFIX_RICH", prefixRich ?? ""),
                    ColumnVal.Create("SUBJECT_NAME", pSubjectName ?? ""),
                    ColumnVal.Create("SUBJECT_COLOR", contextColor),
                    ColumnVal.Create("CONTENT", pContent.Plain ?? ""),
                    ColumnVal.Create("CONTENT_RICH", pContent.Rich ?? ""),
                    ColumnVal.Create("EVENT_TYPE", pEventType ?? ""),
                    ColumnVal.Create("CONTEXT_KINGDOM_ID", pContextKingdom != null ? pContextKingdom.id : -1L),
                    ColumnVal.Create("CONTEXT_KINGDOM_NAME", contextName ?? ""),
                    ColumnVal.Create("CONTEXT_KINGDOM_COLOR", contextColor),
                    ColumnVal.Create("TARGET_TYPE", target.IsValid ? target.type : ""),
                    ColumnVal.Create("TARGET_ID", target.IsValid ? target.id : -1L)
                };
                if (pExtraCols != null) cols.AddRange(pExtraCols);
                db.Insert(pTable, cols.ToArray());
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
        private static HistoryTarget ResolveTarget(HistoryTarget pExplicit, HistoryText pContent)
        {
            if (!string.IsNullOrEmpty(pContent.TargetType) && pContent.TargetId >= 0)
                return HistoryTarget.From(pContent.TargetType, pContent.TargetId);
            return pExplicit;
        }
    }
}
