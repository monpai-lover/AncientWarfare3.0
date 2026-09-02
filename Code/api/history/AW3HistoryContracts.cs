using System;

namespace AncientWarfare3.api.history
{
    public static class AW3HistoryDomains
    {
        public const string Biography = "biography";
        public const string Genealogy = "genealogy";
        public const string Chronicle = "chronicle";
        public const string Diplomacy = "diplomacy";
        public const string OfficialCareer = "official_career";
    }

    public static class AW3HistoryEventTypes
    {
        public const string Birth = "born";
        public const string Death = "died";
        public const string Founded = "founded";
        public const string Ruler = "ruler";
        public const string Appointment = "appointed";
        public const string OfficeEnded = "office_ended";
    }

    public sealed class AW3HistoryEvent
    {
        public AW3HistoryEvent(long recordId, string domain, string source,
            long projectionKey, string eventType, double worldTime,
            int worldYear, string yearText, long subjectId, long targetId,
            long kingdomId, long contextKingdomId, string subjectName,
            string content, string category, string projectionKeyText)
        {
            RecordId = recordId;
            Domain = Text(domain);
            Source = Text(source);
            ProjectionKey = projectionKey;
            EventType = Text(eventType);
            WorldTime = double.IsNaN(worldTime) || double.IsInfinity(worldTime)
                ? -1d : worldTime;
            WorldYear = worldYear;
            YearText = Text(yearText);
            SubjectId = subjectId;
            TargetId = targetId;
            KingdomId = kingdomId;
            ContextKingdomId = contextKingdomId;
            SubjectName = Text(subjectName);
            Content = Text(content);
            Category = Text(category);
            ProjectionKeyText = Text(projectionKeyText);
        }

        public long RecordId { get; }
        public string Domain { get; }
        public string Source { get; }
        public long ProjectionKey { get; }
        public string EventType { get; }
        public double WorldTime { get; }
        public int WorldYear { get; }
        public string YearText { get; }
        public long SubjectId { get; }
        public long TargetId { get; }
        public long KingdomId { get; }
        public long ContextKingdomId { get; }
        public string SubjectName { get; }
        public string Content { get; }
        public string Category { get; }
        public string ProjectionKeyText { get; }

        private static string Text(string value) => value ?? "";
    }

    public sealed class AW3GenealogyEntry
    {
        public AW3GenealogyEntry(long actorId, string displayName,
            long fatherId, long motherId, long lineageId, long shiBranchId,
            double birthTime, double deathTime, bool alive)
        {
            ActorId = actorId;
            DisplayName = displayName ?? "";
            FatherId = fatherId;
            MotherId = motherId;
            LineageId = lineageId;
            ShiBranchId = shiBranchId;
            BirthTime = birthTime;
            DeathTime = deathTime;
            Alive = alive;
        }

        public long ActorId { get; }
        public string DisplayName { get; }
        public long FatherId { get; }
        public long MotherId { get; }
        public long LineageId { get; }
        public long ShiBranchId { get; }
        public double BirthTime { get; }
        public double DeathTime { get; }
        public bool Alive { get; }
    }

    public sealed class AW3BiographyEntry
    {
        public AW3BiographyEntry(long recordId, long actorId, string eventType,
            string category, int age, string roleSnapshot, bool wasKing,
            long contextKingdomId, long targetId, string content,
            string yearText)
        {
            RecordId = recordId;
            ActorId = actorId;
            EventType = eventType ?? "";
            Category = category ?? "";
            Age = age;
            RoleSnapshot = roleSnapshot ?? "";
            WasKing = wasKing;
            ContextKingdomId = contextKingdomId;
            TargetId = targetId;
            Content = content ?? "";
            YearText = yearText ?? "";
        }

        public long RecordId { get; }
        public long ActorId { get; }
        public string EventType { get; }
        public string Category { get; }
        public int Age { get; }
        public string RoleSnapshot { get; }
        public bool WasKing { get; }
        public long ContextKingdomId { get; }
        public long TargetId { get; }
        public string Content { get; }
        public string YearText { get; }
    }

    public sealed class AW3ChronicleEntry
    {
        public AW3ChronicleEntry(long recordId, string scope, long subjectId,
            long targetId, long kingdomId, long cityId, string eventType,
            double worldTime, int worldYear, string yearText, string subjectName,
            string content)
        {
            RecordId = recordId;
            Scope = scope ?? "";
            SubjectId = subjectId;
            TargetId = targetId;
            KingdomId = kingdomId;
            CityId = cityId;
            EventType = eventType ?? "";
            WorldTime = worldTime;
            WorldYear = worldYear;
            YearText = yearText ?? "";
            SubjectName = subjectName ?? "";
            Content = content ?? "";
        }

