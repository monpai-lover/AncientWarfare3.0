using System;
using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.ui;
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
        internal readonly struct DeferredContext
        {
            public readonly double world_time;
            public readonly string year_prefix;
            public readonly string year_prefix_rich;
            public readonly long kingdom_id;
            public readonly string kingdom_name;
            public readonly string kingdom_color;

            public DeferredContext(double pWorldTime, string pYearPrefix, string pYearPrefixRich,
                long pKingdomId, string pKingdomName, string pKingdomColor)
            {
                world_time = pWorldTime;
                year_prefix = pYearPrefix ?? "";
                year_prefix_rich = pYearPrefixRich ?? "";
                kingdom_id = pKingdomId;
                kingdom_name = pKingdomName ?? "";
                kingdom_color = pKingdomColor ?? "";
            }
        }

        internal readonly struct PersonSnapshot
        {
            public readonly int age;
            public readonly int is_king;
            public readonly string role;

            public PersonSnapshot(int pAge, int pIsKing, string pRole)
            {
                age = pAge;
                is_king = pIsKing;
                role = pRole ?? "";
            }
        }

        internal static DeferredContext CaptureDeferredContext(Kingdom pKingdom)
        {
            double time = World.world?.getCurWorldTime() ?? 0.0;
            return new DeferredContext(time, BuildYearPrefix(time, pKingdom),
                BuildYearPrefixRich(time, pKingdom), pKingdom?.data?.id ?? -1L,
                pKingdom?.name ?? "", HistoryColors.FromKingdom(pKingdom));
        }

        internal static PersonSnapshot CapturePersonSnapshot(Actor pActor)
        {
            int age = -1;
            int isKing = 0;
            if (pActor?.data != null)
            {
                try { age = pActor.getAge(); } catch { }
                try { isKing = pActor.isKing() ? 1 : 0; } catch { }
            }
            return new PersonSnapshot(age, isKing, ResolveRoleSnapshot(pActor));
        }

        internal static void RecordDeferredPerson(DeferredContext pContext, PersonSnapshot pPerson,
            long pActorId, string pActorName, string pEventType, HistoryText pContent,
            string pCategory, HistoryTarget pTarget)
        {
            InsertDeferred(PersonBiographyTableItem.GetTableName(), pContext, pEventType, pContent,
                pActorName, pTarget.IsValid ? pTarget : HistoryTarget.Actor(pActorId),
                ColumnVal.Create("ACTOR_ID", pActorId),
                ColumnVal.Create("CATEGORY", pCategory ?? ""),
                ColumnVal.Create("AGE_AT_EVENT", pPerson.age),
                ColumnVal.Create("IS_KING_AT_EVENT", pPerson.is_king),
                ColumnVal.Create("ROLE_SNAPSHOT", pPerson.role),
                ColumnVal.Create("ROLE_LABEL", RoleLabel(pPerson.role)));
        }

        internal static void RecordDeferredCity(DeferredContext pContext, long pCityId,
            string pCityName, string pEventType, HistoryText pContent, HistoryTarget pTarget)
        {
            if (pCityId < 0) return;
            InsertDeferred(CityHistoryTableItem.GetTableName(), pContext, pEventType, pContent,
                pCityName, pTarget.IsValid ? pTarget : HistoryTarget.From("city", pCityId),
                ColumnVal.Create("CITY_ID", pCityId),
                ColumnVal.Create("KINGDOM_NAME", pContext.kingdom_name),
                ColumnVal.Create("KINGDOM_COLOR", pContext.kingdom_color));
        }
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
            try
            {
                if (pActor.isKing())
                    return GovernmentTitleRules.RoleSnapshot(
                        RepublicGovernmentService.IsRepublic(pActor.kingdom),
                        pIsRuler: true, pIsSuccessor: false,
                        HeirTitleRules.IsImperialOrMandate(pActor.kingdom));
            }
            catch { }
            try
            {
                pActor.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
                if (isHeir || HeirService.IsCurrentHeir(pActor.kingdom, pActor))
                    return GovernmentTitleRules.RoleSnapshot(
                        RepublicGovernmentService.IsRepublic(pActor.kingdom),
                        pIsRuler: false, pIsSuccessor: true,
                        HeirTitleRules.IsImperialOrMandate(pActor.kingdom));
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
                case "king": return AW_L10n.Text("aw_role_king", "Ruler");
                case "heir_shizi": return AW_L10n.Text("aw_heir_shizi", "Heir apparent");
                case "heir_taizi": return AW_L10n.Text("aw_heir_taizi", "Crown prince");
                case "republic_head": return AW_L10n.Text("aw_republic_head", "Head of state");
                case "republic_elder": return AW_L10n.Text("aw_republic_elder", "Elder");
                case "city_leader": return AW_L10n.Text("aw_role_city_leader", "City leader");
                case "clan_chief": return AW_L10n.Text("aw_role_clan_chief", "Clan chief");
                case "royal_guard_captain":
                    return AW_L10n.Text("aw_role_royal_guard_captain", "Royal guard captain");
                case "royal_guard": return AW_L10n.Text("aw_role_royal_guard", "Royal guard");
                case "fief_holder": return AW_L10n.Text("aw_role_fief_holder", "Fief general");
                case "general": return AW_L10n.Text("aw_role_general", "General");
                case "slave": return AW_L10n.Text("aw_role_slave", "Slave");
                case "warrior": return AW_L10n.Text("aw_role_warrior", "Soldier");
                case "noble": return AW_L10n.Text("aw_role_noble", "Noble");
                case "common_lineage":
                    return AW_L10n.Text("aw_role_common_lineage", "Lineage commoner");
                case "common": return AW_L10n.Text("aw_role_common", "Commoner");
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
            int[] raw = Date.getRawDate(pTime); // [day, month, year]
            string date = ChronicleFormatRules.FormatDateParts(
                raw[2], raw[1], raw[0]);
            string era = pKingdom != null ? YearNameService.GetYearName(pKingdom) : "";
            return GanzhiChronologyRules.FormatPrefix(era, date, raw[2]);
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
            int[] raw = Date.getRawDate(pTime); // [day, month, year]
            string date = ChronicleFormatRules.FormatDateParts(
                raw[2], raw[1], raw[0]);
            if (string.IsNullOrEmpty(pYearPrefixSnapshot))
                return GanzhiChronologyRules.FormatPrefix("", date, raw[2]);
            if (GanzhiChronologyRules.IsCanonicalPrefix(
                    pYearPrefixSnapshot))
                return pYearPrefixSnapshot;

            string trimmed = pYearPrefixSnapshot.Trim();
            string yearOnly = Date.getYear(pTime) + "\u5e74";
            if (trimmed == yearOnly)
                return GanzhiChronologyRules.FormatPrefix("", date, raw[2]);

            int space = trimmed.LastIndexOf(' ');
            if (space >= 0 && space < trimmed.Length - 1)
            {
                string era = trimmed.Substring(space + 1);
                return GanzhiChronologyRules.FormatPrefix(era, date, raw[2]);
            }
            return GanzhiChronologyRules.FormatPrefix(trimmed, date, raw[2]);
        }

        private static void Insert(string pTable, Kingdom pContextKingdom,
            string pEventType, HistoryText pContent, string pSubjectName, HistoryTarget pTarget,
            params ColumnVal[] pExtraCols)
        {
            if (!HistoryWriteTimingRules.ShouldRecord(Config.game_loaded,
                    SmoothLoader.isLoading()))
                return;
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null)
            {
                ModClass.LogWarning("HistoryWriter: DB unavailable; event skipped (" + pTable + "/" + pEventType + ")");
                return;
            }

            double t = World.world.getCurWorldTime();
            string prefix = BuildYearPrefix(t, pContextKingdom);
            string prefixRich = BuildYearPrefixRich(t, pContextKingdom);
            string contextName = pContextKingdom?.data != null ? pContextKingdom.name : "";
            string contextColor = HistoryColors.FromKingdom(pContextKingdom);
            HistoryTarget target = ResolveTarget(pTarget, pContent);

            try
            {
                var cols = new List<ColumnVal>
                {
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
                InsertCaptured(pTable, db, cols, pThrowOnFailure: false);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("HistoryWriter.Insert failed (" + pTable + "): " + e.Message);
            }
        }

        private static void InsertDeferred(string pTable, DeferredContext pContext,
            string pEventType, HistoryText pContent, string pSubjectName, HistoryTarget pTarget,
            params ColumnVal[] pExtraCols)
        {
            if (!HistoryWriteTimingRules.ShouldRecord(Config.game_loaded,
                    SmoothLoader.isLoading()))
                return;
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) throw new InvalidOperationException("History archive is unavailable.");

            HistoryTarget target = ResolveTarget(pTarget, pContent);
            var cols = new List<ColumnVal>
            {
                ColumnVal.Create("WORLD_TIME", pContext.world_time),
                ColumnVal.Create("YEAR_PREFIX", pContext.year_prefix),
                ColumnVal.Create("YEAR_PREFIX_RICH", pContext.year_prefix_rich),
                ColumnVal.Create("SUBJECT_NAME", pSubjectName ?? ""),
                ColumnVal.Create("SUBJECT_COLOR", pContext.kingdom_color),
                ColumnVal.Create("CONTENT", pContent.Plain ?? ""),
                ColumnVal.Create("CONTENT_RICH", pContent.Rich ?? ""),
                ColumnVal.Create("EVENT_TYPE", pEventType ?? ""),
                ColumnVal.Create("CONTEXT_KINGDOM_ID", pContext.kingdom_id),
                ColumnVal.Create("CONTEXT_KINGDOM_NAME", pContext.kingdom_name),
                ColumnVal.Create("CONTEXT_KINGDOM_COLOR", pContext.kingdom_color),
                ColumnVal.Create("TARGET_TYPE", target.IsValid ? target.type : ""),
                ColumnVal.Create("TARGET_ID", target.IsValid ? target.id : -1L)
            };
            if (pExtraCols != null) cols.AddRange(pExtraCols);
            InsertCaptured(pTable, db, cols, pThrowOnFailure: true);
        }

        private static void InsertCaptured(string pTable,
            System.Data.SQLite.SQLiteConnection pDatabase,
            List<ColumnVal> pColumns, bool pThrowOnFailure)
        {
            long eventId = 0L;
            try
            {
                var captured = new HistoricalSqlColumn[pColumns.Count];
                for (int index = 0; index < pColumns.Count; index++)
                {
                    ColumnVal column = pColumns[index];
                    captured[index] = new HistoricalSqlColumn(
                        column.Name, column.Value);
                }
                if (!HistoricalSynchronousWriteCoordinator.TryAppendOrExecute(
                        TimeSpan.FromSeconds(5),
                        (out long allocatedId, out string appendError) =>
                            HistoricalWriteService.TryAppendHistory(pTable,
                                captured, out allocatedId, out appendError),
                        HistoricalWriteService.FlushForSynchronousFallback,
                        allocatedId =>
                        {
                            var synchronous = new List<ColumnVal>(
                                pColumns.Count + 1)
                            {
                                ColumnVal.Create("EVENT_ID", allocatedId)
                            };
                            synchronous.AddRange(pColumns);
                            pDatabase.Insert(pTable, synchronous.ToArray());
                        }, out eventId, out string writeError))
                    throw new InvalidOperationException(writeError);
            }
            catch (Exception error)
            {
                if (pThrowOnFailure) throw;
                ModClass.LogWarning("HistoryWriter.Insert failed (" + pTable +
                                    "/" + eventId + "): " + error.Message);
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
