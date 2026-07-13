namespace AncientWarfare3.core.schools
{
    internal readonly struct SchoolLectureSeniority
    {
        public SchoolLectureSeniority(int pFirstLectureYear, double pFirstLectureTime)
        {
            FirstLectureYear = pFirstLectureYear;
            FirstLectureTime = pFirstLectureTime;
        }

        public int FirstLectureYear { get; }
        public double FirstLectureTime { get; }
    }

    internal sealed class SchoolInstitutionReadModel
    {
        public long InstitutionId { get; set; }
        public string InstitutionType { get; set; } = "";
        public string SchoolId { get; set; } = "";
        public long CityId { get; set; } = -1L;
        public long FounderActorId { get; set; } = -1L;
        public int FoundingYear { get; set; } = -1;
        public int Level { get; set; } = 1;
        public double Condition { get; set; } = 100d;
        public int Active { get; set; } = 1;
    }

    internal sealed class SchoolWorkReadModel
    {
        public long WorkId { get; set; }
        public string WorkKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SchoolId { get; set; } = "";
        public long AuthorActorId { get; set; } = -1L;
        public long CityId { get; set; } = -1L;
        public int WrittenYear { get; set; } = -1;
        public double Condition { get; set; } = 100d;
    }

    internal sealed class SchoolDebateReadModel
    {
        public long DebateId { get; set; }
        public long CityId { get; set; } = -1L;
        public int DebateYear { get; set; } = -1;
        public string TopicId { get; set; } = "";
        public long FirstActorId { get; set; } = -1L;
        public string FirstSchoolId { get; set; } = "";
        public long SecondActorId { get; set; } = -1L;
        public string SecondSchoolId { get; set; } = "";
        public string Result { get; set; } = "";
        public bool Presented { get; set; }
    }

    internal sealed class SchoolEventReadModel
    {
        public string EventType { get; set; } = "";
        public long ActorId { get; set; } = -1L;
        public long TargetActorId { get; set; } = -1L;
        public string SchoolId { get; set; } = "";
        public long CityId { get; set; } = -1L;
        public long KingdomId { get; set; } = -1L;
        public int EventYear { get; set; } = -1;
        public string Payload { get; set; } = "";
        public int Importance { get; set; }
    }
}