        public long RecordId { get; }
        public string Scope { get; }
        public long SubjectId { get; }
        public long TargetId { get; }
        public long KingdomId { get; }
        public long CityId { get; }
        public string EventType { get; }
        public double WorldTime { get; }
        public int WorldYear { get; }
        public string YearText { get; }
        public string SubjectName { get; }
        public string Content { get; }
    }

    public sealed class AW3Reign
    {
        public AW3Reign(long kingdomId, long rulerId, string rulerName,
            double startTime, double endTime, bool current)
        {
            KingdomId = kingdomId;
            RulerId = rulerId;
            RulerName = rulerName ?? "";
            StartTime = startTime;
            EndTime = endTime;
            Current = current;
        }

        public long KingdomId { get; }
        public long RulerId { get; }
        public string RulerName { get; }
        public double StartTime { get; }
        public double EndTime { get; }
        public bool Current { get; }
    }

    public sealed class AW3CityPeriod
    {
        public AW3CityPeriod(long cityId, long kingdomId, string kingdomName,
            double startTime, double endTime, bool current)
        {
            CityId = cityId;
            KingdomId = kingdomId;
            KingdomName = kingdomName ?? "";
            StartTime = startTime;
            EndTime = endTime;
            Current = current;
        }

        public long CityId { get; }
        public long KingdomId { get; }
        public string KingdomName { get; }
        public double StartTime { get; }
        public double EndTime { get; }
        public bool Current { get; }
    }

    public sealed class AW3DiplomacyEvent
    {
        public AW3DiplomacyEvent(long recordId, string eventType,
            long firstKingdomId, long secondKingdomId, double worldTime,
            int worldYear, string yearText, string status, string content)
            : this(recordId, eventType, firstKingdomId, secondKingdomId,
                worldTime, worldYear, yearText, status, content, "Diplomacy")
        {
        }

        public AW3DiplomacyEvent(long recordId, string eventType,
            long firstKingdomId, long secondKingdomId, double worldTime,
            int worldYear, string yearText, string status, string content,
            string source)
        {
            RecordId = recordId;
            Source = source ?? "";
            EventType = eventType ?? "";
            FirstKingdomId = firstKingdomId;
            SecondKingdomId = secondKingdomId;
            WorldTime = worldTime;
            WorldYear = worldYear;
            YearText = yearText ?? "";
            Status = status ?? "";
            Content = content ?? "";
        }

        public long RecordId { get; }
        public string Source { get; }
        public string EventType { get; }
        public long FirstKingdomId { get; }
        public long SecondKingdomId { get; }
        public double WorldTime { get; }
        public int WorldYear { get; }
        public string YearText { get; }
        public string Status { get; }
        public string Content { get; }
    }

    public sealed class AW3OfficialCareerEntry
    {
        public AW3OfficialCareerEntry(long officerId, long actorId,
            long kingdomId, long cityId, long countyId, string layer,
            string officeId, string rankId, int grade, double appointedTime,
            int startYear, int endYear, bool current, string endReason,
            string actorName, string kingdomName, string cityName)
        {
            OfficerId = officerId;
            ActorId = actorId;
            KingdomId = kingdomId;
            CityId = cityId;
            CountyId = countyId;
            Layer = layer ?? "";
            OfficeId = officeId ?? "";
            RankId = rankId ?? "";
            Grade = grade;
            AppointedTime = appointedTime;
            StartYear = startYear;
            EndYear = endYear;
            Current = current;
            EndReason = endReason ?? "";
            ActorName = actorName ?? "";
            KingdomName = kingdomName ?? "";
            CityName = cityName ?? "";
        }

        public long OfficerId { get; }
        public long ActorId { get; }
        public long KingdomId { get; }
        public long CityId { get; }
        public long CountyId { get; }
        public string Layer { get; }
        public string OfficeId { get; }
        public string RankId { get; }
        public int Grade { get; }
        public double AppointedTime { get; }
        public int StartYear { get; }
        public int EndYear { get; }
        public bool Current { get; }
        public string EndReason { get; }
        public string ActorName { get; }
        public string KingdomName { get; }
        public string CityName { get; }
    }
}
