using System;
using AncientWarfare3.api.history;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.historyapi
{
    internal sealed class AW3HistoryRow
    {
        public long RecordId = -1L;
        public string Domain = "";
        public string Source = "";
        public long ProjectionKey = -1L;
        public string ProjectionKeyText = "";
        public string EventType = "";
        public double WorldTime = -1d;
        public int WorldYear = -1;
        public string YearText = "";
        public long SubjectId = -1L;
        public long TargetId = -1L;
        public long KingdomId = -1L;
        public long ContextKingdomId = -1L;
        public string SubjectName = "";
        public string Content = "";
        public string Category = "";
        public int Age = -1;
        public string RoleSnapshot = "";
        public bool WasKing;
    }

    internal static class AW3HistoryDtoMapper
    {
        public static AW3HistoryEvent ToEvent(AW3HistoryRow row)
        {
            row = row ?? new AW3HistoryRow();
            return new AW3HistoryEvent(row.RecordId, Text(row.Domain),
                Text(row.Source), row.ProjectionKey, Text(row.EventType),
                FiniteOrUnknown(row.WorldTime), row.WorldYear,
                Text(row.YearText), row.SubjectId, row.TargetId,
                row.KingdomId, row.ContextKingdomId, Text(row.SubjectName),
                Text(row.Content), Text(row.Category),
                Text(row.ProjectionKeyText));
        }

        public static AW3BiographyEntry ToBiography(AW3HistoryRow row,
            int age, string roleSnapshot, bool wasKing)
        {
            row = row ?? new AW3HistoryRow();
            return new AW3BiographyEntry(row.RecordId, row.SubjectId,
                Text(row.EventType), Text(row.Category), age,
                Text(roleSnapshot), wasKing, row.ContextKingdomId,
                row.TargetId, Text(row.Content), Text(row.YearText));
        }

        public static AW3ChronicleEntry ToChronicle(AW3HistoryRow row,
            string scope, long cityId)
        {
            row = row ?? new AW3HistoryRow();
            return new AW3ChronicleEntry(row.RecordId, Text(scope),
                row.SubjectId, row.TargetId, row.KingdomId, cityId,
                Text(row.EventType), FiniteOrUnknown(row.WorldTime),
                row.WorldYear, Text(row.YearText), Text(row.SubjectName),
                Text(row.Content));
        }

        public static AW3DiplomacyEvent ToDiplomacy(AW3HistoryRow row,
            long firstKingdomId, long secondKingdomId, string status)
        {
            row = row ?? new AW3HistoryRow();
            return new AW3DiplomacyEvent(row.RecordId, Text(row.EventType),
                firstKingdomId, secondKingdomId,
                FiniteOrUnknown(row.WorldTime), row.WorldYear,
                Text(row.YearText), Text(status), Text(row.Content),
                Text(row.Source));
        }

        public static AW3OfficialCareerEntry ToCareer(
            OfficialCareerHistoryRow row)
        {
            if (row == null)
                return new AW3OfficialCareerEntry(-1L, -1L, -1L, -1L, -1L,
                    "", "", "", -1, -1d, -1, -1, false, "", "", "", "");
            return new AW3OfficialCareerEntry(row.OfficerId, row.ActorId,
                row.KingdomId, row.CityId, row.CountyId, row.Layer,
                row.OfficeId, row.RankId, row.Grade, row.AppointedTime,
                row.StartYear, row.EndYear, row.IsCurrent, row.EndReason,
                row.ActorName, row.KingdomName, row.CityName);
        }

        private static string Text(string value) => value ?? "";

        private static double FiniteOrUnknown(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? -1d : value;
        }
    }
}
