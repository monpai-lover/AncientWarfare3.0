namespace AncientWarfare3.core.court
{
    public sealed class OfficialCareerReadModel
    {
        public OfficialCareerReadModel(long pOfficerId, long pKingdomId, long pActorId,
            long pCityId, string pLayer, string pOfficeId,
            string pInstitutionAtAppointment, int pAppointedYear,
            double pAppointedTime, int pEndedYear, double pEndedTime, bool pIsCurrent,
            string pEndReason, string pKingdomName, string pKingdomColor, string pCityName,
            int pRankAtAppointment, int pLocalGradeAtAppointment,
            long pCountyId = -1L)
        {
            OfficerId = pOfficerId;
            KingdomId = pKingdomId;
            ActorId = pActorId;
            CityId = pCityId;
            CountyId = pCountyId;
            Layer = pLayer ?? "";
            OfficeId = pOfficeId ?? "";
            InstitutionAtAppointment = pInstitutionAtAppointment ?? "";
            AppointedYear = pAppointedYear;
            AppointedTime = pAppointedTime;
            EndedYear = pEndedYear;
            EndedTime = pEndedTime;
            IsCurrent = pIsCurrent;
            EndReason = pEndReason ?? "";
            KingdomName = pKingdomName ?? "";
            KingdomColor = NormalizeKingdomColor(pKingdomColor);
            CityName = pCityName ?? "";
            RankAtAppointment = pRankAtAppointment > 0
                ? OfficialCareerRankRules.ClampRank(pRankAtAppointment)
                : -1;
            LocalGradeAtAppointment = pLocalGradeAtAppointment;
        }

        public static string NormalizeKingdomColor(string pColor)
        {
            if (string.IsNullOrWhiteSpace(pColor)) return "";
            string hex = pColor.Trim();
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length != 6 && hex.Length != 8) return "";
            foreach (char value in hex)
            {
                bool digit = value >= '0' && value <= '9';
                bool lower = value >= 'a' && value <= 'f';
                bool upper = value >= 'A' && value <= 'F';
                if (!digit && !lower && !upper) return "";
            }
            return "#" + hex;
        }

        public long OfficerId { get; }
        public long KingdomId { get; }
        public long ActorId { get; }
        public long CityId { get; }
        public long CountyId { get; }
        public string Layer { get; }
        public string OfficeId { get; }
        public string InstitutionAtAppointment { get; }
        public int AppointedYear { get; }
        public double AppointedTime { get; }
        public int EndedYear { get; }
        public double EndedTime { get; }
        public bool IsCurrent { get; }
        public string EndReason { get; }
        public string KingdomName { get; }
        public string KingdomColor { get; }
        public string CityName { get; }
        public int RankAtAppointment { get; }
        public int LocalGradeAtAppointment { get; }
        public bool HasCity => CityId >= 0;
    }
}
