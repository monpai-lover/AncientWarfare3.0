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
            return new HistoryTarget("kingdom", pKingdom?.data?.id ?? -1L);
        }

        public static HistoryTarget City(City pCity)
        {
            return new HistoryTarget("city", pCity?.data?.id ?? -1L);
        }

        public static HistoryTarget From(string pType, long pId)
        {
            return new HistoryTarget(pType, pId);
        }

        public bool IsValid => !string.IsNullOrEmpty(type) && id >= 0;
    }

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

        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, string pContent, string pCategory)
        {
            RecordPerson(pActorId, pContextKingdom, pSubjectName, pEventType,
                HistoryText.PlainText(pContent), pCategory);
        }

        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, HistoryText pContent, string pCategory)
        {
            RecordPerson(pActorId, pContextKingdom, pSubjectName, pEventType, pContent, pCategory,
                HistoryTarget.Actor(pActorId));
        }

        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, HistoryText pContent, string pCategory, HistoryTarget pTarget)
        {
            var extra = new System.Collections.Generic.List<ColumnVal>
            {
                ColumnVal.Create("ACTOR_ID", pActorId),
                ColumnVal.Create("CATEGORY", pCategory ?? "")
            };
            extra.AddRange(BuildPersonSnapshotColumns(pActorId));

            Insert(PersonBiographyTableItem.GetTableName(), pContextKingdom, pEventType, pContent, pSubjectName,
                pTarget.IsValid ? pTarget : HistoryTarget.Actor(pActorId),
                extra.ToArray());
        }

        private static ColumnVal[] BuildPersonSnapshotColumns(long pActorId)
        {
            int age = -1;
            int isKing = 0;
            string role = "";

            Actor actor = GetActor(pActorId);
            if (actor?.data != null)
            {
                try { age = actor.getAge(); } catch { age = -1; }
                try { isKing = actor.isKing() ? 1 : 0; } catch { isKing = 0; }
                role = ResolveRoleSnapshot(actor);
            }

            return new[]
            {
                ColumnVal.Create("AGE_AT_EVENT", age),
                ColumnVal.Create("IS_KING_AT_EVENT", isKing),
                ColumnVal.Create("ROLE_SNAPSHOT", role ?? ""),
                ColumnVal.Create("ROLE_LABEL", RoleLabel(role))
            };
        }

        private static Actor GetActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static string ResolveRoleSnapshot(Actor pActor)
        {
            if (pActor?.data == null) return "";
            try { if (pActor.isKing()) return "king"; } catch { }
            try
            {
                pActor.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
                if (isHeir || HeirService.IsCurrentHeir(pActor.kingdom, pActor))
                    return HeirTitleRules.RoleSnapshot(MandateService.IsMandateKingdom(pActor.kingdom));
            }
            catch { }
            try
            {
                pActor.data.get(LineageKeys.ROYAL_GUARD_CAPTAIN, out bool captain, false);
                if (captain) return "royal_guard_captain";
                pActor.data.get(LineageKeys.ROYAL_GUARD, out bool guard, false);
                if (guard) return "royal_guard";
            }
            catch { }

            try
            {
                if (GeneralService.IsFiefHolder(pActor)) return "fief_holder";
                if (GeneralService.IsGeneral(pActor)) return "general";
            }
            catch { }

            try { if (pActor.isCityLeader()) return "city_leader"; } catch { }
            try { if (pActor.clan != null && pActor.clan.getChief() == pActor) return "clan_chief"; } catch { }

            try
            {
                if (pActor.hasTrait(LineageKeys.TRAIT_SLAVE)) return "slave";
                pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
                if (status == LineageStatus.SLAVE) return "slave";
                if (pActor.isWarrior()) return "warrior";
                if (status == LineageStatus.NOBLE || ChronicleGate.IsNobleActor(pActor)) return "noble";
                if (status == LineageStatus.COMMON) return "common_lineage";
            }
            catch { }

            try { if (pActor.isWarrior()) return "warrior"; } catch { }
            return "common";
        }

        private static string RoleLabel(string pRole)
        {
            switch (pRole)
            {
                case "king": return "\u541b\u4e3b";
                case "heir_shizi": return "\u4e16\u5b50";
                case "heir_taizi": return "\u592a\u5b50";
                case "city_leader": return "\u57ce\u4e3b";
                case "clan_chief": return "\u6c0f\u65cf\u5bb6\u4e3b";
                case "royal_guard_captain": return "\u7981\u536b\u519b\u7edf\u9886";
                case "royal_guard": return "\u7981\u536b\u519b";
                case "fief_holder": return "\u5c01\u5730\u5927\u5c06";
                case "general": return "\u5927\u5c06";
                case "slave": return "\u5974\u96b6";
                case "warrior": return "\u58eb\u5175";
                case "noble": return "\u8d35\u65cf";
                case "common_lineage": return "\u6709\u6c0f\u5e73\u6c11";
                case "common": return "\u5e73\u6c11";
                default: return "";
            }
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
            if (pKingdom?.data == null) return;
            Insert(KingdomHistoryTableItem.GetTableName(), pKingdom, pEventType, pContent, pKingdom.name,
                pTarget.IsValid ? pTarget : HistoryTarget.Kingdom(pKingdom),
                ColumnVal.Create("KINGDOM_ID", pKingdom.data.id));
        }

        public static void RecordCity(City pCity, Kingdom pContextKingdom, string pEventType, string pContent)
        {
            RecordCity(pCity, pContextKingdom, pEventType, HistoryText.PlainText(pContent));
        }

        public static void RecordCity(City pCity, Kingdom pContextKingdom, string pEventType, HistoryText pContent)
        {
            RecordCity(pCity, pContextKingdom, pEventType, pContent, HistoryTarget.City(pCity));
        }

        public static void RecordCity(City pCity, Kingdom pContextKingdom, string pEventType, HistoryText pContent,
            HistoryTarget pTarget)
        {
            if (pCity?.data == null) return;
            string kingdomName = pContextKingdom?.data != null ? pContextKingdom.name : "";
            Insert(CityHistoryTableItem.GetTableName(), pContextKingdom, pEventType, pContent, pCity.data.name,
                pTarget.IsValid ? pTarget : HistoryTarget.City(pCity),
                ColumnVal.Create("CITY_ID", pCity.data.id),
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
            return ChronicleFormatRules.FormatDateParts(raw[2], raw[1], raw[0]);
        }

        internal static string NormalizeYearPrefix(string pYearPrefixSnapshot, double pTime)
        {
            string date = FormatDate(pTime);
            if (string.IsNullOrEmpty(pYearPrefixSnapshot)) return date;
            if (pYearPrefixSnapshot.Contains("(")) return pYearPrefixSnapshot;

            string trimmed = pYearPrefixSnapshot.Trim();
            string yearOnly = Date.getYear(pTime) + "\u5e74";
            if (trimmed == yearOnly) return date;

            int space = trimmed.LastIndexOf(' ');
            if (space >= 0 && space < trimmed.Length - 1)
            {
                string era = trimmed.Substring(space + 1);
                return era + "(" + date + ")";
            }
            return trimmed + "(" + date + ")";
        }

        private static void Insert(string pTable, Kingdom pContextKingdom,
            string pEventType, HistoryText pContent, string pSubjectName, HistoryTarget pTarget,
            params ColumnVal[] pExtraCols)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null)
            {
                ModClass.LogWarning("HistoryWriter: DB unavailable; event skipped (" + pTable + "/" + pEventType + ")");
                return;
            }

            double t = World.world.getCurWorldTime();
            string prefix = BuildYearPrefix(t, pContextKingdom);
            string prefixRich = BuildYearPrefixRich(t, pContextKingdom);
            long eventId = NextEventId(db, pTable);
            string contextName = pContextKingdom?.data != null ? pContextKingdom.name : "";
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
                    ColumnVal.Create("CONTEXT_KINGDOM_ID", pContextKingdom?.data?.id ?? -1L),
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
                ModClass.LogWarning("HistoryWriter.Insert failed (" + pTable + "): " + e.Message);
            }
        }

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
                return 1;
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
