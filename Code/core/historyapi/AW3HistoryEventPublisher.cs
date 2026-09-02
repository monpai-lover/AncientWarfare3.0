using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.api.history;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.historyapi
{
    internal static class AW3HistoryEventPublisher
    {
        private static readonly AW3HistorySubscriptionRegistry Registry =
            new AW3HistorySubscriptionRegistry();
        private static readonly object Gate = new object();
        private static readonly HashSet<string> PublishedIds =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> PublishedProjections =
            new HashSet<string>(StringComparer.Ordinal);

        public static IDisposable Subscribe(AW3HistorySubscription filter,
            Action<AW3HistoryEvent> handler)
        {
            return Registry.Subscribe(filter, handler);
        }

        public static void PublishCommitted(AW3HistoryEvent item)
        {
            if (!TryAccept(item)) return;
            Registry.PublishCommitted(item);
        }

        public static void PublishDiplomacy(long recordId, string eventType,
            long firstKingdomId, long secondKingdomId, double worldTime,
            int worldYear, string yearText, string status, string content)
        {
            PublishDiplomacy(recordId, "DiplomacyDialogue", eventType,
                firstKingdomId, secondKingdomId, worldTime, worldYear,
                yearText, status, content);
        }

        public static void PublishDiplomacy(long recordId, string source,
            string eventType, long firstKingdomId, long secondKingdomId,
            double worldTime, int worldYear, string yearText, string status,
            string content)
        {
            source = string.IsNullOrEmpty(source) ? "Diplomacy" : source;
            PublishCommitted(new AW3HistoryEvent(recordId,
                AW3HistoryDomains.Diplomacy, source, recordId,
                eventType, worldTime, worldYear, yearText, -1L, -1L,
                firstKingdomId, secondKingdomId, "", content, "", ""));
        }

        public static void PublishCareerRecord(long officerId, string eventType)
        {
            if (officerId < 0L) return;
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return;
            try
            {
                using var command = new SQLiteCommand(
                    "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME," +
                    "LAYER,OFFICE_ID,APPOINTED_YEAR,APPOINTED_TIME," +
                    "ENDED_YEAR,ENDED_TIME,ACTIVE,END_REASON FROM CourtOfficer " +
                    "WHERE OFFICER_ID=@id LIMIT 1", db);
                command.Parameters.AddWithValue("@id", officerId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return;
                bool ended = string.Equals(eventType, "office_ended",
                    StringComparison.Ordinal);
                double time = ended ? ReadDouble(reader, 9, -1d) :
                    ReadDouble(reader, 7, -1d);
                int year = ended ? ReadInt(reader, 8, -1) :
                    ReadInt(reader, 6, -1);
                string source = ended ? "CourtOfficerEnd" : "CourtOfficerAppointment";
                PublishCommitted(new AW3HistoryEvent(officerId,
                    AW3HistoryDomains.OfficialCareer, source, officerId,
                    ended ? "office_ended" : "appointed", time, year, "",
                    ReadLong(reader, 2), officerId, ReadLong(reader, 1),
                    ReadLong(reader, 1), ReadString(reader, 3),
                    ReadString(reader, 5), "official_career", officerId.ToString()));
            }
            catch (SQLiteException) { }
        }

        public static void PublishFailed(AW3HistoryEvent item)
        {
            Registry.PublishFailed(item);
        }

        public static int Drain(int maximumEvents = 64)
        {
            return Registry.Drain(maximumEvents);
        }

        public static void Clear()
        {
            lock (Gate)
            {
                PublishedIds.Clear();
                PublishedProjections.Clear();
            }
            Registry.Clear();
        }

        public static void PublishHistoryRow(string table, long eventId)
        {
            // The canonical write path calls this only after its insert commits.
            // A later read can still fail; publication is best effort.
            if (eventId <= 0L || string.IsNullOrEmpty(table)) return;
            AW3HistoryEvent item = AW3HistoryReadService.ReadCommittedRow(
                table, eventId);
            if (item != null) PublishCommitted(item);
        }

        private static bool TryAccept(AW3HistoryEvent item)
        {
            if (item == null) return false;
            string identity = AW3HistoryEventIdentityRules.Build(
                item.Domain, item.Source, item.RecordId);
            string projection = AW3HistoryEventIdentityRules.BuildProjection(
                item.Domain, item.Source, item.ProjectionKeyText);
            lock (Gate)
            {
                if (!PublishedIds.Add(identity)) return false;
                if (!string.IsNullOrEmpty(item.ProjectionKeyText) &&
                    !PublishedProjections.Add(projection))
                {
                    PublishedIds.Remove(identity);
                    return false;
                }
                return true;
            }
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
