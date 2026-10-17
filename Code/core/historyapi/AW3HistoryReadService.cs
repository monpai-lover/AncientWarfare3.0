using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.api.history;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.historyapi
{
    internal static class AW3HistoryReadService
    {
        public static AW3HistoryEvent ReadCommittedRow(string table,
            long eventId)
        {
            if (eventId <= 0L) return null;
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return null;
            string source;
            string idColumn;
            switch (table)
            {
                case "PersonBiography": source = "PersonBiography"; idColumn = "EVENT_ID"; break;
                case "KingdomHistory": source = "KingdomHistory"; idColumn = "EVENT_ID"; break;
                case "CityHistory": source = "CityHistory"; idColumn = "EVENT_ID"; break;
                default: return null;
            }
            try
            {
                using var command = new SQLiteCommand(
                    "SELECT EVENT_ID,WORLD_TIME,YEAR_PREFIX,EVENT_TYPE," +
                    "SUBJECT_NAME,CONTENT," +
                    (source == "PersonBiography" ? "CATEGORY," : "'' AS CATEGORY,") +
                    "CONTEXT_KINGDOM_ID," +
                    "TARGET_ID,PROJECTION_KEY," +
                    (source == "PersonBiography" ? "ACTOR_ID" :
                        source == "KingdomHistory" ? "KINGDOM_ID" : "CITY_ID") + " FROM " + source +
                    " WHERE " + idColumn + "=@id LIMIT 1", db);
                command.Parameters.AddWithValue("@id", eventId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return null;
                var row = new AW3HistoryRow
                {
                    RecordId = ReadLong(reader, 0), Source = source,
                    Domain = source == "PersonBiography"
                        ? AW3HistoryDomains.Biography : AW3HistoryDomains.Chronicle,
                    ProjectionKey = ReadLong(reader, 0),
                    ProjectionKeyText = ReadString(reader, 9),
                    WorldTime = ReadDouble(reader, 1, -1d),
                    YearText = ReadString(reader, 2), EventType = ReadString(reader, 3),
                    SubjectName = ReadString(reader, 4), Content = ReadString(reader, 5),
                    Category = ReadString(reader, 6),
                    ContextKingdomId = ReadLong(reader, 7), TargetId = ReadLong(reader, 8),
                    SubjectId = ReadLong(reader, 10),
                    KingdomId = source == "KingdomHistory"
                        ? ReadLong(reader, 10) : ReadLong(reader, 7)
                };
                return AW3HistoryDtoMapper.ToEvent(row);
            }
            catch (SQLiteException) { return null; }
        }

        public static AW3HistoryPage<AW3HistoryEvent> ReadEvents(
            AW3HistoryQuery query)
        {
            query = query ?? AW3HistoryQuery.Create();
            if (!ValidCursor(query)) return EmptyPage<AW3HistoryEvent>();
            return AW3HistoryReadConnection.TryRead(
                    db => ReadEventsOnConnection(db, query),
                    out AW3HistoryPage<AW3HistoryEvent> result)
                ? result : EmptyPage<AW3HistoryEvent>();
        }

        public static AW3HistoryPage<AW3BiographyEntry> ReadBiography(
            long actorId, AW3HistoryQuery query)
        {
            query = query ?? AW3HistoryQuery.ForActor(actorId);
            if (!ValidCursor(query)) return EmptyPage<AW3BiographyEntry>();
            return AW3HistoryReadConnection.TryRead(
                    db => ReadBiographyOnConnection(db, actorId, query),
                    out AW3HistoryPage<AW3BiographyEntry> result)
                ? result : EmptyPage<AW3BiographyEntry>();
        }

        public static AW3HistoryPage<AW3ChronicleEntry> ReadKingdomEvents(
            long kingdomId, AW3HistoryQuery query)
        {
            query = query ?? AW3HistoryQuery.ForKingdom(kingdomId);
            if (!ValidCursor(query)) return EmptyPage<AW3ChronicleEntry>();
            return AW3HistoryReadConnection.TryRead(
                    db => ReadKingdomEventsOnConnection(db, kingdomId, query),
                    out AW3HistoryPage<AW3ChronicleEntry> result)
                ? result : EmptyPage<AW3ChronicleEntry>();
        }

        public static AW3HistoryPage<AW3ChronicleEntry> ReadCityEvents(
            long cityId, AW3HistoryQuery query)
        {
            query = query ?? AW3HistoryQuery.Create(cityId: cityId);
            if (!ValidCursor(query)) return EmptyPage<AW3ChronicleEntry>();
            return AW3HistoryReadConnection.TryRead(
                    db => ReadCityEventsOnConnection(db, cityId, query),
                    out AW3HistoryPage<AW3ChronicleEntry> result)
                ? result : EmptyPage<AW3ChronicleEntry>();
        }

        public static IReadOnlyList<AW3Reign> ReadReigns(long kingdomId)
        {
            return AW3HistoryReadConnection.TryRead(
                    db => HistoryQuery.GetKingdomReigns(kingdomId),
                    out List<ReignPeriod> periods)
                ? periods.Select(period => ToReign(period, kingdomId)).ToList()
                : new List<AW3Reign>();
        }

        public static IReadOnlyList<AW3CityPeriod> ReadCityPeriods(long cityId)
        {
            return AW3HistoryReadConnection.TryRead(
                    db => HistoryQuery.GetCityPeriods(cityId),
                    out List<ReignPeriod> periods)
                ? periods.Select(period => ToCityPeriod(period, cityId)).ToList()
                : new List<AW3CityPeriod>();
        }

        public static IReadOnlyList<AW3GenealogyEntry> ReadParents(long actorId)
        {
            return ReadRelations(actorId, parents: true);
        }

        public static IReadOnlyList<AW3GenealogyEntry> ReadChildren(long actorId)
        {
            return ReadRelations(actorId, parents: false);
        }

        public static IReadOnlyList<AW3GenealogyEntry> ReadAncestors(
            long actorId, int maxDepth)
        {
            int depthLimit = Math.Max(0, Math.Min(64, maxDepth));
            return AW3HistoryReadConnection.TryRead(db =>
            {
                var result = new List<AW3GenealogyEntry>();
                var seen = new HashSet<long> { actorId };
                var frontier = new List<long> { actorId };
                for (int depth = 0; depth < depthLimit && frontier.Count > 0; depth++)
                {
                    var next = new List<long>();
                    foreach (long current in frontier)
                    {
                        foreach (long parentId in LineageQuery.GetParentIds(
                            current, pUseReverseLiveLookup: false))
                        {
                            if (parentId < 0 || !seen.Add(parentId)) continue;
                            AW3GenealogyEntry entry = ReadActor(db, parentId);
                            if (entry != null) result.Add(entry);
                            next.Add(parentId);
                        }
                    }
                    frontier = next;
                }
                return (IReadOnlyList<AW3GenealogyEntry>)result;
            }, out IReadOnlyList<AW3GenealogyEntry> result)
                ? result : new List<AW3GenealogyEntry>();
        }

        public static IReadOnlyList<AW3GenealogyEntry> ReadFamilyTree(
            long actorId)
        {
            return AW3HistoryReadConnection.TryRead(db =>
            {
                var result = new List<AW3GenealogyEntry>();
                AW3GenealogyEntry center = ReadActor(db, actorId);
                if (center != null) result.Add(center);
                foreach (long id in LineageQuery.GetParentIds(actorId, false))
                {
                    AW3GenealogyEntry parent = ReadActor(db, id);
                    if (parent != null) result.Add(parent);
                }
                foreach (long id in LineageQuery.GetChildIds(actorId))
                {
                    AW3GenealogyEntry child = ReadActor(db, id);
                    if (child != null) result.Add(child);
                }
                return (IReadOnlyList<AW3GenealogyEntry>)result;
            }, out IReadOnlyList<AW3GenealogyEntry> result)
                ? result : new List<AW3GenealogyEntry>();
        }

        public static AW3HistoryPage<AW3DiplomacyEvent> ReadDiplomacy(
            long? firstKingdomId, long? secondKingdomId,
            AW3HistoryQuery query)
        {
            query = query ?? AW3HistoryQuery.Create();
            if (!ValidCursor(query)) return EmptyPage<AW3DiplomacyEvent>();
            return AW3HistoryReadConnection.TryRead(
                    db => ReadDiplomacyOnConnection(db, firstKingdomId,
                        secondKingdomId, query),
                    out AW3HistoryPage<AW3DiplomacyEvent> result)
                ? result : EmptyPage<AW3DiplomacyEvent>();
        }

        public static AW3HistoryPage<AW3OfficialCareerEntry> ReadCareer(
            long? actorId, OfficialCareerHistoryScope scope,
            AW3HistoryQuery query)
        {
            query = query ?? AW3HistoryQuery.Create();
            if (!ValidCursor(query)) return EmptyPage<AW3OfficialCareerEntry>();
            return AW3HistoryReadConnection.TryRead(db =>
            {
                var rows = ReadCareerRows(db, actorId, scope, query);
                var items = rows.Select(ToCareer).ToList();
                return Page(items.Select(item => new HistoryItem<AW3OfficialCareerEntry>(
                    item, new AW3HistoryCursorKey(item.AppointedTime,
                        AW3HistoryDomains.OfficialCareer, "CourtOfficer",
                        item.OfficerId)))
                    .Where(item => Matches(item.Key, query) &&
                        (query.Domain == "" || query.Domain == AW3HistoryDomains.OfficialCareer) &&
                        (query.OfficeId == "" || item.Value.OfficeId == query.OfficeId) &&
                        (query.KingdomId < 0L || item.Value.KingdomId == query.KingdomId) &&
                        (query.CityId < 0L || item.Value.CityId == query.CityId) &&
                        (query.CountyId < 0L || item.Value.CountyId == query.CountyId)), query);
            }, out AW3HistoryPage<AW3OfficialCareerEntry> result)
                ? result : EmptyPage<AW3OfficialCareerEntry>();
        }

        private static AW3HistoryPage<AW3HistoryEvent> ReadEventsOnConnection(
            SQLiteConnection db, AW3HistoryQuery query)
        {
            var items = new List<HistoryItem<AW3HistoryEvent>>();
            if (query.ActorId >= 0)
            {
                items.AddRange(ReadBiographyRows(db, query.ActorId, query)
                    .Select(row => EventItem(row, AW3HistoryDomains.Biography,
                        "PersonBiography")));
                AddCareerRows(db, query, items);
            }
            else if (query.CityId >= 0)
            {
                items.AddRange(ReadChronicleRows(db, "CityHistory", query.CityId, query)
                    .Select(row => EventItem(row, AW3HistoryDomains.Chronicle,
                        "CityHistory")));
                AddCareerRows(db, query, items);
            }
            else if (query.KingdomId >= 0)
            {
                items.AddRange(ReadChronicleRows(db, "KingdomHistory", query.KingdomId, query)
                    .Select(row => EventItem(row, AW3HistoryDomains.Chronicle,
                        "KingdomHistory")));
                AddCareerRows(db, query, items);
            }
            else
            {
                if (query.Domain == "" || query.Domain == AW3HistoryDomains.Biography)
                    items.AddRange(ReadAllHistoryRows(db, "PersonBiography",
                        AW3HistoryDomains.Biography, query));
                if (query.Domain == "" || query.Domain == AW3HistoryDomains.Chronicle)
                {
                    items.AddRange(ReadAllHistoryRows(db, "KingdomHistory",
                        AW3HistoryDomains.Chronicle, query));
                    items.AddRange(ReadAllHistoryRows(db, "CityHistory",
                        AW3HistoryDomains.Chronicle, query));
                }
                if (query.Domain == "" || query.Domain == AW3HistoryDomains.OfficialCareer)
                    AddCareerRows(db, query, items);
            }
            if ((query.Domain == AW3HistoryDomains.Diplomacy ||
                 query.Domain == "") && query.ActorId < 0L &&
                query.CityId < 0L && query.CountyId < 0L && query.OfficeId == "")
            {
                items.AddRange(ReadDiplomacyItemsOnConnection(db,
                        query.KingdomId >= 0 ? query.KingdomId : (long?)null,
                        null, query).Select(item => ToHistoryItem(item.Value)));
            }
            items = items.Where(item => Matches(item.Key, query) &&
                (query.Domain == "" || item.Value.Domain == query.Domain) &&
                (query.EventType == "" || item.Value.EventType == query.EventType)).ToList();
            return Page(items, query);
        }

        private static List<HistoryItem<AW3HistoryEvent>> ReadAllHistoryRows(
            SQLiteConnection db, string source, string domain, AW3HistoryQuery query)
        {
            return ReadHistoryRows(db, source, domain, query).Select(row =>
                EventItem(row, domain, source)).ToList();
        }

        private static List<AW3HistoryRow> ReadHistoryRows(
            SQLiteConnection db, string source, string domain, AW3HistoryQuery query,
            long? subjectId = null)
        {
            var result = new List<AW3HistoryRow>();
            try
            {
                string sql = source == "PersonBiography"
                    ? "SELECT EVENT_ID,ACTOR_ID,WORLD_TIME,YEAR_PREFIX,EVENT_TYPE," +
                      "SUBJECT_NAME,CONTENT,CATEGORY,AGE_AT_EVENT,IS_KING_AT_EVENT," +
                      "ROLE_SNAPSHOT,CONTEXT_KINGDOM_ID,TARGET_ID,PROJECTION_KEY " +
                      "FROM PersonBiography WHERE 1=1"
                    : "SELECT EVENT_ID," + (source == "CityHistory" ? "CITY_ID" : "KINGDOM_ID") +
                      ",WORLD_TIME,YEAR_PREFIX,EVENT_TYPE,SUBJECT_NAME,CONTENT," +
                      "CONTEXT_KINGDOM_ID,TARGET_ID,PROJECTION_KEY FROM " + source +
                      " WHERE 1=1";
                using var command = new SQLiteCommand(sql, db);
                string subjectColumn = source == "PersonBiography" ? "ACTOR_ID" :
                    source == "KingdomHistory" ? "KINGDOM_ID" : "CITY_ID";
                if (subjectId.HasValue)
                {
                    command.CommandText += " AND " + subjectColumn + "=@subject";
                    command.Parameters.AddWithValue("@subject", subjectId.Value);
                }
                command.CommandText += PageSql(command, query, "WORLD_TIME",
                    domain, source, "EVENT_ID", "EVENT_TYPE");
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var row = new AW3HistoryRow
                    {
                        RecordId = ReadLong(reader, 0), SubjectId = ReadLong(reader, 1),
                        WorldTime = ReadDouble(reader, 2, -1d), YearText = ReadString(reader, 3),
                        EventType = ReadString(reader, 4), SubjectName = ReadString(reader, 5),
                        Content = ReadString(reader, 6), Domain = domain, Source = source
                    };
                    if (source == "PersonBiography")
                    {
                        row.Category = ReadString(reader, 7);
                        row.Age = ReadInt(reader, 8, -1);
                        row.WasKing = ReadInt(reader, 9, 0) != 0;
                        row.RoleSnapshot = ReadString(reader, 10);
                        row.ContextKingdomId = ReadLong(reader, 11);
                        row.TargetId = ReadLong(reader, 12);
                        row.ProjectionKeyText = ReadString(reader, 13);
                    }
                    else
                    {
                        row.KingdomId = source == "KingdomHistory" ? row.SubjectId : ReadLong(reader, 7);
                        row.ContextKingdomId = ReadLong(reader, 7);
                        row.TargetId = ReadLong(reader, 8);
                        row.ProjectionKeyText = ReadString(reader, 9);
                    }
                    if (source == "PersonBiography") row.KingdomId = row.ContextKingdomId;
                    row.ProjectionKey = row.RecordId;
                    result.Add(row);
                }
            }
            catch (SQLiteException) { }
            return result;
        }

        private static void AddCareerRows(SQLiteConnection db,
            AW3HistoryQuery query, List<HistoryItem<AW3HistoryEvent>> target)
        {
            if (query.Domain != "" && query.Domain != AW3HistoryDomains.OfficialCareer)
                return;
            AddCareerEventRows(db, query, target, false);
            AddCareerEventRows(db, query, target, true);
        }

        private static void AddCareerEventRows(SQLiteConnection db,
            AW3HistoryQuery query, List<HistoryItem<AW3HistoryEvent>> target, bool ended)
        {
            string source = ended ? "CourtOfficerEnd" : "CourtOfficerAppointment";
            string type = ended ? "office_ended" : "appointed";
            try
            {
                using var command = new SQLiteCommand(
                    "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME,LAYER,OFFICE_ID," +
                    "APPOINTED_YEAR,APPOINTED_TIME,ENDED_YEAR,ENDED_TIME,ACTIVE,END_REASON " +
                    "FROM CourtOfficer WHERE 1=1" + (ended ? " AND ENDED_TIME>=0" : ""), db);
                AppendCareerFilters(command, query);
                command.CommandText += PageSql(command, query,
                    ended ? "ENDED_TIME" : "APPOINTED_TIME", AW3HistoryDomains.OfficialCareer,
                    source, "OFFICER_ID", "'" + type + "'");
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    AddCareerEvent(target, ReadLong(reader, 0), ReadLong(reader, 2),
                        ReadLong(reader, 1), ReadString(reader, 3), ReadString(reader, 4),
                        ReadString(reader, 5), ReadDouble(reader, ended ? 9 : 7, -1d),
                        ReadInt(reader, ended ? 8 : 6, -1), type,
                        ended ? ReadString(reader, 11) : "", source);
            }
            catch (SQLiteException) { }
        }

        private static void AppendCareerFilters(SQLiteCommand command, AW3HistoryQuery query)
        {
            foreach (var filter in new[]
            {
                ("ACTOR_ID", "@actorFilter", query.ActorId),
                ("KINGDOM_ID", "@kingdomFilter", query.KingdomId),
                ("CITY_ID", "@cityFilter", query.CityId),
                ("COUNTY_ID", "@countyFilter", query.CountyId)
            })
                if (filter.Item3 >= 0)
                {
                    command.CommandText += " AND " + filter.Item1 + "=" + filter.Item2;
                    command.Parameters.AddWithValue(filter.Item2, filter.Item3);
                }
            if (query.OfficeId != "")
            {
                command.CommandText += " AND OFFICE_ID=@officeFilter";
                command.Parameters.AddWithValue("@officeFilter", query.OfficeId);
            }
        }

        private static void AddCareerEvent(List<HistoryItem<AW3HistoryEvent>> target,
            long officerId, long actorId, long kingdomId, string actorName,
            string layer, string officeId, double time, int year, string type,
            string content, string source)
        {
            var item = new AW3HistoryEvent(officerId,
                AW3HistoryDomains.OfficialCareer, source, officerId, type,
                time, year, "", actorId, officerId, kingdomId, kingdomId,
                actorName, content, "official_career", type);
            target.Add(new HistoryItem<AW3HistoryEvent>(item,
                new AW3HistoryCursorKey(time, AW3HistoryDomains.OfficialCareer,
                    source, officerId)));
        }

        private static AW3HistoryPage<AW3BiographyEntry> ReadBiographyOnConnection(
            SQLiteConnection db, long actorId, AW3HistoryQuery query)
        {
            var items = ReadBiographyRows(db, actorId, query).Select(row =>
            {
                AW3BiographyEntry entry = AW3HistoryDtoMapper.ToBiography(row,
                    row.Age, row.RoleSnapshot, row.WasKing);
                return new HistoryItem<AW3BiographyEntry>(entry,
                    new AW3HistoryCursorKey(row.WorldTime,
                        AW3HistoryDomains.Biography, "PersonBiography", row.RecordId));
            });
            return Page(items.Where(item => Matches(item.Key, query) &&
                (query.Domain == "" || query.Domain == AW3HistoryDomains.Biography) &&
                (query.EventType == "" || item.Value.EventType == query.EventType)), query);
        }

        private static AW3HistoryPage<AW3ChronicleEntry> ReadKingdomEventsOnConnection(
            SQLiteConnection db, long kingdomId, AW3HistoryQuery query)
        {
            var items = ReadChronicleRows(db, "KingdomHistory", kingdomId, query).Select(row =>
                new HistoryItem<AW3ChronicleEntry>(
                    AW3HistoryDtoMapper.ToChronicle(row, "kingdom", -1L),
                    new AW3HistoryCursorKey(row.WorldTime,
                        AW3HistoryDomains.Chronicle, "KingdomHistory", row.RecordId)));
            return Page(items.Where(item => Matches(item.Key, query) &&
                (query.Domain == "" || query.Domain == AW3HistoryDomains.Chronicle) &&
                (query.EventType == "" || item.Value.EventType == query.EventType)), query);
        }

        private static AW3HistoryPage<AW3ChronicleEntry> ReadCityEventsOnConnection(
            SQLiteConnection db, long cityId, AW3HistoryQuery query)
        {
            var items = ReadChronicleRows(db, "CityHistory", cityId, query).Select(row =>
                new HistoryItem<AW3ChronicleEntry>(
                    AW3HistoryDtoMapper.ToChronicle(row, "city", cityId),
                    new AW3HistoryCursorKey(row.WorldTime,
                        AW3HistoryDomains.Chronicle, "CityHistory", row.RecordId)));
            return Page(items.Where(item => Matches(item.Key, query) &&
                (query.Domain == "" || query.Domain == AW3HistoryDomains.Chronicle) &&
                (query.EventType == "" || item.Value.EventType == query.EventType)), query);
        }

        private static List<AW3HistoryRow> ReadBiographyRows(
            SQLiteConnection db, long actorId, AW3HistoryQuery query)
        {
            return ReadHistoryRows(db, "PersonBiography", AW3HistoryDomains.Biography,
                query, actorId);
        }

        private static List<AW3HistoryRow> ReadChronicleRows(
            SQLiteConnection db, string source, long id, AW3HistoryQuery query)
        {
            return ReadHistoryRows(db, source, AW3HistoryDomains.Chronicle, query, id);
        }

        private static AW3GenealogyEntry ReadActor(SQLiteConnection db, long actorId)
        {
            if (actorId < 0) return null;
            using var command = new SQLiteCommand(
                "SELECT ID,IFNULL(DISPLAY_NAME,''),IFNULL(PARENT_ID_1,-1)," +
                "IFNULL(PARENT_ID_2,-1),IFNULL(LINEAGE_ID,-1),IFNULL(SHI_ID,-1)," +
                "IFNULL(BIRTH_TIME,-1),IFNULL(DEATH_TIME,-1),IFNULL(IS_ALIVE,0) " +
                "FROM " + ActorArchiveTableItem.GetTableName() + " WHERE ID=@id LIMIT 1", db);
            command.Parameters.AddWithValue("@id", actorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new AW3GenealogyEntry(actorId, ReadString(reader, 1),
                ReadLong(reader, 2), ReadLong(reader, 3), ReadLong(reader, 4),
                ReadLong(reader, 5), ReadDouble(reader, 6, -1d),
                ReadDouble(reader, 7, -1d), ReadLong(reader, 8) != 0L);
        }

        private static IReadOnlyList<AW3GenealogyEntry> ReadRelations(
            long actorId, bool parents)
        {
            return AW3HistoryReadConnection.TryRead(db =>
            {
                var result = new List<AW3GenealogyEntry>();
                IEnumerable<long> ids = parents
                    ? LineageQuery.GetParentIds(actorId, false)
                    : LineageQuery.GetChildIds(actorId);
                foreach (long id in ids)
                {
                    AW3GenealogyEntry entry = ReadActor(db, id);
                    if (entry != null) result.Add(entry);
                }
                return (IReadOnlyList<AW3GenealogyEntry>)result;
            }, out IReadOnlyList<AW3GenealogyEntry> result)
                ? result : new List<AW3GenealogyEntry>();
        }

        private static List<OfficialCareerHistoryRow> ReadCareerRows(
            SQLiteConnection db, long? actorId, OfficialCareerHistoryScope scope,
            AW3HistoryQuery query)
        {
            var result = new List<OfficialCareerHistoryRow>();
            if (!actorId.HasValue && !scope.IsValid) return result;
            using var command = new SQLiteCommand(
                "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,CITY_ID,IFNULL(COUNTY_ID,-1)," +
                "IFNULL(LAYER,''),IFNULL(OFFICE_ID,''),IFNULL(ACTOR_NAME,'')," +
                "APPOINTED_YEAR,ENDED_YEAR,ACTIVE,IFNULL(END_REASON,'')," +
                "APPOINTED_TIME,IFNULL(RANK_AT_APPOINTMENT,0)," +
                "IFNULL(LOCAL_GRADE_AT_APPOINTMENT,0) FROM " +
                CourtOfficerTableItem.GetTableName() +
                " WHERE 1=1", db);
            if (actorId.HasValue)
            {
                command.CommandText += " AND ACTOR_ID=@actor";
                command.Parameters.AddWithValue("@actor", actorId.Value);
            }
            else
            {
                command.CommandText += " AND KINGDOM_ID=@kingdom AND LAYER=@layer AND OFFICE_ID=@office";
                command.Parameters.AddWithValue("@kingdom", scope.KingdomId);
                command.Parameters.AddWithValue("@layer", scope.Layer);
                command.Parameters.AddWithValue("@office", scope.OfficeId);
                if (scope.HasCity)
                {
                    command.CommandText += " AND (CITY_ID=@city OR CITY_ID<0)";
                    command.Parameters.AddWithValue("@city", scope.CityId);
                }
                if (scope.HasCounty)
                {
                    command.CommandText += " AND COUNTY_ID=@county";
                    command.Parameters.AddWithValue("@county", scope.CountyId);
                }
            }
            AppendCareerFilters(command, query);
            command.CommandText += PageSql(command, query, "APPOINTED_TIME",
                AW3HistoryDomains.OfficialCareer, "CourtOfficer", "OFFICER_ID");
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new OfficialCareerHistoryRow(ReadLong(reader, 1),
                    ReadLong(reader, 0), ReadLong(reader, 2), ReadLong(reader, 3),
                    ReadString(reader, 5), ReadString(reader, 6), ReadString(reader, 7),
                    ReadInt(reader, 8, -1), ReadInt(reader, 9, -1),
                    ReadInt(reader, 10, 0) == 1, ReadString(reader, 11),
                    ReadDouble(reader, 12, -1d), "", "", ReadInt(reader, 13, 0).ToString(),
                    ReadInt(reader, 14, 0), ReadLong(reader, 4)));
            }
            return result;
        }

        private static AW3HistoryPage<AW3DiplomacyEvent> ReadDiplomacyOnConnection(
            SQLiteConnection db, long? first, long? second, AW3HistoryQuery query)
        {
            var rows = ReadDiplomacyItemsOnConnection(db, first, second, query);
            return Page(rows.Where(item => Matches(item.Key, query) &&
                (query.Domain == "" || query.Domain == AW3HistoryDomains.Diplomacy) &&
                (query.EventType == "" || item.Value.EventType == query.EventType)), query);
        }

        private static List<HistoryItem<AW3DiplomacyEvent>>
            ReadDiplomacyItemsOnConnection(SQLiteConnection db, long? first,
                long? second, AW3HistoryQuery query)
        {
            var rows = new List<HistoryItem<AW3DiplomacyEvent>>();
            // Query lifecycle projections separately: an old row can have a much
            // later response/end time, so limiting records first loses events.
            AddDiplomacySource(db, first, second, query, rows, "DiplomacyDialogue",
                "EVENT_ID", "KINGDOM_A_ID", "KINGDOM_B_ID", "EVENT_TIME", "EVENT_YEAR",
                "YEAR_PREFIX", "''", "DETAIL", "'dialogue:' || IFNULL(EVENT_TYPE,'')", "DiplomacyDialogue");
            AddDiplomacySource(db, first, second, query, rows, "DiplomacyProposal",
                "PROPOSAL_ID", "REQUESTER_KINGDOM_ID", "RESPONDER_KINGDOM_ID", "CREATED_TIME", "CREATED_YEAR",
                "REQUEST_YEAR_PREFIX", "STATUS", "''", "'proposal:' || IFNULL(PROPOSAL_TYPE,'')", "DiplomacyProposal");
            AddDiplomacySource(db, first, second, query, rows, "DiplomacyProposal",
                "PROPOSAL_ID", "RESPONDER_KINGDOM_ID", "REQUESTER_KINGDOM_ID", "RESPONSE_TIME", "RESPONSE_YEAR",
                "RESPONSE_YEAR_PREFIX", "STATUS", "RESPONSE_REASON", "'proposal_response'", "DiplomacyProposalResponse",
                "RESPONSE_TIME>=0");
            AddDiplomacySource(db, first, second, query, rows, "DiplomaticMarriage",
                "MARRIAGE_ID", "KINGDOM_A_ID", "KINGDOM_B_ID", "START_TIME", "START_YEAR",
                "''", "'active'", "''", "'marriage_started'", "DiplomaticMarriageStart");
            AddDiplomacySource(db, first, second, query, rows, "DiplomaticMarriage",
                "MARRIAGE_ID", "KINGDOM_A_ID", "KINGDOM_B_ID", "END_TIME", "-1",
                "''", "'ended'", "''", "'marriage_ended'", "DiplomaticMarriageEnd", "END_TIME>=0");
            AddDiplomacySource(db, first, second, query, rows, "DiplomaticOperation",
                "OPERATION_ID", "SOURCE_KINGDOM_ID", "TARGET_KINGDOM_ID", "START_TIME", "START_YEAR",
                "''", "STATUS", "''", "'operation_started:' || IFNULL(OPERATION_TYPE,'')", "DiplomaticOperationStart");
            AddDiplomacySource(db, first, second, query, rows, "DiplomaticOperation",
                "OPERATION_ID", "SOURCE_KINGDOM_ID", "TARGET_KINGDOM_ID", "DUE_TIME", "-1",
                "''", "STATUS", "RESULT", "'operation_result'", "DiplomaticOperationResult", "IFNULL(RESULT,'')<>''");
            AddDiplomacySource(db, first, second, query, rows, "DiplomaticCoalition",
                "COALITION_ID", "MEMBER_A_ID", "MEMBER_B_ID", "START_TIME", "START_YEAR",
                "''", "'active'", "''", "'coalition_started'", "DiplomaticCoalitionStart");
            AddDiplomacySource(db, first, second, query, rows, "DiplomaticCoalition",
                "COALITION_ID", "MEMBER_A_ID", "MEMBER_B_ID", "END_TIME", "-1",
                "''", "'ended'", "''", "'coalition_ended'", "DiplomaticCoalitionEnd", "END_TIME>=0");
            AddDiplomacySource(db, first, second, query, rows, "PeaceSettlement",
                "SETTLEMENT_ID", "WINNER_KINGDOM_ID", "LOSER_KINGDOM_ID", "WORLD_TIME", "-1",
                "''", "'committed'", "TERMS_TEXT", "'peace_settlement'", "PeaceSettlement");
            return rows;
        }

        private static void AddDiplomacySource(SQLiteConnection db, long? first,
            long? second, AW3HistoryQuery query, List<HistoryItem<AW3DiplomacyEvent>> target,
            string table, string id, string firstColumn, string secondColumn,
            string time, string year, string yearText, string status, string content,
            string eventType, string source, string predicate = "1=1")
        {
            try
            {
                using var command = new SQLiteCommand("SELECT " + id + "," + firstColumn + "," +
                    secondColumn + "," + time + "," + year + "," + yearText + "," + status + "," +
                    content + "," + eventType + " FROM " + table + " WHERE " + predicate, db);
                if (first.HasValue)
                {
                    command.CommandText += second.HasValue
                        ? " AND ((" + firstColumn + "=@first AND " + secondColumn + "=@second) OR (" +
                          firstColumn + "=@second AND " + secondColumn + "=@first))"
                        : " AND (" + firstColumn + "=@first OR " + secondColumn + "=@first)";
                    command.Parameters.AddWithValue("@first", first.Value);
                    command.Parameters.AddWithValue("@second", second ?? -1L);
                }
                command.CommandText += PageSql(command, query, time,
                    AW3HistoryDomains.Diplomacy, source, id, eventType);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    AddDiplomacy(target, ReadLong(reader, 0), ReadString(reader, 8),
                        ReadLong(reader, 1), ReadLong(reader, 2), ReadDouble(reader, 3, -1d),
                        ReadInt(reader, 4, -1), ReadString(reader, 5), ReadString(reader, 6),
                        ReadString(reader, 7), source);
            }
            catch (SQLiteException) { }
        }

        private static void AddDiplomacy(List<HistoryItem<AW3DiplomacyEvent>> target,
            long recordId, string type, long first, long second, double time,
            int year, string yearText, string status, string content,
            string source = "Diplomacy")
        {
            var row = new AW3HistoryRow
            {
                RecordId = recordId, EventType = type, WorldTime = time,
                WorldYear = year, YearText = yearText, Content = content,
                KingdomId = first, ContextKingdomId = second, Source = source,
                ProjectionKey = recordId
            };
            target.Add(new HistoryItem<AW3DiplomacyEvent>(
                AW3HistoryDtoMapper.ToDiplomacy(row, first, second, status),
                new AW3HistoryCursorKey(time, AW3HistoryDomains.Diplomacy,
                    source, recordId)));
        }

        private static bool PairMatches(long a, long b, long first, long? second)
        {
            if (!second.HasValue) return a == first || b == first;
            return (a == first && b == second.Value) ||
                   (a == second.Value && b == first);
        }

        private static HistoryItem<AW3HistoryEvent> EventItem(AW3HistoryRow row,
            string domain, string source)
        {
            row.Domain = domain;
            row.Source = source;
            return new HistoryItem<AW3HistoryEvent>(AW3HistoryDtoMapper.ToEvent(row),
                new AW3HistoryCursorKey(row.WorldTime, domain, source, row.RecordId));
        }

        private static HistoryItem<AW3HistoryEvent> ToHistoryItem(
            AW3DiplomacyEvent item)
        {
            var row = new AW3HistoryRow
            {
                RecordId = item.RecordId, Domain = AW3HistoryDomains.Diplomacy,
                Source = item.Source, EventType = item.EventType,
                WorldTime = item.WorldTime, WorldYear = item.WorldYear,
                YearText = item.YearText, KingdomId = item.FirstKingdomId,
                ContextKingdomId = item.SecondKingdomId, Content = item.Content,
                ProjectionKey = item.RecordId
            };
            return EventItem(row, AW3HistoryDomains.Diplomacy, item.Source);
        }

        private static AW3HistoryRow FromHistory(HistoryEntry entry,
            string domain, string source, long subjectId, long kingdomId)
        {
            return new AW3HistoryRow
            {
                RecordId = entry?.event_id ?? -1L,
                Domain = domain, Source = source,
                ProjectionKey = entry?.event_id ?? -1L,
                ProjectionKeyText = entry?.projection_key,
                EventType = entry?.event_type,
                WorldTime = entry?.world_time ?? -1d,
                YearText = entry?.year_prefix,
                WorldYear = -1,
                SubjectId = subjectId,
                TargetId = entry?.target_id ?? -1L,
                KingdomId = kingdomId,
                ContextKingdomId = entry?.context_kingdom_id ?? -1L,
                SubjectName = entry?.subject_name,
                Content = entry?.content,
                Category = entry?.category,
                Age = entry?.age_at_event ?? -1,
                RoleSnapshot = entry?.role_snapshot,
                WasKing = entry != null && entry.is_king_at_event != 0
            };
        }

        private static AW3Reign ToReign(ReignPeriod period, long kingdomId)
        {
            return new AW3Reign(kingdomId, period?.king_actor_id ?? -1L,
                period?.king_name, period?.start_time ?? -1d,
                period?.end_time ?? -1d, period != null && period.end_time < 0d);
        }

        private static AW3CityPeriod ToCityPeriod(ReignPeriod period, long cityId)
        {
            long kingdomId = period?.events?.FirstOrDefault()?.context_kingdom_id ?? -1L;
            return new AW3CityPeriod(cityId, kingdomId, period?.owner_name,
                period?.start_time ?? -1d, period?.end_time ?? -1d,
                period != null && period.end_time < 0d);
        }

        private static AW3OfficialCareerEntry ToCareer(OfficialCareerHistoryRow row)
        {
            return AW3HistoryDtoMapper.ToCareer(row);
        }

        private static string PageSql(SQLiteCommand command, AW3HistoryQuery query,
            string timeColumn, string domain, string source, string idColumn,
            string eventType = null)
        {
            // Match ReadDouble/DTO normalization, including SQLite infinities.
            // Otherwise an infinite time can sort after the current page but
            // map before its cursor, making that record permanently unreachable.
            string time = "(CASE WHEN " + timeColumn +
                " BETWEEN -1.7976931348623157E308 AND 1.7976931348623157E308 THEN " +
                timeColumn + " ELSE -1 END)";
            string sql = "";
            if (query.WorldTimeFrom >= 0d)
            {
                sql += " AND " + time + ">=@timeFrom";
                command.Parameters.AddWithValue("@timeFrom", query.WorldTimeFrom);
            }
            if (query.WorldTimeTo >= 0d)
            {
                sql += " AND " + time + "<=@timeTo";
                command.Parameters.AddWithValue("@timeTo", query.WorldTimeTo);
            }
            if (eventType != null && query.EventType != "")
            {
                sql += " AND (" + eventType + ")=@eventType";
                command.Parameters.AddWithValue("@eventType", query.EventType);
            }
            if (query.Domain != "" && query.Domain != domain) sql += " AND 0=1";
            if (AW3HistoryCursorRules.TryDecode(query.Cursor, out AW3HistoryCursorKey cursor))
            {
                int order = string.CompareOrdinal(domain, cursor.Domain);
                if (order == 0) order = string.CompareOrdinal(source, cursor.Source);
                string tie = order > 0 ? "1=1" : order < 0 ? "0=1" : idColumn + ">@cursorId";
                sql += " AND (" + time + ">@cursorTime OR (" + time +
                    "=@cursorTime AND " + tie + "))";
                command.Parameters.AddWithValue("@cursorTime", cursor.WorldTime);
                command.Parameters.AddWithValue("@cursorId", cursor.RecordId);
            }
            command.Parameters.AddWithValue("@pageLimit", query.Limit + 1);
            return sql + " ORDER BY " + time + "," + idColumn + " LIMIT @pageLimit";
        }

        private static bool Matches(AW3HistoryCursorKey key, AW3HistoryQuery query)
        {
            if (query == null) return true;
            if (query.WorldTimeFrom >= 0d && key.WorldTime < query.WorldTimeFrom) return false;
            if (query.WorldTimeTo >= 0d && key.WorldTime > query.WorldTimeTo) return false;
            if (AW3HistoryCursorRules.TryDecode(query.Cursor,
                    out AW3HistoryCursorKey cursor) &&
                AW3HistoryCursorRules.Compare(key, cursor) <= 0) return false;
            return true;
        }

        private static bool ValidCursor(AW3HistoryQuery query)
        {
            return query == null || string.IsNullOrEmpty(query.Cursor) ||
                AW3HistoryCursorRules.TryDecode(query.Cursor,
                    out AW3HistoryCursorKey ignored);
        }

        private static AW3HistoryPage<T> Page<T>(IEnumerable<HistoryItem<T>> source,
            AW3HistoryQuery query)
        {
            var items = source?.ToList() ?? new List<HistoryItem<T>>();
            items.Sort((left, right) => AW3HistoryCursorRules.Compare(left.Key, right.Key));
            int limit = query?.Limit ?? AW3HistoryQuery.MaximumLimit;
            bool hasMore = items.Count > limit;
            if (hasMore) items.RemoveRange(limit, items.Count - limit);
            return AW3HistoryPage<T>.Create(items.Select(item => item.Value), hasMore,
                hasMore ? AW3HistoryCursorRules.Encode(items[items.Count - 1].Key) : "");
        }

        private static AW3HistoryPage<T> EmptyPage<T>() =>
            AW3HistoryPage<T>.Create(new List<T>());

        private sealed class HistoryItem<T>
        {
            public HistoryItem(T value, AW3HistoryCursorKey key)
            {
                Value = value;
                Key = key;
            }
            public T Value { get; }
            public AW3HistoryCursorKey Key { get; }
        }

        private static long ReadLong(SQLiteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? -1L : Convert.ToInt64(reader.GetValue(ordinal));
        }

        private static int ReadInt(SQLiteDataReader reader, int ordinal, int fallback)
        {
            return reader.IsDBNull(ordinal) ? fallback : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static double ReadDouble(SQLiteDataReader reader, int ordinal,
            double fallback)
        {
            if (reader.IsDBNull(ordinal)) return fallback;
            double value = Convert.ToDouble(reader.GetValue(ordinal));
            return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
        }

        private static string ReadString(SQLiteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal)) ?? "";
        }
    }
}
