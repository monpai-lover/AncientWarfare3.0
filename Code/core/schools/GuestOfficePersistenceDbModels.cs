using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AncientWarfare3.core.schools
{
    internal sealed class GuestOfficeAffiliationRow
    {
        public long ActorId;
        public long HomeKingdomId;
        public string HomeKingdomName = "";
        public long HometownCityId;
        public long ResidenceCityId;
        public long PreviousResidenceCityId;
        public long DestinationCityId;
        public long ServiceKingdomId;
        public string LifecycleState = "";
        public int ServiceStartYear;
        public int ServiceEndYear;
        public int LastTravelYear;
        public int TravelWaitStartYear;
        public int VoyageStartYear;
        public int VoyageArrivalYear;
        public int TransportFailures;
        public double UpdatedTime;

        public GuestOfficeAffiliationRow Copy()
        {
            return (GuestOfficeAffiliationRow)MemberwiseClone();
        }

        public bool Exact(GuestOfficeAffiliationRow pOther)
        {
            return pOther != null && ActorId == pOther.ActorId &&
                   HomeKingdomId == pOther.HomeKingdomId &&
                   HomeKingdomName == pOther.HomeKingdomName &&
                   HometownCityId == pOther.HometownCityId &&
                   ResidenceCityId == pOther.ResidenceCityId &&
                   PreviousResidenceCityId == pOther.PreviousResidenceCityId &&
                   DestinationCityId == pOther.DestinationCityId &&
                   ServiceKingdomId == pOther.ServiceKingdomId &&
                   LifecycleState == pOther.LifecycleState &&
                   ServiceStartYear == pOther.ServiceStartYear &&
                   ServiceEndYear == pOther.ServiceEndYear &&
                   LastTravelYear == pOther.LastTravelYear &&
                   TravelWaitStartYear == pOther.TravelWaitStartYear &&
                   VoyageStartYear == pOther.VoyageStartYear &&
                   VoyageArrivalYear == pOther.VoyageArrivalYear &&
                   TransportFailures == pOther.TransportFailures &&
                   UpdatedTime.Equals(pOther.UpdatedTime);
        }

        public bool ExactExceptUpdatedTime(GuestOfficeAffiliationRow pOther)
        {
            if (pOther == null) return false;
            GuestOfficeAffiliationRow copy = pOther.Copy();
            copy.UpdatedTime = UpdatedTime;
            return Exact(copy);
        }
    }

    internal sealed class GuestOfficeCareerRow
    {
        public long OfficerId = -1L;
        public long KingdomId;
        public long ActorId;
        public string ActorName = "";
        public long CityId;
        public string Layer = "";
        public string OfficeId = "";
        public string SchoolId = "";
        public double Influence;
        public int AppointedYear;
        public double AppointedTime;
        public int EndedYear = -1;
        public double EndedTime = -1d;
        public int Active = 1;
        public string EndReason = "";
        public double UpdatedTime;
        public string InstitutionAtAppointment = "";
        public int RankAtAppointment;
        public int LocalGradeAtAppointment;
        public bool IsActing;

        public GuestOfficeCareerRow Copy()
        {
            return (GuestOfficeCareerRow)MemberwiseClone();
        }

        public bool Exact(GuestOfficeCareerRow pOther, bool pRequireOfficerId)
        {
            return pOther != null && (!pRequireOfficerId || OfficerId == pOther.OfficerId) &&
                   KingdomId == pOther.KingdomId && ActorId == pOther.ActorId &&
                   ActorName == pOther.ActorName && CityId == pOther.CityId &&
                   Layer == pOther.Layer && OfficeId == pOther.OfficeId &&
                   SchoolId == pOther.SchoolId && Influence.Equals(pOther.Influence) &&
                   AppointedYear == pOther.AppointedYear &&
                   AppointedTime.Equals(pOther.AppointedTime) &&
                   EndedYear == pOther.EndedYear && EndedTime.Equals(pOther.EndedTime) &&
                   Active == pOther.Active && EndReason == pOther.EndReason &&
                   UpdatedTime.Equals(pOther.UpdatedTime) &&
                   InstitutionAtAppointment == pOther.InstitutionAtAppointment &&
                   RankAtAppointment == pOther.RankAtAppointment &&
                   LocalGradeAtAppointment == pOther.LocalGradeAtAppointment &&
                   IsActing == pOther.IsActing;
        }
    }

    internal sealed class GuestOfficeEventRow
    {
        public long EventId = -1L;
        public string OperationKey = "";
        public string EventType = "";
        public long ActorId;
        public long TargetActorId = -1L;
        public string SchoolId = "";
        public long CityId;
        public long KingdomId;
        public int EventYear;
        public string Payload = "";
        public int Importance;
        public double WorldTime;

        public GuestOfficeEventRow Copy()
        {
            return (GuestOfficeEventRow)MemberwiseClone();
        }

        public bool Exact(GuestOfficeEventRow pOther, bool pRequireEventId)
        {
            return pOther != null && (!pRequireEventId || EventId == pOther.EventId) &&
                   OperationKey == pOther.OperationKey && EventType == pOther.EventType &&
                   ActorId == pOther.ActorId && TargetActorId == pOther.TargetActorId &&
                   SchoolId == pOther.SchoolId && CityId == pOther.CityId &&
                   KingdomId == pOther.KingdomId && EventYear == pOther.EventYear &&
                   Payload == pOther.Payload && Importance == pOther.Importance &&
                   WorldTime.Equals(pOther.WorldTime);
        }
    }

    internal sealed class GuestOfficeStartSeed
    {
        public GuestOfficeAffiliationRow ExpectedAffiliation;
        public GuestOfficeCareerRow DesiredCareer;
        public string EventType = "";
        public int ServiceEndYear;
    }

    internal sealed class GuestOfficeDbStartRequest
    {
        public GuestOfficeDbStartRequest(GuestOfficeAffiliationRow pOriginalAffiliation,
            GuestOfficeAffiliationRow pDesiredAffiliation, GuestOfficeCareerRow pDesiredCareer,
            GuestOfficeEventRow pEventTemplate, string pPayloadBase)
        {
            OriginalAffiliation = pOriginalAffiliation ??
                throw new ArgumentNullException(nameof(pOriginalAffiliation));
            DesiredAffiliation = pDesiredAffiliation ??
                throw new ArgumentNullException(nameof(pDesiredAffiliation));
            DesiredCareer = pDesiredCareer ??
                throw new ArgumentNullException(nameof(pDesiredCareer));
            if (pEventTemplate == null)
                throw new ArgumentNullException(nameof(pEventTemplate));
            EventPayloadBase = pPayloadBase ?? "";
            TupleFingerprint = GuestOfficeTupleFingerprint.Compute(DesiredAffiliation,
                DesiredCareer, pEventTemplate, EventPayloadBase);
            OperationKey = GuestOfficeOperationKeyRules.Build(pEventTemplate.EventType,
                pEventTemplate.ActorId, pEventTemplate.KingdomId, pEventTemplate.CityId,
                pEventTemplate.SchoolId, DesiredCareer.OfficeId,
                pEventTemplate.EventYear, DesiredAffiliation.ServiceEndYear,
                TupleFingerprint);
            DesiredEvent = pEventTemplate.Copy();
            DesiredEvent.OperationKey = OperationKey;
            DesiredEvent.Payload = EventPayloadBase + "|tuple=" + TupleFingerprint;
        }

        public GuestOfficeAffiliationRow OriginalAffiliation { get; }
        public GuestOfficeAffiliationRow DesiredAffiliation { get; }
        public GuestOfficeCareerRow DesiredCareer { get; }
        public GuestOfficeEventRow DesiredEvent { get; }
        public string EventPayloadBase { get; }
        public string TupleFingerprint { get; }
        public string OperationKey { get; }
        public bool IdsFrozen => DesiredCareer.OfficerId >= 0 && DesiredEvent.EventId >= 0;

        public void FreezeIds(long pOfficerId, long pEventId)
        {
            if (pOfficerId < 0 || pEventId < 0)
                throw new ArgumentOutOfRangeException("guest durable ids must be non-negative");
            if (IdsFrozen && (DesiredCareer.OfficerId != pOfficerId ||
                              DesiredEvent.EventId != pEventId))
                throw new InvalidOperationException("guest durable ids are already frozen");
            DesiredCareer.OfficerId = pOfficerId;
            DesiredEvent.EventId = pEventId;
        }
    }

    internal sealed class GuestOfficeDbStartResult
    {
        public GuestOfficeDbStartResult(GuestOfficePersistenceOutcome pOutcome,
            GuestOfficeDbStartRequest pRequest, bool pRecoveredExisting)
        {
            Outcome = pOutcome;
            Request = pRequest;
            RecoveredExisting = pRecoveredExisting;
        }

        public GuestOfficePersistenceOutcome Outcome { get; }
        public GuestOfficeDbStartRequest Request { get; }
        public bool RecoveredExisting { get; }
    }

    internal sealed class GuestOfficeDbRecoveryResult
    {
        public GuestOfficeDbRecoveryResult(GuestOfficeRecoveryDecision pDecision,
            GuestOfficeAffiliationRow pAffiliation, GuestOfficeCareerRow pCareer,
            GuestOfficeEventRow pEvent)
        {
            Decision = pDecision;
            Affiliation = pAffiliation;
            Career = pCareer;
            Event = pEvent;
        }

        public GuestOfficeRecoveryDecision Decision { get; }
        public GuestOfficeAffiliationRow Affiliation { get; }
        public GuestOfficeCareerRow Career { get; }
        public GuestOfficeEventRow Event { get; }
    }

    internal static class GuestOfficeTupleFingerprint
    {
        public static string Compute(GuestOfficeAffiliationRow pAffiliation,
            GuestOfficeCareerRow pCareer, GuestOfficeEventRow pEvent, string pPayloadBase)
        {
            if (pAffiliation == null || pCareer == null || pEvent == null) return "";
            var canonical = new StringBuilder("guest-office-tuple:v1;");
            Add(canonical, "aff.actor", pAffiliation.ActorId);
            Add(canonical, "aff.home", pAffiliation.HomeKingdomId);
            Add(canonical, "aff.home_name", pAffiliation.HomeKingdomName);
            Add(canonical, "aff.hometown", pAffiliation.HometownCityId);
            Add(canonical, "aff.residence", pAffiliation.ResidenceCityId);
            Add(canonical, "aff.previous", pAffiliation.PreviousResidenceCityId);
            Add(canonical, "aff.destination", pAffiliation.DestinationCityId);
            Add(canonical, "aff.service", pAffiliation.ServiceKingdomId);
            Add(canonical, "aff.state", pAffiliation.LifecycleState);
            Add(canonical, "aff.start", pAffiliation.ServiceStartYear);
            Add(canonical, "aff.end", pAffiliation.ServiceEndYear);
            Add(canonical, "aff.last", pAffiliation.LastTravelYear);
            Add(canonical, "aff.wait", pAffiliation.TravelWaitStartYear);
            Add(canonical, "aff.voyage_start", pAffiliation.VoyageStartYear);
            Add(canonical, "aff.voyage_arrival", pAffiliation.VoyageArrivalYear);
            Add(canonical, "aff.failures", pAffiliation.TransportFailures);
            Add(canonical, "aff.updated", pAffiliation.UpdatedTime);

            Add(canonical, "career.kingdom", pCareer.KingdomId);
            Add(canonical, "career.actor", pCareer.ActorId);
            Add(canonical, "career.actor_name", pCareer.ActorName);
            Add(canonical, "career.city", pCareer.CityId);
            Add(canonical, "career.layer", pCareer.Layer);
            Add(canonical, "career.office", pCareer.OfficeId);
            Add(canonical, "career.school", pCareer.SchoolId);
            Add(canonical, "career.influence", pCareer.Influence);
            Add(canonical, "career.year", pCareer.AppointedYear);
            Add(canonical, "career.time", pCareer.AppointedTime);
            Add(canonical, "career.ended_year", pCareer.EndedYear);
            Add(canonical, "career.ended_time", pCareer.EndedTime);
            Add(canonical, "career.active", pCareer.Active);
            Add(canonical, "career.reason", pCareer.EndReason);
            Add(canonical, "career.updated", pCareer.UpdatedTime);

            Add(canonical, "event.type", pEvent.EventType);
            Add(canonical, "event.actor", pEvent.ActorId);
            Add(canonical, "event.target", pEvent.TargetActorId);
            Add(canonical, "event.school", pEvent.SchoolId);
            Add(canonical, "event.city", pEvent.CityId);
            Add(canonical, "event.kingdom", pEvent.KingdomId);
            Add(canonical, "event.year", pEvent.EventYear);
            Add(canonical, "event.payload", pPayloadBase ?? "");
            Add(canonical, "event.importance", pEvent.Importance);
            Add(canonical, "event.time", pEvent.WorldTime);

            byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(bytes);
            var result = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
                result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private static void Add(StringBuilder pBuilder, string pName, string pValue)
        {
            string value = pValue ?? "";
            pBuilder.Append(pName).Append('=').Append(value.Length.ToString(
                CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
        }

        private static void Add(StringBuilder pBuilder, string pName, long pValue)
        {
            Add(pBuilder, pName, pValue.ToString(CultureInfo.InvariantCulture));
        }

        private static void Add(StringBuilder pBuilder, string pName, double pValue)
        {
            Add(pBuilder, pName, pValue.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
